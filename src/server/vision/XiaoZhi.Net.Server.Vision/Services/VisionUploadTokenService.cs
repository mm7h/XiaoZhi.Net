using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiaoZhi.Net.Server.Abstractions.Mcp;
using XiaoZhi.Net.Server.Vision.Abstractions.Common;
using XiaoZhi.Net.Server.Vision.Common;

namespace XiaoZhi.Net.Server.Vision.Services;

/// <summary>
/// 签发、校验和撤销绑定设备会话的 Vision 上传令牌。
/// </summary>
internal sealed class VisionUploadTokenService
{
    private static readonly JsonSerializerOptions s_serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, VisionUploadTokenBinding> _activeTokens = new(StringComparer.Ordinal);
    private readonly byte[] _signingKey;

    public VisionUploadTokenService(VisionServerOptions options)
    {
        this._signingKey = Encoding.UTF8.GetBytes(options.UploadTokenSigningKey);
    }

    public IssuedVisionUploadToken Issue(DeviceMcpCapabilityContext context)
    {
        string tokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        VisionUploadTokenBinding binding = new(context.SessionId, context.DeviceId);
        this._activeTokens[tokenId] = binding;

        VisionUploadTokenPayload payload = new(tokenId, context.SessionId, context.DeviceId);
        string encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, s_serializerOptions));
        byte[] signature = HMACSHA256.HashData(this._signingKey, Encoding.UTF8.GetBytes(encodedPayload));
        return new IssuedVisionUploadToken(tokenId, string.Concat(encodedPayload, ".", Base64UrlEncode(signature)));
    }

    public bool IsValid(string token, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        string[] parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 2 || !TryBase64UrlDecode(parts[1], out byte[]? providedSignature))
        {
            return false;
        }

        byte[] expectedSignature = HMACSHA256.HashData(this._signingKey, Encoding.UTF8.GetBytes(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature)
            || !TryBase64UrlDecode(parts[0], out byte[]? payloadBytes))
        {
            return false;
        }

        VisionUploadTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<VisionUploadTokenPayload>(payloadBytes, s_serializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return payload is not null
            && this._activeTokens.TryGetValue(payload.TokenId, out VisionUploadTokenBinding? binding)
            && string.Equals(payload.SessionId, binding.SessionId, StringComparison.Ordinal)
            && string.Equals(payload.DeviceId, binding.DeviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(deviceId, binding.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    public void Revoke(string tokenId)
    {
        this._activeTokens.TryRemove(tokenId, out _);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            0 => padded,
            2 => padded + "==",
            3 => padded + "=",
            _ => string.Empty
        };

        if (padded.Length == 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
