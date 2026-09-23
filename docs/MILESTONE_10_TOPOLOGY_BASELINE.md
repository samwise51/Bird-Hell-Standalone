# Milestone 10 topology baseline

Measured on the unchanged `128 × 30`, 35% fill, five-pass, 50/50 threshold,
radius-5 generator. The fixed evaluation corpus is `m10-quality-0000` through
`m10-quality-0255`; the smoke corpus is `m10-smoke-0000` through
`m10-smoke-0015`.

## Evaluation corpus (256 seeds)

| Metric | Min | P10 | Median | P90 | Max | Mean |
|---|---:|---:|---:|---:|---:|---:|
| Open space (%) | 76.2500 | 79.6354 | 82.0573 | 83.8542 | 85.7552 | 81.8691 |
| Main logical share (%) | 100 | 100 | 100 | 100 | 100 | 100 |
| Main horizontal coverage (%) | 95.3125 | 97.6562 | 98.4375 | 98.4375 | 98.4375 | 98.0896 |
| Main vertical coverage (%) | 93.3333 | 93.3333 | 93.3333 | 93.3333 | 93.3333 | 93.3333 |
| Floor horizontal width | 1.0000 | 1.0000 | 3.0000 | 87.0000 | 126.0000 | 21.2028 |
| Floor surface length | 1.4142 | 1.4142 | 3.8284 | 100.5980 | 148.3675 | 24.6672 |
| Grounded landing sites | 387 | 413 | 436 | 458 | 501 | 435.5352 |
| Occupied landing elevation bands | 2 | 4 | 5 | 6 | 7 | 5.0352 |
| Landing elevation span | 4.0298 | 15.6034 | 22.0000 | 24.6640 | 25.8408 | 20.8030 |
| Player-clear logical tile centers | 2672 | 2793 | 2891 | 2956 | 3033 | 2880.7422 |
| Player-clear navigation cells | 2637 | 2767 | 2860 | 2923 | 3004 | 2849.7383 |
| Enemy-clear navigation cells | 2637 | 2767 | 2860 | 2923 | 3004 | 2849.7383 |
| Navigation components | 1 | 1 | 1 | 1 | 2 | 1.0039 |
| Approximate bottleneck cells | 64 | 75 | 86 | 99 | 121 | 87.3867 |

All 256 seeds pass the default non-negotiable geometric checks. One outlier,
`m10-quality-0117`, has two navigation components containing 2820 and 6 cells;
its logical open space remains one connected component. This is a grid-clearance
disconnection, not proof that player jump traversal is impossible.

## Smoke corpus (16 seeds)

- Open space: 80.2604–85.1823%, mean 82.6286%
- Main logical share: 100% for every seed
- Grounded landing sites: 421–475, mean 439.5
- Occupied landing elevation bands: 3–7, mean 4.875
- Player-clear logical tile centers: 2820–3009
- Enemy-clear navigation cells: 2788–2970
- Navigation components: exactly one for every smoke seed

## Provisional opt-in thresholds

These are calibration guardrails, not default rejection rules:

- Open space: 75–87%
- Main logical component share: at least 99%
- Main horizontal coverage: at least 95%
- Main vertical coverage: at least 90%
- Grounded landing sites: at least 350
- Player-clear logical tile centers: at least 2500
- Enemy-clear navigation cells: at least 2500

No evaluation-corpus seed fails this provisional set. Thresholds should remain
configurable until richer generation parameters and authored shelves are measured.

Approximate bottlenecks are navigation cells with 2–4 valid neighbors and no
more than 1.5 world units of boundary clearance. They are a grid-local quality
signal, not articulation points and not proof of jump reachability.
