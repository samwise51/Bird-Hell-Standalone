#!/usr/bin/env python3
"""Deterministic validation for complete Milestone 13 runtime assets."""

from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from clean_environment_edges import read_rgba_png, rgba_at


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
            and replacement["atlasPlacement"] == {"row": 1, "firstColumn": 0, "lastColumn": 7},
            "Replacement Run integration target is incorrect")
    expected = {
        "Idle": (9, 7, True), "Run": (8, 12, True), "Jump": (6, 10, False),
        "Airborne": (1, 0, True), "Crouch": (1, 0, True), "Roll": (6, 12, False),
        "Attack": (7, 14, False),
    }
    require(set(manifest["animations"]) == set(expected), "Player manifest animation set is incorrect")
    rectangles: list[dict[str, int]] = []
    grounded_bottoms: dict[str, list[int]] = {}
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
            require(occupied, f"Player {animation} frame {frame_index} is empty")
            min_x, max_x = min(x for x, _ in occupied), max(x for x, _ in occupied)
            min_y, max_y = min(y for _, y in occupied), max(y for _, y in occupied)
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
                            for ny in range(max(0, y - 2), min(rectangle["height"], y + 3))
                            for nx in range(max(0, x - 2), min(rectangle["width"], x + 3))),
                        f"Player {animation} frame {frame_index} has isolated low-alpha fringe at ({x}, {y})")
            if entry["anchor"] == "groundBaseline":
                grounded_bottoms.setdefault(animation, []).append(max_y)

    all_grounded = [bottom for values in grounded_bottoms.values() for bottom in values]
    require(max(all_grounded) - min(all_grounded) <= 1,
            f"Grounded player baselines vary: {grounded_bottoms}")
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
    replacement_source = ROOT / replacement["source"]
    run_state = manifest["animations"]["Run"]["sourceState"]
    if replacement_source.exists():
        require(run_state == "approved dedicated replacement strip"
                and all(frame["source"] == replacement["source"] for frame in manifest["animations"]["Run"]["frames"]),
                "Present replacement Run strip was not integrated")
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
    print(f"PASS: player atlas/manifest (38 frames, {width}x{height}, baseline {all_grounded[0]}px)")


if __name__ == "__main__":
    validate_player()
    validate_bird()
