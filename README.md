# Bird Hell Standalone

Bird Hell Standalone is a standalone C# port of the original Unity game **Bird Hell**.

This foundation uses:

- .NET 10
- MonoGame DesktopGL
- Linux desktop as the current development target

Windows support is planned for a later stage, with potential mobile targets after the desktop port. The port will preserve the original game's behavior where practical while keeping game logic separate from rendering and platform-specific code.

## Build

```sh
dotnet restore BirdHell.sln
dotnet build BirdHell.sln
```

## Run

```sh
dotnet run --project src/BirdHell/BirdHell.csproj
```

To capture only Update, Draw, simulation catch-up, input-polling, or between-callback delays of at least
100 ms, enable the development stall diagnostics:

```sh
dotnet run --project src/BirdHell/BirdHell.csproj -- --stall-diagnostics
```

The JSONL output path is printed to the terminal at startup. Root Update/Draw/callback events retain the
100 ms threshold. When Update is slow, its keyboard, mouse, per-controller gamepad, input-adapter,
lifecycle, accumulator, simulation, and base-Update children use a 25 ms threshold and share an event-group
ID. Slow simulation steps are further divided by gameplay system. Use `--stall-diagnostics-dir <directory>`
ID. Slow simulation steps are further divided by gameplay system. Slow Draw calls are divided into clear,
environment layers, camera/cave work, entity submissions, SpriteBatch Begin/End calls, debug overlays, and
base Draw. Use `--stall-diagnostics-dir <directory>` to select the output directory. Stall diagnostics and
the much heavier Flight Recorder are independent. Within an already-slow enemy-navigation section,
5 ms fine-grained records distinguish each bird's repath, endpoint resolution, radius-clear searches,
A*, direct-target check, and movement/steering work.

Radius-clear endpoint projection searches deterministic nearest-first grid rings and stops once no unvisited
cell can beat the selected endpoint. Collider clearance, component constraints, maximum distance, and the
legacy row-major tie-break remain unchanged; synchronized 0.5-second bird repaths remain unchanged.

## Controls

- `A` / `D`, Left / Right, or controller left stick: move
- `SPACE` or controller A: jump (the original controller allows one air jump)
- `S` or Down: crouch animation only (movement, physics, and collider are unchanged)
- Left mouse button, Left Ctrl, or controller right trigger: fire once per press/pull in the direction the player faces
- `R`: start a fresh run with a new cave seed while gameplay is active
- `F5`: restart the active run with the exact current seed
- `O`: toggle terrain outlines
- `C`: toggle the player collider, ground-normal, and fixed-aim/facing debug view
- `N`: toggle navigation obstacles plus the first living enemy's path and steering target
- `V`: toggle the procedural cloud atmosphere (presentation only)
- `ESC` or controller Start/Menu: pause during gameplay and resume while paused
- controller Select/View: Back in nested menus such as Options (it does not pause gameplay)
- controller B: unused for menu Back/Pause
- Pause Menu: Resume, same-seed Restart, fresh-seed New Cave, or Return to Main Menu

The game uses a 50 Hz renderer-independent player/projectile/enemy simulation. Its logical 2D camera starts at `(0,0)` and, once per fixed step, lerps toward `player position + (0,1)` by exactly `0.15`. The view always spans 10 world units vertically; viewport aspect determines the horizontal span. There is no clamp, dead zone, or look-ahead, so horizontal and vertical movement remain smooth even beyond cave edges.

The normalized anime explorer atlas, normalized purple-bird flight atlas, and original sword/slash sprites provide production gameplay presentation. The player switches deterministically among idle, run, takeoff jump, sustained airborne, second-jump roll, crouch, and non-looping attack animations. Each bird uses a stable run-seed/ID-derived flap phase and subtle visual-only rate variation, so flocks do not flap in lockstep; projectiles retain their original looping frames. The `C` debug view remains separate and shows collision plus a short yellow line from the weapon muzzle in the fixed horizontal aim/facing direction. The window title shows player health, living enemy count, player death, and `ALL BIRDS DEAD`.

The normalized player and purple-bird atlases and the sword/slash use bilinear filtering. The explorer uses a fixed normalized body pivot, a shared grounded baseline, and a `1.2` presentation scale; its representative `123`-pixel visible body is approximately `1.476` world units tall (about `106` screen pixels at 720p). The purple bird uses its manifest `(80,72)` torso pivot and a `1.3` presentation scale; its tallest measured 94-pixel wing pose is approximately 88 screen pixels at 720p. Authoritative player and enemy colliders remain unchanged, so the bird's wings intentionally extend beyond its approximately `0.9161`-unit-diameter collider.

### Replacement Run Art Specification

The approved production Run cycle comes from `art/Source/Player/player_anime_explorer_run_v2.png`, an eight-cell, `1536 × 176` straight-RGBA strip. Each `192 × 176` frame faces right and uses this order: right contact, right recoil, right passing, right airborne, left contact, left recoil, left passing, left airborne. The builder applies the measured `0.8` Run-only scale correction centered at `x=96` and bottom-aligned to the existing `y=160` ground baseline, while preserving the `(96,88)` logical body pivot and `1.2` presentation scale at 100 PPU. It cleans low-alpha edge colors and packs the cycle into atlas row 1, columns 0–7 of `src/BirdHell/Content/Sprites/Player/player_explorer_runtime_v1.png` at 12 FPS, looping. Validation checks dimensions, order, corrected bounds and scale, gutter, contact baseline, alpha cleanliness, non-overlap, exact packed pixels, atlas placement, and the straight-PNG/premultiplied-upload contract.

The retired Sunnyland presentation remains on disk for comparison but is no longer loaded. The standalone now composes 16 transparent alien-desert assets into deterministic mountain, city, ruin, rock, and rare-plant layers. Named per-layer sub-seeds derive from the run seed: `F5` reproduces the layout, while `R` changes it without perturbing gameplay RNG. Each layer uses independent horizontal/vertical parallax, overscan, culling, tint, opacity, and brightness settings without changing `Camera2D`.

Behind the authored scenery, four deterministic `512 × 256` tileable cloud alpha fields are generated once from a visual-only seed using anisotropic, domain-warped, five-octave value noise. SpriteBatch linearly filters and cross-fades neighboring temporal phases while elapsed presentation time drives slow drift and evolution. Clouds use very distant parallax (`0.04` horizontal, `0.015` vertical), allocate nothing in the update/draw loop, and never touch gameplay RNG. `R` and `F5` preserve atmospheric seed, clock, phase, and visibility. Cave terrain still occludes all environment decoration and debug overlays draw last. Impact/death effects, audio, exact red damage-flash parity, and other effect work remain deferred.

## Procedural run lifecycle

Each run contains one generated cave, one 100-health player, and exactly 15 birds. The lifecycle has only three derived states:

- `Playing`: the player is alive and at least one bird is alive.
- `PlayerDead`: health reached zero; movement and firing remain disabled until `R`, `F5`, Pause Restart, or Pause New Cave is selected. Nothing restarts automatically.
- `AllBirdsDead`: the player remains controllable in the current cave; no birds respawn and nothing advances or regenerates until `R` or `F5` is pressed.

Both reset keys replace the cave, collision, player, projectiles, enemies, and navigation as one complete run. Pause Menu Restart and New Cave route through those same reset paths. The camera retains its current logical position and smoothly travels toward the replacement player; it never retains a player/entity reference. Debug visualization toggles remain selected. While paused, simulation and recorder steps stop and the last gameplay frame is shown through a downsampled frosted-glass treatment. There are no levels, difficulty progression, scene transitions, score, death screen, or victory screen in the active procedural path.
