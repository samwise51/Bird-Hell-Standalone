using BirdHell.Core;
using BirdHell.Diagnostics;

BirdHellCommandLineOptions options;
try
{
    options = BirdHellCommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine($"Bird Hell: {exception.Message}");
    Environment.ExitCode = 2;
    return;
}

using var game = new BirdHellGame(options.CaveProfile, options.FlightRecorder);
game.Run();
