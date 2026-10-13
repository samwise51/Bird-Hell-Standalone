#!/usr/bin/env python3
"""Build the normalized Milestone 13 player and bird runtime atlases."""

from __future__ import annotations

import hashlib
import json
import math
import statistics
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

PLAYER_MAIN = ROOT / "art/Source/Player/anime_cave_explorer_sprite_sheet.png"
PLAYER_ADDONS = ROOT / "art/Source/Player/player_anime_explorer_addons_v1.png"
PLAYER_ATTACK = ROOT / "art/Source/Player/player_anime_explorer_attack_v1.png"
PLAYER_OUTPUT = ROOT / "src/BirdHell/Content/Sprites/Player/player_explorer_runtime_v1.png"
PLAYER_MANIFEST = PLAYER_OUTPUT.with_suffix(".json")
PLAYER_SOURCE_HASHES = {
    PLAYER_MAIN: "16160c15685a5c40e689a6e0ed88706177e5f23dee982524c53458ce7bd38885",
    PLAYER_ADDONS: "ebc14cfdba7d070faed4f96962c6630871bb5db8441e3f54b0870cad2f7d175f",
    PLAYER_ATTACK: "89e5065058dacf1b6967f783e3939d628a5cd9dec6f0fc530ff13e242778f407",
}
PLAYER_CELL_WIDTH = 192
PLAYER_CELL_HEIGHT = 176
PLAYER_PIVOT_X = 96
PLAYER_PIVOT_Y = 88
PLAYER_GROUND_BASELINE = 160
PLAYER_RUN_SCALE_CORRECTION = 0.873
PLAYER_COLUMNS = 9
PLAYER_ROWS = 6
PLAYER_MIN_GUTTER = 4
PLAYER_REPLACEMENT_RUN_SOURCE = ROOT / "art/Source/Player/player_anime_explorer_run_v2.png"


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


