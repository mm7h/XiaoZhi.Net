using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal class StreamAudioPlayer : BaseProvider<StreamAudioPlayer, AudioSetting>, IAudioPlayer
    {
        private readonly SemaphoreSlim _audioPlayerSlim = new SemaphoreSlim(1, 1);
        private readonly IStreamAudioPlayer _streamAudioPlayer;

        public override string ProviderType => "audio player";

        public override string ModelName => nameof(StreamAudioPlayer);

        public PlaybackState PlaybackState => this._streamAudioPlayer.State;

        public event Action<string>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing; // use Memory then to span?
        public event Action<string, bool>? OnProcessed;

        public StreamAudioPlayer(IStreamAudioPlayer streamAudioPlayer, ILogger<StreamAudioPlayer> logger) : base(logger)
        {
            this._streamAudioPlayer = streamAudioPlayer;
        }

        public override bool Build(AudioSetting settings)
        {
            throw new NotImplementedException();
        }

        public async Task PlayAsync(CancellationToken cancellationToken = default, params string[] urls)
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

        public async Task SeekAsync(TimeSpan position)
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
        }
    }
}
