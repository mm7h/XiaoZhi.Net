using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Protocol;
using XiaoZhi.Net.Server.Providers;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class PlayAudioFileHandler : BaseHandler, IHandler
    {
        public PlayAudioFileHandler(XiaoZhiConfig config, ILogger<PlayAudioFileHandler> logger) : base(config, logger)
        {
        }

        public override string HandlerName => nameof(PlayAudioFileHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;

        public async Task Handle(string? sttMessage, Emotion emotion = Emotion.Neutral, params string[] audioFilePath)
        {
            Session session = this.SendOutter.GetSession();
            if (audioFilePath is null || audioFilePath.Length == 0)
            {
                this.Logger.LogWarning("No audio file paths provided to play for session: {sessionId}.", session.SessionId);
                return;
            }
            IAudioPlayer? audioPlayer = session.AudioPlayer;
            if (audioPlayer is null)
            {
                this.Logger.LogError("Audio player is not initialized for session: {sessionId}", session.SessionId);
                return;
            }



        }

    }
}
