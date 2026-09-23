# Milestone 10G generation comparison

Generated from the fixed `m10-quality-0000` through `m10-quality-0255` corpus.
Every profile is evaluated once per seed with no retry or filtering; failures remain in the CSV and the failure table below.

| Profile | Fill | Smooth | Wall threshold | Room threshold | Passage radius | Successful pipelines |
|---|---:|---:|---:|---:|---:|---:|
| Baseline | 35% | 5 | 50 | 50 | 5 | 256/256 |
| Conservative irregular | 38% | 4 | 40 | 50 | 4 | 256/256 |
| Dense | 42% | 4 | 40 | 50 | 4 | 256/256 |
| Complex | 45% | 3 | 35 | 50 | 3 | 256/256 |
| Extreme | 48% | 3 | 35 | 50 | 3 | 256/256 |

Parameter semantics are the existing generator semantics: initial interior wall probability, count of in-place cellular smoothing passes, minimum retained wall-region size, minimum retained open-room size, and circular room-connection carving radius.

## Aggregate medians

| Metric | Baseline | Conservative irregular | Dense | Complex | Extreme |
|---|---:|---:|---:|---:|---:|
| Open space % | 82.0573 | 78.3073 | 70.7292 | 59.6615 | 45.5990 |
| Open tiles | 3151.0000 | 3007.0000 | 2716.0000 | 2291.0000 | 1751.0000 |
| Logical components | 1.0000 | 1.0000 | 1.0000 | 1.0000 | 1.0000 |
| Main logical share % | 100.0000 | 100.0000 | 100.0000 | 100.0000 | 100.0000 |
| Navigation components | 1.0000 | 1.0000 | 1.0000 | 2.0000 | 3.0000 |
| Navigation cells | 2860.0000 | 2706.0000 | 2384.0000 | 1905.0000 | 1337.0000 |
| Player-clear logical centers | 2891.0000 | 2737.0000 | 2418.0000 | 1956.0000 | 1395.0000 |
| Player-clear navigation cells | 2860.0000 | 2706.0000 | 2384.0000 | 1905.0000 | 1337.0000 |
| Floor runs | 6.0000 | 7.0000 | 11.0000 | 15.0000 | 18.0000 |
| Median floor width | 3.0000 | 5.0000 | 7.0000 | 7.0000 | 6.0000 |
| Platform-scale floor runs | 4.0000 | 5.0000 | 7.0000 | 11.0000 | 13.0000 |
| Sloped floor surfaces | 35.0000 | 38.0000 | 44.0000 | 52.0000 | 56.0000 |
| Landing sites | 436.0000 | 438.0000 | 461.0000 | 508.0000 | 520.0000 |
| Landing-bearing floor runs | 5.0000 | 7.0000 | 10.0000 | 14.0000 | 17.0000 |
| Elevation bands | 5.0000 | 6.0000 | 7.0000 | 7.0000 | 7.0000 |
| Elevation span | 22.0000 | 22.3966 | 23.4873 | 24.4873 | 24.3105 |
| Approximate bottlenecks | 86.0000 | 100.0000 | 130.0000 | 167.0000 | 183.0000 |
| Generation ms | 4.6584 | 4.0730 | 4.0463 | 3.5112 | 5.4961 |
| Full pipeline ms | 1305.6451 | 1342.7460 | 1463.2532 | 1566.4384 | 1555.0171 |

## Validation outcomes

| Profile | Generated | Topology valid | Player spawn valid | 15-bird spawn valid |
|---|---:|---:|---:|---:|
| Baseline | 256/256 | 256/256 | 256/256 | 256/256 |
| Conservative irregular | 256/256 | 256/256 | 256/256 | 256/256 |
| Dense | 256/256 | 256/256 | 256/256 | 256/256 |
| Complex | 256/256 | 256/256 | 256/256 | 256/256 |
| Extreme | 256/256 | 256/256 | 256/256 | 256/256 |

## Candidate deltas

Paired medians are candidate minus reference over seeds where both sides produced metrics.

| Candidate | Reference | Open % | Nav cells | Floor runs | Elevation bands | Bottlenecks | Pipeline ms |
|---|---|---:|---:|---:|---:|---:|---:|
| Dense | Baseline | -10.8594 | -457.0000 | +5.0000 | +1.0000 | +43.0000 | +149.3237 |
| Dense | Conservative irregular | -7.4479 | -319.0000 | +3.0000 | +1.0000 | +29.0000 | +103.3807 |
| Complex | Baseline | -21.9531 | -938.0000 | +9.0000 | +2.0000 | +78.0000 | +257.8783 |
| Complex | Conservative irregular | -18.3073 | -785.0000 | +8.0000 | +1.0000 | +65.0000 | +211.3550 |
| Extreme | Baseline | -35.9115 | -1492.0000 | +12.0000 | +2.0000 | +95.0000 | +234.9109 |
| Extreme | Conservative irregular | -32.5000 | -1344.0000 | +10.0000 | +1.0000 | +82.0000 | +184.3343 |

## Recommendation

Among candidates with 256/256 valid full pipelines, `extreme` has the strongest median floor/elevation opportunity score. Its medians are 18.0000 floor runs, 7.0000 occupied landing-elevation bands, 183.0000 approximate bottlenecks, 1337.0000 navigation cells, and 1555.0171 ms full-pipeline runtime. Navigation-cell deltas are -1523.0000 versus baseline and -1369.0000 versus conservative-irregular. Advance it only to host playtesting; keep baseline as the runtime default.

This comparison remains an offline experiment. The measured candidates are selectable for controlled testing, but baseline remains the runtime default. No shelves or artificial platforms are generated.

## Hard failures

None.

## Representative comparisons
- [m10-quality-0042-baseline-vs-conservative-vs-dense.svg](m10-quality-0042-baseline-vs-conservative-vs-dense.svg)
- [m10-quality-0064-baseline-vs-conservative-vs-dense.svg](m10-quality-0064-baseline-vs-conservative-vs-dense.svg)
- [m10-quality-0245-baseline-vs-conservative-vs-dense.svg](m10-quality-0245-baseline-vs-conservative-vs-dense.svg)
- [m10-quality-0127-baseline-vs-conservative-vs-complex.svg](m10-quality-0127-baseline-vs-conservative-vs-complex.svg)
- [m10-quality-0157-baseline-vs-conservative-vs-complex.svg](m10-quality-0157-baseline-vs-conservative-vs-complex.svg)
- [m10-quality-0244-baseline-vs-conservative-vs-complex.svg](m10-quality-0244-baseline-vs-conservative-vs-complex.svg)
- [m10-quality-0118-baseline-vs-conservative-vs-extreme.svg](m10-quality-0118-baseline-vs-conservative-vs-extreme.svg)
- [m10-quality-0092-baseline-vs-conservative-vs-extreme.svg](m10-quality-0092-baseline-vs-conservative-vs-extreme.svg)
- [m10-quality-0095-baseline-vs-conservative-vs-extreme.svg](m10-quality-0095-baseline-vs-conservative-vs-extreme.svg)
