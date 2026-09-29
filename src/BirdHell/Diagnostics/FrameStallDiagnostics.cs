using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BirdHell.Diagnostics;

public sealed record FrameStallDiagnosticOptions
{
    public bool Enabled { get; init; }
    public string? OutputDirectory { get; init; }

    public static string DefaultOutputDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BirdHell",
        "stall-diagnostics");

    public string ResolveOutputDirectory() => Path.GetFullPath(OutputDirectory ?? DefaultOutputDirectory);
}

public readonly record struct StallTimingSample(long Timestamp, int Gen0, int Gen1, int Gen2);

public readonly record struct StallSectionTiming(StallTimingSample Start, StallTimingSample End)
{
    public long ElapsedStopwatchTicks => End.Timestamp - Start.Timestamp;
}

internal struct RenderBatchTimings
{
    public StallSectionTiming Preparation;
    public StallSectionTiming Begin;
    public StallSectionTiming Submission;
    public StallSectionTiming End;
}

/// <summary>Opt-in, low-volume timing diagnostics. JSON serialization and flushing run off the game thread.</summary>
public sealed class FrameStallDiagnostics : IDisposable
{
    public const double ThresholdMilliseconds = 100d;
    public const double NestedThresholdMilliseconds = 25d;
    public const double FineNestedThresholdMilliseconds = 5d;
    public static readonly long ThresholdStopwatchTicks =
        (long)Math.Ceiling(Stopwatch.Frequency * ThresholdMilliseconds / 1000d);
    public static readonly long NestedThresholdStopwatchTicks =
        (long)Math.Ceiling(Stopwatch.Frequency * NestedThresholdMilliseconds / 1000d);
    public static readonly long FineNestedThresholdStopwatchTicks =
        (long)Math.Ceiling(Stopwatch.Frequency * FineNestedThresholdMilliseconds / 1000d);
    private const int SchemaVersion = 1;

    private readonly bool _flightRecorderEnabled;
    private readonly Channel<StallDiagnosticEvent> _events;
    private readonly StreamWriter _writer;
    private readonly Task _writerTask;
    private StallTimingSample _lastCallbackEnd;
    private StallTimingSample _lastUpdateStart;
    private string? _lastCallback;
    private long _nextEventGroupId;
    private bool _disposed;

