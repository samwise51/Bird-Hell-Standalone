using BirdHell.Diagnostics;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: BirdHell.FlightAnalyzer <session-directory> [--output <directory>] [--bird <id> --export-bundle <directory>]");
    return 2;
}

try
{
    var session = Path.GetFullPath(args[0]);
    string? output = null;
    string? bundle = null;
    int? birdId = null;
    for (var index = 1; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "--output": output = ReadValue(args, ref index); break;
            case "--bird":
                if (!int.TryParse(ReadValue(args, ref index), out var parsed) || parsed < 0)
                    throw new ArgumentException("--bird requires a non-negative integer ID.");
                birdId = parsed;
                break;
            case "--export-bundle": bundle = ReadValue(args, ref index); break;
            default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
        }
    }
    if ((birdId is null) != (bundle is null))
        throw new ArgumentException("--bird and --export-bundle must be used together.");

    var summary = FlightRecordingAnalyzer.Analyze(session);
    var reportDirectory = Path.GetFullPath(output ?? Path.Combine(session, "analysis"));
    FlightRecordingAnalyzer.WriteReports(summary, reportDirectory);
    if (birdId is { } id) FlightRecordingAnalyzer.ExportBirdBundle(session, id, Path.GetFullPath(bundle!));
    Console.WriteLine($"Analyzed {session}");
    Console.WriteLine($"Markdown: {Path.Combine(reportDirectory, "report.md")}");
    Console.WriteLine($"JSON: {Path.Combine(reportDirectory, "summary.json")}");
    if (bundle is not null) Console.WriteLine($"Bird bundle: {Path.GetFullPath(bundle)}");
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or IOException or JsonException)
{
    Console.Error.WriteLine($"Flight analyzer: {exception.Message}");
    return 2;
}

static string ReadValue(string[] arguments, ref int index)
{
    if (++index >= arguments.Length) throw new ArgumentException($"Option '{arguments[index - 1]}' requires a value.");
    return arguments[index];
}
