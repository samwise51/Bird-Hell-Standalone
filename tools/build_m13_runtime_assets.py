#!/usr/bin/env python3
"""Build the normalized Milestone 13 player and bird runtime atlases."""

from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from clean_environment_edges import read_rgba_png, rgba_at, set_rgba, write_rgba_png


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art/Source/Enemy/purple_raven_flight_sprite_sheet.png"
OUTPUT = ROOT / "src/BirdHell/Content/Sprites/Enemy/enemy_bird_purple_flight_runtime_v1.png"
MANIFEST = OUTPUT.with_suffix(".json")
EXPECTED_SOURCE_HASH = "4644ff36413f2da0263dc43e182cefc060bbab3a893c0d1c85f83e9988d7b0e8"

ALPHA_CORE = 128
FRINGE_RADIUS = 3
SCALE = 0.32
CELL_WIDTH = 128
CELL_HEIGHT = 120
PIVOT_X = 80
PIVOT_Y = 72
COLUMNS = 4
ROWS = 4

PLAYER_CANONICAL_DIR = ROOT / "art/Source/Player/Canonical"
PLAYER_CANONICAL_MANIFEST = PLAYER_CANONICAL_DIR / "player_canonical_manifest_v1.json"
PLAYER_OUTPUT = ROOT / "src/BirdHell/Content/Sprites/Player/player_explorer_runtime_v1.png"
PLAYER_MANIFEST = PLAYER_OUTPUT.with_suffix(".json")
PLAYER_CELL_WIDTH = 192
PLAYER_CELL_HEIGHT = 176
PLAYER_PIVOT_X = 96
PLAYER_PIVOT_Y = 88
PLAYER_GROUND_BASELINE = 160
PLAYER_COLUMNS = 9
PLAYER_ROWS = 6
PLAYER_MIN_GUTTER = 4
PLAYER_ANIMATIONS = {
    "Idle": ("player_idle_v1.png", 9, 0, 0, 7, True),
    "Run": ("player_run_v1.png", 8, 1, 0, 12, True),
    "Jump": ("player_jump_v1.png", 6, 2, 0, 10, False),
    "Airborne": ("player_airborne_v1.png", 1, 3, 0, 0, True),
    "Crouch": ("player_crouch_v1.png", 1, 3, 1, 0, True),
    "Roll": ("player_roll_v1.png", 6, 4, 0, 12, False),
    "Attack": ("player_attack_v1.png", 7, 5, 0, 14, False),
}