    private FrameStallDiagnostics(string outputFile, bool flightRecorderEnabled)
    {
        OutputFile = outputFile;
        _flightRecorderEnabled = flightRecorderEnabled;
        _events = Channel.CreateUnbounded<StallDiagnosticEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });
        _writer = new StreamWriter(
            new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };
        _writerTask = Task.Run(WriteEventsAsync);
    }

    public string OutputFile { get; }

    public static FrameStallDiagnostics? Start(FrameStallDiagnosticOptions options, bool flightRecorderEnabled)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled) return null;

        var directory = options.ResolveOutputDirectory();
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddTHHmmss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        var outputFile = Path.Combine(directory, $"stalls-{stamp}-{Guid.NewGuid():N}.jsonl");
        var diagnostics = new FrameStallDiagnostics(outputFile, flightRecorderEnabled);
        Console.Error.WriteLine($"Bird Hell stall diagnostics: {outputFile}");
        return diagnostics;
    }

    public static StallTimingSample Capture() => new(
        Stopwatch.GetTimestamp(),
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2));

    public static bool IsLongFrame(long elapsedStopwatchTicks) =>
        elapsedStopwatchTicks >= ThresholdStopwatchTicks;

    public static bool IsLongNestedSection(long elapsedStopwatchTicks) =>
        elapsedStopwatchTicks >= NestedThresholdStopwatchTicks;

    public static bool IsLongFineNestedSection(long elapsedStopwatchTicks) =>
        elapsedStopwatchTicks >= FineNestedThresholdStopwatchTicks;

    public static StallSectionTiming Complete(StallTimingSample start) => new(start, Capture());

    public void ObserveCallbackStart(string callback, TimeSpan gameTime)
    {
        var now = Capture();
        if (_lastCallbackEnd.Timestamp != 0)
            RecordElapsed(
                "between_callbacks",
                _lastCallbackEnd.Timestamp,
                now.Timestamp,
                gameTime,
                0,
                _lastCallbackEnd,
                _lastCallback,
                callback,
                null,
                NextEventGroupId(),
                ThresholdStopwatchTicks,
                ThresholdMilliseconds);
        if (callback == "update")
        {
            if (_lastUpdateStart.Timestamp != 0)
                RecordElapsed(
                    "update_interval",
                    _lastUpdateStart.Timestamp,
                    now.Timestamp,
                    gameTime,
                    0,
                    _lastUpdateStart,
                    null,
                    null,
                    null,
                    NextEventGroupId(),
                    ThresholdStopwatchTicks,
                    ThresholdMilliseconds);
            _lastUpdateStart = now;
        }
    }

    public void ObserveCallbackEnd(string callback)
    {
        _lastCallback = callback;
        _lastCallbackEnd = Capture();
    }

    public void RecordSection(
        string section,
        StallTimingSample start,
        TimeSpan gameTime,
        int fixedStepCount = 0)
    {
        RecordRootSection(section, Complete(start), gameTime, fixedStepCount);
    }

    public long RecordRootSection(
        string section,
        StallSectionTiming timing,
        TimeSpan gameTime,
        int fixedStepCount = 0)
    {
        if (!IsLongFrame(timing.ElapsedStopwatchTicks)) return 0;
        var eventGroupId = NextEventGroupId();
        RecordElapsed(
            section,
            timing.Start.Timestamp,
            timing.End.Timestamp,
            gameTime,
            fixedStepCount,
            timing.Start,
            null,
            null,
            null,
            eventGroupId,
            ThresholdStopwatchTicks,
            ThresholdMilliseconds,
            timing.End);
        return eventGroupId;
    }

    public bool RecordNestedSection(
        string section,
        string parentSection,
        long eventGroupId,
        StallSectionTiming timing,
        TimeSpan gameTime,
        int fixedStepCount = 0)
    {
        if (eventGroupId == 0 || !IsLongNestedSection(timing.ElapsedStopwatchTicks)) return false;
        RecordElapsed(
            section,
            timing.Start.Timestamp,
            timing.End.Timestamp,
            gameTime,
            fixedStepCount,
            timing.Start,
            null,
            null,
            parentSection,
            eventGroupId,
            NestedThresholdStopwatchTicks,
            NestedThresholdMilliseconds,
            timing.End);
        return true;
    }

    public bool RecordFineNestedSection(
        string section,
        string parentSection,
        long eventGroupId,
        StallSectionTiming timing,
        TimeSpan gameTime)
    {
        if (eventGroupId == 0 || !IsLongFineNestedSection(timing.ElapsedStopwatchTicks)) return false;
        RecordElapsed(
            section,
            timing.Start.Timestamp,
            timing.End.Timestamp,
            gameTime,
            0,
            timing.Start,
            null,
            null,
            parentSection,
            eventGroupId,
            FineNestedThresholdStopwatchTicks,
            FineNestedThresholdMilliseconds,
            timing.End);
        return true;
    }

    private void RecordElapsed(
        string section,
        long start,
        long end,
        TimeSpan gameTime,
        int fixedStepCount,
        StallTimingSample gcBefore,
        string? previousCallback,
        string? nextCallback,
        string? parentSection,
        long eventGroupId,
        long thresholdStopwatchTicks,
        double thresholdMilliseconds,
        StallTimingSample? capturedEnd = null)
    {
        var elapsedTicks = end - start;
        if (elapsedTicks < thresholdStopwatchTicks) return;

        var gcAfter = capturedEnd ?? Capture();
        _events.Writer.TryWrite(new StallDiagnosticEvent(
            SchemaVersion,
            eventGroupId,
            DateTimeOffset.UtcNow,
            gameTime.TotalSeconds,
            section,
            parentSection,
            DescribeScope(section),
            elapsedTicks * 1000d / Stopwatch.Frequency,
            thresholdMilliseconds,
            fixedStepCount,
            fixedStepCount > 1,
            previousCallback,
            nextCallback,
            _flightRecorderEnabled,
            new GcCollectionCounts(
                gcBefore.Gen0,
                gcBefore.Gen1,
                gcBefore.Gen2,
                gcAfter.Gen0,
                gcAfter.Gen1,
                gcAfter.Gen2,
                gcAfter.Gen0 - gcBefore.Gen0,
                gcAfter.Gen1 - gcBefore.Gen1,
                gcAfter.Gen2 - gcBefore.Gen2)));
    }

    private long NextEventGroupId() => ++_nextEventGroupId;

    private static string DescribeScope(string section) => section switch
    {
        "update" => "BirdHellGame.Update override: first statement through base.Update completion, before return",
        "draw" => "BirdHellGame.Draw override: first statement through base.Draw completion, before return",
        "update_interval" => "one BirdHellGame.Update entry marker to the next Update entry marker",
        "between_callbacks" => "previous Update/Draw end marker to the next Update/Draw entry marker; framework, present, and idle time",
        _ => "named subsection within its parent callback",
    };

    private async Task WriteEventsAsync()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await foreach (var item in _events.Reader.ReadAllAsync())
            await _writer.WriteLineAsync(JsonSerializer.Serialize(item, json));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _events.Writer.TryComplete();
        _writerTask.GetAwaiter().GetResult();
        _writer.Dispose();
        _disposed = true;
    }

    private sealed record StallDiagnosticEvent(
        int SchemaVersion,
        long EventGroupId,
        DateTimeOffset TimestampUtc,
        double GameTimeSeconds,
        string Section,
        string? ParentSection,
        string MeasurementScope,
        double ElapsedMilliseconds,
        double ThresholdMilliseconds,
        int FixedStepCount,
        bool SimulationCatchUp,
        string? PreviousCallback,
        string? NextCallback,
        bool FlightRecorderEnabled,
        GcCollectionCounts GcCollections);

    private sealed record GcCollectionCounts(
        int Gen0Before,
        int Gen1Before,
        int Gen2Before,
        int Gen0After,
        int Gen1After,
        int Gen2After,
        int Gen0Delta,
        int Gen1Delta,
        int Gen2Delta);
}
