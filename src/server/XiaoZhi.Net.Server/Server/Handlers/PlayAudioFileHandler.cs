using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Protocol;

namespace XiaoZhi.Net.Server.Handlers
{
    internal class PlayAudioFileHandler : BaseHandler
    {
        public PlayAudioFileHandler(XiaoZhiConfig config, ILogger logger) : base(config, logger)
        {
        }

        public override string HandlerName => nameof(PlayAudioFileHandler);
        public IBizSendOutter SendOutter { get; set; } = null!;

        public async Task Handle()
        {
            //await foreach (var reader in this.PreviousReader.ReadAllAsync()) await this.Handle(reader);
        }

        public async void Handle(string audioFilePath)
        {
            if (!File.Exists(audioFilePath))
            {
                this.Logger.LogError("The audio file does not exist: {filePath}", audioFilePath);
                return;
            }



        }
    }
}
