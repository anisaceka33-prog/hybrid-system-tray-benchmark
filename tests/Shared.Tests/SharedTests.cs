using System.Text.Json;
using WebView2SystemTrayBenchmark.Shared;
namespace Shared.Tests;
public class SharedTests
{
    [Fact] public void PayloadHasRequestedSize() => Assert.Equal(1024, DeterministicBusinessService.GeneratePayload(1024).Length);
    [Fact] public void MessageRoundTrips() { var message = new WebViewMessage("id", 1, "echo", "payload"); var parsed = BridgeMessageRouter.Deserialize(JsonSerializer.Serialize(message)); Assert.Equal(message, parsed); }
    [Fact] public void SettingsRoundTrip() { var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".json"); var service = new DeterministicBusinessService(path); var expected = new AppSettings("Test", 10); service.SaveSettings(expected); Assert.Equal(expected, service.GetSettings()); File.Delete(path); }
    [Fact] public void LifecycleReuseHidesWithoutDisposing() { var lifecycle = new LifecycleController(); lifecycle.BeginInitialize(); lifecycle.MarkVisible(); Assert.Equal(LifecycleState.Hidden, lifecycle.Hide(LifecycleMode.Reuse)); }
    [Fact] public void ResultWriterDoesNotOverwrite() { var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var writer = new ResultWriter(); var result = new MeasurementRecord("run","session",DateTimeOffset.UtcNow,"native","reuse",1024,"host-to-ui","tti",1,"ms",1,"ok",null,"unknown","8"); var first = writer.WriteMeasurements(new[]{result},dir,"x"); var second = writer.WriteMeasurements(new[]{result},dir,"x"); Assert.NotEqual(first,second); Directory.Delete(dir, true); }
}
