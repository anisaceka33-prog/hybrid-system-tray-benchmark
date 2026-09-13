using System.Globalization;
using WebView2SystemTrayBenchmark.Shared;

namespace Shared.Tests;

public class CsvSerializationTests
{
    [Fact]
    public void DoubleIsSerializedWithDotRegardlessOfCulture()
    {
        var record = new MeasurementRecord("r","s", DateTimeOffset.Parse("2026-09-13T17:40:43.8797231+00:00"), "hybrid", "reuse", 1024, "process-to-ui", "tti", 3632.8454, "ms", 1, "ok", null, "c", "8.0.30");
        var writer = new ResultWriter();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = writer.WriteMeasurements(new[] { record }, dir, "test");
        var line = File.ReadAllLines(path)[1];
        // ensure value uses dot
        Assert.Contains("3632.8454", line);
        Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("it-IT")]
    public void SerializationStableUnderCommaDecimalCultures(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var record = new MeasurementRecord("r","s", DateTimeOffset.UtcNow, "hybrid", "reuse", 1024, "process-to-ui", "tti", 56.2468, "ms", 1, "ok", null, "c", "8.0.30");
            var writer = new ResultWriter();
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var path = writer.WriteMeasurements(new[] { record }, dir, "test");
            var line = File.ReadAllLines(path)[1];
            Assert.Contains("56.2468", line);
            Directory.Delete(dir, true);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void CsvHasCorrectColumnCount()
    {
        var record = new MeasurementRecord("r","s", DateTimeOffset.UtcNow, "hybrid", "reuse", 1024, "process-to-ui", "tti", 12.34, "ms", 1, "ok", "", "c", "8.0.30");
        var writer = new ResultWriter();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = writer.WriteMeasurements(new[] { record, record }, dir, "test");
        var lines = File.ReadAllLines(path);
        var headerCols = lines[0].Split(',').Length;
        foreach (var l in lines.Skip(1)) Assert.Equal(headerCols, l.Split(',').Length);
        Directory.Delete(dir, true);
    }
}
