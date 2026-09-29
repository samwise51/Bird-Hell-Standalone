using BirdHell.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BirdHell.Presentation;

public readonly record struct MenuMouseHit(int ItemIndex, MenuAction Action);

public static class MenuPulse
{
    public const double CycleSeconds = 1.75;

    public static float Value(double totalSeconds) =>
        0.5f + 0.5f * MathF.Sin((float)(totalSeconds / CycleSeconds * Math.PI * 2d));
}

/// <summary>Lightweight screen-space menu renderer and deterministic control layout.</summary>
public sealed class MenuPresentation : IDisposable
{
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _pixel;

    public MenuPresentation(GraphicsDevice graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _batch = new SpriteBatch(graphics);
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData([Color.White]);
    }

    public MenuMouseHit? HitTest(GameFlow flow, Point point)
    {
        var controls = Layout(flow.Screen, _graphics.Viewport.Bounds);
        for (var index = 0; index < controls.Length; index++)
        {
            var control = controls[index];
            if (!control.Bounds.Contains(point)) continue;
            if (control.LeftArrow.Contains(point)) return new MenuMouseHit(index, MenuAction.Decrease);
            if (control.RightArrow.Contains(point)) return new MenuMouseHit(index, MenuAction.Increase);
            return new MenuMouseHit(index, MenuAction.Activate);
        }
        return null;
    }

    public void Draw(GameFlow flow, double totalSeconds, bool drawBackdrop = true)
    {
        var viewport = _graphics.Viewport.Bounds;
        var controls = Layout(flow.Screen, viewport);
        var pulse = MenuPulse.Value(totalSeconds);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (drawBackdrop) DrawBackdrop(viewport, totalSeconds);
        if (flow.Screen == GameScreen.MainMenu)
            DrawMainMenu(flow, controls, pulse, viewport);
        else if (flow.Screen == GameScreen.Options)
            DrawOptions(flow, controls, pulse, viewport);
        else if (flow.Screen == GameScreen.Paused)
            DrawPause(flow, controls, pulse, viewport);
        _batch.End();
    }

    private void DrawMainMenu(GameFlow flow, IReadOnlyList<Control> controls, float pulse, Rectangle viewport)
    {
        DrawCenteredText("BIRD HELL", viewport.Center.X, Math.Max(70, viewport.Height / 7), 8,
            new Color(255, 224, 238));
        DrawCenteredText("DESCEND. SURVIVE. FIGHT BACK.", viewport.Center.X,
            Math.Max(145, viewport.Height / 4), 2, new Color(170, 160, 198));
        var labels = new[] { "PLAY", "OPTIONS", "EXIT" };
        for (var index = 0; index < controls.Count; index++)
            DrawButton(controls[index].Bounds, labels[index], index == flow.FocusedIndex, pulse);
        DrawCenteredText("ARROWS / STICK TO MOVE     ENTER / A TO SELECT", viewport.Center.X,
            viewport.Bottom - 38, 2, new Color(125, 119, 155));
    }

    private void DrawOptions(GameFlow flow, IReadOnlyList<Control> controls, float pulse, Rectangle viewport)
    {
        DrawCenteredText("OPTIONS", viewport.Center.X, Math.Max(44, viewport.Height / 12), 6,
            new Color(255, 224, 238));
        var labels = new[] { "BIRD COUNT", "CAVE PROFILE", "INVINCIBILITY", "DISPLAY MODE" };
        var values = new[]
        {
            flow.Options.BirdCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            flow.Options.CaveProfileDisplayName.ToUpperInvariant(),
            flow.Options.Invincible ? "ON" : "OFF",
            flow.Options.Fullscreen ? "FULLSCREEN" : "WINDOWED",
        };
        for (var index = 0; index < 4; index++)
            DrawOptionRow(controls[index], labels[index], values[index], index == flow.FocusedIndex, pulse);
        DrawButton(controls[4].Bounds, "BACK", flow.FocusedIndex == 4, pulse);
        DrawCenteredText("LEFT / RIGHT TO CHANGE     ESC / VIEW TO GO BACK", viewport.Center.X,
            viewport.Bottom - 30, 2, new Color(125, 119, 155));
    }

    private void DrawPause(GameFlow flow, IReadOnlyList<Control> controls, float pulse, Rectangle viewport)
    {
        DrawCenteredText("PAUSED", viewport.Center.X, Math.Max(62, viewport.Height / 8), 7,
            new Color(255, 224, 238));
        var labels = new[] { "RESUME", "RESTART", "NEW CAVE", "RETURN TO MAIN MENU" };
        for (var index = 0; index < controls.Count; index++)
            DrawButton(controls[index].Bounds, labels[index], index == flow.FocusedIndex, pulse);
        DrawCenteredText("ESC / MENU TO RESUME", viewport.Center.X,
            viewport.Bottom - 38, 2, new Color(174, 164, 198));
    }

