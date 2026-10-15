#!/usr/bin/env python3
"""Deterministic validation for complete Milestone 13 runtime assets."""

from __future__ import annotations

import hashlib
import json
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from clean_environment_edges import read_rgba_png, rgba_at
from build_m13_runtime_assets import (
    ALPHA_CORE,
    FRINGE_RADIUS,
    PLAYER_ANIMATIONS,
    PLAYER_CELL_HEIGHT,
    PLAYER_CELL_WIDTH,
    PLAYER_COLUMNS,
    PLAYER_GROUND_BASELINE,
    PLAYER_MIN_GUTTER,
    PLAYER_PIVOT_X,
    PLAYER_ROWS,
)


ROOT = Path(__file__).resolve().parents[1]
CANONICAL_MANIFEST = ROOT / "art/Source/Player/Canonical/player_canonical_manifest_v1.json"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def intersects(first: dict[str, int], second: dict[str, int]) -> bool:
    return not (
        first["x"] + first["width"] <= second["x"]
        or second["x"] + second["width"] <= first["x"]
        or first["y"] + first["height"] <= second["y"]
        or second["y"] + second["height"] <= first["y"]
    )


def extract_cell(
    pixels: bytearray, width: int, left: int, top: int,
    cell_width: int = PLAYER_CELL_WIDTH, cell_height: int = PLAYER_CELL_HEIGHT,
) -> bytes:
    result = bytearray(cell_width * cell_height * 4)
    for y in range(cell_height):
        source_start = ((top + y) * width + left) * 4
        target_start = y * cell_width * 4
        result[target_start:target_start + cell_width * 4] = pixels[
            source_start:source_start + cell_width * 4]
    return bytes(result)


def validate_bird() -> None:
    texture = ROOT / "src/BirdHell/Content/Sprites/Enemy/enemy_bird_purple_flight_runtime_v1.png"
    manifest_path = texture.with_suffix(".json")
    require(texture.exists() and manifest_path.exists(), "Missing bird runtime asset")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    width, height, pixels = read_rgba_png(texture)
    require((width, height) == (manifest["atlas"]["width"], manifest["atlas"]["height"]),
            "Bird atlas dimensions do not match its manifest")
    require(manifest["nativeFacing"] == "right" and manifest["filtering"] == "linear",
            "Bird presentation contract changed")
    flight = manifest["animations"]["Flight"]
    require(len(flight["frames"]) == 16 and flight["fps"] == 12 and flight["loop"] is True,
            "Bird animation contract changed")
    for index, frame in enumerate(flight["frames"]):
        rectangle = frame["rectangle"]
        require(rectangle["width"] == manifest["cellWidth"]
                and rectangle["height"] == manifest["cellHeight"],
                f"Bird frame {index} has the wrong cell size")
        occupied = []
        for y in range(rectangle["height"]):
            for x in range(rectangle["width"]):
                pixel = rgba_at(pixels, width, rectangle["x"] + x, rectangle["y"] + y)
                require(pixel[3] or pixel[:3] == (0, 0, 0),
                        f"Bird frame {index} has transparent RGB contamination")
                if pixel[3]:
                    occupied.append((x, y))
        require(occupied, f"Bird frame {index} is empty")
        require(min(x for x, _ in occupied) > 0 and min(y for _, y in occupied) > 0
                and max(x for x, _ in occupied) < rectangle["width"] - 1
                and max(y for _, y in occupied) < rectangle["height"] - 1,
                f"Bird frame {index} clips")
    print(f"PASS: bird atlas/manifest (16 frames, {width}x{height})")


def scale_metadata(value: object, path: str = "") -> list[tuple[str, object]]:
    found: list[tuple[str, object]] = []
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = f"{path}.{key}" if path else key
            if "scale" in key.lower():
                found.append((child_path, child))
            found.extend(scale_metadata(child, child_path))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found.extend(scale_metadata(child, f"{path}[{index}]"))
    return found


