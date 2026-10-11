namespace XiaoZhi.Net.Server.Vision.Common;

/// <summary>
/// 表示刚签发的 Vision 上传令牌及其内部标识。
/// </summary>
internal sealed record IssuedVisionUploadToken(string TokenId, string Token);
