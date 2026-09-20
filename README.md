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

## Controls

- `A` / `D` or Left / Right: move
- `SPACE`: jump (the original controller allows one air jump)
- `S` or Down: crouch animation only (movement, physics, and collider are unchanged)
- Left mouse button, Left Ctrl, or controller A: fire horizontally in the direction the player faces
- `R`: start a fresh run with a new cave seed (the standalone production replacement for Unity's Start/`Escape` regeneration)
- `F5`: debug extension that restarts the run with the exact current seed
- `O`: toggle terrain outlines
- `C`: toggle the player collider, ground-normal, and fixed-aim/facing debug view
- `N`: toggle navigation obstacles plus the first living enemy's path and steering target
- `ESC`: exit

The game uses a 50 Hz renderer-independent player/projectile/enemy simulation. Its logical 2D camera starts at `(0,0)` and, once per fixed step, lerps toward `player position + (0,1)` by exactly `0.15`. The view always spans 10 world units vertically; viewport aspect determines the horizontal span. There is no clamp, dead zone, or look-ahead, so horizontal and vertical movement remain smooth even beyond cave edges.

The original Adventurer, eagle, sword/slash, and Sunnyland background sprites now provide production presentation. The player switches deterministically among idle, run, jump, second-jump roll, crouch, and non-looping attack animations; birds and projectiles loop their original frames. The `C` debug view remains separate and shows collision plus a short yellow line from the weapon muzzle in the fixed horizontal aim/facing direction. The window title shows player health, living enemy count, player death, and `ALL BIRDS DEAD`.

Filtering follows the active Unity importers per asset: eagles and the background are point sampled, while the player and sword/slash use bilinear filtering inherited from Unity’s Sprite default. Sprite dimensions preserve Unity’s 100-pixels-per-unit settings and object scales without changing authoritative gameplay colliders: the player is approximately `2.0 × 1.48` world units and an eagle is approximately `1.2 × 1.23`. The oversized `38.4 × 24` background is attached to the camera at the original `(-2.66,-5)` local offset, with no parallax, tiling, or dynamic viewport stretch. Cave terrain still uses primitive geometry and debug overlays draw last. Impact/death effects, audio, exact red damage-flash parity, and other effect work remain deferred.

## Procedural run lifecycle

Each run contains one generated cave, one 100-health player, and exactly 15 birds. The lifecycle has only three derived states:

- `Playing`: the player is alive and at least one bird is alive.
- `PlayerDead`: health reached zero; movement and firing remain disabled until `R` or `F5` is pressed. Nothing restarts automatically.
- `AllBirdsDead`: the player remains controllable in the current cave; no birds respawn and nothing advances or regenerates until `R` or `F5` is pressed.

Both reset keys replace the cave, collision, player, projectiles, enemies, and navigation as one complete run. The camera retains its current logical position and smoothly travels toward the replacement player; it never retains a player/entity reference. Debug visualization toggles remain selected. `ESC` remains the unambiguous standalone exit key. There are no levels, difficulty progression, scene transitions, score, death screen, or victory screen in the active procedural path.
