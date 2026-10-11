namespace XiaoZhi.Net.Server.Vision.Common;

/// <summary>
/// 将 Vision 上传令牌绑定到服务端会话和设备标识。
/// </summary>
internal sealed record VisionUploadTokenBinding(string SessionId, string DeviceId);
