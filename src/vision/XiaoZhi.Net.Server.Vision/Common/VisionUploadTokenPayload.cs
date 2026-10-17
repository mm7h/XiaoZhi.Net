namespace XiaoZhi.Net.Server.Vision.Common;

/// <summary>
/// 表示写入 Vision 上传令牌中的已签名载荷。
/// </summary>
internal sealed record VisionUploadTokenPayload(string TokenId, string SessionId, string DeviceId);
