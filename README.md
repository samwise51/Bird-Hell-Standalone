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
- Left mouse button, Left Ctrl, or controller A: fire horizontally in the direction the player faces
- `R`: start a fresh run with a new cave seed (the standalone production replacement for Unity's Start/`Escape` regeneration)
- `F5`: debug extension that restarts the run with the exact current seed
- `O`: toggle terrain outlines
- `C`: toggle the player collider, ground-normal, and fixed-aim/facing debug view
- `N`: toggle navigation obstacles plus the first living enemy's path and steering target
- `ESC`: exit

The game uses a 50 Hz renderer-independent player/projectile/enemy simulation and a horizontally following camera. Yellow diamonds are temporary projectile rendering; orange starbursts are flying enemies. The `C` debug view also shows a short yellow line from the weapon muzzle in the fixed horizontal aim/facing direction. The window title shows player health, living enemy count, player death, and `ALL BIRDS DEAD`.

## Procedural run lifecycle

Each run contains one generated cave, one 100-health player, and exactly 15 birds. The lifecycle has only three derived states:

- `Playing`: the player is alive and at least one bird is alive.
- `PlayerDead`: health reached zero; movement and firing remain disabled until `R` or `F5` is pressed. Nothing restarts automatically.
- `AllBirdsDead`: the player remains controllable in the current cave; no birds respawn and nothing advances or regenerates until `R` or `F5` is pressed.

Both reset keys replace the cave, collision, player, projectiles, enemies, and navigation as one complete run. Debug visualization toggles remain selected. `ESC` remains the unambiguous standalone exit key. There are no levels, difficulty progression, scene transitions, score, death screen, or victory screen in the active procedural path.
