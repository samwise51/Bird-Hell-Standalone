#!/usr/bin/env python3
"""Build deterministic Milestone 19 cave edge and prop assets."""
from __future__ import annotations
import hashlib, json, math, shutil
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art/Source/Terrain/cave_tileset_hand_painted_v1.png"
CANONICAL_DIR = ROOT / "art/Source/Terrain/Canonical"
RUNTIME_DIR = ROOT / "src/BirdHell/Content/Sprites/Terrain"
MANIFEST = CANONICAL_DIR / "cave_decorations_v1.json"
SOURCE_SIZE = (1448, 1086)
SOURCE_SHA256 = "8902a8076bc7d2978b020297eb58ba1399ed467a2e0897e5ce6e99d74bd630d1"
BACKGROUND_CUTOFF = 18
SEAM_FEATHER = 24
ASSETS = {
    "ceiling": {"name": "cave_ceiling_cap_v1.png", "crop": (990, 0, 1200, 145), "size": (512, 192),
        "transform": "vertical flip, horizontal repeat synthesis", "purpose": "downward-facing cave boundary ribbon",
        "worldRepeatLength": 3.25, "worldOpenOverhang": 0.55, "worldSolidDepth": 0.30},
    "wall": {"name": "cave_wall_edge_v1.png", "crop": (8, 145, 98, 545), "size": (512, 128),
        "transform": "90-degree counter-clockwise rotation, horizontal repeat synthesis", "purpose": "strongly vertical cave boundary ribbon",
        "worldRepeatLength": 3.50, "worldOpenOverhang": 0.14, "worldSolidDepth": 0.55},
    "boulder": {"name": "cave_boulder_small_v1.png", "crop": (704, 725, 786, 794), "size": (128, 108),
        "transform": "proportional resize", "purpose": "sparse floor-boundary decoration",
        "worldWidth": 0.85, "worldHeight": 0.55, "worldSolidInset": 0.20},
}

def digest(path: Path) -> str: return hashlib.sha256(path.read_bytes()).hexdigest()
def require(condition: bool, message: str) -> None:
    if not condition: raise RuntimeError(message)

def extract_premultiplied_rgba(source: Image.Image) -> Image.Image:
    result = Image.new("RGBA", source.size); output = result.load(); pixels = source.convert("RGB").load()
    for y in range(source.height):
        for x in range(source.width):
            red, green, blue = pixels[x, y]
            alpha = min(255, round(max(red, green, blue) * 255 / BACKGROUND_CUTOFF))
            output[x, y] = (red, green, blue, alpha)
    return result

def enforce_premultiplied(image: Image.Image) -> Image.Image:
    output = image.load()
    for y in range(image.height):
        for x in range(image.width):
            red, green, blue, alpha = output[x, y]
            output[x, y] = (min(red, alpha), min(green, alpha), min(blue, alpha), alpha)
    return image

