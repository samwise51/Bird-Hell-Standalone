#!/usr/bin/env python3
"""Deterministic validation for complete Milestone 13 runtime assets."""

from __future__ import annotations

import json
import statistics
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from clean_environment_edges import read_rgba_png, rgba_at
from build_m13_runtime_assets import (
    ALPHA_CORE,
    FRINGE_RADIUS,
    PLAYER_CELL_HEIGHT,
    PLAYER_GROUND_BASELINE,
    PLAYER_PIVOT_X,
    PLAYER_RUN_SCALE_CORRECTION,
    PLAYER_CELL_WIDTH,
    replacement_run_cells,
)


ROOT = Path(__file__).resolve().parents[1]
PLAYER_MIN_GUTTER = 4


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def intersects(first: dict[str, int], second: dict[str, int]) -> bool:
    return not (
        first["x"] + first["width"] <= second["x"]
        or second["x"] + second["width"] <= first["x"]
        or first["y"] + first["height"] <= second["y"]
        or second["y"] + second["height"] <= first["y"]
    )


def validate_bird() -> None:
    texture = ROOT / "src/BirdHell/Content/Sprites/Enemy/enemy_bird_purple_flight_runtime_v1.png"
    manifest_path = texture.with_suffix(".json")
    require(texture.exists(), f"Missing bird atlas: {texture}")
    require(manifest_path.exists(), f"Missing bird manifest: {manifest_path}")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    width, height, pixels = read_rgba_png(texture)
    require((width, height) == (manifest["atlas"]["width"], manifest["atlas"]["height"]),
            "Bird atlas dimensions do not match its manifest")
    require(manifest["nativeFacing"] == "right", "Bird native facing must be right")
    require(manifest["filtering"] == "linear", "Bird filtering metadata must be linear")
    flight = manifest["animations"]["Flight"]
    frames = flight["frames"]
    require(len(frames) == 16, f"Expected 16 bird frames, found {len(frames)}")
    require(flight["fps"] == 12 and flight["loop"] is True, "Bird timing metadata is incorrect")
    require([frame["sourceOrderIndex"] for frame in frames] == list(range(16)),
            "Bird source-order indices are not contiguous row-major values")
    rectangles = [frame["rectangle"] for frame in frames]
    for index, rectangle in enumerate(rectangles):
        require(rectangle["width"] == manifest["cellWidth"] and rectangle["height"] == manifest["cellHeight"],
                f"Bird frame {index} does not use the fixed cell size")
        require(rectangle["x"] >= 0 and rectangle["y"] >= 0
                and rectangle["x"] + rectangle["width"] <= width
                and rectangle["y"] + rectangle["height"] <= height,
                f"Bird frame {index} is outside the atlas")
        for earlier in rectangles[:index]:
            require(not intersects(rectangle, earlier), f"Bird frame {index} overlaps an earlier cell")

        occupied: list[tuple[int, int]] = []
        for y in range(rectangle["height"]):
            for x in range(rectangle["width"]):
                red, green, blue, alpha = rgba_at(
                    pixels, width, rectangle["x"] + x, rectangle["y"] + y)
                require(alpha != 0 or (red, green, blue) == (0, 0, 0),
                        f"Bird frame {index} has RGB contamination in a transparent pixel")
                if alpha:
                    occupied.append((x, y))
        require(occupied, f"Bird frame {index} is empty")
        min_x, max_x = min(x for x, _ in occupied), max(x for x, _ in occupied)
        min_y, max_y = min(y for _, y in occupied), max(y for _, y in occupied)
        require(min_x > 0 and min_y > 0 and max_x < rectangle["width"] - 1
                and max_y < rectangle["height"] - 1,
                f"Bird frame {index} clips or lacks transparent edge padding")

        substantial = {(x, y) for x, y in occupied if rgba_at(
            pixels, width, rectangle["x"] + x, rectangle["y"] + y)[3] >= 32}
        for x, y in occupied:
            alpha = rgba_at(pixels, width, rectangle["x"] + x, rectangle["y"] + y)[3]
            if alpha >= 32:
                continue
            require(any((nx, ny) in substantial
                        for ny in range(max(0, y - 2), min(rectangle["height"], y + 3))
                        for nx in range(max(0, x - 2), min(rectangle["width"], x + 3))),
                    f"Bird frame {index} contains isolated low-alpha fringe at ({x}, {y})")

    maximum_height = max(
        max(y for _, y in [(x, y) for y in range(rectangle["height"])
                           for x in range(rectangle["width"])
                           if rgba_at(pixels, width, rectangle["x"] + x, rectangle["y"] + y)[3]])
        - min(y for _, y in [(x, y) for y in range(rectangle["height"])
                             for x in range(rectangle["width"])
                             if rgba_at(pixels, width, rectangle["x"] + x, rectangle["y"] + y)[3]]) + 1
        for rectangle in rectangles
    )
    require(80 <= maximum_height <= 95, f"Bird maximum visual height is {maximum_height}px, expected 80-95px")
    print(f"PASS: bird atlas/manifest (16 frames, {width}x{height}, max visual height {maximum_height}px)")