    private void DrawButton(Rectangle bounds, string label, bool focused, float pulse)
    {
        DrawFocus(bounds, focused, pulse);
        Fill(bounds, focused ? new Color(65, 31, 88, 235) : new Color(31, 27, 59, 220));
        Border(bounds, focused ? new Color(255, 87, 178) : new Color(90, 74, 121), focused ? 3 : 2);
        DrawCenteredText(label, bounds.Center.X, bounds.Center.Y - 11,
            4, focused ? new Color(255, 245, 251) : new Color(194, 186, 211));
    }

    private void DrawOptionRow(Control control, string label, string value, bool focused, float pulse)
    {
        var bounds = control.Bounds;
        DrawFocus(bounds, focused, pulse);
        Fill(bounds, focused ? new Color(57, 30, 79, 235) : new Color(28, 25, 54, 220));
        Border(bounds, focused ? new Color(244, 76, 168) : new Color(77, 66, 105), focused ? 3 : 1);
        DrawText(label, bounds.Left + 28, bounds.Center.Y - 8, 3,
            focused ? new Color(255, 230, 243) : new Color(184, 178, 203));
        DrawCenteredText("<", control.LeftArrow.Center.X, bounds.Center.Y - 8, 3,
            focused ? new Color(255, 114, 191) : new Color(124, 111, 151));
        DrawCenteredText(value, (control.LeftArrow.Right + control.RightArrow.Left) / 2,
            bounds.Center.Y - 8, 3, focused ? Color.White : new Color(205, 198, 220));
        DrawCenteredText(">", control.RightArrow.Center.X, bounds.Center.Y - 8, 3,
            focused ? new Color(255, 114, 191) : new Color(124, 111, 151));
    }

    private void DrawFocus(Rectangle bounds, bool focused, float pulse)
    {
        if (!focused) return;
        var outer = Inflate(bounds, 10 + (int)(pulse * 3));
        var middle = Inflate(bounds, 6);
        Border(outer, new Color(228, 42, 151, 25 + (int)(pulse * 25)), 5);
        Border(middle, new Color(255, 84, 181, 60 + (int)(pulse * 45)), 3);
    }

    private void DrawBackdrop(Rectangle viewport, double totalSeconds)
    {
        Fill(viewport, new Color(12, 11, 31));
        var bandHeight = Math.Max(1, viewport.Height / 12);
        for (var band = 0; band < 12; band++)
            Fill(new Rectangle(0, band * bandHeight, viewport.Width, bandHeight + 1),
                new Color(15 + band, 12 + band, 38 + band * 2));
        for (var index = 0; index < 28; index++)
        {
            var x = (index * 193 + 47) % Math.Max(1, viewport.Width);
            var y = (index * 83 + 29) % Math.Max(1, viewport.Height - 70);
            var shimmer = 0.55f + 0.45f * MathF.Sin((float)totalSeconds * 1.3f + index);
            Fill(new Rectangle(x, y, index % 5 == 0 ? 3 : 2, index % 5 == 0 ? 3 : 2),
                new Color(180, 139, 205) * shimmer);
        }
        Fill(new Rectangle(0, viewport.Bottom - 70, viewport.Width, 70), new Color(8, 8, 23, 230));
        Fill(new Rectangle(0, viewport.Bottom - 72, viewport.Width, 2), new Color(104, 33, 99, 150));
    }

    private void DrawCenteredText(string text, int centerX, int y, int scale, Color color) =>
        DrawText(text, centerX - TextWidth(text, scale) / 2, y, scale, color);

    private void DrawText(string text, int x, int y, int scale, Color color)
    {
        var cursor = x;
        foreach (var character in text)
        {
            var glyph = Glyph(character);
            for (var row = 0; row < 7; row++)
            for (var column = 0; column < 5; column++)
                if ((glyph[row] & (1 << (4 - column))) != 0)
                    Fill(new Rectangle(cursor + column * scale, y + row * scale, scale, scale), color);
            cursor += 6 * scale;
        }
    }

    private static int TextWidth(string text, int scale) => Math.Max(0, text.Length * 6 - 1) * scale;
    private void Fill(Rectangle rectangle, Color color) => _batch.Draw(_pixel, rectangle, color);

