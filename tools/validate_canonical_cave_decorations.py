#!/usr/bin/env python3
"""Validate canonical/runtime Milestone 19 cave decoration assets."""
import json
from PIL import Image
from build_canonical_cave_decorations import ASSETS, BACKGROUND_CUTOFF, CANONICAL_DIR, MANIFEST, RUNTIME_DIR, SOURCE, SOURCE_SHA256, digest, seam_metrics

def require(condition: bool, message: str) -> None:
    if not condition: raise RuntimeError(message)

def main() -> None:
    require(MANIFEST.exists(), "Cave decoration manifest is missing")
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    require(manifest["formatVersion"] == 1, "Unexpected manifest version")
    require(manifest["source"]["sha256"] == SOURCE_SHA256 == digest(SOURCE), "Wrong source hash")
    require(manifest["generation"]["backgroundCutoff"] == BACKGROUND_CUTOFF, "Wrong matte cutoff")
    records = {record["id"]: record for record in manifest["assets"]}
    require(set(records) == set(ASSETS), "Manifest asset set differs from builder asset set")
    for asset_id, spec in ASSETS.items():
        record = records[asset_id]; canonical = CANONICAL_DIR / spec["name"]; runtime = RUNTIME_DIR / spec["name"]
        require(record["sourceCrop"] == list(spec["crop"]), f"Wrong {asset_id} crop")
        require(record["dimensions"] == list(spec["size"]), f"Wrong {asset_id} dimensions")
        require(record["sha256"] == digest(canonical) == digest(runtime), f"Wrong {asset_id} digest")
        require(record["status"] == "production", f"{asset_id} is not production")
        with Image.open(canonical) as image:
            require(image.mode == "RGBA" and image.size == spec["size"], f"Wrong {asset_id} PNG contract")
            pixels = list(image.getdata())
            require(any(p[3] == 0 for p in pixels) and any(p[3] == 255 for p in pixels), f"Wrong {asset_id} alpha range")
            require(all(max(p[:3]) <= p[3] for p in pixels), f"{asset_id} is not premultiplied")
            require(all(p[:3] == (0, 0, 0) for p in pixels if p[3] == 0), f"{asset_id} has unsafe alpha-zero RGB")
            if asset_id != "boulder":
                metrics = seam_metrics(image)
                require(metrics == record["edgeSeamMetrics"], f"{asset_id} seam metrics changed")
                require(metrics["meanRgbaDifference"] <= 8 and metrics["rmsRgbaDifference"] <= 12 and metrics["maximumRepresentativeDifferenceP95"] <= 20,
                    f"{asset_id} seam difference is too high")
        print(f"PASS: {asset_id} canonical/runtime asset {spec['size'][0]}x{spec['size'][1]}, hash {record['sha256']}")

if __name__ == "__main__": main()
