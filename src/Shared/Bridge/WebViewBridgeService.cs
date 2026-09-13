using System.Text.Json;

namespace WebView2SystemTrayBenchmark.Shared;

public sealed class BridgeMessageRouter
{
    public const int CurrentProtocolVersion = 1;
    private readonly DeterministicBusinessService _business;
    public BridgeMessageRouter(DeterministicBusinessService business) => _business = business;
    public WebViewResponse Handle(WebViewMessage message)
    {
        if (message.ProtocolVersion != CurrentProtocolVersion) return Error(message, "unsupported protocol version");
        return message.Type switch
        {
            "frontend-ready" => Ok(message, "frontend-ready", "ack"),
            "echo" or "ping" => Ok(message, message.Type == "ping" ? "pong" : "echo", _business.EchoPayload(message.Payload)),
            "operation" => Ok(message, "response", _business.SimulatedBusinessOperation(message.Payload).Output),
            "close-ui" or "benchmark-complete" => Ok(message, message.Type, "ack"),
            _ => Error(message, $"unknown message type: {message.Type}")
        };
    }
    private static WebViewResponse Ok(WebViewMessage m, string type, string payload) => new(m.Id, m.ProtocolVersion, true, type, payload, null);
    private static WebViewResponse Error(WebViewMessage m, string error) => new(m.Id, m.ProtocolVersion, false, "error", null, error);
    public static WebViewMessage Deserialize(string json) => JsonSerializer.Deserialize<WebViewMessage>(json) ?? throw new JsonException("Invalid WebView message");
    public static string Serialize(WebViewResponse response) => JsonSerializer.Serialize(response);
}