    private void Border(Rectangle rectangle, Color color, int thickness)
    {
        Fill(new Rectangle(rectangle.Left, rectangle.Top, rectangle.Width, thickness), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Bottom - thickness, rectangle.Width, thickness), color);
        Fill(new Rectangle(rectangle.Left, rectangle.Top, thickness, rectangle.Height), color);
        Fill(new Rectangle(rectangle.Right - thickness, rectangle.Top, thickness, rectangle.Height), color);
    }

    private static Rectangle Inflate(Rectangle value, int amount) =>
        new(value.X - amount, value.Y - amount, value.Width + amount * 2, value.Height + amount * 2);

    private static Control[] Layout(GameScreen screen, Rectangle viewport)
    {
        var centerX = viewport.Center.X;
        if (screen == GameScreen.MainMenu)
        {
            var width = Math.Min(390, viewport.Width - 60);
            var top = Math.Max(220, viewport.Height / 3);
            return Enumerable.Range(0, 3)
                .Select(index => new Control(new Rectangle(centerX - width / 2, top + index * 92, width, 64)))
                .ToArray();
        }

        if (screen == GameScreen.Paused)
        {
            var width = Math.Min(470, viewport.Width - 60);
            var top = Math.Max(105, viewport.Height / 4);
            var spacing = Math.Clamp((viewport.Height - top - 70) / 4, 62, 78);
            return Enumerable.Range(0, 4)
                .Select(index => new Control(new Rectangle(centerX - width / 2, top + index * spacing, width, 58)))
                .ToArray();
        }

        var rowWidth = Math.Min(760, viewport.Width - 50);
        var rowTop = Math.Max(135, viewport.Height / 5);
        var rows = new Control[5];
        for (var index = 0; index < 4; index++)
        {
            var bounds = new Rectangle(centerX - rowWidth / 2, rowTop + index * 82, rowWidth, 60);
            rows[index] = new Control(bounds,
                new Rectangle(bounds.Right - 280, bounds.Top, 60, bounds.Height),
                new Rectangle(bounds.Right - 60, bounds.Top, 60, bounds.Height));
        }
        var backWidth = Math.Min(300, viewport.Width - 80);
        rows[4] = new Control(new Rectangle(centerX - backWidth / 2, rowTop + 4 * 82 + 8, backWidth, 58));
        return rows;
    }

    private static byte[] Glyph(char value) => value switch
    {
        'A' => [14, 17, 17, 31, 17, 17, 17], 'B' => [30, 17, 17, 30, 17, 17, 30],
        'C' => [14, 17, 16, 16, 16, 17, 14], 'D' => [30, 17, 17, 17, 17, 17, 30],
        'E' => [31, 16, 16, 30, 16, 16, 31], 'F' => [31, 16, 16, 30, 16, 16, 16],
        'G' => [14, 17, 16, 23, 17, 17, 14], 'H' => [17, 17, 17, 31, 17, 17, 17],
        'I' => [31, 4, 4, 4, 4, 4, 31], 'J' => [7, 2, 2, 2, 18, 18, 12],
        'K' => [17, 18, 20, 24, 20, 18, 17], 'L' => [16, 16, 16, 16, 16, 16, 31],
        'M' => [17, 27, 21, 21, 17, 17, 17], 'N' => [17, 25, 21, 19, 17, 17, 17],
        'O' => [14, 17, 17, 17, 17, 17, 14], 'P' => [30, 17, 17, 30, 16, 16, 16],
        'Q' => [14, 17, 17, 17, 21, 18, 13], 'R' => [30, 17, 17, 30, 20, 18, 17],
        'S' => [15, 16, 16, 14, 1, 1, 30], 'T' => [31, 4, 4, 4, 4, 4, 4],
        'U' => [17, 17, 17, 17, 17, 17, 14], 'V' => [17, 17, 17, 17, 17, 10, 4],
        'W' => [17, 17, 17, 21, 21, 21, 10], 'X' => [17, 17, 10, 4, 10, 17, 17],
        'Y' => [17, 17, 10, 4, 4, 4, 4], 'Z' => [31, 1, 2, 4, 8, 16, 31],
        '0' => [14, 17, 19, 21, 25, 17, 14], '1' => [4, 12, 4, 4, 4, 4, 14],
        '2' => [14, 17, 1, 2, 4, 8, 31], '3' => [30, 1, 1, 14, 1, 1, 30],
        '4' => [2, 6, 10, 18, 31, 2, 2], '5' => [31, 16, 16, 30, 1, 1, 30],
        '6' => [14, 16, 16, 30, 17, 17, 14], '7' => [31, 1, 2, 4, 8, 8, 8],
        '8' => [14, 17, 17, 14, 17, 17, 14], '9' => [14, 17, 17, 15, 1, 1, 14],
        '<' => [2, 4, 8, 16, 8, 4, 2], '>' => [8, 4, 2, 1, 2, 4, 8],
        '/' => [1, 1, 2, 4, 8, 16, 16], '.' => [0, 0, 0, 0, 0, 12, 12],
        _ => [0, 0, 0, 0, 0, 0, 0],
    };

    public void Dispose()
    {
        _pixel.Dispose();
        _batch.Dispose();
    }

    private readonly record struct Control(Rectangle Bounds, Rectangle LeftArrow = default, Rectangle RightArrow = default);
}
