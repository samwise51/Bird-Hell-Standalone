# Milestone 10E generation comparison

Generated from the fixed `m10-quality-0000` through `m10-quality-0255` corpus.

| Profile | Fill | Smooth | Wall threshold | Room threshold | Passage radius | Successful pipelines |
|---|---:|---:|---:|---:|---:|---:|
| Baseline | 35% | 5 | 50 | 50 | 5 | 256/256 |
| Conservative irregular | 38% | 4 | 40 | 50 | 4 | 256/256 |

## Aggregate medians

| Metric | Baseline | Experimental | Delta |
|---|---:|---:|---:|
| Open space % | 82.0573 | 78.3073 | -3.7500 |
| Floor runs | 6.0000 | 7.0000 | +1.0000 |
| Median floor width | 3.0000 | 5.0000 | +2.0000 |
| Platform-scale floor runs | 4.0000 | 5.0000 | +1.0000 |
| Sloped floor surfaces | 35.0000 | 38.0000 | +3.0000 |
| Landing sites | 436.0000 | 438.0000 | +2.0000 |
| Landing-bearing floor runs | 5.0000 | 7.0000 | +2.0000 |
| Elevation bands | 5.0000 | 6.0000 | +1.0000 |
| Elevation span | 22.0000 | 22.3966 | +0.3966 |
| Approximate bottlenecks | 86.0000 | 100.0000 | +14.0000 |
| Navigation cells | 2860.0000 | 2706.0000 | -154.0000 |

Baseline-specific provisional thresholds are reported for comparison only; geometric, player-spawn, and enemy-spawn validity remain the non-negotiable requirements.

## Representative comparisons
- [m10-quality-0036-baseline-vs-conservative.svg](m10-quality-0036-baseline-vs-conservative.svg)
- [m10-quality-0196-baseline-vs-conservative.svg](m10-quality-0196-baseline-vs-conservative.svg)
- [m10-quality-0053-baseline-vs-conservative.svg](m10-quality-0053-baseline-vs-conservative.svg)
- [m10-quality-0034-baseline-vs-conservative.svg](m10-quality-0034-baseline-vs-conservative.svg)
