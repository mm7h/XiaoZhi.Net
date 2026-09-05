using Microsoft.Extensions.AI;

namespace XiaoZhi.Net.Server.Abstractions
{
    public interface IAgentMemory
    {
        /// <summary>
        /// 保存客户端本次会话的聊天记录。
        /// </summary>
        Task SaveMemoryAsync(string deviceId, string sessionId, IReadOnlyList<ChatMessage> chatMessages);

        /// <summary>
        /// 获取需要注入系统提示词的客户端记忆。
        /// </summary>
        Task<string?> GetMemoryInstructionAsync(string deviceId);
    }
}
