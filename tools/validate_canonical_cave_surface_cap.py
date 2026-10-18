#!/usr/bin/env python3
"""Validate the canonical/runtime Milestone 18 cave surface-cap contract."""

from __future__ import annotations

import json

from PIL import Image

from build_canonical_cave_surface_cap import (
    BACKGROUND_CUTOFF,
    CANONICAL,
    MANIFEST,
    OUTPUT_SIZE,
    ROOT,
    RUNTIME,
    SOURCE,
    SOURCE_CROP,
    SOURCE_SHA256,
    WORLD_OPEN_OVERHANG,
    WORLD_REPEAT_LENGTH,
    WORLD_SOLID_DEPTH,
    digest,
    seam_metrics,
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> None:
    require(CANONICAL.exists(), "Canonical cave surface cap is missing")
    require(RUNTIME.exists(), "Runtime cave surface cap is missing")
    require(MANIFEST.exists(), "Canonical cave surface-cap manifest is missing")
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    require(manifest["formatVersion"] == 1, "Unexpected surface-cap manifest version")
    require(manifest["source"]["path"] == str(SOURCE.relative_to(ROOT)), "Wrong source path")
    require(manifest["source"]["sha256"] == SOURCE_SHA256 == digest(SOURCE), "Wrong source hash")
    require(manifest["source"]["crop"] == list(SOURCE_CROP), "Wrong source crop")
    require(manifest["output"]["dimensions"] == list(OUTPUT_SIZE), "Wrong output dimensions metadata")
    require(manifest["output"]["sha256"] == digest(CANONICAL), "Canonical PNG hash differs from manifest")
    require(digest(RUNTIME) == digest(CANONICAL), "Runtime surface cap differs from canonical PNG")
    require(manifest["output"]["alphaMode"] == "premultiplied alpha recovered from black matte",
            "Surface cap must use the documented premultiplied-alpha contract")
    require(manifest["generation"]["backgroundCutoff"] == BACKGROUND_CUTOFF, "Wrong matte cutoff")
    require(manifest["intendedFilter"] == "linear", "Surface-cap filter must be linear")
    require(manifest["intendedWrapMode"] == {"u": "wrap", "v": "clamp"}, "Wrong wrap mode")
    require(manifest["presentation"] == {
        "worldRepeatLength": WORLD_REPEAT_LENGTH,
        "worldOpenOverhang": WORLD_OPEN_OVERHANG,
        "worldSolidDepth": WORLD_SOLID_DEPTH,
        "eligibleBoundary": "traversable-side normal y >= 0.35",
    }, "Wrong surface-cap placement metadata")
    require(manifest["status"] == "production", "Surface cap is not marked production")

    with Image.open(CANONICAL) as image:
        require(image.size == OUTPUT_SIZE, f"Expected {OUTPUT_SIZE}, found {image.size}")
        require(image.mode == "RGBA", f"Expected RGBA PNG, found {image.mode}")
        pixels = list(image.getdata())
        require(any(pixel[3] == 0 for pixel in pixels), "Surface cap has no transparent backdrop")
        require(any(pixel[3] == 255 for pixel in pixels), "Surface cap has no opaque paint")
        require(all(pixel[0] <= pixel[3] and pixel[1] <= pixel[3] and pixel[2] <= pixel[3]
                    for pixel in pixels), "Surface cap RGB exceeds alpha; texture is not premultiplied")
        metrics = seam_metrics(image)

    require(metrics == manifest["edgeSeamMetrics"], "Measured seam metrics differ from manifest")
    require(metrics["meanRgbaDifference"] <= 8.0, "Mean horizontal seam difference is too high")
    require(metrics["rmsRgbaDifference"] <= 12.0, "RMS horizontal seam difference is too high")
    require(metrics["maximumRepresentativeDifferenceP95"] <= 20.0,
            "Representative horizontal seam difference is too high")
    print(f"PASS: canonical/runtime cave surface cap {OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]}, hash {digest(CANONICAL)}")
    print(f"PASS: premultiplied alpha, linear filter, U-wrap/V-clamp, seam metrics {metrics}")


if __name__ == "__main__":
    main()