def validate_player() -> None:
    texture = ROOT / "src/BirdHell/Content/Sprites/Player/player_explorer_runtime_v1.png"
    manifest_path = texture.with_suffix(".json")
    require(texture.exists(), f"Missing player atlas: {texture}")
    require(manifest_path.exists(), f"Missing player manifest: {manifest_path}")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    width, height, pixels = read_rgba_png(texture)
    require((width, height) == (manifest["atlas"]["width"], manifest["atlas"]["height"]),
            "Player atlas dimensions do not match its manifest")
    require(manifest["nativeFacing"] == "right", "Player native facing must be right")
    require(manifest["filtering"] == "linear", "Player filtering metadata must be linear")
    require(manifest["pivot"]["x"] == PLAYER_PIVOT_X and manifest["pivot"]["y"] == 88
            and manifest["anchors"]["groundBaseline"]
            == {"x": PLAYER_PIVOT_X, "y": PLAYER_GROUND_BASELINE},
            "Player pivot or grounded baseline metadata changed")
    normalization = manifest["normalization"]
    require(normalization["pngAlphaMode"] == "straight"
            and normalization["runtimeUploadAlphaMode"]
            == "premultiply with DefaultColorProcessors.PremultiplyAlpha",
            "Player straight-alpha PNG / premultiplied runtime upload contract is missing")
    presentation_source = (ROOT / "src/BirdHell/Presentation/SpritePresentation.cs").read_text(encoding="utf-8")
    require("textureId == BirdHellVisuals.PlayerTexture" in presentation_source
            and "DefaultColorProcessors.PremultiplyAlpha" in presentation_source,
            "Player texture upload does not apply the manifest's premultiplication contract")
    replacement = manifest["replacementRun"]
    require(replacement["source"] == "art/Source/Player/player_anime_explorer_run_v2.png"
            and replacement["frameCount"] == 8
            and (replacement["cellWidth"], replacement["cellHeight"]) == (192, 176)
            and replacement["atlasPlacement"] == {"row": 1, "firstColumn": 0, "lastColumn": 7}
            and replacement["runtimeScaleCorrection"] == PLAYER_RUN_SCALE_CORRECTION
            and replacement["runtimeScaleReason"]
            == "visual-scale parity with Idle after host-test correction"
            and replacement["runtimeScaleOrigin"]
            == {"x": PLAYER_PIVOT_X, "y": PLAYER_GROUND_BASELINE},
            "Replacement Run integration target is incorrect")
    replacement_source = ROOT / replacement["source"]
    require(replacement_source.exists(), "Replacement Run source strip is missing")
    source_width, source_height, source_pixels = read_rgba_png(replacement_source)
    cleaned_run_cells = replacement_run_cells(source_width, source_height, source_pixels)
    expected = {
        "Idle": (9, 7, True), "Run": (8, 12, True), "Jump": (6, 10, False),
        "Airborne": (1, 0, True), "Crouch": (1, 0, True), "Roll": (6, 12, False),
        "Attack": (7, 14, False),
    }
    require(set(manifest["animations"]) == set(expected), "Player manifest animation set is incorrect")
    rectangles: list[dict[str, int]] = []
    grounded_bottoms: dict[str, list[int]] = {}
    visual_bounds: dict[str, list[tuple[int, int, int, int]]] = {}
    core_heights: dict[str, list[int]] = {}
    core_areas: dict[str, list[int]] = {}
    for animation, (frame_count, fps, loop) in expected.items():
        entry = manifest["animations"][animation]
        require(len(entry["frames"]) == frame_count,
                f"Player {animation} expected {frame_count} frames, found {len(entry['frames'])}")
        require(entry["fps"] == fps and entry["loop"] is loop,
                f"Player {animation} timing metadata is incorrect")
        for frame_index, frame in enumerate(entry["frames"]):
            rectangle = frame["rectangle"]
            require(rectangle["width"] == manifest["cellWidth"]
                    and rectangle["height"] == manifest["cellHeight"],
                    f"Player {animation} frame {frame_index} does not use the fixed cell size")
            require(rectangle["x"] >= 0 and rectangle["y"] >= 0
                    and rectangle["x"] + rectangle["width"] <= width
                    and rectangle["y"] + rectangle["height"] <= height,
                    f"Player {animation} frame {frame_index} is outside the atlas")
            for earlier in rectangles:
                require(not intersects(rectangle, earlier),
                        f"Player {animation} frame {frame_index} overlaps another frame cell")
            rectangles.append(rectangle)

            occupied: list[tuple[int, int]] = []
            substantial: set[tuple[int, int]] = set()
            core: list[tuple[int, int]] = []
            for y in range(rectangle["height"]):
                for x in range(rectangle["width"]):
                    red, green, blue, alpha = rgba_at(
                        pixels, width, rectangle["x"] + x, rectangle["y"] + y)
                    require(alpha != 0 or (red, green, blue) == (0, 0, 0),
                            f"Player {animation} frame {frame_index} has RGB in a transparent pixel")
                    if alpha:
                        occupied.append((x, y))
                    if alpha >= 32:
                        substantial.add((x, y))
                    if alpha >= ALPHA_CORE:
                        core.append((x, y))
            require(occupied, f"Player {animation} frame {frame_index} is empty")
            min_x, max_x = min(x for x, _ in occupied), max(x for x, _ in occupied)
            min_y, max_y = min(y for _, y in occupied), max(y for _, y in occupied)
            visual_bounds.setdefault(animation, []).append((min_x, min_y, max_x, max_y))
            require(core, f"Player {animation} frame {frame_index} has no core pixels")
            core_heights.setdefault(animation, []).append(
                max(y for _, y in core) - min(y for _, y in core) + 1)
            core_areas.setdefault(animation, []).append(len(core))
            require(min_x > 0 and min_y > 0 and max_x < rectangle["width"] - 1
                    and max_y < rectangle["height"] - 1,
                    f"Player {animation} frame {frame_index} clips or lacks transparent padding")
            margins = (min_x, rectangle["width"] - 1 - max_x,
                       min_y, rectangle["height"] - 1 - max_y)
            require(min(margins) >= PLAYER_MIN_GUTTER,
                    f"Player {animation} frame {frame_index} has only {min(margins)}px gutter")
            for x, y in occupied:
                red, green, blue, alpha = rgba_at(
                    pixels, width, rectangle["x"] + x, rectangle["y"] + y)
                premultiplied = tuple(round(channel * alpha / 255) for channel in (red, green, blue))
                require(max(premultiplied) <= alpha,
                        f"Player {animation} frame {frame_index} cannot be premultiplied safely at ({x}, {y})")
                if alpha >= 32:
                    continue
                require(any((nx, ny) in substantial
                            for ny in range(max(0, y - FRINGE_RADIUS),
                                            min(rectangle["height"], y + FRINGE_RADIUS + 1))
                            for nx in range(max(0, x - FRINGE_RADIUS),
                                            min(rectangle["width"], x + FRINGE_RADIUS + 1))),
                        f"Player {animation} frame {frame_index} has isolated low-alpha fringe at ({x}, {y})")
                if animation == "Run":
                    require(any(
                        rgba_at(pixels, width, rectangle["x"] + nx, rectangle["y"] + ny)[3] >= 128
                        and rgba_at(pixels, width, rectangle["x"] + nx, rectangle["y"] + ny)[:3]
                        == (red, green, blue)
                        for ny in range(max(0, y - FRINGE_RADIUS),
                                        min(rectangle["height"], y + FRINGE_RADIUS + 1))
                        for nx in range(max(0, x - FRINGE_RADIUS),
                                        min(rectangle["width"], x + FRINGE_RADIUS + 1))),
                        f"Player Run frame {frame_index} has unmatched low-alpha edge RGB at ({x}, {y})")
            if entry["anchor"] == "groundBaseline" and (animation != "Run" or frame_index in (0, 4)):
                grounded_bottoms.setdefault(animation, []).append(max_y)

    all_grounded = [bottom for values in grounded_bottoms.values() for bottom in values]
    require(max(all_grounded) - min(all_grounded) <= 1,
            f"Grounded player baselines vary: {grounded_bottoms}")
    require(grounded_bottoms["Run"] == [PLAYER_GROUND_BASELINE, PLAYER_GROUND_BASELINE],
            f"Run contact frames do not meet baseline {PLAYER_GROUND_BASELINE}: "
            f"{grounded_bottoms['Run']}")
    idle_core_height = statistics.median(core_heights["Idle"])
    run_core_height = statistics.median(core_heights["Run"])
    idle_alpha_height = statistics.median(
        bottom - top + 1 for _, top, _, bottom in visual_bounds["Idle"])
    require(idle_core_height == 120 and min(core_heights["Idle"]) == 119
            and max(core_heights["Idle"]) == 121 and idle_alpha_height == 123,
            "Idle visual reference no longer matches the measured 119-121px core / 123px alpha bounds")
    require(run_core_height == 120,
            f"Scaled Run median core height is {run_core_height}px; expected 120px")
    idle_core_area = statistics.median(core_areas["Idle"])
    run_core_area = statistics.median(core_areas["Run"])
    require(1.18 <= run_core_area / idle_core_area <= 1.23,
            f"Scaled Run/Idle median core-area ratio is {run_core_area / idle_core_area:.6f}")
    recommended_run_bounds = [
        (47, 43, 144, 160), (53, 39, 138, 158),
        (52, 34, 140, 157), (51, 35, 140, 151),
        (49, 40, 142, 160), (52, 37, 139, 158),
        (51, 34, 140, 157), (51, 37, 140, 151),
    ]
    for frame_index, (actual, recommended) in enumerate(
            zip(visual_bounds["Run"], recommended_run_bounds)):
        require(all(abs(value - target) <= 1 for value, target in zip(actual, recommended)),
                f"Scaled Run frame {frame_index} bounds {actual} differ from {recommended}")
    run_envelope = (
        min(bounds[0] for bounds in visual_bounds["Run"]),
        min(bounds[1] for bounds in visual_bounds["Run"]),
        max(bounds[2] for bounds in visual_bounds["Run"]),
        max(bounds[3] for bounds in visual_bounds["Run"]),
    )
    require(run_envelope == (47, 34, 144, 160),
            f"Scaled Run aggregate envelope is {run_envelope}; expected (47, 34, 144, 160)")
    roll = manifest["animations"]["Roll"]["frames"]
    require(not any(frame["source"].endswith("anime_cave_explorer_sprite_sheet.png")
                    and frame["sourceOrderIndex"] == 4 for frame in roll),
            "Roll still references the sword-bearing original frame")
    require(any(frame["source"].endswith("player_anime_explorer_addons_v1.png")
                and frame["sourceOrderIndex"] == 2 for frame in roll),
            "Roll does not reference the sword-free addon replacement")
    attack = manifest["animations"]["Attack"]["frames"]
    require([frame["sourceOrderIndex"] for frame in attack] == list(range(7)),
            "Attack source-order indices are not 0-6")
    run_state = manifest["animations"]["Run"]["sourceState"]
    if replacement_source.exists():
        run_entry = manifest["animations"]["Run"]
        require(run_state == "approved replacement run cycle"
                and run_entry["status"] == "approved replacement run cycle"
                and run_entry["nativeFacing"] == "right"
                and run_entry["anchor"] == "groundBaseline"
                and replacement["status"] == "approved replacement run cycle"
                and all(frame["source"] == replacement["source"] for frame in manifest["animations"]["Run"]["frames"]),
                "Present replacement Run strip was not integrated")
        for frame_index, (frame, expected_cell) in enumerate(zip(run_entry["frames"], cleaned_run_cells)):
            rectangle = frame["rectangle"]
            for y in range(PLAYER_CELL_HEIGHT):
                for x in range(PLAYER_CELL_WIDTH):
                    actual = rgba_at(pixels, width, rectangle["x"] + x, rectangle["y"] + y)
                    expected_pixel = rgba_at(expected_cell, PLAYER_CELL_WIDTH, x, y)
                    require(actual == expected_pixel,
                            f"Packed Run frame {frame_index} differs from cleaned source at ({x}, {y})")
    else:
        require(run_state.startswith("rejected") and replacement["status"] == "awaiting source strip",
                "Rejected Run source material is not explicitly isolated in the manifest")
    used = {(rectangle["x"] // manifest["cellWidth"], rectangle["y"] // manifest["cellHeight"])
            for rectangle in rectangles}
    for row in range(manifest["atlas"]["rows"]):
        for column in range(manifest["atlas"]["columns"]):
            if (column, row) in used:
                continue
            for y in range(row * manifest["cellHeight"], (row + 1) * manifest["cellHeight"]):
                for x in range(column * manifest["cellWidth"], (column + 1) * manifest["cellWidth"]):
                    require(rgba_at(pixels, width, x, y) == (0, 0, 0, 0),
                            f"Unused player cell ({column}, {row}) is not transparent")
    print(f"PASS: player atlas/manifest (38 frames, {width}x{height}, "
          f"baseline {PLAYER_GROUND_BASELINE}px, Run envelope {run_envelope}, "
          f"Run median core height {run_core_height}px)")


if __name__ == "__main__":
    validate_player()
    validate_bird()
