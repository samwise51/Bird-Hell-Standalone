#!/usr/bin/env python3
"""Build fixed-cell canonical player strips without touching runtime assets."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image

from build_m13_runtime_assets import (
    ALPHA_CORE,
    PLAYER_CELL_HEIGHT as CELL_H,
    PLAYER_CELL_WIDTH as CELL_W,
    PLAYER_GROUND_BASELINE as BASELINE_Y,
    PLAYER_PIVOT_X as ORIGIN_X,
    cleaned_components,
    component_bounds,
    player_anchor,
    render_player_cell,
    replacement_run_cells,
    substantial_components,
)

ROOT = Path(__file__).resolve().parents[1]
SOURCE_DIR = ROOT / "art/Source/Player"
OUTPUT_DIR = SOURCE_DIR / "Canonical"
MANIFEST = OUTPUT_DIR / "player_canonical_manifest_v1.json"
BODY_ANCHOR_Y = 88
FORMAT_VERSION = 1
NATIVE_FACING = "right"

MAIN = SOURCE_DIR / "anime_cave_explorer_sprite_sheet.png"
ADDONS = SOURCE_DIR / "player_anime_explorer_addons_v1.png"
ATTACK = SOURCE_DIR / "player_anime_explorer_attack_v1.png"
RUN = SOURCE_DIR / "player_anime_explorer_run_v2.png"
EXPECTED = {
    MAIN: ((1448, 1086), "16160c15685a5c40e689a6e0ed88706177e5f23dee982524c53458ce7bd38885"),
    ADDONS: ((1448, 1086), "ebc14cfdba7d070faed4f96962c6630871bb5db8441e3f54b0870cad2f7d175f"),
    ATTACK: ((2172, 724), "89e5065058dacf1b6967f783e3939d628a5cd9dec6f0fc530ff13e242778f407"),
    RUN: ((1536, 176), "c4dbd78e87759dfefc1a1bed760560f7c9967157ab884771c72155a6a1a09329"),
}
FILES = {
    "Idle": "player_idle_v1.png", "Run": "player_run_v1.png",
    "Jump": "player_jump_v1.png", "Airborne": "player_airborne_v1.png",
    "Crouch": "player_crouch_v1.png", "Roll": "player_roll_v1.png",
    "Attack": "player_attack_v1.png",
}
COUNTS = {"Idle": 9, "Run": 8, "Jump": 6, "Airborne": 1,
          "Crouch": 1, "Roll": 6, "Attack": 7}
SCALES = {"Idle": 0.50, "Run": 0.873, "Jump": 0.50, "Airborne": 0.23,
          "Crouch": 0.25, "Roll": 0.50, "Attack": 0.3284}
ROLL_REPLACEMENT_SOURCE_SCALE = 0.25
ROLL_REPLACEMENT_OPTICAL_CORRECTION = 0.95


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path: Path) -> tuple[int, int, bytearray]:
    expected_size, expected_hash = EXPECTED[path]
    actual_hash = digest(path)
    if actual_hash != expected_hash:
        raise RuntimeError(f"Unexpected source hash for {path.name}: {actual_hash}")
    with Image.open(path) as image:
        if image.mode != "RGBA" or image.size != expected_size:
            raise RuntimeError(
                f"Unexpected {path.name} format: {image.mode} {image.size}; "
                f"expected RGBA {expected_size}")
        return image.width, image.height, bytearray(image.tobytes())


def combined_bounds(parts: list[list[int]], width: int) -> list[int]:
    boxes = [component_bounds(part, width) for part in parts]
    return [min(box[0] for box in boxes), min(box[1] for box in boxes),
            max(box[2] for box in boxes), max(box[3] for box in boxes)]


def render(width: int, height: int, pixels: bytearray, parts: list[list[int]],
           scale: float, grounded: bool) -> bytearray:
    clean = cleaned_components(width, height, pixels, parts)
    anchor = player_anchor(parts, width, pixels, grounded)
    return render_player_cell(width, height, clean, anchor, scale, grounded)


def pack(cells: list[bytearray]) -> bytearray:
    width = CELL_W * len(cells)
    output = bytearray(width * CELL_H * 4)
    for frame, cell in enumerate(cells):
        for y in range(CELL_H):
            source = y * CELL_W * 4
            target = (y * width + frame * CELL_W) * 4
            output[target:target + CELL_W * 4] = cell[source:source + CELL_W * 4]
    return output


def save(path: Path, cells: list[bytearray]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.frombytes("RGBA", (CELL_W * len(cells), CELL_H), bytes(pack(cells))).save(
        path, "PNG", optimize=False, compress_level=9)


def metrics(cell: bytearray) -> dict[str, object]:
    core = [(i % CELL_W, i // CELL_W) for i in range(CELL_W * CELL_H)
            if cell[i * 4 + 3] >= ALPHA_CORE]
    visible = [(i % CELL_W, i // CELL_W) for i in range(CELL_W * CELL_H)
               if cell[i * 4 + 3]]
    if not core or not visible:
        raise RuntimeError("Generated an empty canonical cell")
    return {
        "coreArea": len(core),
        "coreBoundsInclusive": [min(x for x, _ in core), min(y for _, y in core),
                                max(x for x, _ in core), max(y for _, y in core)],
        "alphaBoundsInclusive": [min(x for x, _ in visible), min(y for _, y in visible),
                                 max(x for x, _ in visible), max(y for _, y in visible)],
    }


def provenance(path: Path, source_index: int, box: list[int], source_scale: float,
               effective_scale: float, anchor: str, status: str = "approved") -> dict[str, object]:
    return {
        "source": str(path.relative_to(ROOT)),
        "sourceOrderIndex": source_index,
        "sourceBoundsExclusive": box,
        "normalizationMethod":
            "component isolation; premultiplied-aware bilinear resampling; straight-RGBA output",
        "sourceScaleBaked": source_scale,
        "effectiveScaleBaked": effective_scale,
        "anchor": anchor,
        "status": status,
    }


def build() -> None:
    loaded = {path: load(path) for path in EXPECTED}
    mw, mh, mp = loaded[MAIN]
    components = substantial_components(mw, mh, mp, 5_000)
    bounded = [(part, component_bounds(part, mw)) for part in components]
    idle = sorted([part for part, box in bounded if box[1] < 280 and box[3] <= 270],
                  key=lambda part: component_bounds(part, mw)[0])
    jump = sorted([part for part, box in bounded if 508 <= box[1] < 700 and box[3] <= 730],
                  key=lambda part: component_bounds(part, mw)[0])
    roll_parts = sorted([part for part, box in bounded if 700 <= box[1] and box[3] <= 901],
                        key=lambda part: component_bounds(part, mw)[0])
    if (len(idle), len(jump), len(roll_parts)) != (9, 6, 7):
        raise RuntimeError("Unexpected main-sheet component grouping")
    original_roll = [[roll_parts[0]], [roll_parts[1]], [roll_parts[2]],
                     [roll_parts[3], roll_parts[4]], [roll_parts[6]]]

    aw, ah, ap = loaded[ADDONS]
    addons = sorted(substantial_components(aw, ah, ap, 10_000),
                    key=lambda part: component_bounds(part, aw)[0])
    if len(addons) != 3:
        raise RuntimeError(f"Expected three add-on components, found {len(addons)}")
    crouch, airborne, roll_replacement = addons
    tw, th, tp = loaded[ATTACK]
    attacks = sorted(substantial_components(tw, th, tp, 5_000),
                     key=lambda part: component_bounds(part, tw)[0])
    if len(attacks) != 7:
        raise RuntimeError(f"Expected seven attack components, found {len(attacks)}")

    cells: dict[str, list[bytearray]] = {}
    records: dict[str, list[dict[str, object]]] = {}

    def add(name: str, source: Path, data: tuple[int, int, bytearray],
            groups: list[list[list[int]]], scale: float, grounded: bool,
            indices: list[int]) -> None:
        width, height, pixels = data
        anchor = "groundBaseline" if grounded else "bodyAnchor"
        cells[name] = [render(width, height, pixels, group, scale, grounded) for group in groups]
        records[name] = [
            provenance(source, index, combined_bounds(group, width), scale, scale, anchor)
            for group, index in zip(groups, indices)
        ]

    add("Idle", MAIN, loaded[MAIN], [[part] for part in idle], 0.50, True, list(range(9)))
    add("Jump", MAIN, loaded[MAIN], [[part] for part in jump], 0.50, False, list(range(6)))
    add("Airborne", ADDONS, loaded[ADDONS], [[airborne]], 0.23, False, [1])
    add("Crouch", ADDONS, loaded[ADDONS], [[crouch]], 0.25, True, [0])
    add("Attack", ATTACK, loaded[ATTACK], [[part] for part in attacks],
        0.3284, True, list(range(7)))

    rw, rh, rp = loaded[RUN]
    cells["Run"] = replacement_run_cells(rw, rh, rp)
    records["Run"] = [
        provenance(RUN, i, [i * CELL_W, 0, (i + 1) * CELL_W, CELL_H],
                   0.873, 0.873, "groundBaseline") for i in range(8)
    ]

    roll_cells = [render(mw, mh, mp, group, 0.50, False) for group in original_roll[:4]]
    roll_records = [
        provenance(MAIN, i, combined_bounds(group, mw), 0.50, 0.50, "bodyAnchor")
        for i, group in enumerate(original_roll[:4])
    ]
    effective = ROLL_REPLACEMENT_SOURCE_SCALE * ROLL_REPLACEMENT_OPTICAL_CORRECTION
    roll_cells.append(render(aw, ah, ap, [roll_replacement], effective, False))
    replacement = provenance(
        ADDONS, 2, combined_bounds([roll_replacement], aw),
        ROLL_REPLACEMENT_SOURCE_SCALE, effective, "bodyAnchor", "provisional")
    replacement["opticalScaleCorrection"] = ROLL_REPLACEMENT_OPTICAL_CORRECTION
    replacement["provisionalReason"] = (
        "0.95x corrects the documented ~1.05x anatomical overscale; the curled pose "
        "retains a larger envelope/core area than frames 3 and 5 and needs in-game review")
    roll_records.append(replacement)
    roll_cells.append(render(mw, mh, mp, original_roll[4], 0.50, False))
    roll_records.append(provenance(
        MAIN, 5, combined_bounds(original_roll[4], mw), 0.50, 0.50, "bodyAnchor"))
    cells["Roll"], records["Roll"] = roll_cells, roll_records

    hashes: dict[str, str] = {}
    all_metrics: dict[str, list[dict[str, object]]] = {}
    for name, filename in FILES.items():
        if len(cells[name]) != COUNTS[name]:
            raise RuntimeError(f"Unexpected {name} frame count: {len(cells[name])}")
        output = OUTPUT_DIR / filename
        save(output, cells[name])
        hashes[filename] = digest(output)
        all_metrics[name] = [metrics(cell) for cell in cells[name]]

    manifest: dict[str, object] = {
        "canonicalFormatVersion": FORMAT_VERSION,
        "cellSize": {"width": CELL_W, "height": CELL_H},
        "nativeFacing": NATIVE_FACING,
        "xOrigin": ORIGIN_X,
        "filteringAssumption": "linear",
        "alphaContract": {
            "mode": "straight RGBA", "alphaZeroRgb": [0, 0, 0], "matte": "none",
            "resampling": "premultiplied-aware bilinear; converted to straight RGBA",
        },
        "futureBuilderScale": 1.0,
        "sources": [
            {"path": str(path.relative_to(ROOT)), "dimensions": list(size), "sha256": source_hash}
            for path, (size, source_hash) in EXPECTED.items()
        ],
        "animations": {},
    }
    anchors = {"Idle": "groundBaseline", "Run": "groundBaseline",
               "Jump": "bodyAnchor", "Airborne": "bodyAnchor",
               "Crouch": "groundBaseline", "Roll": "bodyAnchor",
               "Attack": "groundBaseline"}
    old_scales: dict[str, object] = {
        "Idle": 0.50, "Run": 0.873, "Jump": 0.50, "Airborne": 0.23,
        "Crouch": 0.25, "Roll": {"frames0To3And5": 0.50, "frame4": 0.25},
        "Attack": 0.29,
    }
    baked_corrections: dict[str, object] = {
        "Idle": 0.50, "Run": 0.873, "Jump": 0.50, "Airborne": 0.23,
        "Crouch": 0.25,
        "Roll": {"frames0To3And5": 0.50, "frame4": effective,
                 "frame4OpticalCorrection": ROLL_REPLACEMENT_OPTICAL_CORRECTION},
        "Attack": 0.3284,
    }
    animations = manifest["animations"]
    assert isinstance(animations, dict)
    for name, filename in FILES.items():
        count = COUNTS[name]
        entry: dict[str, object] = {
            "file": filename, "sha256": hashes[filename], "frameCount": count,
            "cellSize": {"width": CELL_W, "height": CELL_H},
            "stripDimensions": {"width": CELL_W * count, "height": CELL_H},
            "nativeFacing": NATIVE_FACING, "xOrigin": ORIGIN_X,
            "anchorType": anchors[name], "futureBuilderScale": 1.0,
            "runtimeScaleRequired": False, "oldBuilderScale": old_scales[name],
            "bakedCanonicalCorrection": baked_corrections[name], "frames": [],
        }
        if anchors[name] == "groundBaseline":
            entry["baselineY"] = BASELINE_Y
        else:
            entry["bodyAnchor"] = {"x": ORIGIN_X, "y": BODY_ANCHOR_Y}
            entry["anchorStrategy"] = (
                "largest core component median aligned to (96,88), preserving approved placement")
        frames = entry["frames"]
        assert isinstance(frames, list)
        for index, record in enumerate(records[name]):
            frame = dict(record)
            frame["canonicalCell"] = {
                "x": index * CELL_W, "y": 0, "width": CELL_W, "height": CELL_H}
            frame["metrics"] = all_metrics[name][index]
            frames.append(frame)
        animations[name] = entry

    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for name, filename in FILES.items():
        print(f"Wrote {OUTPUT_DIR.relative_to(ROOT) / filename} "
              f"({CELL_W * COUNTS[name]}x{CELL_H})")
    print(f"Wrote {MANIFEST.relative_to(ROOT)}")


if __name__ == "__main__":
    build()
