using BirdHell.Entities;
using BirdHell.Diagnostics;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace BirdHell.Core;

public sealed class BirdHellGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly CaveGenerationProfileSelection _caveProfile;
    private readonly FlightRecorderOptions _flightRecorderOptions;
    private readonly Camera2D _camera = new();
    private readonly DebugPresentationSettings _debug = new();
    private readonly WorldAnimationState _animations = new();
    private readonly CloudAtmosphereState _atmosphere = new();
    private BasicEffect? _effect;
    private SpritePresentation? _spritePresentation;
    private EnvironmentPresentation? _environmentPresentation;
    private MainScreenRun? _run;
    private VertexPositionColor[] _terrainVertices = [];
    private int[] _triangleIndices = [];
    private VertexPositionColor[] _outlineVertices = [];
    private KeyboardState _previousKeyboardState;
    private readonly GameplayInputAdapter _gameplayInput = new();
    private GamePadState[] _gamePadStates = [];
    private double _simulationAccumulator;
    private int _displayedHealth = -1;
    private int _displayedEnemyCount = -1;
    private bool _displayedAllEnemiesDead;
    private float _navigationDiagnosticTitleTimer;
    private FlightRecorder? _flightRecorder;

    private const float SimulationStep = 0.02f;
    private const double MaximumFrameTime = 0.1;

    public BirdHellGame(
        CaveGenerationProfileSelection? caveProfile = null,
        FlightRecorderOptions? flightRecorderOptions = null)
    {
        _caveProfile = caveProfile ?? CaveGenerationProfiles.Default;
        _flightRecorderOptions = flightRecorderOptions ?? new FlightRecorderOptions();
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };

        Content.RootDirectory = "Content";
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Bird Hell Standalone";
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        _run = new MainScreenRun(CaveGenerator.CreateRandomSeed(), _caveProfile);
        _flightRecorder = FlightRecorder.Start(_flightRecorderOptions, _run, SimulationStep);
        if (_flightRecorder is not null) _run.AttachFlightRecorder(_flightRecorder);
        CompleteHostReset();
        _gamePadStates = new GamePadState[GamePad.MaximumGamePadCount];
        var keyboardState = Keyboard.GetState();
        SampleGamePads();
        _gameplayInput.UpdateFrame(keyboardState, Mouse.GetState(), _gamePadStates);
        _previousKeyboardState = keyboardState;
        base.Initialize();
    }

    protected override void LoadContent()
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, Content.RootDirectory);
        _spritePresentation = new SpritePresentation(
            GraphicsDevice,
            contentRoot);
        _environmentPresentation = new EnvironmentPresentation(GraphicsDevice, contentRoot);
        if (_run is not null)
            _environmentPresentation.SetRunLayout(_run.Seed, _run.Geometry.MinX, _run.Geometry.MaxX);
        _effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboardState = Keyboard.GetState();
        var mouseState = Mouse.GetState();
        SampleGamePads();
        _gameplayInput.UpdateFrame(keyboardState, mouseState, _gamePadStates);
        if (keyboardState.IsKeyDown(Keys.Escape)) Exit();
        var resetThisFrame = false;
        if (WasPressed(keyboardState, Keys.R))
        {
            _run?.StartNewRun();
            CompleteHostReset();
            resetThisFrame = true;
        }
        else if (WasPressed(keyboardState, Keys.F5))
        {
            _run?.RestartSameSeed();
            CompleteHostReset();
            resetThisFrame = true;
        }
        if (WasPressed(keyboardState, Keys.O))
        {
            _debug.ToggleOutlines();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.C))
        {
            _debug.ToggleCollision();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.N))
        {
            _debug.ToggleNavigation();
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.V))
        {
            _atmosphere.ToggleVisibility();
            UpdateWindowTitle();
        }

        _atmosphere.Advance(gameTime.ElapsedGameTime.TotalSeconds);

        if (!resetThisFrame)
        {
            _simulationAccumulator += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, MaximumFrameTime);
            while (_simulationAccumulator >= SimulationStep)
            {
                Simulate(_gameplayInput.ConsumeSimulationStep());
                _simulationAccumulator -= SimulationStep;
            }
        }
        _previousKeyboardState = keyboardState;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(16, 23, 47));
        _environmentPresentation?.DrawSky();
        _environmentPresentation?.DrawClouds(_camera, _atmosphere);
        _environmentPresentation?.DrawSceneryLayer(_camera, EnvironmentSceneryLayer.HugeMountains);
        _environmentPresentation?.DrawSceneryLayer(_camera, EnvironmentSceneryLayer.DistantCities);
        _environmentPresentation?.DrawSceneryLayer(_camera, EnvironmentSceneryLayer.Ruins);
        _environmentPresentation?.DrawSceneryLayer(_camera, EnvironmentSceneryLayer.NearRocks);
        _environmentPresentation?.DrawSceneryLayer(_camera, EnvironmentSceneryLayer.RarePlants);
        if (_run is not null && _effect is not null)
        {
            ConfigureCamera(_effect);

            // Distant background and clouds were drawn first. Terrain, entities, player,
            // projectiles/future effects, and debug overlays follow in documented order.
            DrawCaveGeometry(_effect);
            if (_spritePresentation is not null)
            {
                _spritePresentation.BeginWorldSprites(_camera, BirdHellVisuals.Enemy.Frames[0].Region.Filter);
                DrawEnemies(_spritePresentation);
                _spritePresentation.EndWorldSprites();
                _spritePresentation.BeginWorldSprites(_camera, BirdHellVisuals.PlayerIdle.Frames[0].Region.Filter);
                DrawPlayer(_spritePresentation);
                DrawProjectiles(_spritePresentation);
                _spritePresentation.EndWorldSprites();
            }
            DrawDebugOverlays(_effect);
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _flightRecorder?.Dispose();
        _environmentPresentation?.Dispose();
        _spritePresentation?.Dispose();
        _effect?.Dispose();
    }

    private bool WasPressed(KeyboardState currentState, Keys key) =>
        currentState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void CompleteHostReset()
    {
        if (_run is null) return;
        _simulationAccumulator = 0d;
        _gameplayInput.ClearBufferedEdges();
        _atmosphere.OnGameplayReset();
        _environmentPresentation?.SetRunLayout(_run.Seed, _run.Geometry.MinX, _run.Geometry.MaxX);
        var keyboard = Keyboard.GetState();
        _animations.Reset(CreatePlayerVisualSnapshot(
            keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)));
        BuildRenderData(_run.Geometry);
        UpdateWindowTitle();
    }

    private void BuildRenderData(CaveGeometry geometry)
    {
        var terrainColor = new Color(218, 209, 176);
        _terrainVertices = geometry.Vertices
            .Select(vertex => new VertexPositionColor(new Vector3(vertex.X, vertex.Y, 0f), terrainColor))
            .ToArray();
        _triangleIndices = geometry.Triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .ToArray();

        var outlineColor = new Color(255, 54, 149);
        var lineVertices = new List<VertexPositionColor>();
        foreach (var outline in geometry.Outlines)
            for (var i = 0; i < outline.Points.Count - 1; i++)
            {
                var first = outline.Points[i];
                var second = outline.Points[i + 1];
                lineVertices.Add(new VertexPositionColor(new Vector3(first.X, first.Y, -0.1f), outlineColor));
                lineVertices.Add(new VertexPositionColor(new Vector3(second.X, second.Y, -0.1f), outlineColor));
            }
        _outlineVertices = lineVertices.ToArray();
    }

    private void ConfigureCamera(BasicEffect effect)
    {
        var viewport = GraphicsDevice.Viewport;
        var view = _camera.GetView(viewport.Width, viewport.Height);

        effect.Projection = Matrix.CreateOrthographicOffCenter(
            view.Left,
            view.Right,
            view.Bottom,
            view.Top,
            0f,
            1f);
    }

    private void DrawCaveGeometry(BasicEffect effect)
    {
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_triangleIndices.Length > 0)
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _terrainVertices,
                    0,
                    _terrainVertices.Length,
                    _triangleIndices,
                    0,
                    _triangleIndices.Length / 3);
        }
    }

    private void DrawPlayer(SpritePresentation sprites)
    {
        if (_run is null || !_animations.Player.ShouldRender || _run.State == RunState.PlayerDead) return;
        var player = _run.Player;
        sprites.Draw(
            _animations.Player.Playback.CurrentFrame,
            player.Position,
            PlayerAnimationState.FlipForFacing(player.FacingDirection),
            player.DamageFlashTime > 0f ? Color.White : Color.White);
    }

    private static IReadOnlyList<Microsoft.Xna.Framework.Vector2> BuildPlayerPolygon(Player player)
    {
        const int arcSteps = 8;
        var collider = player.Collider;
        var center = player.ColliderCenter;
        var polygon = new List<Microsoft.Xna.Framework.Vector2>();
        for (var i = 0; i <= arcSteps; i++)
        {
            var angle = i * MathF.PI / arcSteps;
            polygon.Add(new Microsoft.Xna.Framework.Vector2(
                center.X + MathF.Cos(angle) * collider.Radius,
                center.Y + collider.CoreHalfHeight + MathF.Sin(angle) * collider.Radius));
        }
        for (var i = 0; i <= arcSteps; i++)
        {
            var angle = MathF.PI + i * MathF.PI / arcSteps;
            polygon.Add(new Microsoft.Xna.Framework.Vector2(
                center.X + MathF.Cos(angle) * collider.Radius,
                center.Y - collider.CoreHalfHeight + MathF.Sin(angle) * collider.Radius));
        }
        return polygon;
    }

    private void Simulate(GameplayInputState input)
    {
        if (_run is null) return;
        var shotsBefore = _run.WeaponState.ShotsFired;
        var stepResult = _run.Step(
            new PlayerInput(input.Horizontal, input.JumpPressed, input.JumpHeld),
            input.FirePressed,
            SimulationStep);
        _animations.Player.Update(
            CreatePlayerVisualSnapshot(input.CrouchHeld),
            _run.WeaponState.ShotsFired > shotsBefore,
            stepResult.SecondJumpPerformed,
            SimulationStep);
        _animations.AdvanceEnemies(SimulationStep);
        _camera.Step(_run.Player.Position);
        _navigationDiagnosticTitleTimer += SimulationStep;
        if (_displayedHealth != _run.Player.Health || _displayedEnemyCount != _run.Enemies.LivingCount ||
            _displayedAllEnemiesDead != _run.Enemies.AllEnemiesDead)
            UpdateWindowTitle();
        else if (_debug.ShowNavigation && _navigationDiagnosticTitleTimer >= 0.25f)
            UpdateWindowTitle();
    }

    private void SampleGamePads()
    {
        for (var index = 0; index < _gamePadStates.Length; index++)
            _gamePadStates[index] = GamePad.GetState(index, GamePadDeadZone.None);
    }

    private PlayerVisualSnapshot CreatePlayerVisualSnapshot(bool crouchHeld)
    {
        if (_run is null) return default;
        return new PlayerVisualSnapshot(
            _run.Player.IsAlive,
            _run.Player.IsGrounded,
            crouchHeld,
            _run.Player.Velocity.X);
    }

    private void DrawEnemies(SpritePresentation sprites)
    {
        if (_run is null) return;
        foreach (var enemy in _run.Enemies.LivingEnemies)
            sprites.Draw(
                _animations.Enemy.CurrentFrame,
                enemy.Position,
                WorldAnimationState.EnemyFlip(enemy.FacingDirection),
                enemy.DamageFlashTime > 0f ? Color.White : Color.White);
    }

    private void DrawNavigationDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var navigationGrid = _run.NavigationGrid;
        var diagnostics = _run.EnemyNavigation.GetDiagnostics(_run.Enemies.Enemies, _run.Player.Position);
        var lines = new List<VertexPositionColor>();
        var blockedColor = new Color(170, 40, 210, 110);
        for (var y = 0; y < navigationGrid.Height; y += 2)
        for (var x = 0; x < navigationGrid.Width; x += 2)
        {
            var cell = new NavigationCell(x, y);
            if (navigationGrid.IsWalkable(cell)) continue;
            var center = navigationGrid.CellToWorld(cell);
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.18f, center.Y, -0.45f), blockedColor));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.18f, center.Y, -0.45f), blockedColor));
        }

        foreach (var item in diagnostics.Where(item =>
                     item.StallDuration >= 0.5f || item.RecentlyCollidedWithTerrain))
        {
            var enemy = _run.Enemies.LivingEnemies.First(candidate => candidate.Id == item.EnemyId);
            var center = enemy.ColliderCenter;
            var color = item.StallDuration >= 0.5f ? Color.Red : Color.Orange;
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.3f, center.Y - 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.3f, center.Y + 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X - 0.3f, center.Y + 0.3f, -0.48f), color));
            lines.Add(new VertexPositionColor(new Vector3(center.X + 0.3f, center.Y - 0.3f, -0.48f), color));
        }

        var selectedDiagnostic = diagnostics
            .OrderByDescending(item => item.StallDuration)
            .ThenByDescending(item => item.RecentlyCollidedWithTerrain)
            .ThenByDescending(item => item.NearbyEnemyCount)
            .ThenBy(item => item.EnemyId)
            .FirstOrDefault();
        var selected = selectedDiagnostic is null
            ? null
            : _run.Enemies.LivingEnemies.First(enemy => enemy.Id == selectedDiagnostic.EnemyId);
        if (selected is not null)
        {
            for (var index = Math.Max(0, selected.PathIndex - 1); index < selected.Path.Count - 1; index++)
            {
                var first = selected.Path[index];
                var second = selected.Path[index + 1];
                lines.Add(new VertexPositionColor(new Vector3(first.X, first.Y, -0.46f), Color.LimeGreen));
                lines.Add(new VertexPositionColor(new Vector3(second.X, second.Y, -0.46f), Color.LimeGreen));
            }
            lines.Add(new VertexPositionColor(new Vector3(selected.ColliderCenter.X, selected.ColliderCenter.Y, -0.47f), Color.Yellow));
            lines.Add(new VertexPositionColor(new Vector3(selected.SteeringTarget.X, selected.SteeringTarget.Y, -0.47f), Color.Yellow));
        }

        if (lines.Count == 0) return;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private void DrawProjectiles(SpritePresentation sprites)
    {
        if (_run is null) return;
        foreach (var projectile in _run.Projectiles.Projectiles)
        {
            var frameIndex = WorldAnimationState.ProjectileFrame(projectile.Age);
            sprites.Draw(
                BirdHellVisuals.Projectile.Frames[frameIndex],
                projectile.Position,
                WorldAnimationState.ProjectileFlip(projectile.Direction.X),
                Color.White);
        }
    }

    private void DrawDebugOverlays(BasicEffect effect)
    {
        if (_debug.ShowOutlines) DrawTerrainOutlineDebug(effect);
        if (_debug.ShowCollision)
        {
            DrawPlayerCollisionDebug(effect);
            DrawAimDebug(effect);
        }
        if (_debug.ShowNavigation) DrawNavigationDebug(effect);
    }

    private void DrawTerrainOutlineDebug(BasicEffect effect)
    {
        if (_outlineVertices.Length == 0) return;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                _outlineVertices,
                0,
                _outlineVertices.Length / 2);
        }
    }

    private void DrawPlayerCollisionDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var player = _run.Player;
        var center = player.ColliderCenter;
        var polygon = BuildPlayerPolygon(player);
        var debugColor = player.IsGrounded ? Color.LimeGreen : Color.Orange;
        var lines = new List<VertexPositionColor>();
        for (var index = 0; index < polygon.Count; index++)
        {
            lines.Add(new VertexPositionColor(new Vector3(polygon[index], -0.3f), debugColor));
            lines.Add(new VertexPositionColor(
                new Vector3(polygon[(index + 1) % polygon.Count], -0.3f),
                debugColor));
        }
        lines.Add(new VertexPositionColor(new Vector3(center.X, center.Y, -0.3f), Color.Yellow));
        lines.Add(new VertexPositionColor(
            new Vector3(center.X + player.GroundNormal.X, center.Y + player.GroundNormal.Y, -0.3f),
            Color.Yellow));
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private void DrawAimDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var direction = _run.GetAimDirection();
        var muzzle = _run.GetMuzzlePosition();
        var aimEnd = muzzle + direction * 1.5f;
        VertexPositionColor[] vertices =
        [
            new VertexPositionColor(new Vector3(muzzle.X, muzzle.Y, -0.35f), new Color(255, 196, 64)),
            new VertexPositionColor(new Vector3(aimEnd.X, aimEnd.Y, -0.35f), new Color(255, 196, 64)),
        ];
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, vertices, 0, 1);
        }
    }

    private void UpdateWindowTitle()
    {
        if (_run is null) return;
        _navigationDiagnosticTitleTimer = 0f;
        _displayedHealth = _run.Player.Health;
        _displayedEnemyCount = _run.Enemies.LivingCount;
        _displayedAllEnemiesDead = _run.Enemies.AllEnemiesDead;
        var state = _run.State switch
        {
            RunState.PlayerDead => " - PLAYER DEAD",
            RunState.AllBirdsDead => " - ALL BIRDS DEAD",
            _ => " - PLAYING",
        };
        var navigationDiagnostic = string.Empty;
        if (_debug.ShowNavigation)
        {
            var item = _run.EnemyNavigation.GetDiagnostics(_run.Enemies.Enemies, _run.Player.Position)
                .OrderByDescending(item => item.StallDuration)
                .ThenByDescending(item => item.RecentlyCollidedWithTerrain)
                .ThenByDescending(item => item.NearbyEnemyCount)
                .ThenBy(item => item.EnemyId)
                .FirstOrDefault();
            if (item is not null)
            {
                var waypoint = item.CurrentWaypoint is { } target ? $"{target.X:F1},{target.Y:F1}" : "none";
                navigationDiagnostic =
                    $" - N bird {item.EnemyId} pos {item.Position.X:F1},{item.Position.Y:F1} " +
                    $"vel {item.Velocity.X:F1},{item.Velocity.Y:F1} wp {waypoint} " +
                    $"move {item.LastStepDistance:F3} progress {item.PlayerDistanceDelta:+0.000;-0.000;0.000} " +
                    $"dist {item.PlayerDistance:F1} comp {item.NavigationComponentId} " +
                    $"collision {(item.RecentlyCollidedWithTerrain ? "yes" : "no")} " +
                    $"near {item.NearbyEnemyCount} stall {item.StallDuration:F1}s";
            }
        }
        Window.Title = $"Bird Hell [{_run.CaveProfileName}] - HP {_displayedHealth}/100 - Enemies {_displayedEnemyCount}/15{state}{navigationDiagnostic} - " +
                       $"A/D or left stick move, SPACE/A jump, S/Down crouch, LMB/Ctrl/RT fire, R new, F5 same, O outlines, C collision, N navigation, V clouds ({(_atmosphere.IsVisible ? "on" : "off")}), ESC exit - Seed: {_run.Seed}";
    }
}
