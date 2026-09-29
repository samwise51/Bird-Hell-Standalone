namespace BirdHell.Diagnostics;

public sealed record FlightRecorderOptions
{
    public const int DefaultMaximumEventMegabytes = 64;
    public const int DefaultMaximumIncidentMegabytes = 64;
    public const int DefaultPreIncidentSeconds = 5;
    public const int DefaultPostIncidentSeconds = 5;

    public bool Enabled { get; init; }
    public string? OutputDirectory { get; init; }
    public int MaximumEventMegabytes { get; init; } = DefaultMaximumEventMegabytes;
    public int MaximumIncidentMegabytes { get; init; } = DefaultMaximumIncidentMegabytes;
    public int PreIncidentSeconds { get; init; } = DefaultPreIncidentSeconds;
    public int PostIncidentSeconds { get; init; } = DefaultPostIncidentSeconds;

    public static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BirdHell",
        "flight-recordings");

    public string ResolveOutputDirectory() => Path.GetFullPath(OutputDirectory ?? DefaultOutputDirectory);
}

public sealed record BirdHellCommandLineOptions(
    World.CaveGenerationProfileSelection CaveProfile,
    FlightRecorderOptions FlightRecorder,
    FrameStallDiagnosticOptions StallDiagnostics);

public static class BirdHellCommandLine
{
    public const string EnableRecorderOption = "--flight-recorder";
    public const string RecorderDirectoryOption = "--flight-recorder-dir";
    public const string RecorderMaximumMegabytesOption = "--flight-recorder-max-mb";
    public const string RecorderIncidentMaximumMegabytesOption = "--flight-recorder-incident-max-mb";
    public const string EnableStallDiagnosticsOption = "--stall-diagnostics";
    public const string StallDiagnosticsDirectoryOption = "--stall-diagnostics-dir";

    public static BirdHellCommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var caveArguments = new List<string>();
        var enabled = false;
        var enableOptionSeen = false;
        string? directory = null;
        var eventMegabytes = FlightRecorderOptions.DefaultMaximumEventMegabytes;
        var incidentMegabytes = FlightRecorderOptions.DefaultMaximumIncidentMegabytes;
        var stallDiagnosticsEnabled = false;
        var stallEnableOptionSeen = false;
        string? stallDiagnosticsDirectory = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case EnableRecorderOption:
                    if (enableOptionSeen) throw new ArgumentException($"Option '{EnableRecorderOption}' may be specified only once.");
                    enableOptionSeen = true;
                    enabled = true;
                    break;
                case RecorderDirectoryOption:
                    if (directory is not null) throw new ArgumentException($"Option '{RecorderDirectoryOption}' may be specified only once.");
                    directory = ReadValue(arguments, ref index, argument);
                    enabled = true;
                    break;
                case RecorderMaximumMegabytesOption:
                    eventMegabytes = ReadPositiveInteger(arguments, ref index, argument);
                    enabled = true;
                    break;
                case RecorderIncidentMaximumMegabytesOption:
                    incidentMegabytes = ReadPositiveInteger(arguments, ref index, argument);
                    enabled = true;
                    break;
                case EnableStallDiagnosticsOption:
                    if (stallEnableOptionSeen)
                        throw new ArgumentException($"Option '{EnableStallDiagnosticsOption}' may be specified only once.");
                    stallEnableOptionSeen = true;
                    stallDiagnosticsEnabled = true;
                    break;
                case StallDiagnosticsDirectoryOption:
                    if (stallDiagnosticsDirectory is not null)
                        throw new ArgumentException($"Option '{StallDiagnosticsDirectoryOption}' may be specified only once.");
                    stallDiagnosticsDirectory = ReadValue(arguments, ref index, argument);
                    stallDiagnosticsEnabled = true;
                    break;
                case World.CaveProfileCommandLine.OptionName:
                    caveArguments.Add(argument);
                    caveArguments.Add(ReadValue(arguments, ref index, argument));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        return new BirdHellCommandLineOptions(
            World.CaveProfileCommandLine.Parse(caveArguments),
            new FlightRecorderOptions
            {
                Enabled = enabled,
                OutputDirectory = directory,
                MaximumEventMegabytes = eventMegabytes,
                MaximumIncidentMegabytes = incidentMegabytes,
            },
            new FrameStallDiagnosticOptions
            {
                Enabled = stallDiagnosticsEnabled,
                OutputDirectory = stallDiagnosticsDirectory,
            });
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Option '{option}' requires a value.");
        return arguments[++index];
    }

    private static int ReadPositiveInteger(IReadOnlyList<string> arguments, ref int index, string option)
    {
        var text = ReadValue(arguments, ref index, option);
        if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0 || value > 4096)
            throw new ArgumentException($"Option '{option}' requires an integer from 1 through 4096.");
        return value;
    }
}
