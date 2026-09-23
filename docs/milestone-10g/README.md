# Milestone 10G offline cave-generation experiment

`BirdHell.GenerationComparisonChecks` evaluates the unchanged `baseline` and
`conservative-irregular` configurations alongside three measured candidates:

| Candidate | Initial wall fill | Smoothing passes | Wall threshold | Room threshold | Passage radius |
|---|---:|---:|---:|---:|---:|
| Dense | 42% | 4 | 40 | 50 | 4 |
| Complex | 45% | 3 | 35 | 50 | 3 |
| Extreme | 48% | 3 | 35 | 50 | 3 |

The runner uses `m10-quality-0000` through `m10-quality-0255` exactly once per
profile. It does not retry or filter failures. Profile order rotates by seed,
and source-level regression checks verify same-seed repeatability, evaluation-
order independence, unchanged default generation, exact candidate parameters,
and that runtime profile selection reuses the exact measured options.

Running the comparison writes these measured artifacts into this directory:

- `comparison-results.csv`: all 1,280 outcomes, stage validity, failures,
  topology/spawn metrics, and timings;
- `comparison-summary.md`: aggregate tradeoffs, every hard failure, and a
  measured recommendation that leaves baseline as the runtime default;
- representative three-panel SVG maps for baseline,
  conservative-irregular, and each candidate.

No measured output is checked in until the runner has actually executed. The
comparison reuses the production generator, geometry, occupancy, navigation,
topology, player-spawn, and 15-bird spawn pipeline without changing them.
The three candidates are available through `--cave-profile` for controlled
testing; `baseline` remains the default when the option is omitted.
