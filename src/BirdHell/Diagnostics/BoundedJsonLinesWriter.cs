using System.Text;
using System.Text.Json;

namespace BirdHell.Diagnostics;

internal sealed class BoundedJsonLinesWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly long _maximumBytes;

    public BoundedJsonLinesWriter(string path, int maximumMegabytes)
    {
        _maximumBytes = maximumMegabytes * 1024L * 1024L;
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };
    }

    public long BytesWritten { get; private set; }
    public long DroppedRecords { get; private set; }

    public bool Write(object value, JsonSerializerOptions options)
    {
        var line = JsonSerializer.Serialize(value, value.GetType(), options);
        var bytes = Encoding.UTF8.GetByteCount(line) + 1;
        if (BytesWritten + bytes > _maximumBytes)
        {
            DroppedRecords++;
            return false;
        }
        _writer.WriteLine(line);
        BytesWritten += bytes;
        return true;
    }

    public void Dispose() => _writer.Dispose();
}
