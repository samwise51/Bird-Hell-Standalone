#!/usr/bin/env python3
"""Build the deterministic seamless cave base material and its runtime copy."""

from __future__ import annotations

import hashlib
import json
import math
import shutil
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art/Source/Terrain/hand_painted_cave_terrain_asset_sheet.png"
CANONICAL_DIR = ROOT / "art/Source/Terrain/Canonical"
CANONICAL = CANONICAL_DIR / "cave_base_material_v1.png"
MANIFEST = CANONICAL_DIR / "cave_base_material_v1.json"
RUNTIME = ROOT / "src/BirdHell/Content/Sprites/Terrain/cave_base_material_v1.png"

SOURCE_SIZE = (1448, 1086)
SOURCE_SHA256 = "4a00075a4c68fe85a3e69f205f613210176219fca7f10b08fd6b61a25227a727"
# Painted interior of the first neutral material swatch. The inset omits the
# rounded transparent swatch border; no floor lip, moss, or cave prop is included.
SOURCE_CROP = (24, 878, 220, 1058)
OUTPUT_SIZE = (512, 512)
EDGE_BAND = 8
WORLD_REPEAT = (6.0, 6.0)
SEAM_FEATHER = 24


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def synthesize_periodic(source: Image.Image) -> Image.Image:
    """Blend nine wrapped source phases with periodic raised-cosine weights.

    Every source-edge discontinuity receives zero weight. Three phases per axis
    avoid the conspicuous bilateral cross produced by a single mirrored seam.
    """

    width, height = OUTPUT_SIZE
    base = source.convert("RGB").resize(OUTPUT_SIZE, Image.Resampling.LANCZOS)
    source_pixels = base.load()
    result = Image.new("RGB", OUTPUT_SIZE)
    result_pixels = result.load()
    x_shifts = (0, width // 3, 2 * width // 3)
    y_shifts = (0, height // 3, 2 * height // 3)

    x_samples = []
    for x in range(width):
        entries = []
        for shift in x_shifts:
            sample = (x + shift) % width
            weight = math.sin(math.pi * (sample + 0.5) / width) ** 6
            entries.append((sample, weight))
        x_samples.append(entries)

    y_samples = []
    for y in range(height):
        entries = []
        for shift in y_shifts:
            sample = (y + shift) % height
            weight = math.sin(math.pi * (sample + 0.5) / height) ** 6
            entries.append((sample, weight))
        y_samples.append(entries)

    for y in range(height):
        for x in range(width):
            red = green = blue = total = 0.0
            for sample_y, weight_y in y_samples[y]:
                for sample_x, weight_x in x_samples[x]:
                    weight = weight_x * weight_y
                    pixel = source_pixels[sample_x, sample_y]
                    red += pixel[0] * weight
                    green += pixel[1] * weight
                    blue += pixel[2] * weight
                    total += weight
            result_pixels[x, y] = (
                round(red / total),
                round(green / total),
                round(blue / total),
            )
    return feather_opposite_edges(result)


def feather_opposite_edges(image: Image.Image) -> Image.Image:
    """Reconcile a narrow opposing-edge band for bilinear wrap sampling."""

    result = image.copy()
    pixels = result.load()
    width, height = result.size
    for offset in range(SEAM_FEATHER):
        strength = 0.5 * (1.0 + math.cos(math.pi * offset / SEAM_FEATHER))
        left_x = offset
        right_x = width - 1 - offset
        for y in range(height):
            left = pixels[left_x, y]
            right = pixels[right_x, y]
            average = tuple((left[channel] + right[channel]) / 2.0 for channel in range(3))
            pixels[left_x, y] = tuple(round(left[channel] * (1.0 - strength) + average[channel] * strength)
                                      for channel in range(3))
            pixels[right_x, y] = tuple(round(right[channel] * (1.0 - strength) + average[channel] * strength)
                                       for channel in range(3))

    for offset in range(SEAM_FEATHER):
        strength = 0.5 * (1.0 + math.cos(math.pi * offset / SEAM_FEATHER))
        top_y = offset
        bottom_y = height - 1 - offset
        for x in range(width):
            top = pixels[x, top_y]
            bottom = pixels[x, bottom_y]
            average = tuple((top[channel] + bottom[channel]) / 2.0 for channel in range(3))
            pixels[x, top_y] = tuple(round(top[channel] * (1.0 - strength) + average[channel] * strength)
                                     for channel in range(3))
            pixels[x, bottom_y] = tuple(round(bottom[channel] * (1.0 - strength) + average[channel] * strength)
                                        for channel in range(3))
    return result


def seam_metrics(image: Image.Image, band: int = EDGE_BAND) -> dict[str, dict[str, float]]:
    pixels = image.convert("RGB").load()
    width, height = image.size

    def summarize(differences: list[int]) -> dict[str, float]:
        ordered = sorted(differences)
        representative_index = min(len(ordered) - 1, math.ceil(len(ordered) * 0.95) - 1)
        return {
            "meanRgbDifference": round(sum(differences) / len(differences), 4),
            "rmsRgbDifference": round(math.sqrt(sum(value * value for value in differences) / len(differences)), 4),
            "maximumRepresentativeDifferenceP95": float(ordered[representative_index]),
        }

    horizontal: list[int] = []
    for y in range(height):
        for offset in range(band):
            left = pixels[offset, y]
            right = pixels[width - 1 - offset, y]
            horizontal.extend(abs(left[channel] - right[channel]) for channel in range(3))

    vertical: list[int] = []
    for x in range(width):
        for offset in range(band):
            top = pixels[x, offset]
            bottom = pixels[x, height - 1 - offset]
            vertical.extend(abs(top[channel] - bottom[channel]) for channel in range(3))

    return {"leftRight": summarize(horizontal), "topBottom": summarize(vertical)}


def main() -> None:
    require(SOURCE.exists(), f"Missing source artwork: {SOURCE}")
    require(digest(SOURCE) == SOURCE_SHA256, "Cave source artwork hash differs from the audited source")
    with Image.open(SOURCE) as source:
        require(source.size == SOURCE_SIZE, f"Expected cave source dimensions {SOURCE_SIZE}, found {source.size}")
        crop = source.crop(SOURCE_CROP)
        alpha = crop.convert("RGBA").getchannel("A")
        require(alpha.getextrema()[0] >= 246,
                "Selected cave material crop contains source transparency outside the swatch paint")
        material = synthesize_periodic(crop)

    metrics = seam_metrics(material)
    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    RUNTIME.parent.mkdir(parents=True, exist_ok=True)
    material.save(CANONICAL, format="PNG", optimize=False, compress_level=9)
    shutil.copyfile(CANONICAL, RUNTIME)

    manifest = {
        "formatVersion": 1,
        "source": {
            "path": str(SOURCE.relative_to(ROOT)),
            "sha256": SOURCE_SHA256,
            "dimensions": list(SOURCE_SIZE),
            "crop": list(SOURCE_CROP),
            "cropDescription": "painted interior of first neutral brown material swatch",
        },
        "output": {
            "path": str(CANONICAL.relative_to(ROOT)),
            "runtimePath": str(RUNTIME.relative_to(ROOT)),
            "dimensions": list(OUTPUT_SIZE),
            "sha256": digest(CANONICAL),
            "mode": "RGB",
            "alphaMode": "opaque RGB",
        },
        "generation": {
            "method": "three-phase per-axis toroidal raised-cosine overlap blend with narrow opposing-edge feather",
            "resampling": "Pillow LANCZOS",
            "deterministic": True,
            "edgeBandPixels": EDGE_BAND,
            "seamFeatherPixels": SEAM_FEATHER,
        },
        "edgeSeamMetrics": metrics,
        "intendedFilter": "linear",
        "intendedWrapMode": {"u": "wrap", "v": "wrap"},
        "intendedWorldRepeatSize": {"width": WORLD_REPEAT[0], "height": WORLD_REPEAT[1]},
        "status": "production",
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: built {CANONICAL.relative_to(ROOT)} ({OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]})")
    print(f"SHA-256: {manifest['output']['sha256']}")
    print(f"left/right: {metrics['leftRight']}")
    print(f"top/bottom: {metrics['topBottom']}")


if __name__ == "__main__":
    main()
