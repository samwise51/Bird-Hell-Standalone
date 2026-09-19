using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BirdHell.Core;

public sealed class BirdHellGame : Game
{
    private readonly GraphicsDeviceManager _graphics;

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
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        base.Draw(gameTime);
    }
}
