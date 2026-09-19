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
    private readonly CaveGenerator _caveGenerator = new();
    private readonly CaveGeometryBuilder _geometryBuilder = new();
    private readonly WeaponSystem _weaponSystem = new();
    private readonly WeaponState _weaponState = new();
    private BasicEffect? _effect;
    private CaveMap? _caveMap;
    private CaveGeometry? _geometry;
    private Player? _player;
    private TerrainCollisionSystem? _terrainCollision;
    private PlayerMovementSystem? _playerMovement;
    private ProjectileSystem? _projectileSystem;
    private VertexPositionColor[] _terrainVertices = [];
    private int[] _triangleIndices = [];
    private VertexPositionColor[] _outlineVertices = [];
    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private GamePadState _previousGamePadState;
    private string _currentSeed = string.Empty;
    private bool _showOutlines;
    private bool _showCollisionDebug;
    private bool _pendingJump;
    private bool _pendingFire;
    private double _simulationAccumulator;
    private float _cameraX;

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
        GenerateNewCave();
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
        if (WasPressed(keyboardState, Keys.R)) GenerateNewCave();
        else if (WasPressed(keyboardState, Keys.F5)) GenerateCave(_currentSeed);
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

        _pendingJump |= WasPressed(keyboardState, Keys.Space);
        _pendingFire |= WasPressed(keyboardState, Keys.LeftControl) ||
                        mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released ||
                        gamePadState.IsButtonDown(Buttons.A) && _previousGamePadState.IsButtonUp(Buttons.A);
        _simulationAccumulator += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, MaximumFrameTime);
        while (_simulationAccumulator >= SimulationStep)
        {
            SimulatePlayer(keyboardState);
            SimulateCombat();
            _pendingJump = false;
            _pendingFire = false;
            _simulationAccumulator -= SimulationStep;
        }
        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
        _previousGamePadState = gamePadState;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 12, 16));
        if (_geometry is not null && _effect is not null)
        {
            ConfigureCamera(_geometry, _effect);
            DrawCaveGeometry(_effect);
            if (_player is not null) DrawPlayer(_player, _effect);
            DrawCombatDebug(_effect);
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _effect?.Dispose();
    }

    private bool WasPressed(KeyboardState currentState, Keys key) =>
        currentState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void GenerateNewCave() => GenerateCave(CaveGenerator.CreateRandomSeed());

    private void GenerateCave(string seed)
    {
        _currentSeed = seed;
        _caveMap = _caveGenerator.Generate(seed);
        _geometry = _geometryBuilder.Build(_caveMap);
        _terrainCollision = new TerrainCollisionSystem(_geometry);
        _playerMovement = new PlayerMovementSystem(_terrainCollision);
        _projectileSystem = new ProjectileSystem(_terrainCollision);
        var spawn = PlayerSpawnSystem.FindSafeSpawn(_geometry, PlayerCollider.Original);
        if (_player is null) _player = new Player(spawn);
        else _player.Reset(spawn);
        _terrainCollision.RefreshContacts(_player);
        _simulationAccumulator = 0d;
        _pendingJump = false;
        _pendingFire = false;
        _weaponState.Reset();
        _cameraX = _player.Position.X;
        BuildRenderData(_geometry);
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
        var targetX = _player?.Position.X ?? 0f;
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

        var playerColor = new Color(72, 210, 255);
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

    private void SimulatePlayer(KeyboardState keyboardState)
    {
        if (_player is null || _playerMovement is null) return;
        var horizontal = 0f;
        if (keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.Left)) horizontal -= 1f;
        if (keyboardState.IsKeyDown(Keys.D) || keyboardState.IsKeyDown(Keys.Right)) horizontal += 1f;
        _playerMovement.Step(
            _player,
            new PlayerInput(horizontal, _pendingJump, keyboardState.IsKeyDown(Keys.Space)),
            SimulationStep);
    }

    private void SimulateCombat()
    {
        if (_player is null || _projectileSystem is null) return;
        var projectile = _weaponSystem.Fire(_weaponState, _player, _pendingFire);
        if (projectile is not null) _projectileSystem.Spawn(projectile);
        _projectileSystem.Step(SimulationStep);
    }

    private void DrawCombatDebug(BasicEffect effect)
    {
        if (_player is null || _projectileSystem is null) return;
        var direction = _weaponSystem.GetAimDirection(_player);
        var muzzle = _weaponSystem.GetMuzzlePosition(_player);
        var aimEnd = muzzle + direction * 1.5f;
        var aimVertices = new[]
        {
            new VertexPositionColor(new Vector3(muzzle.X, muzzle.Y, -0.35f), new Color(255, 196, 64)),
            new VertexPositionColor(new Vector3(aimEnd.X, aimEnd.Y, -0.35f), new Color(255, 196, 64)),
        };

        var projectileVertices = new List<VertexPositionColor>();
        var projectileIndices = new List<int>();
        const float debugRadius = 0.12f;
        foreach (var projectile in _projectileSystem.Projectiles)
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

    private void UpdateWindowTitle() =>
        Window.Title = $"Bird Hell - A/D or Arrows move, SPACE jump, LMB/Left Ctrl/controller A fire, R new, F5 same, O outlines, C collision, ESC exit - Seed: {_currentSeed}";
}
