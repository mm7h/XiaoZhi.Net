using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal class StreamAudioPlayer : BaseProvider<StreamAudioPlayer, AudioPlayerConfig>, IAudioPlayer
    {
        private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        private readonly SemaphoreSlim _audioPlayerSlim = new SemaphoreSlim(1, 1);

        public override string ProviderType => "audio player";

        public override string ModelName => nameof(StreamAudioPlayer);

        public PlayingStatus PlayingStatus { get; private set; }

        public event Action<string>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing; // use Memory then to span?
        public event Action<string, bool>? OnProcessed;

        public StreamAudioPlayer(ILogger<StreamAudioPlayer> logger) : base(logger)
        {

        }

        public override bool Build(AudioPlayerConfig settings)
        {
            throw new NotImplementedException();
        }

        public async Task PlayAsync(params string[] urls)
        {
            try
            {
                await this._audioPlayerSlim.WaitAsync();




            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task PauseAsync()
        {
            try
            {
                await this._audioPlayerSlim.WaitAsync();


                this._pauseEvent.Reset();


            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task ResumeAsync()
        {
            try
            {
                await this._audioPlayerSlim.WaitAsync();


                this._pauseEvent.Set();


            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task StopAsync()
        {
            try
            {
                await this._audioPlayerSlim.WaitAsync();




            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task SeekAsync(long positionMs)
        {
            try
            {
                await this._audioPlayerSlim.WaitAsync();




            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }


        public override void Dispose()
        {
            this._audioPlayerSlim.Dispose();
            this._pauseEvent.Dispose();
        }
    }
}
