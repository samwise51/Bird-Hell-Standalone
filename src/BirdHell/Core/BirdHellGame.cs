using BirdHell.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace BirdHell.Core;

public sealed class BirdHellGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly CaveGenerator _caveGenerator = new();
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private CaveMap? _caveMap;
    private KeyboardState _previousKeyboardState;
    private string _currentSeed = string.Empty;

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
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboardState = Keyboard.GetState();
        if (keyboardState.IsKeyDown(Keys.Escape)) Exit();
        if (WasPressed(keyboardState, Keys.R)) GenerateNewCave();
        else if (WasPressed(keyboardState, Keys.Space)) GenerateCave(_currentSeed);
        _previousKeyboardState = keyboardState;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 12, 16));
        if (_caveMap is not null && _spriteBatch is not null && _pixel is not null)
            DrawCave(_caveMap, _spriteBatch, _pixel);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
    }

    private bool WasPressed(KeyboardState currentState, Keys key) =>
        currentState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void GenerateNewCave() => GenerateCave(CaveGenerator.CreateRandomSeed());

    private void GenerateCave(string seed)
    {
        _currentSeed = seed;
        _caveMap = _caveGenerator.Generate(seed);
        Window.Title = $"Bird Hell Standalone - Seed: {_currentSeed}";
    }

    private void DrawCave(CaveMap map, SpriteBatch spriteBatch, Texture2D pixel)
    {
        const int padding = 16;
        var viewport = GraphicsDevice.Viewport;
        var tileSize = Math.Max(1, Math.Min(
            (viewport.Width - (padding * 2)) / map.Width,
            (viewport.Height - (padding * 2)) / map.Height));
        var offsetX = (viewport.Width - (map.Width * tileSize)) / 2;
        var offsetY = (viewport.Height - (map.Height * tileSize)) / 2;
        var wallColor = new Color(34, 40, 49);
        var openColor = new Color(218, 209, 176);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            var destination = new Rectangle(offsetX + (x * tileSize), offsetY + (y * tileSize), tileSize, tileSize);
            spriteBatch.Draw(pixel, destination, map[x, y] == CaveTile.Wall ? wallColor : openColor);
        }
        spriteBatch.End();
    }
}
