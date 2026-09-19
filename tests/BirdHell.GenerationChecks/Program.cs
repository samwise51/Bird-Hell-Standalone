using BirdHell.World;

var generator = new CaveGenerator();
var first = generator.Generate("milestone-1-repeatable-seed");
var second = generator.Generate("milestone-1-repeatable-seed");

Assert(first.Width == CaveGenerationOptions.DefaultWidth, "Generated width does not match configuration.");
Assert(first.Height == CaveGenerationOptions.DefaultHeight, "Generated height does not match configuration.");
for (var x = 0; x < first.Width; x++)
for (var y = 0; y < first.Height; y++)
{
    Assert(first[x, y] is CaveTile.Open or CaveTile.Wall, $"Unexpected tile at ({x}, {y}).");
    Assert(first[x, y] == second[x, y], $"Same-seed maps differ at ({x}, {y}).");
}

Console.WriteLine("PASS: default generation completed without throwing");
Console.WriteLine("PASS: generated dimensions match configured dimensions");
Console.WriteLine("PASS: identical explicit seeds produce identical maps");
Console.WriteLine("PASS: all cells contain valid logical tile values");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
