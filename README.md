# Bird Hell Standalone

Bird Hell Standalone is a standalone C#/.NET and MonoGame reimplementation of the original Unity game **Bird Hell**. It carries the original project's procedural cave and combat ideas into a desktop-focused codebase that does not require Unity.

Each run places you in a generated cave with a flock of hostile birds. Move through the terrain, use two-stage jumping to navigate ledges and passages, and shoot the birds while avoiding contact damage. Clearing the flock leaves you free to explore; you can replay the same cave or generate a new one at any time.

## Features

- Procedurally generated cave runs with deterministic seeds and selectable cave profiles
- Responsive horizontal movement, a variable-height first jump, and a second air jump
- Directional projectile combat with health, contact damage, death, and hit feedback
- Animated bird enemies that pursue the player using cave-aware navigation and pathfinding
- Keyboard and mouse controls plus gamepad support
- Main, options, and pause menus, including same-seed restart and new-cave actions
- Configurable bird count, cave profile, invincibility, and windowed/fullscreen display mode
- Music and gameplay sound effects
- Hand-painted cave materials, edge treatments, deterministic scenery, and sparse decorative cave elements

## Controls

### Gameplay

| Action | Keyboard / Mouse | Gamepad |
| --- | --- | --- |
| Move | `A` / `D` or Left / Right arrows | Left stick |
| Jump | `Space` | A |
| Crouch pose | `S` or Down arrow | — |
| Shoot | Left mouse button or Left Ctrl | Right trigger |
| Pause / resume | `Esc` | Start / Menu |
| New cave | `R` | — |
| Restart same cave seed | `F5` | — |

The first jump is variable-height: hold the jump control to rise higher. Release and press it again while airborne for the second jump. Shooting fires once per click, key press, or trigger pull in the direction the player is facing. Crouching currently changes the player animation only; it does not change movement, collision, or physics.

### Menus

| Action | Keyboard / Mouse | Gamepad |
| --- | --- | --- |
| Move selection | Arrow keys | D-pad or left stick |
| Change an option | Left / Right arrows or click an option arrow | D-pad or left stick Left / Right |
| Select | `Enter`, `Space`, or left click | A |
| Back from Options | `Esc` | Select / View |
| Resume from pause | `Esc` | Start / Menu |

Mouse movement can focus menu items, and left click activates them. Controller B is not currently assigned to a global Back or Pause action.

### Debugging

These keyboard toggles are available while gameplay is active:

- `O`: terrain outlines
- `C`: player collision, ground normal, and aim/facing
- `N`: navigation obstacles and the first living bird's path and steering target
- `V`: procedural cloud atmosphere visibility

## Gameplay

A run begins with a living player and a configurable flock of birds. The birds navigate the cave toward the player and deal damage on contact. Player shots travel horizontally, collide with cave terrain, and damage birds. When player health reaches zero, movement and firing stop until the run is restarted or replaced; the game does not restart automatically.

Press `F5` or choose **Restart** from the pause menu to rebuild the run with the same seed. This reproduces the cave and its seeded placements while resetting the player, enemies, and projectiles. Press `R` or choose **New Cave** for a fresh seed. The pause menu can also resume play or return to the main menu.

## Procedural Caves

Caves are generated from deterministic seeds. Reusing a seed with the same cave profile reproduces the generated cave, player spawn, enemy placement, and seed-derived presentation; generating a new seed produces a different run.

The cave presentation uses hand-painted terrain materials, floor, ceiling, and wall surface treatments, and sparse boulder accents. Scenery and cave decoration are deterministic presentation layers. They do not alter the authoritative generated cave collision used by movement, projectiles, spawning, or enemy navigation.

## Building and Running

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A desktop environment supported by MonoGame DesktopGL

From the repository root, build the solution:

```sh
dotnet build BirdHell.sln
```

Run the game:

```sh
dotnet run --project src/BirdHell/BirdHell.csproj
```

The project targets `net10.0` and uses MonoGame DesktopGL 3.8.5.1.

## Platform Support

Bird Hell Standalone is currently developed and tested on Linux. MonoGame DesktopGL is intended to keep the project portable across desktop systems, but Windows and macOS builds are not currently claimed as officially tested releases. Broader desktop publishing support remains future work.

## Project Status

The standalone port is under active development. The core procedural run, movement, combat, enemies, menus, audio, and current cave presentation are implemented, while gameplay balance, visuals, platform packaging, and overall polish continue to evolve.

## Original Project

This repository is a standalone reimplementation/port of the original Unity project and aims to preserve its behavior where practical while using a separate C#/.NET and MonoGame architecture.

Original Unity project: [samwise51/Bird-Hell-Procedural](https://github.com/samwise51/Bird-Hell-Procedural)