def frame_geometry(cell: bytes) -> dict[str, object]:
    pixels = [tuple(cell[index:index + 4]) for index in range(0, len(cell), 4)]
    visible = [(index % PLAYER_CELL_WIDTH, index // PLAYER_CELL_WIDTH)
               for index, pixel in enumerate(pixels) if pixel[3]]
    core = [(index % PLAYER_CELL_WIDTH, index // PLAYER_CELL_WIDTH)
            for index, pixel in enumerate(pixels) if pixel[3] >= ALPHA_CORE]
    body = [(index % PLAYER_CELL_WIDTH, index // PLAYER_CELL_WIDTH)
            for index, (red, green, blue, alpha) in enumerate(pixels)
            if alpha >= ALPHA_CORE
            and not (blue > 120 and blue > red * 1.25 and blue > green * 1.05)]
    require(bool(visible and core and body), "Player frame is empty")
    return {
        "visible": (min(x for x, _ in visible), min(y for _, y in visible),
                    max(x for x, _ in visible), max(y for _, y in visible)),
        "coreBounds": (min(x for x, _ in core), min(y for _, y in core),
                       max(x for x, _ in core), max(y for _, y in core)),
        "coreArea": len(core),
        "bodyHeight": max(y for _, y in body) - min(y for _, y in body) + 1,
        "bodyArea": len(body),
    }


def validate_alpha(animation: str, frame_index: int, cell: bytes) -> dict[str, object]:
    substantial: set[tuple[int, int]] = set()
    occupied: list[tuple[int, int]] = []
    for index in range(PLAYER_CELL_WIDTH * PLAYER_CELL_HEIGHT):
        red, green, blue, alpha = cell[index * 4:index * 4 + 4]
        x, y = index % PLAYER_CELL_WIDTH, index // PLAYER_CELL_WIDTH
        require(alpha or (red, green, blue) == (0, 0, 0),
                f"Player {animation} frame {frame_index} has transparent RGB at ({x}, {y})")
        if alpha:
            occupied.append((x, y))
        if alpha >= 32:
            substantial.add((x, y))
    require(occupied, f"Player {animation} frame {frame_index} is empty")
    bounds = (min(x for x, _ in occupied), min(y for _, y in occupied),
              max(x for x, _ in occupied), max(y for _, y in occupied))
    margins = (bounds[0], bounds[1], PLAYER_CELL_WIDTH - 1 - bounds[2],
               PLAYER_CELL_HEIGHT - 1 - bounds[3])
    require(min(margins) >= PLAYER_MIN_GUTTER,
            f"Player {animation} frame {frame_index} has only {min(margins)}px gutter")
    for index in range(PLAYER_CELL_WIDTH * PLAYER_CELL_HEIGHT):
        alpha = cell[index * 4 + 3]
        if not 0 < alpha < 32:
            continue
        x, y = index % PLAYER_CELL_WIDTH, index // PLAYER_CELL_WIDTH
        require(any((nx, ny) in substantial
                    for ny in range(max(0, y - FRINGE_RADIUS),
                                    min(PLAYER_CELL_HEIGHT, y + FRINGE_RADIUS + 1))
                    for nx in range(max(0, x - FRINGE_RADIUS),
                                    min(PLAYER_CELL_WIDTH, x + FRINGE_RADIUS + 1))),
                f"Player {animation} frame {frame_index} has isolated low alpha at ({x}, {y})")
    return frame_geometry(cell)


def validate_player() -> None:
    texture = ROOT / "src/BirdHell/Content/Sprites/Player/player_explorer_runtime_v1.png"
    manifest_path = texture.with_suffix(".json")
    require(texture.exists() and manifest_path.exists(), "Missing player runtime asset")
    runtime = json.loads(manifest_path.read_text(encoding="utf-8"))
    canonical = json.loads(CANONICAL_MANIFEST.read_text(encoding="utf-8"))
    width, height, atlas = read_rgba_png(texture)
    require((width, height) == (PLAYER_CELL_WIDTH * PLAYER_COLUMNS,
                                PLAYER_CELL_HEIGHT * PLAYER_ROWS),
            "Player runtime atlas dimensions changed")
    require(runtime["atlas"] == {"width": width, "height": height,
                                  "columns": PLAYER_COLUMNS, "rows": PLAYER_ROWS},
            "Player runtime atlas metadata changed")
    require(runtime["cellWidth"] == PLAYER_CELL_WIDTH
            and runtime["cellHeight"] == PLAYER_CELL_HEIGHT,
            "Player runtime cell size changed")
    require(runtime["nativeFacing"] == "right" and runtime["filtering"] == "linear",
            "Player facing/filtering contract changed")
    require(runtime["pivot"] == {"x": PLAYER_PIVOT_X, "y": 88, "space": "cellPixels",
                                  "semantic": "normalized logical player/body pivot"}
            and runtime["anchors"]["groundBaseline"]
            == {"x": PLAYER_PIVOT_X, "y": PLAYER_GROUND_BASELINE},
            "Player pivot or ground baseline changed")
    canonical_reference = runtime["canonicalManifest"]
    require(canonical_reference == {
        "path": str(CANONICAL_MANIFEST.relative_to(ROOT)),
        "sha256": digest(CANONICAL_MANIFEST),
        "formatVersion": 1,
    }, "Runtime canonical manifest reference is invalid")
    require(canonical["cellSize"] == {"width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT}
            and canonical["futureBuilderScale"] == 1.0,
            "Canonical player runtime contract changed")
    require(runtime["packing"] == {
        "method": "direct copy of canonical 192x176 RGBA cells",
        "runtimeBuilderScale": 1.0,
        "geometricResampling": False,
    }, "Player runtime packing is not neutral direct copy")
    require(scale_metadata(runtime) == [("packing.runtimeBuilderScale", 1.0)],
            f"Unexpected runtime player scaling metadata: {scale_metadata(runtime)}")
    alpha_contract = runtime["alpha"]
    require(alpha_contract["pngAlphaMode"] == "straight"
            and alpha_contract["alphaZeroRgb"] == [0, 0, 0]
            and alpha_contract["runtimeUploadAlphaMode"]
            == "premultiply with DefaultColorProcessors.PremultiplyAlpha",
            "Player alpha contract changed")
    presentation = (ROOT / "src/BirdHell/Presentation/SpritePresentation.cs").read_text(
        encoding="utf-8")
    require("textureId == BirdHellVisuals.PlayerTexture" in presentation
            and "DefaultColorProcessors.PremultiplyAlpha" in presentation,
            "Player runtime upload no longer premultiplies the straight-RGBA atlas")
    require(set(runtime["animations"]) == set(PLAYER_ANIMATIONS),
            "Player runtime animation set changed")

    rectangles: list[dict[str, int]] = []
    geometry: dict[str, list[dict[str, object]]] = {}
    used: set[tuple[int, int]] = set()
    for animation, (filename, count, row, first_column, fps, loop) in PLAYER_ANIMATIONS.items():
        canonical_entry = canonical["animations"][animation]
        source_path = ROOT / "art/Source/Player/Canonical" / filename
        require(digest(source_path) == canonical_entry["sha256"],
                f"Canonical {animation} source hash differs from its manifest")
        strip_width, strip_height, strip = read_rgba_png(source_path)
        require((strip_width, strip_height) == (PLAYER_CELL_WIDTH * count, PLAYER_CELL_HEIGHT),
                f"Canonical {animation} dimensions are invalid")
        require(canonical_entry["frameCount"] == count
                and canonical_entry["cellSize"]
                == {"width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT}
                and canonical_entry["futureBuilderScale"] == 1.0
                and canonical_entry["runtimeScaleRequired"] is False,
                f"Canonical {animation} manifest geometry/scale contract is invalid")

        entry = runtime["animations"][animation]
        expected_source = str(source_path.relative_to(ROOT))
        require(entry["frameCount"] == count
                and entry["fps"] == fps and entry["loop"] is loop,
                f"Player {animation} timing changed")
        require(entry["canonicalSource"] == {
            "path": expected_source, "sha256": canonical_entry["sha256"]},
            f"Player {animation} canonical provenance is invalid")
        require(entry["anchor"] == canonical_entry["anchorType"]
                and entry["nativeFacing"] == "right",
                f"Player {animation} anchor/facing changed")
        require(entry["atlasPlacement"] == {
            "row": row, "firstColumn": first_column,
            "lastColumn": first_column + count - 1},
            f"Player {animation} atlas placement changed")
        require(len(entry["frames"]) == count, f"Player {animation} frame count changed")
        geometry[animation] = []
        for frame_index, frame in enumerate(entry["frames"]):
            rectangle = frame["rectangle"]
            expected_rectangle = {
                "x": (first_column + frame_index) * PLAYER_CELL_WIDTH,
                "y": row * PLAYER_CELL_HEIGHT,
                "width": PLAYER_CELL_WIDTH,
                "height": PLAYER_CELL_HEIGHT,
            }
            require(rectangle == expected_rectangle,
                    f"Player {animation} frame {frame_index} atlas placement changed")
            require(frame["sourceOrderIndex"] == frame_index
                    and frame["source"] == expected_source
                    and frame["canonicalCell"] == {
                        "x": frame_index * PLAYER_CELL_WIDTH, "y": 0,
                        "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT},
                    f"Player {animation} frame {frame_index} source metadata is invalid")
            require(not any(intersects(rectangle, earlier) for earlier in rectangles),
                    f"Player {animation} frame {frame_index} overlaps another cell")
            rectangles.append(rectangle)
            used.add((first_column + frame_index, row))
            actual = extract_cell(atlas, width, rectangle["x"], rectangle["y"])
            expected = extract_cell(strip, strip_width, frame_index * PLAYER_CELL_WIDTH, 0)
            require(actual == expected,
                    f"Player {animation} frame {frame_index} differs from its canonical cell")
            frame_data = validate_alpha(animation, frame_index, actual)
            geometry[animation].append(frame_data)
            if canonical_entry["anchorType"] == "groundBaseline":
                bottom = frame_data["visible"][3]
                if animation == "Run" and frame_index not in (0, 4):
                    require(bottom <= PLAYER_GROUND_BASELINE + 1,
                            f"Player Run frame {frame_index} exceeds the ground baseline")
                else:
                    require(PLAYER_GROUND_BASELINE - 1 <= bottom <= PLAYER_GROUND_BASELINE + 1,
                            f"Player {animation} frame {frame_index} baseline is {bottom}")

    roll_frame = runtime["animations"]["Roll"]["frames"][4]
    require(roll_frame.get("status") == "provisional"
            and roll_frame.get("provenance") == {
                "manifest": str(CANONICAL_MANIFEST.relative_to(ROOT)),
                "animation": "Roll", "frameIndex": 4},
            "Roll frame 4 provisional canonical provenance is missing")
    require(runtime["animations"]["Roll"]["status"]
            == "provisional; frame 4 requires host approval",
            "Roll animation provisional status is missing")

    attack_representative = [geometry["Attack"][index] for index in (0, 2, 3, 6)]
    attack_height = statistics.median(item["bodyHeight"] for item in attack_representative)
    attack_area = statistics.median(item["bodyArea"] for item in attack_representative)
    require(90 <= attack_height <= 100 and 4300 <= attack_area <= 4600,
            f"Attack anatomy changed: height={attack_height}, area={attack_area}")
    for index, item in enumerate(geometry["Attack"]):
        left, top, right, bottom = item["visible"]
        require(left > 0 and top > 0 and right < PLAYER_CELL_WIDTH - 1
                and bottom < PLAYER_CELL_HEIGHT - 1,
                f"Attack frame {index} sword/slash clips")

    roll = geometry["Roll"]
    frame4 = roll[4]
    neighbors = [roll[3], roll[5]]
    frame4_width = frame4["coreBounds"][2] - frame4["coreBounds"][0] + 1
    frame4_height = frame4["coreBounds"][3] - frame4["coreBounds"][1] + 1
    neighbor_envelopes = [
        (item["coreBounds"][2] - item["coreBounds"][0] + 1)
        * (item["coreBounds"][3] - item["coreBounds"][1] + 1)
        for item in neighbors
    ]
    envelope_ratio = frame4_width * frame4_height / statistics.median(neighbor_envelopes)
    core_ratio = frame4["coreArea"] / statistics.median(item["coreArea"] for item in neighbors)

    for row in range(PLAYER_ROWS):
        for column in range(PLAYER_COLUMNS):
            if (column, row) in used:
                continue
            empty = extract_cell(
                atlas, width, column * PLAYER_CELL_WIDTH, row * PLAYER_CELL_HEIGHT)
            require(not any(empty), f"Unused player cell ({column}, {row}) is not transparent")

    print(f"PASS: player canonical-to-runtime exact equivalence (38 frames, {width}x{height})")
    print(f"Attack representative body: height={attack_height:.1f}px, area={attack_area:.0f}px; "
          "grounded and unclipped")
    print(f"Roll frame 4: canonical exact; envelope ratio={envelope_ratio:.3f}; "
          f"core ratio={core_ratio:.3f}; status=provisional")


if __name__ == "__main__":
    validate_player()
    validate_bird()
