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
    private BasicEffect? _effect;
    private CaveMap? _caveMap;
    private CaveGeometry? _geometry;
    private VertexPositionColor[] _terrainVertices = [];
    private int[] _triangleIndices = [];
    private VertexPositionColor[] _outlineVertices = [];
    private KeyboardState _previousKeyboardState;
    private string _currentSeed = string.Empty;
    private bool _showOutlines;

    public BirdHellGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Bird Hell Standalone";
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        GenerateNewCave();
        _previousKeyboardState = Keyboard.GetState();
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
        if (keyboardState.IsKeyDown(Keys.Escape)) Exit();
        if (WasPressed(keyboardState, Keys.R)) GenerateNewCave();
        else if (WasPressed(keyboardState, Keys.Space)) GenerateCave(_currentSeed);
        if (WasPressed(keyboardState, Keys.O))
        {
            _showOutlines = !_showOutlines;
            UpdateWindowTitle();
        }
        _previousKeyboardState = keyboardState;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 12, 16));
        if (_geometry is not null && _effect is not null) DrawCaveGeometry(_geometry, _effect);
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

    private void DrawCaveGeometry(CaveGeometry geometry, BasicEffect effect)
    {
        var viewport = GraphicsDevice.Viewport;
        const float paddingScale = 1.05f;
        var geometryWidth = geometry.MaxX - geometry.MinX;
        var geometryHeight = geometry.MaxY - geometry.MinY;
        var viewportAspect = viewport.Width / (float)viewport.Height;
        var halfWidth = geometryWidth * paddingScale / 2f;
        var halfHeight = geometryHeight * paddingScale / 2f;
        if (geometryWidth / geometryHeight > viewportAspect) halfHeight = halfWidth / viewportAspect;
        else halfWidth = halfHeight * viewportAspect;

        effect.Projection = Matrix.CreateOrthographicOffCenter(-halfWidth, halfWidth, -halfHeight, halfHeight, 0f, 1f);
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

    private void UpdateWindowTitle() =>
        Window.Title = $"Bird Hell Standalone - Seed: {_currentSeed} - Outlines: {(_showOutlines ? "On" : "Off")}";
}
