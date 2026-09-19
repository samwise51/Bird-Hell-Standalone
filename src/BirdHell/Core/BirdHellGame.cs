using BirdHell.Entities;
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
    private BasicEffect? _effect;
    private MainScreenRun? _run;
    private VertexPositionColor[] _terrainVertices = [];
    private int[] _triangleIndices = [];
    private VertexPositionColor[] _outlineVertices = [];
    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private GamePadState _previousGamePadState;
    private bool _showOutlines;
    private bool _showCollisionDebug;
    private bool _showNavigationDebug;
    private bool _pendingJump;
    private bool _pendingFire;
    private double _simulationAccumulator;
    private float _cameraX;
    private int _displayedHealth = -1;
    private int _displayedEnemyCount = -1;
    private bool _displayedAllEnemiesDead;

    private const float SimulationStep = 0.02f;
    private const double MaximumFrameTime = 0.1;

    public BirdHellGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };

        Content.RootDirectory = "Content";
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.Title = "Bird Hell Standalone";
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        _run = new MainScreenRun(CaveGenerator.CreateRandomSeed());
        CompleteHostReset();
        _previousKeyboardState = Keyboard.GetState();
        _previousMouseState = Mouse.GetState();
        _previousGamePadState = GamePad.GetState(PlayerIndex.One);
        base.Initialize();
    }

    protected override void LoadContent()
    {
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
        var gamePadState = GamePad.GetState(PlayerIndex.One);
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
            _showOutlines = !_showOutlines;
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.C))
        {
            _showCollisionDebug = !_showCollisionDebug;
            UpdateWindowTitle();
        }
        if (WasPressed(keyboardState, Keys.N))
        {
            _showNavigationDebug = !_showNavigationDebug;
            UpdateWindowTitle();
        }

        if (!resetThisFrame)
        {
            _pendingJump |= WasPressed(keyboardState, Keys.Space);
            _pendingFire |= WasPressed(keyboardState, Keys.LeftControl) ||
                            mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released ||
                            gamePadState.IsButtonDown(Buttons.A) && _previousGamePadState.IsButtonUp(Buttons.A);
        }
        if (!resetThisFrame)
        {
            _simulationAccumulator += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, MaximumFrameTime);
            while (_simulationAccumulator >= SimulationStep)
            {
                Simulate(keyboardState);
                _pendingJump = false;
                _pendingFire = false;
                _simulationAccumulator -= SimulationStep;
            }
        }
        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
        _previousGamePadState = gamePadState;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 12, 16));
        if (_run is not null && _effect is not null)
        {
            ConfigureCamera(_run.Geometry, _effect);
            DrawCaveGeometry(_effect);
            DrawPlayer(_run.Player, _effect);
            DrawEnemies(_effect);
            DrawCombatDebug(_effect);
            if (_showNavigationDebug) DrawNavigationDebug(_effect);
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _effect?.Dispose();
    }

    private bool WasPressed(KeyboardState currentState, Keys key) =>
        currentState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void CompleteHostReset()
    {
        if (_run is null) return;
        _simulationAccumulator = 0d;
        _pendingJump = false;
        _pendingFire = false;
        _cameraX = _run.Player.Position.X;
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

    private void ConfigureCamera(CaveGeometry geometry, BasicEffect effect)
    {
        var viewport = GraphicsDevice.Viewport;
        const float paddingScale = 1.05f;
        var geometryHeight = geometry.MaxY - geometry.MinY;
        var viewportAspect = viewport.Width / (float)viewport.Height;
        var halfHeight = geometryHeight * paddingScale / 2f;
        var halfWidth = halfHeight * viewportAspect;
        var targetX = _run?.Player.Position.X ?? 0f;
        var minimumCameraX = geometry.MinX + halfWidth;
        var maximumCameraX = geometry.MaxX - halfWidth;
        _cameraX = minimumCameraX <= maximumCameraX
            ? Math.Clamp(targetX, minimumCameraX, maximumCameraX)
            : (geometry.MinX + geometry.MaxX) / 2f;
        var cameraY = (geometry.MinY + geometry.MaxY) / 2f;

        effect.Projection = Matrix.CreateOrthographicOffCenter(
            _cameraX - halfWidth,
            _cameraX + halfWidth,
            cameraY - halfHeight,
            cameraY + halfHeight,
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
            if (_showOutlines && _outlineVertices.Length > 0)
                GraphicsDevice.DrawUserPrimitives(
                    PrimitiveType.LineList,
                    _outlineVertices,
                    0,
                    _outlineVertices.Length / 2);
        }
    }

    private void DrawPlayer(Player player, BasicEffect effect)
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

        var playerColor = !player.IsAlive ? new Color(80, 80, 90) :
            player.DamageFlashTime > 0f ? Color.White : new Color(72, 210, 255);
        var vertices = new VertexPositionColor[polygon.Count + 1];
        vertices[0] = new VertexPositionColor(new Vector3(center.X, center.Y, -0.2f), playerColor);
        for (var i = 0; i < polygon.Count; i++)
            vertices[i + 1] = new VertexPositionColor(new Vector3(polygon[i], -0.2f), playerColor);
        var indices = new int[polygon.Count * 3];
        for (var i = 0; i < polygon.Count; i++)
        {
            indices[i * 3] = 0;
            indices[i * 3 + 1] = i + 1;
            indices[i * 3 + 2] = (i + 1) % polygon.Count + 1;
        }

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                vertices.Length,
                indices,
                0,
                indices.Length / 3);
        }

        if (!_showCollisionDebug) return;
        var debugColor = player.IsGrounded ? Color.LimeGreen : Color.Orange;
        var lines = new List<VertexPositionColor>();
        for (var i = 0; i < polygon.Count; i++)
        {
            lines.Add(new VertexPositionColor(new Vector3(polygon[i], -0.3f), debugColor));
            lines.Add(new VertexPositionColor(new Vector3(polygon[(i + 1) % polygon.Count], -0.3f), debugColor));
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

    private void Simulate(KeyboardState keyboardState)
    {
        if (_run is null) return;
        var horizontal = 0f;
        if (keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.Left)) horizontal -= 1f;
        if (keyboardState.IsKeyDown(Keys.D) || keyboardState.IsKeyDown(Keys.Right)) horizontal += 1f;
        _run.Step(
            new PlayerInput(horizontal, _pendingJump, keyboardState.IsKeyDown(Keys.Space)),
            _pendingFire,
            SimulationStep);
        if (_displayedHealth != _run.Player.Health || _displayedEnemyCount != _run.Enemies.LivingCount ||
            _displayedAllEnemiesDead != _run.Enemies.AllEnemiesDead)
            UpdateWindowTitle();
    }

    private void DrawEnemies(BasicEffect effect)
    {
        const int steps = 16;
        if (_run is null) return;
        foreach (var enemy in _run.Enemies.LivingEnemies)
        {
            var center = enemy.ColliderCenter;
            var color = enemy.DamageFlashTime > 0f ? Color.White : new Color(255, 104, 64);
            var vertices = new VertexPositionColor[steps + 1];
            vertices[0] = new VertexPositionColor(new Vector3(center.X, center.Y, -0.25f), color);
            for (var index = 0; index < steps; index++)
            {
                var angle = index * MathF.Tau / steps;
                var radius = enemy.ColliderRadius * (index % 2 == 0 ? 1f : 0.82f);
                vertices[index + 1] = new VertexPositionColor(
                    new Vector3(center.X + MathF.Cos(angle) * radius, center.Y + MathF.Sin(angle) * radius, -0.25f),
                    color);
            }
            var indices = new int[steps * 3];
            for (var index = 0; index < steps; index++)
            {
                indices[index * 3] = 0;
                indices[index * 3 + 1] = index + 1;
                indices[index * 3 + 2] = (index + 1) % steps + 1;
            }
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, indices.Length / 3);
            }
        }
    }

    private void DrawNavigationDebug(BasicEffect effect)
    {
        if (_run is null) return;
        var navigationGrid = _run.NavigationGrid;
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

        var selected = _run.Enemies.LivingEnemies.FirstOrDefault();
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

    private void DrawCombatDebug(BasicEffect effect)
    {
        if (_run is null) return;
        VertexPositionColor[] aimVertices = [];
        if (_showCollisionDebug)
        {
            var direction = _run.GetAimDirection();
            var muzzle = _run.GetMuzzlePosition();
            var aimEnd = muzzle + direction * 1.5f;
            aimVertices =
            [
                new VertexPositionColor(new Vector3(muzzle.X, muzzle.Y, -0.35f), new Color(255, 196, 64)),
                new VertexPositionColor(new Vector3(aimEnd.X, aimEnd.Y, -0.35f), new Color(255, 196, 64)),
            ];
        }

        var projectileVertices = new List<VertexPositionColor>();
        var projectileIndices = new List<int>();
        const float debugRadius = 0.12f;
        foreach (var projectile in _run.Projectiles.Projectiles)
        {
            var center = projectile.ColliderCenter;
            var start = projectileVertices.Count;
            projectileVertices.Add(new VertexPositionColor(new Vector3(center.X, center.Y + debugRadius, -0.4f), Color.Yellow));
            projectileVertices.Add(new VertexPositionColor(new Vector3(center.X + debugRadius, center.Y, -0.4f), Color.Yellow));
            projectileVertices.Add(new VertexPositionColor(new Vector3(center.X, center.Y - debugRadius, -0.4f), Color.Yellow));
            projectileVertices.Add(new VertexPositionColor(new Vector3(center.X - debugRadius, center.Y, -0.4f), Color.Yellow));
            projectileIndices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (aimVertices.Length > 0)
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, aimVertices, 0, 1);
            if (projectileIndices.Count > 0)
                GraphicsDevice.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    projectileVertices.ToArray(),
                    0,
                    projectileVertices.Count,
                    projectileIndices.ToArray(),
                    0,
                    projectileIndices.Count / 3);
        }
    }

    private void UpdateWindowTitle()
    {
        if (_run is null) return;
        _displayedHealth = _run.Player.Health;
        _displayedEnemyCount = _run.Enemies.LivingCount;
        _displayedAllEnemiesDead = _run.Enemies.AllEnemiesDead;
        var state = _run.State switch
        {
            RunState.PlayerDead => " - PLAYER DEAD",
            RunState.AllBirdsDead => " - ALL BIRDS DEAD",
            _ => " - PLAYING",
        };
        Window.Title = $"Bird Hell - HP {_displayedHealth}/100 - Enemies {_displayedEnemyCount}/15{state} - " +
                       $"A/D move, SPACE jump, LMB/Ctrl/A fire, R new, F5 same, O outlines, C collision, N navigation, ESC exit - Seed: {_run.Seed}";
    }
}
