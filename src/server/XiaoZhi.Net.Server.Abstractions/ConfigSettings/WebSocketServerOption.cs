namespace XiaoZhi.Net.Server.Abstractions.ConfigSettings;

public sealed class WebSocketServerOption
{
    public string IP { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 4530;
    public string Path { get; set; } = "/xiaozhi/v1/";
    public WssOption? WssOption { get; set; }
}
