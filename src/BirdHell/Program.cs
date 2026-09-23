using BirdHell.Core;
using BirdHell.World;

CaveGenerationProfileSelection caveProfile;
try
{
    caveProfile = CaveProfileCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Bird Hell: {exception.Message}");
    Environment.ExitCode = 2;
    return;
}

using var game = new BirdHellGame(caveProfile);
game.Run();
