using System.Diagnostics;

public static class ProgramHelper
{
    public static string GetEventFilePath(string executable)
    {
        var executablePath = Path.GetFullPath(executable);
        var fileName = executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native-events.jsonl" : "hybrid-events.jsonl";
        return Path.Combine(Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory, fileName);
    }

    public static long GetEventFileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    public static async Task<bool> WaitForFreshEventAsync(string path, string name, TimeSpan timeout, long startPosition = 0)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) break;
            await Task.Delay(50);
        }

        if (!File.Exists(path)) return false;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Min(startPosition, stream.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) { await Task.Delay(50); continue; }
            if (line.Contains($"\"eventName\":\"{name}\"", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static IEnumerable<string> ReadEventLines(string path, long startPosition)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Min(startPosition, stream.Length), SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }
}
