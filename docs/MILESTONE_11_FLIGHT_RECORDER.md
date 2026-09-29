# Milestone 11 flight recorder

The flight recorder is a development-only, opt-in gameplay evidence collector. Normal launches do not create a recorder, allocate its rolling buffers, write files, or change simulation state. It records gameplay facts only; it does not inspect credentials, environment variables, unrelated files, or OpenClaw data.

## Record a CachyOS playtest (fish)

From the repository root, restore/build once:

```fish
dotnet restore BirdHell.sln
dotnet build BirdHell.sln --no-restore
```

Record Complex or Extreme with the default destination:

```fish
dotnet run --project src/BirdHell/BirdHell.csproj --no-build -- --cave-profile complex --flight-recorder
dotnet run --project src/BirdHell/BirdHell.csproj --no-build -- --cave-profile extreme --flight-recorder
```

On Linux the default root is `~/.local/share/BirdHell/flight-recordings`. Every launch creates a unique timestamp/GUID session directory beneath it. `R` and `F5` stay in the same application session and emit a new `run_start` record with the complete seed and reason.

Use a repository-local destination when preparing a bundle (the directory is gitignored):

```fish
set recorder_root "$PWD/flight-recordings"
dotnet run --project src/BirdHell/BirdHell.csproj --no-build -- --cave-profile complex --flight-recorder --flight-recorder-dir "$recorder_root"
```

The directory option implies `--flight-recorder`, so that flag may be omitted. Cave-profile order is unrestricted. Optional disk caps are whole MiB:

```fish
dotnet run --project src/BirdHell/BirdHell.csproj --no-build -- --cave-profile extreme --flight-recorder-dir "$recorder_root" --flight-recorder-max-mb 256 --flight-recorder-incident-max-mb 128
```

Defaults are 64 MiB for `events.jsonl` and 64 MiB for `incidents.jsonl`. Records and the movement samples contained in records beyond either cap are counted as dropped. The in-memory pre-window is fixed at 5 seconds (250 fixed ticks) per active bird; an incident can retain another 5 seconds (250 ticks). At the worst case of all 15 birds having active incidents, the recorder retains at most 11,250 movement-sample references plus small state queues.

Recording serializes and flushes evidence during gameplay, so expect additional CPU allocations, filesystem writes, and possible frame-time impact on slow storage. There is no credible hardware-independent percentage; use release builds and local SSD storage for a representative playtest. Disabled overhead is one nullable recorder branch per simulation step.

## Session files and schema

All numeric JSON is emitted by `System.Text.Json` using culture-independent JSON number syntax. Schema version 1 uses:

- `metadata.json`: schema/session/application version, initial complete seed, selected profile, all cave-generation parameters, fixed 0.02-second step, coordinate definitions, and configured retention limits.
- `events.jsonl`: one ordered JSON object per event. It includes monotonic sequence/tick/game time/run, run starts, fixed-tick player input/movement and bird samples, waypoint changes, successful repaths, pathfinding failures, terrain collisions, firing, damage, and deaths.
- `incidents.jsonl`: one JSON object per retained incident, with detailed ordered per-tick samples around the trigger. Each sample contains intended/actual movement, velocity, waypoint, player target/distance/progress, navigation component, terrain contact, nearby-bird count, and path/repath state.
- `completion.json`: written on normal disposal with byte/drop counts. Its absence marks an incomplete session. JSONL files are flushed continuously, so the analyzer can use complete lines left by a crash and reports malformed/truncated tails instead of inventing values.

Coordinates are world units with `+X` right and `+Y` up; one generated cave tile is one world unit. Entity `position` is its render/gameplay origin and `colliderCenter` is explicitly named when used.

Schema version 2 extends `pathfinding_failure` events with the attempted start world position/cell, requested target world position, unconstrained nearest target cell, component-constrained resolved destination cell, traversability and bird-radius-clearance flags for each endpoint, component IDs, and an explicit failure reason. This is additive to the version 1 movement and incident records; the analyzer continues to accept either version.

A stall incident begins at 0.5 seconds of the existing distant/no-movement diagnostic. A repeated-collision incident begins at five terrain contacts in one second. One continuous symptom is suppressed until the bird has been healthy for one second, preventing duplicate incidents.

## Analyze a session

Choose the newest default session and generate `analysis/report.md` plus `analysis/summary.json`:

```fish
set session (find "$HOME/.local/share/BirdHell/flight-recordings" -mindepth 1 -maxdepth 1 -type d | sort | tail -n 1)
dotnet run --project tools/BirdHell.FlightAnalyzer/BirdHell.FlightAnalyzer.csproj --no-build -- "$session"
```

For a repository-local recording:

```fish
set session (find "$PWD/flight-recordings" -mindepth 1 -maxdepth 1 -type d | sort | tail -n 1)
dotnet run --project tools/BirdHell.FlightAnalyzer/BirdHell.FlightAnalyzer.csproj --no-build -- "$session" --output "$session/analysis"
```

The Markdown and JSON distinguish measured statistics/timelines from explicitly conditional possible explanations. Missing completion markers, malformed lines, drop counts, and absent incidents are surfaced as evidence limitations. Location groups use two-world-unit bins plus the observable trigger type; they are not diagnoses.

## Export the smallest useful stalled-bird bundle

Read `analysis/report.md`, identify the incident and bird ID, then export only metadata, completion status, that bird's per-tick samples/events, and that bird's incidents:

```fish
set bird_id 7
set bundle "$PWD/flight-recordings/bird-$bird_id-bundle"
dotnet run --project tools/BirdHell.FlightAnalyzer/BirdHell.FlightAnalyzer.csproj --no-build -- "$session" --bird "$bird_id" --export-bundle "$bundle"
du -sh "$bundle"
```

Review the bundle before sharing it. Seeds and gameplay input are intentionally included for diagnosis; no screenshots, machine configuration, credentials, or unrelated files are included.

## Safely remove old recordings

Confirm the resolved directory before deleting anything:

```fish
set recorder_root "$HOME/.local/share/BirdHell/flight-recordings"
realpath "$recorder_root"
find "$recorder_root" -mindepth 1 -maxdepth 1 -type d -printf '%TY-%Tm-%Td %TH:%TM %p\n' | sort
```

Prefer a recoverable trash operation on CachyOS (install `trash-cli` if needed):

```fish
trash-put -- "$session"
```

Only use permanent removal after confirming that `$session` is one specific session directory beneath the expected recorder root.

## Scope and limitations

The recorder prepares stable IDs, complete run metadata, ordered fixed-tick input, and state evidence for future replay. Milestone 11 does not implement deterministic headless replay. Incident buffers that have not yet triggered remain memory-only and cannot survive a process crash. Reported nearby-bird counts, collision contacts, route repetition, and progress are observations; they cannot independently prove crowding, bad waypoint selection, or an A* defect.
