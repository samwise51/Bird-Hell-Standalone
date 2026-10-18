#!/usr/bin/env python3
"""Validate the canonical and runtime Milestone 17 cave material contract."""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image

from build_canonical_cave_material import (
    CANONICAL,
    EDGE_BAND,
    MANIFEST,
    OUTPUT_SIZE,
    ROOT,
    RUNTIME,
    SOURCE,
    SOURCE_CROP,
    SOURCE_SHA256,
    WORLD_REPEAT,
    digest,
    seam_metrics,
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> None:
    require(CANONICAL.exists(), "Canonical cave material is missing")
    require(RUNTIME.exists(), "Runtime cave material is missing")
    require(MANIFEST.exists(), "Canonical cave material manifest is missing")
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))

    require(manifest["formatVersion"] == 1, "Unexpected cave material manifest version")
    require(manifest["source"]["path"] == str(SOURCE.relative_to(ROOT)), "Wrong source path")
    require(manifest["source"]["sha256"] == SOURCE_SHA256 == digest(SOURCE), "Wrong source hash")
    require(manifest["source"]["crop"] == list(SOURCE_CROP), "Wrong source crop")
    require(manifest["output"]["dimensions"] == list(OUTPUT_SIZE), "Wrong output dimensions metadata")
    require(manifest["output"]["sha256"] == digest(CANONICAL), "Canonical PNG hash differs from manifest")
    require(digest(RUNTIME) == digest(CANONICAL), "Runtime cave material differs from canonical PNG")
    require(manifest["output"]["alphaMode"] == "opaque RGB", "Cave material must be opaque RGB")
    require(manifest["intendedFilter"] == "linear", "Cave material filter must be linear")
    require(manifest["intendedWrapMode"] == {"u": "wrap", "v": "wrap"}, "Both axes must wrap")
    require(manifest["intendedWorldRepeatSize"] == {
        "width": WORLD_REPEAT[0], "height": WORLD_REPEAT[1]}, "Wrong world repeat size")
    require(manifest["status"] == "production", "Cave material is not marked production")

    with Image.open(CANONICAL) as image:
        require(image.size == OUTPUT_SIZE, f"Expected {OUTPUT_SIZE}, found {image.size}")
        require(image.mode == "RGB", f"Expected opaque RGB PNG, found {image.mode}")
        metrics = seam_metrics(image, EDGE_BAND)

    require(metrics == manifest["edgeSeamMetrics"], "Measured seam metrics differ from manifest")
    for axis, values in metrics.items():
        require(values["meanRgbDifference"] <= 8.0, f"{axis} mean seam difference is too high")
        require(values["rmsRgbDifference"] <= 12.0, f"{axis} RMS seam difference is too high")
        require(values["maximumRepresentativeDifferenceP95"] <= 20.0,
                f"{axis} representative maximum seam difference is too high")

    expected_hash = "3c60b37fd6d1a9e700944ff79a69e280109309a7b27dedd048110f74abe90ddb"
    require(digest(CANONICAL) == expected_hash, "Canonical cave material does not match the approved hash")
    print(f"PASS: canonical/runtime cave material {OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]}, hash {expected_hash}")
    print(f"PASS: 8px-band seam metrics left/right={metrics['leftRight']}, top/bottom={metrics['topBottom']}")
    print("PASS: opaque RGB, linear filtering metadata, and U/V wrapping metadata")


if __name__ == "__main__":
    main()
