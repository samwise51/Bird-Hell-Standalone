#!/usr/bin/env python3
"""Build the deterministic repeating cave surface-cap ribbon and runtime copy."""

from __future__ import annotations

import hashlib
import json
import math
import shutil
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art/Source/Terrain/cave_tileset_hand_painted_v1.png"
CANONICAL_DIR = ROOT / "art/Source/Terrain/Canonical"
CANONICAL = CANONICAL_DIR / "cave_surface_cap_v1.png"
MANIFEST = CANONICAL_DIR / "cave_surface_cap_v1.json"
RUNTIME = ROOT / "src/BirdHell/Content/Sprites/Terrain/cave_surface_cap_v1.png"

SOURCE_SIZE = (1448, 1086)
SOURCE_SHA256 = "8902a8076bc7d2978b020297eb58ba1399ed467a2e0897e5ce6e99d74bd630d1"
# Interior run of the first long horizontal ledge. It includes transparent air,
# the irregular pebble silhouette, orange soil lip, and one shallow rock course.
SOURCE_CROP = (145, 156, 565, 240)
OUTPUT_SIZE = (512, 128)
BACKGROUND_CUTOFF = 18
SEAM_FEATHER = 24
WORLD_REPEAT_LENGTH = 2.75
WORLD_OPEN_OVERHANG = 0.18
WORLD_SOLID_DEPTH = 0.62


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def extract_premultiplied_rgba(source: Image.Image) -> Image.Image:
    """Recover antialiased transparency from artwork composited on pure black.

    The source RGB is already premultiplied by its black matte. Alpha is inferred
    only inside the narrow near-black range; painted pixels above that range stay
    fully opaque, avoiding a broad color-key that would eat dark rock detail.
    """

    result = Image.new("RGBA", source.size)
    output = result.load()
    pixels = source.convert("RGB").load()
    for y in range(source.height):
        for x in range(source.width):
            red, green, blue = pixels[x, y]
            maximum = max(red, green, blue)
            alpha = min(255, round(maximum * 255 / BACKGROUND_CUTOFF))
            output[x, y] = (red, green, blue, alpha)
    return result


def synthesize_horizontal_repeat(source: Image.Image) -> Image.Image:
    base = source.resize(OUTPUT_SIZE, Image.Resampling.LANCZOS)
    width, height = base.size
    source_pixels = base.load()
    result = Image.new("RGBA", base.size)
    output = result.load()
    shifts = (0, width // 3, 2 * width // 3)

    samples = []
    for x in range(width):
        entries = []
        for shift in shifts:
            sample = (x + shift) % width
            weight = math.sin(math.pi * (sample + 0.5) / width) ** 6
            entries.append((sample, weight))
        samples.append(entries)

    for y in range(height):
        for x in range(width):
            channels = [0.0, 0.0, 0.0, 0.0]
            total = 0.0
            for sample_x, weight in samples[x]:
                pixel = source_pixels[sample_x, y]
                for channel in range(4):
                    channels[channel] += pixel[channel] * weight
                total += weight
            output[x, y] = tuple(round(value / total) for value in channels)

    for offset in range(SEAM_FEATHER):
        strength = 0.5 * (1.0 + math.cos(math.pi * offset / SEAM_FEATHER))
        left_x = offset
        right_x = width - 1 - offset
        for y in range(height):
            left = output[left_x, y]
            right = output[right_x, y]
            average = tuple((left[channel] + right[channel]) / 2.0 for channel in range(4))
            output[left_x, y] = tuple(
                round(left[channel] * (1.0 - strength) + average[channel] * strength)
                for channel in range(4))
            output[right_x, y] = tuple(
                round(right[channel] * (1.0 - strength) + average[channel] * strength)
                for channel in range(4))
    # LANCZOS can overshoot individual channels by a few values at hard alpha
    # transitions. Reassert the premultiplied invariant after all filtering.
    for y in range(height):
        for x in range(width):
            red, green, blue, alpha = output[x, y]
            output[x, y] = (min(red, alpha), min(green, alpha), min(blue, alpha), alpha)
    return result


def seam_metrics(image: Image.Image, band: int = 8) -> dict[str, float]:
    pixels = image.convert("RGBA").load()
    differences = []
    for y in range(image.height):
        for offset in range(band):
            left = pixels[offset, y]
            right = pixels[image.width - 1 - offset, y]
            differences.extend(abs(left[channel] - right[channel]) for channel in range(4))
    ordered = sorted(differences)
    representative_index = min(len(ordered) - 1, math.ceil(len(ordered) * 0.95) - 1)
    return {
        "meanRgbaDifference": round(sum(differences) / len(differences), 4),
        "rmsRgbaDifference": round(
            math.sqrt(sum(value * value for value in differences) / len(differences)), 4),
        "maximumRepresentativeDifferenceP95": float(ordered[representative_index]),
    }


def main() -> None:
    require(SOURCE.exists(), f"Missing source artwork: {SOURCE}")
    require(digest(SOURCE) == SOURCE_SHA256, "New cave tileset hash differs from the audited source")
    with Image.open(SOURCE) as source:
        require(source.size == SOURCE_SIZE, f"Expected cave source dimensions {SOURCE_SIZE}, found {source.size}")
        require(source.mode == "RGB", f"Expected RGB black-backed source, found {source.mode}")
        crop = source.crop(SOURCE_CROP)
        cap = synthesize_horizontal_repeat(extract_premultiplied_rgba(crop))

    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    RUNTIME.parent.mkdir(parents=True, exist_ok=True)
    cap.save(CANONICAL, format="PNG", optimize=False, compress_level=9)
    shutil.copyfile(CANONICAL, RUNTIME)
    metrics = seam_metrics(cap)
    manifest = {
        "formatVersion": 1,
        "source": {
            "path": str(SOURCE.relative_to(ROOT)),
            "sha256": SOURCE_SHA256,
            "dimensions": list(SOURCE_SIZE),
            "crop": list(SOURCE_CROP),
            "cropDescription": "interior span of first long horizontal ledge: air, pebble silhouette, soil lip, shallow rock course",
            "background": "pure black RGB matte",
        },
        "output": {
            "path": str(CANONICAL.relative_to(ROOT)),
            "runtimePath": str(RUNTIME.relative_to(ROOT)),
            "dimensions": list(OUTPUT_SIZE),
            "sha256": digest(CANONICAL),
            "mode": "RGBA",
            "alphaMode": "premultiplied alpha recovered from black matte",
        },
        "generation": {
            "method": "near-black matte alpha recovery followed by three-phase horizontal raised-cosine overlap blend and opposing-edge feather",
            "resampling": "Pillow LANCZOS",
            "deterministic": True,
            "backgroundCutoff": BACKGROUND_CUTOFF,
            "seamFeatherPixels": SEAM_FEATHER,
        },
        "edgeSeamMetrics": metrics,
        "intendedFilter": "linear",
        "intendedWrapMode": {"u": "wrap", "v": "clamp"},
        "presentation": {
            "worldRepeatLength": WORLD_REPEAT_LENGTH,
            "worldOpenOverhang": WORLD_OPEN_OVERHANG,
            "worldSolidDepth": WORLD_SOLID_DEPTH,
            "eligibleBoundary": "traversable-side normal y >= 0.35",
        },
        "status": "production",
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: built {CANONICAL.relative_to(ROOT)} ({OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]})")
    print(f"SHA-256: {manifest['output']['sha256']}")
    print(f"left/right: {metrics}")


if __name__ == "__main__":
    main()
