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
- `R`: generate a new seed and respawn
- `F5`: regenerate the current seed and respawn deterministically
- `O`: toggle terrain outlines
- `C`: toggle the player collider and ground-normal debug view
- `ESC`: exit

The game uses a 50 Hz renderer-independent player/projectile simulation and a horizontally following camera. Yellow diamonds and the short yellow line are temporary projectile and fixed-aim debug rendering.