def player_anchor(
    components: list[list[int]], width: int, pixels: bytearray, grounded: bool
) -> tuple[float, float]:
    if grounded:
        core = [index for component in components for index in component]
        bottom = max(index // width for index in core)
        body_x = []
        for index in core:
            red, green, blue, _ = rgba_at(pixels, width, index % width, index // width)
            if blue > 120 and blue > red * 1.25 and blue > green * 1.05:
                continue
            body_x.append(index % width)
        return float(statistics.median(body_x)), float(bottom)
    body = max(components, key=len)
    return (
        float(statistics.median(index % width for index in body)),
        float(statistics.median(index // width for index in body)),
    )


def render_player_cell(
    source_width: int,
    source_height: int,
    frame: bytearray,
    source_anchor: tuple[float, float],
    scale: float,
    grounded: bool,
) -> bytearray:
    target_y = PLAYER_GROUND_BASELINE if grounded else PLAYER_PIVOT_Y
    cell = bytearray(PLAYER_CELL_WIDTH * PLAYER_CELL_HEIGHT * 4)
    for y in range(PLAYER_CELL_HEIGHT):
        source_y = source_anchor[1] + ((y + 0.5) - target_y) / scale
        for x in range(PLAYER_CELL_WIDTH):
            source_x = source_anchor[0] + ((x + 0.5) - PLAYER_PIVOT_X) / scale
            set_rgba(cell, PLAYER_CELL_WIDTH, x, y,
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


def replacement_run_cells(width: int, height: int, strip: bytearray) -> list[bytearray]:
    expected_dimensions = (PLAYER_CELL_WIDTH * 8, PLAYER_CELL_HEIGHT)
    if (width, height) != expected_dimensions:
        raise RuntimeError(
            f"Replacement Run strip is {width}x{height}; "
            f"expected {expected_dimensions[0]}x{expected_dimensions[1]}")

    cells: list[bytearray] = []
    core_sizes: list[int] = []
    core_bottoms: list[int] = []
    for frame_index in range(8):
        cell = extract_cell(
            strip, width, frame_index * PLAYER_CELL_WIDTH,
            PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT)
        occupied: list[tuple[int, int]] = []
        core: list[int] = []
        for index in range(PLAYER_CELL_WIDTH * PLAYER_CELL_HEIGHT):
            y, x = divmod(index, PLAYER_CELL_WIDTH)
            red, green, blue, alpha = rgba_at(cell, PLAYER_CELL_WIDTH, x, y)
            if alpha == 0 and (red, green, blue) != (0, 0, 0):
                raise RuntimeError(
                    f"Replacement Run frame {frame_index} has RGB in a transparent pixel at ({x}, {y})")
            if alpha:
                occupied.append((x, y))
            if alpha >= ALPHA_CORE:
                core.append(index)
        if not occupied or not core:
            raise RuntimeError(f"Replacement Run frame {frame_index} is empty")
        min_x, max_x = min(x for x, _ in occupied), max(x for x, _ in occupied)
        min_y, max_y = min(y for _, y in occupied), max(y for _, y in occupied)
        if not (16 <= min_x <= max_x <= 175 and 16 <= min_y <= max_y <= 161):
            raise RuntimeError(
                f"Replacement Run frame {frame_index} visible bounds "
                f"({min_x}, {min_y})..({max_x}, {max_y}) exceed the approved envelope")
        margins = (min_x, PLAYER_CELL_WIDTH - 1 - max_x,
                   min_y, PLAYER_CELL_HEIGHT - 1 - max_y)
        if min(margins) < 14:
            raise RuntimeError(
                f"Replacement Run frame {frame_index} has only {min(margins)}px transparent gutter")
        core_sizes.append(len(core))
        core_bottoms.append(max(index // PLAYER_CELL_WIDTH for index in core))

        cleaned = cleaned_components(
            PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT, cell, [core])
        scaled = render_player_cell(
            PLAYER_CELL_WIDTH,
            PLAYER_CELL_HEIGHT,
            cleaned,
            (float(PLAYER_PIVOT_X), float(PLAYER_GROUND_BASELINE)),
            PLAYER_RUN_SCALE_CORRECTION,
            True,
        )

        # Bilinear sampling can create a one-pixel fringe outside the geometric
        # projection. Keep the runtime envelope centered on x=96 and aligned to
        # y=160, then normalize the newly sampled edge colors.
        scaled_bounds = (
            round(PLAYER_PIVOT_X + (min_x - PLAYER_PIVOT_X) * PLAYER_RUN_SCALE_CORRECTION),
            round(PLAYER_GROUND_BASELINE
                  + (min_y - PLAYER_GROUND_BASELINE) * PLAYER_RUN_SCALE_CORRECTION),
            round(PLAYER_PIVOT_X + (max_x - PLAYER_PIVOT_X) * PLAYER_RUN_SCALE_CORRECTION),
            round(PLAYER_GROUND_BASELINE
                  + (max_y - PLAYER_GROUND_BASELINE) * PLAYER_RUN_SCALE_CORRECTION),
        )
        if not (16 <= scaled_bounds[0] <= scaled_bounds[2] <= 175
                and 16 <= scaled_bounds[1] <= scaled_bounds[3] <= 161):
            raise RuntimeError(
                f"Scaled replacement Run frame {frame_index} bounds {scaled_bounds} "
                "exceed the approved envelope")
        scaled_margins = (
            scaled_bounds[0], PLAYER_CELL_WIDTH - 1 - scaled_bounds[2],
            scaled_bounds[1], PLAYER_CELL_HEIGHT - 1 - scaled_bounds[3],
        )
        if min(scaled_margins) < 14:
            raise RuntimeError(
                f"Scaled replacement Run frame {frame_index} has only "
                f"{min(scaled_margins)}px transparent gutter")
        for y in range(PLAYER_CELL_HEIGHT):
            for x in range(PLAYER_CELL_WIDTH):
                if not (scaled_bounds[0] <= x <= scaled_bounds[2]
                        and scaled_bounds[1] <= y <= scaled_bounds[3]):
                    set_rgba(scaled, PLAYER_CELL_WIDTH, x, y, (0, 0, 0, 0))
        scaled_core = [
            index for index in range(PLAYER_CELL_WIDTH * PLAYER_CELL_HEIGHT)
            if scaled[index * 4 + 3] >= ALPHA_CORE
        ]
        if not scaled_core:
            raise RuntimeError(f"Scaled replacement Run frame {frame_index} is empty")
        cells.append(cleaned_components(
            PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT, scaled, [scaled_core]))

    if max(core_sizes) / min(core_sizes) > 1.12:
        raise RuntimeError(f"Replacement Run character scale varies excessively: {core_sizes}")
    if core_bottoms[0] != PLAYER_GROUND_BASELINE or core_bottoms[4] != PLAYER_GROUND_BASELINE:
        raise RuntimeError(
            f"Replacement Run contact frames do not meet baseline {PLAYER_GROUND_BASELINE}: {core_bottoms}")
    return cells


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
    loaded: dict[Path, tuple[int, int, bytearray]] = {}
    hashes: dict[Path, str] = {}
    for path, expected_hash in PLAYER_SOURCE_HASHES.items():
        actual_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual_hash != expected_hash:
            raise RuntimeError(f"Unexpected player source hash for {path.name}: {actual_hash}")
        hashes[path] = actual_hash
        loaded[path] = read_rgba_png(path)

    replacement_run: tuple[int, int, bytearray] | None = None
    replacement_cells: list[bytearray] | None = None
    if PLAYER_REPLACEMENT_RUN_SOURCE.exists():
        replacement_run = read_rgba_png(PLAYER_REPLACEMENT_RUN_SOURCE)
        replacement_cells = replacement_run_cells(*replacement_run)
        hashes[PLAYER_REPLACEMENT_RUN_SOURCE] = hashlib.sha256(
            PLAYER_REPLACEMENT_RUN_SOURCE.read_bytes()).hexdigest()

    main_width, main_height, main_pixels = loaded[PLAYER_MAIN]
    main_components = substantial_components(main_width, main_height, main_pixels, 5_000)
    bounded = [(component, component_bounds(component, main_width)) for component in main_components]
    idle = sorted([item[0] for item in bounded if item[1][1] < 280 and item[1][3] <= 270],
                  key=lambda item: component_bounds(item, main_width)[0])
    run = sorted([item[0] for item in bounded if 280 <= item[1][1] < 508 and item[1][3] <= 507],
                 key=lambda item: component_bounds(item, main_width)[0])
    jump = sorted([item[0] for item in bounded if 508 <= item[1][1] < 700 and item[1][3] <= 730],
                  key=lambda item: component_bounds(item, main_width)[0])
    roll_parts = sorted([item[0] for item in bounded if 700 <= item[1][1] and item[1][3] <= 901],
                        key=lambda item: component_bounds(item, main_width)[0])
    if (len(idle), len(run), len(jump), len(roll_parts)) != (9, 8, 6, 7):
        raise RuntimeError(
            f"Unexpected main-sheet groups: idle={len(idle)}, run={len(run)}, "
            f"jump={len(jump)}, roll parts={len(roll_parts)}")
    original_roll = [[roll_parts[0]], [roll_parts[1]], [roll_parts[2]],
                     [roll_parts[3], roll_parts[4]], [roll_parts[6]]]

    addon_width, addon_height, addon_pixels = loaded[PLAYER_ADDONS]
    addons = sorted(substantial_components(addon_width, addon_height, addon_pixels, 10_000),
                    key=lambda item: component_bounds(item, addon_width)[0])
    if len(addons) != 3:
        raise RuntimeError(f"Expected crouch, airborne, and roll addon poses; found {len(addons)}")
    crouch, airborne, roll_replacement = addons

    attack_width, attack_height, attack_pixels = loaded[PLAYER_ATTACK]
    attack = sorted(substantial_components(attack_width, attack_height, attack_pixels, 5_000),
                    key=lambda item: component_bounds(item, attack_width)[0])
    if len(attack) != 7:
        raise RuntimeError(f"Expected seven isolated attack frames, found {len(attack)}")

    sources = {
        "main": (PLAYER_MAIN, main_width, main_height, main_pixels),
        "addons": (PLAYER_ADDONS, addon_width, addon_height, addon_pixels),
        "attack": (PLAYER_ATTACK, attack_width, attack_height, attack_pixels),
    }
    specs = {
        "Idle": [("main", [component], 0.50, True, index) for index, component in enumerate(idle)],
        "Run": [("main", [component], 0.50, True, index) for index, component in enumerate(run)],
        "Jump": [("main", [component], 0.50, False, index) for index, component in enumerate(jump)],
        "Airborne": [("addons", [airborne], 0.23, False, 1)],
        "Crouch": [("addons", [crouch], 0.25, True, 0)],
        "Roll": [
            ("main", original_roll[0], 0.50, False, 0),
            ("main", original_roll[1], 0.50, False, 1),
            ("main", original_roll[2], 0.50, False, 2),
            ("main", original_roll[3], 0.50, False, 3),
            ("addons", [roll_replacement], 0.25, False, 2),
            ("main", original_roll[4], 0.50, False, 5),
        ],
        "Attack": [("attack", [component], 0.29, True, index) for index, component in enumerate(attack)],
    }
    layout = {"Idle": (0, 0), "Run": (1, 0), "Jump": (2, 0),
              "Airborne": (3, 0), "Crouch": (3, 1), "Roll": (4, 0), "Attack": (5, 0)}
    timing = {
        "Idle": (7, True), "Run": (12, True), "Jump": (10, False),
        "Airborne": (0, True), "Crouch": (0, True), "Roll": (12, False), "Attack": (14, False),
    }
    source_states = {
        "Idle": "approved idle row",
        "Run": ("approved replacement run cycle" if replacement_run else
                "rejected Milestone 13 source material; retained in atlas only and not used by production"),
        "Jump": "approved first-jump/takeoff row",
        "Airborne": "dedicated center sustained-air pose", "Crouch": "dedicated left crouch pose",
        "Roll": "five valid original roll frames plus rightmost sword-free addon replacement",
        "Attack": "seven isolated poses from regenerated attack source",
    }

    atlas_width = PLAYER_CELL_WIDTH * PLAYER_COLUMNS
    atlas_height = PLAYER_CELL_HEIGHT * PLAYER_ROWS
    atlas = bytearray(atlas_width * atlas_height * 4)
    animations: dict[str, dict[str, object]] = {}
    for animation, frame_specs in specs.items():
        row, first_column = layout[animation]
        frames = []
        for frame_index, (source_key, components, scale, grounded, source_index) in enumerate(frame_specs):
            if animation == "Run" and replacement_cells:
                source_path = PLAYER_REPLACEMENT_RUN_SOURCE
                cell = replacement_cells[frame_index]
                source_index = frame_index
            else:
                source_path, width, height, pixels = sources[source_key]
                cleaned = cleaned_components(width, height, pixels, components)
                anchor = player_anchor(components, width, pixels, grounded)
                cell = render_player_cell(width, height, cleaned, anchor, scale, grounded)
            column = first_column + frame_index
            left, top = column * PLAYER_CELL_WIDTH, row * PLAYER_CELL_HEIGHT
            paste_cell(atlas, atlas_width, cell, PLAYER_CELL_WIDTH, PLAYER_CELL_HEIGHT, left, top)
            frames.append({
                "sourceOrderIndex": source_index,
                "source": str(source_path.relative_to(ROOT)),
                "rectangle": {"x": left, "y": top,
                              "width": PLAYER_CELL_WIDTH, "height": PLAYER_CELL_HEIGHT},
            })
        fps, loop = timing[animation]
        animations[animation] = {
            "frames": frames,
            "fps": fps,
            "loop": loop,
            "sourceState": source_states[animation],
            "anchor": "groundBaseline" if frame_specs[0][3] else "body",
        }
        if animation == "Run" and replacement_run:
            animations[animation]["status"] = "approved replacement run cycle"
            animations[animation]["nativeFacing"] = "right"

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
        "sources": [{"path": str(path.relative_to(ROOT)), "sha256": hashes[path]}
                    for path in (PLAYER_MAIN, PLAYER_ADDONS, PLAYER_ATTACK,
                                 *([PLAYER_REPLACEMENT_RUN_SOURCE] if replacement_run else []))],
        "normalization": {
            "alphaCoreThreshold": ALPHA_CORE,
            "fringeRadiusPixels": FRINGE_RADIUS,
            "minimumTransparentGutterPixels": PLAYER_MIN_GUTTER,
            "pngAlphaMode": "straight",
            "runtimeUploadAlphaMode": "premultiply with DefaultColorProcessors.PremultiplyAlpha",
            "layout": "fixed grid; contiguous animation sequences; unused cells transparent",
            "integrationStatus": ("production; replacement Run row approved" if replacement_run else
                                  "production; rejected Run row isolated behind stable Idle-frame fallback"),
        },
        "replacementRun": {
            "source": str(PLAYER_REPLACEMENT_RUN_SOURCE.relative_to(ROOT)),
            "targetRuntime": str(PLAYER_OUTPUT.relative_to(ROOT)),
            "frameCount": 8,
            "frameOrder": [
                "right contact", "right recoil", "right passing", "right airborne",
                "left contact", "left recoil", "left passing", "left airborne",
            ],
            "nativeFacing": "right",
            "cellWidth": PLAYER_CELL_WIDTH,
            "cellHeight": PLAYER_CELL_HEIGHT,
            "stripWidth": PLAYER_CELL_WIDTH * 8,
            "stripHeight": PLAYER_CELL_HEIGHT,
            "pivot": {"x": PLAYER_PIVOT_X, "y": PLAYER_PIVOT_Y},
            "groundBaselineY": PLAYER_GROUND_BASELINE,
            "runtimeScaleCorrection": PLAYER_RUN_SCALE_CORRECTION,
            "runtimeScaleReason": "visual-scale parity with Idle after host-test correction",
            "runtimeScaleOrigin": {"x": PLAYER_PIVOT_X, "y": PLAYER_GROUND_BASELINE},
            "runtimeScaleAlignment": "centered horizontally and bottom-aligned",
            "fps": 12,
            "loop": True,
            "atlasPlacement": {"row": 1, "firstColumn": 0, "lastColumn": 7},
            "minimumTransparentGutterPixels": 14,
            "maximumVisibleBoundsInclusive": {"left": 16, "top": 16, "right": 175, "bottom": 161},
            "alphaMode": "straight RGBA; zero RGB at alpha 0; no matte contamination; premultiplied at runtime upload",
            "status": "approved replacement run cycle" if replacement_run else "awaiting source strip",
        },
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