def synthesize_horizontal_repeat(source: Image.Image, size: tuple[int, int]) -> Image.Image:
    base = source.resize(size, Image.Resampling.LANCZOS); width, height = base.size
    source_pixels = base.load(); result = Image.new("RGBA", base.size); output = result.load()
    shifts = (0, width // 3, 2 * width // 3); samples = []
    for x in range(width):
        entries = []
        for shift in shifts:
            sample = (x + shift) % width
            entries.append((sample, math.sin(math.pi * (sample + 0.5) / width) ** 6))
        samples.append(entries)
    for y in range(height):
        for x in range(width):
            channels = [0.0] * 4; total = 0.0
            for sample_x, weight in samples[x]:
                pixel = source_pixels[sample_x, y]
                for channel in range(4): channels[channel] += pixel[channel] * weight
                total += weight
            output[x, y] = tuple(round(value / total) for value in channels)
    for offset in range(SEAM_FEATHER):
        strength = 0.5 * (1.0 + math.cos(math.pi * offset / SEAM_FEATHER)); left_x = offset; right_x = width - 1 - offset
        for y in range(height):
            left = output[left_x, y]; right = output[right_x, y]
            average = tuple((left[c] + right[c]) / 2.0 for c in range(4))
            output[left_x, y] = tuple(round(left[c] * (1.0 - strength) + average[c] * strength) for c in range(4))
            output[right_x, y] = tuple(round(right[c] * (1.0 - strength) + average[c] * strength) for c in range(4))
    return enforce_premultiplied(result)

def seam_metrics(image: Image.Image, band: int = 8) -> dict[str, float]:
    pixels = image.convert("RGBA").load(); differences = []
    for y in range(image.height):
        for offset in range(band):
            left = pixels[offset, y]; right = pixels[image.width - 1 - offset, y]
            differences.extend(abs(left[c] - right[c]) for c in range(4))
    ordered = sorted(differences); index = min(len(ordered) - 1, math.ceil(len(ordered) * 0.95) - 1)
    return {"meanRgbaDifference": round(sum(differences) / len(differences), 4),
        "rmsRgbaDifference": round(math.sqrt(sum(v * v for v in differences) / len(differences)), 4),
        "maximumRepresentativeDifferenceP95": float(ordered[index])}

def main() -> None:
    require(SOURCE.exists(), f"Missing source artwork: {SOURCE}")
    require(digest(SOURCE) == SOURCE_SHA256, "Cave tileset hash differs from the audited source")
    CANONICAL_DIR.mkdir(parents=True, exist_ok=True); RUNTIME_DIR.mkdir(parents=True, exist_ok=True); records = []
    with Image.open(SOURCE) as source:
        require(source.size == SOURCE_SIZE and source.mode == "RGB", "Unexpected cave source contract")
        for asset_id, spec in ASSETS.items():
            image = extract_premultiplied_rgba(source.crop(spec["crop"]))
            if asset_id == "ceiling":
                image = synthesize_horizontal_repeat(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM), spec["size"])
            elif asset_id == "wall":
                image = synthesize_horizontal_repeat(image.transpose(Image.Transpose.ROTATE_90), spec["size"])
            else: image = enforce_premultiplied(image.resize(spec["size"], Image.Resampling.LANCZOS))
            canonical = CANONICAL_DIR / spec["name"]; runtime = RUNTIME_DIR / spec["name"]
            image.save(canonical, format="PNG", optimize=False, compress_level=9); shutil.copyfile(canonical, runtime)
            record = {"id": asset_id, "sourceCrop": list(spec["crop"]), "cropDescription": spec["purpose"],
                "outputPath": str(canonical.relative_to(ROOT)), "runtimePath": str(runtime.relative_to(ROOT)),
                "dimensions": list(spec["size"]), "sha256": digest(canonical),
                "alphaMode": "premultiplied alpha recovered from black matte", "generationMethod": spec["transform"],
                "resampling": "Pillow LANCZOS", "intendedFilter": "linear",
                "intendedWrapMode": {"u": "wrap", "v": "clamp"} if asset_id != "boulder" else {"u": "clamp", "v": "clamp"},
                "placementPurpose": spec["purpose"],
                "presentation": {key: value for key, value in spec.items() if key.startswith("world")}, "status": "production"}
            if asset_id != "boulder": record["edgeSeamMetrics"] = seam_metrics(image)
            records.append(record)
    manifest = {"formatVersion": 1,
        "source": {"path": str(SOURCE.relative_to(ROOT)), "sha256": SOURCE_SHA256, "dimensions": list(SOURCE_SIZE), "background": "pure black RGB matte"},
        "generation": {"deterministic": True, "backgroundCutoff": BACKGROUND_CUTOFF, "seamFeatherPixels": SEAM_FEATHER}, "assets": records}
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for record in records:
        print(f"PASS: built {record['outputPath']} ({record['dimensions'][0]}x{record['dimensions'][1]})")
        print(f"SHA-256: {record['sha256']}")

if __name__ == "__main__": main()
