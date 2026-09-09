namespace XiaoZhi.Net.PerformanceTest.Configuration;

internal sealed record TestOptions(Uri ServerUri, int ClientCount, int Rounds, string? AudioSelector)
{
    public const string DefaultServer = "ws://localhost:4530/xiaozhi/v1/";
    public const int DefaultClients = 20;
    public const int DefaultRounds = 1;

    public int TotalRounds => checked(this.ClientCount * this.Rounds);

    public static TestOptions Parse(string? server, string? clients, string? rounds, string? audioSelector)
    {
        string serverValue = string.IsNullOrWhiteSpace(server) ? DefaultServer : server.Trim();
        if (!Uri.TryCreate(serverValue, UriKind.Absolute, out Uri? serverUri)
            || (serverUri.Scheme != Uri.UriSchemeWs && serverUri.Scheme != Uri.UriSchemeWss))
        {
            throw new ArgumentException("--server 必须是绝对 ws:// 或 wss:// 地址。");
        }

        int clientCount = ParsePositiveInt(clients, DefaultClients, "--clients");
        int roundCount = ParsePositiveInt(rounds, DefaultRounds, "--rounds");

        try
        {
            _ = checked(clientCount * roundCount);
        }
        catch (OverflowException)
        {
            throw new ArgumentException("--clients 与 --rounds 的乘积过大。");
        }

        return new TestOptions(serverUri, clientCount, roundCount, string.IsNullOrWhiteSpace(audioSelector) ? null : audioSelector.Trim());
    }

    private static int ParsePositiveInt(string? value, int defaultValue, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int parsed) || parsed <= 0)
        {
            throw new ArgumentException($"{optionName} 必须是大于 0 的整数。");
        }

        return parsed;
    }
}