def alpha_components(width: int, height: int, pixels: bytearray) -> list[list[int]]:
    seen = bytearray(width * height)
    components: list[list[int]] = []
    for start in range(width * height):
        if seen[start] or pixels[start * 4 + 3] < ALPHA_CORE:
            continue
        seen[start] = 1
        pending = [start]
        component: list[int] = []
        while pending:
            index = pending.pop()
            component.append(index)
            y, x = divmod(index, width)
            for ny in range(max(0, y - 1), min(height, y + 2)):
                for nx in range(max(0, x - 1), min(width, x + 2)):
                    neighbor = ny * width + nx
                    if not seen[neighbor] and pixels[neighbor * 4 + 3] >= ALPHA_CORE:
                        seen[neighbor] = 1
                        pending.append(neighbor)
        if len(component) >= 10_000:
            components.append(component)
    if len(components) != 16:
        raise RuntimeError(f"Expected 16 substantial bird components, found {len(components)}")
    components.sort(key=lambda item: (min(index // width for index in item), min(index % width for index in item)))
    rows = [sorted(components[index:index + 4], key=lambda item: min(value % width for value in item))
            for index in range(0, 16, 4)]
    return [component for row in rows for component in row]


def substantial_components(
    width: int, height: int, pixels: bytearray, minimum_size: int
) -> list[list[int]]:
    seen = bytearray(width * height)
    components: list[list[int]] = []
    for start in range(width * height):
        if seen[start] or pixels[start * 4 + 3] < ALPHA_CORE:
            continue
        seen[start] = 1
        pending = [start]
        component: list[int] = []
        while pending:
            index = pending.pop()
            component.append(index)
            y, x = divmod(index, width)
            for ny in range(max(0, y - 1), min(height, y + 2)):
                for nx in range(max(0, x - 1), min(width, x + 2)):
                    neighbor = ny * width + nx
                    if not seen[neighbor] and pixels[neighbor * 4 + 3] >= ALPHA_CORE:
                        seen[neighbor] = 1
                        pending.append(neighbor)
        if len(component) >= minimum_size:
            components.append(component)
    return components


def component_bounds(component: list[int], width: int) -> tuple[int, int, int, int]:
    xs = [index % width for index in component]
    ys = [index // width for index in component]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def component_landmark(component: list[int], width: int, pixels: bytearray) -> tuple[float, float]:
    yellow: list[tuple[int, int]] = []
    for index in component:
        y, x = divmod(index, width)
        red, green, blue, _ = rgba_at(pixels, width, x, y)
        if red > 150 and green > 90 and blue < 100 and red > green:
            yellow.append((x, y))
    if len(yellow) < 100:
        raise RuntimeError("Could not identify the bird head/beak landmark")
    return (sum(x for x, _ in yellow) / len(yellow), sum(y for _, y in yellow) / len(yellow))


def cleaned_frame(width: int, height: int, source: bytearray, component: list[int]) -> bytearray:
    return cleaned_components(width, height, source, [component])


def cleaned_components(
    width: int, height: int, source: bytearray, components: list[list[int]]
) -> bytearray:
    core = {index for component in components for index in component}
    included = set(core)
    nearest_core: dict[int, int] = {}
    for index in core:
        y, x = divmod(index, width)
        for ny in range(max(0, y - FRINGE_RADIUS), min(height, y + FRINGE_RADIUS + 1)):
            for nx in range(max(0, x - FRINGE_RADIUS), min(width, x + FRINGE_RADIUS + 1)):
                candidate = ny * width + nx
                if source[candidate * 4 + 3] == 0:
                    continue
                distance = max(abs(nx - x), abs(ny - y))
                previous = nearest_core.get(candidate)
                if previous is None:
                    included.add(candidate)
                    nearest_core[candidate] = index
                else:
                    py, px = divmod(previous, width)
                    if distance < max(abs(nx - px), abs(ny - py)):
                        nearest_core[candidate] = index

    result = bytearray(width * height * 4)
    for index in included:
        y, x = divmod(index, width)
        red, green, blue, alpha = rgba_at(source, width, x, y)
        if alpha < ALPHA_CORE:
            trusted = nearest_core[index]
            ty, tx = divmod(trusted, width)
            red, green, blue, _ = rgba_at(source, width, tx, ty)
        if alpha >= 250:
            alpha = 255
        set_rgba(result, width, x, y, (red, green, blue, alpha))
    return result


def premultiplied_sample(pixels: bytearray, width: int, height: int, x: float, y: float) -> tuple[int, int, int, int]:
    x0, y0 = math.floor(x), math.floor(y)
    tx, ty = x - x0, y - y0
    accum_a = accum_r = accum_g = accum_b = 0.0
    for px, py, weight in (
        (x0, y0, (1 - tx) * (1 - ty)),
        (x0 + 1, y0, tx * (1 - ty)),
        (x0, y0 + 1, (1 - tx) * ty),
        (x0 + 1, y0 + 1, tx * ty),
    ):
        if not (0 <= px < width and 0 <= py < height):
            continue
        red, green, blue, alpha = rgba_at(pixels, width, px, py)
        coverage = alpha / 255.0
        accum_a += coverage * weight
        accum_r += red * coverage * weight
        accum_g += green * coverage * weight
        accum_b += blue * coverage * weight
    if accum_a <= 0:
        return 0, 0, 0, 0
    output_alpha = round(accum_a * 255)
    if output_alpha == 0:
        return 0, 0, 0, 0
    return (
        round(accum_r / accum_a), round(accum_g / accum_a), round(accum_b / accum_a), output_alpha
    )


def render_cell(
    source_width: int,
    source_height: int,
    frame: bytearray,
    head_landmark: tuple[float, float],
) -> bytearray:
    # The torso pivot is a fixed offset behind/above the stable yellow beak/head
    # landmark. This avoids centering on wing bounds, which would visibly bob.
    anchor_x = head_landmark[0] - 70
    anchor_y = head_landmark[1] - 12
    cell = bytearray(CELL_WIDTH * CELL_HEIGHT * 4)
    for y in range(CELL_HEIGHT):
        source_y = anchor_y + ((y + 0.5) - PIVOT_Y) / SCALE
        for x in range(CELL_WIDTH):
            source_x = anchor_x + ((x + 0.5) - PIVOT_X) / SCALE
            set_rgba(cell, CELL_WIDTH, x, y,
                     premultiplied_sample(frame, source_width, source_height, source_x, source_y))
    return cell


def paste_cell(
    atlas: bytearray,
    atlas_width: int,
    cell: bytearray,
    cell_width: int,
    cell_height: int,
    left: int,
    top: int,
) -> None:
    for y in range(cell_height):
        source_start = y * cell_width * 4
        target_start = ((top + y) * atlas_width + left) * 4
        atlas[target_start:target_start + cell_width * 4] = cell[source_start:source_start + cell_width * 4]


def extract_cell(
    strip: bytearray, strip_width: int, left: int, cell_width: int, cell_height: int
) -> bytearray:
    cell = bytearray(cell_width * cell_height * 4)
    for y in range(cell_height):
        source_start = (y * strip_width + left) * 4
        target_start = y * cell_width * 4
        cell[target_start:target_start + cell_width * 4] = strip[
            source_start:source_start + cell_width * 4]
    return cell


def build_bird() -> None:
    source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    if source_hash != EXPECTED_SOURCE_HASH:
        raise RuntimeError(f"Unexpected bird source hash: {source_hash}")
    width, height, pixels = read_rgba_png(SOURCE)
    components = alpha_components(width, height, pixels)
    atlas_width, atlas_height = CELL_WIDTH * COLUMNS, CELL_HEIGHT * ROWS
    atlas = bytearray(atlas_width * atlas_height * 4)
    frames = []
    for source_index, component in enumerate(components):
        landmark = component_landmark(component, width, pixels)
        cell = render_cell(width, height, cleaned_frame(width, height, pixels, component), landmark)
        column, row = source_index % COLUMNS, source_index // COLUMNS
        left, top = column * CELL_WIDTH, row * CELL_HEIGHT
        paste_cell(atlas, atlas_width, cell, CELL_WIDTH, CELL_HEIGHT, left, top)
        frames.append({
            "sourceOrderIndex": source_index,
            "rectangle": {"x": left, "y": top, "width": CELL_WIDTH, "height": CELL_HEIGHT},
        })

    write_rgba_png(OUTPUT, atlas_width, atlas_height, atlas)
    manifest = {
        "texture": OUTPUT.name,
        "cellWidth": CELL_WIDTH,
        "cellHeight": CELL_HEIGHT,
        "nativeFacing": "right",
        "filtering": "linear",
        "pivot": {
            "x": PIVOT_X,
            "y": PIVOT_Y,
            "space": "cellPixels",
            "semantic": "torso/body (derived from a fixed offset from the beak/head landmark)",
        },
        "atlas": {"width": atlas_width, "height": atlas_height, "columns": COLUMNS, "rows": ROWS},
        "source": {
            "path": "art/Source/Enemy/purple_raven_flight_sprite_sheet.png",
            "sha256": source_hash,
            "state": "approved 16-frame purple bird flight cycle, row-major source order",
        },
        "normalization": {
            "sourceScale": SCALE,
            "alphaCoreThreshold": ALPHA_CORE,
            "fringeRadiusPixels": FRINGE_RADIUS,
            "collisionNote": "Visible wings are presentation-only; do not derive gameplay collision from frame bounds.",
            "target720pHeightPixels": "approximately 80-95 at maximum/high-wing extension",
        },
        "animations": {"Flight": {"frames": frames, "fps": 12, "loop": True}},
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {OUTPUT.relative_to(ROOT)} ({atlas_width}x{atlas_height})")
    print(f"Wrote {MANIFEST.relative_to(ROOT)} ({len(frames)} frames)")


def build_player() -> None:
    canonical_manifest_hash = hashlib.sha256(PLAYER_CANONICAL_MANIFEST.read_bytes()).hexdigest()
    canonical_manifest = json.loads(PLAYER_CANONICAL_MANIFEST.read_text(encoding="utf-8"))
    if canonical_manifest.get("canonicalFormatVersion") != 1:
        raise RuntimeError("Unsupported canonical player manifest format")
    if canonical_manifest.get("cellSize") != {
            "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT}:
        raise RuntimeError("Canonical player manifest has the wrong cell size")
    if (canonical_manifest.get("nativeFacing") != "right"
            or canonical_manifest.get("xOrigin") != PLAYER_PIVOT_X
            or canonical_manifest.get("futureBuilderScale") != 1.0):
        raise RuntimeError("Canonical player manifest has an incompatible runtime contract")
    alpha_contract = canonical_manifest.get("alphaContract", {})
    if (alpha_contract.get("mode") != "straight RGBA"
            or alpha_contract.get("alphaZeroRgb") != [0, 0, 0]):
        raise RuntimeError("Canonical player manifest has an incompatible alpha contract")
    canonical_animations = canonical_manifest.get("animations", {})
    if set(canonical_animations) != set(PLAYER_ANIMATIONS):
        raise RuntimeError("Canonical player manifest has an unexpected animation set")

    loaded: dict[str, tuple[Path, str, bytearray, dict[str, object]]] = {}
    for animation, (filename, count, _row, _column, _fps, _loop) in PLAYER_ANIMATIONS.items():
        entry = canonical_animations[animation]
        expected_dimensions = (PLAYER_CELL_WIDTH * count, PLAYER_CELL_HEIGHT)
        if (entry.get("file") != filename
                or entry.get("frameCount") != count
                or entry.get("cellSize") != {
                    "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT}
                or entry.get("stripDimensions") != {
                    "width": expected_dimensions[0], "height": expected_dimensions[1]}
                or entry.get("futureBuilderScale") != 1.0
                or entry.get("runtimeScaleRequired") is not False):
            raise RuntimeError(f"Canonical {animation} manifest contract is invalid")
        source_path = PLAYER_CANONICAL_DIR / filename
        source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
        if source_hash != entry.get("sha256"):
            raise RuntimeError(f"Canonical {animation} source hash differs from its manifest")
        width, height, pixels = read_rgba_png(source_path)
        if (width, height) != expected_dimensions:
            raise RuntimeError(
                f"Canonical {animation} strip is {width}x{height}; "
                f"expected {expected_dimensions[0]}x{expected_dimensions[1]}")
        for index in range(width * height):
            red, green, blue, alpha = rgba_at(pixels, width, index % width, index // width)
            if alpha == 0 and (red, green, blue) != (0, 0, 0):
                raise RuntimeError(
                    f"Canonical {animation} has RGB in a transparent pixel at "
                    f"({index % width}, {index // width})")
        loaded[animation] = (source_path, source_hash, pixels, entry)

    atlas_width = PLAYER_CELL_WIDTH * PLAYER_COLUMNS
    atlas_height = PLAYER_CELL_HEIGHT * PLAYER_ROWS
    atlas = bytearray(atlas_width * atlas_height * 4)
    animations: dict[str, dict[str, object]] = {}
    for animation, (_filename, count, row, first_column, fps, loop) in PLAYER_ANIMATIONS.items():
        source_path, source_hash, strip, canonical_entry = loaded[animation]
        frames = []
        canonical_frames = canonical_entry.get("frames", [])
        if len(canonical_frames) != count:
            raise RuntimeError(f"Canonical {animation} frame provenance count is invalid")
        for frame_index in range(count):
            cell = extract_cell(
                strip, PLAYER_CELL_WIDTH * count, frame_index * PLAYER_CELL_WIDTH,
                PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT)
            column = first_column + frame_index
            left, top = column * PLAYER_CELL_WIDTH, row * PLAYER_CELL_HEIGHT
            paste_cell(atlas, atlas_width, cell, PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT, left, top)
            frame = {
                "sourceOrderIndex": frame_index,
                "source": str(source_path.relative_to(ROOT)),
                "canonicalCell": {
                    "x": frame_index * PLAYER_CELL_WIDTH, "y": 0,
                    "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT,
                },
                "rectangle": {"x": left, "y": top,
                              "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT},
            }
            canonical_frame = canonical_frames[frame_index]
            if canonical_frame.get("status") == "provisional":
                frame["status"] = "provisional"
                frame["provenance"] = {
                    "manifest": str(PLAYER_CANONICAL_MANIFEST.relative_to(ROOT)),
                    "animation": animation,
                    "frameIndex": frame_index,
                }
            frames.append(frame)
        animations[animation] = {
            "frames": frames,
            "frameCount": count,
            "fps": fps,
            "loop": loop,
            "canonicalSource": {
                "path": str(source_path.relative_to(ROOT)),
                "sha256": source_hash,
            },
            "anchor": canonical_entry["anchorType"],
            "nativeFacing": "right",
            "atlasPlacement": {
                "row": row, "firstColumn": first_column,
                "lastColumn": first_column + count - 1,
            },
        }
        if animation == "Roll":
            animations[animation]["status"] = "provisional; frame 4 requires host approval"

    write_rgba_png(PLAYER_OUTPUT, atlas_width, atlas_height, atlas)
    manifest = {
        "texture": PLAYER_OUTPUT.name,
        "cellWidth": PLAYER_CELL_WIDTH,
        "cellHeight": PLAYER_CELL_HEIGHT,
        "nativeFacing": "right",
        "filtering": "linear",
        "pivot": {"x": PLAYER_PIVOT_X, "y": PLAYER_PIVOT_Y, "space": "cellPixels",
                  "semantic": "normalized logical player/body pivot"},
        "anchors": {
            "groundBaseline": {"x": PLAYER_PIVOT_X, "y": PLAYER_GROUND_BASELINE},
            "body": {"x": PLAYER_PIVOT_X, "y": PLAYER_PIVOT_Y},
        },
        "atlas": {"width": atlas_width, "height": atlas_height,
                  "columns": PLAYER_COLUMNS, "rows": PLAYER_ROWS},
        "canonicalManifest": {
            "path": str(PLAYER_CANONICAL_MANIFEST.relative_to(ROOT)),
            "sha256": canonical_manifest_hash,
            "formatVersion": canonical_manifest["canonicalFormatVersion"],
        },
        "packing": {
            "method": "direct copy of canonical 192x176 RGBA cells",
            "runtimeBuilderScale": 1.0,
            "geometricResampling": False,
        },
        "alpha": {
            "alphaCoreThreshold": ALPHA_CORE,
            "fringeRadiusPixels": FRINGE_RADIUS,
            "minimumTransparentGutterPixels": PLAYER_MIN_GUTTER,
            "pngAlphaMode": "straight",
            "alphaZeroRgb": [0, 0, 0],
            "runtimeUploadAlphaMode": "premultiply with DefaultColorProcessors.PremultiplyAlpha",
        },
        "layout": "fixed grid; contiguous animation sequences; unused cells transparent",
        "animations": animations,
    }
    PLAYER_MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {PLAYER_OUTPUT.relative_to(ROOT)} ({atlas_width}x{atlas_height})")
    print(f"Wrote {PLAYER_MANIFEST.relative_to(ROOT)} ({sum(len(v['frames']) for v in animations.values())} frames)")


def main() -> None:
    build_bird()
    build_player()


if __name__ == "__main__":
    main()
