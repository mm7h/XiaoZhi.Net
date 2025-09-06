using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions;
using XiaoZhi.Net.Server.AudioPlayer.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer.Music
{
    internal class LocalFileMusicPlayer : BaseProvider<LocalFileMusicPlayer, AudioSetting>, IMusicPlayer
    {
        private readonly SemaphoreSlim _audioPlayerSlim = new SemaphoreSlim(1, 1);
        private readonly IUrlAudioPlayer _urlAudioPlayer;

        private Channel<string>? _processingChannel;
        private CancellationTokenSource? _cancellationTokenSource;
        private AudioSetting? _audioSetting;

        public override string ProviderType => "audio player";

        public override string ModelName => nameof(LocalFileMusicPlayer);

        public PlaybackState PlaybackState => this._urlAudioPlayer.State;

        public string? PlayingMusicName { get; private set; }

        public event Action<float[], bool, bool>? OnAudioData;

        public LocalFileMusicPlayer(IUrlAudioPlayer urlAudioPlayer, ILogger<LocalFileMusicPlayer> logger) : base(logger)
        {
            this._urlAudioPlayer = urlAudioPlayer;
            this._urlAudioPlayer.OnAudioDataAvailable += this.FireAudioData;
        }

        public override bool Build(AudioSetting audioSetting)
        {
            if (!this._urlAudioPlayer.CheckFFmpegInstalled())
            {
                this.Logger.LogError("Failed to initialize FFmpeg, please check your the ffmpeg path configuration.");
                return false;
            }
            this._audioSetting = audioSetting;
            int capacity = 50;
            BoundedChannelOptions boundedChannelOptions = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true
            };
            this._processingChannel = Channel.CreateBounded<string>(boundedChannelOptions);

            Task.Factory.StartNew(this.AudioFileProcessingAsync, TaskCreationOptions.LongRunning).ConfigureAwait(false);

            return true;
        }

        public async Task PlayAsync(CancellationToken cancellationToken = default, params string[] files)
        {
            if (this._processingChannel is null)
            {
                this.Logger.LogError("The audio player is not built yet.");
                return;
            }
            if (files is null || files.Length == 0)
            {
                this.Logger.LogWarning("No audio files to play.");
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();

                this._cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                this._cancellationTokenSource.Token.Register(() =>
                {
                    this.StopAsync().ConfigureAwait(false);
                });

                foreach (string file in files)
                {
                    await this._processingChannel.Writer.WriteAsync(file);
                }
            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task PauseAsync()
        {
            if (this.PlaybackState == PlaybackState.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the pausing.", this.PlaybackState);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();
                this._urlAudioPlayer.Pause();
            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task ResumeAsync()
        {
            if (this.PlaybackState == PlaybackState.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the resuming.", PlaybackState);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();
                this._urlAudioPlayer.Play();
            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task StopAsync()
        {
            if (this.PlaybackState == PlaybackState.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the stopping.", PlaybackState);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();
                this._urlAudioPlayer.Stop();
                this._cancellationTokenSource?.Cancel();

            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            if (this.PlaybackState == PlaybackState.Idle)
            {
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();

                this._urlAudioPlayer.Seek(position);


            }
            finally
            {
                this._audioPlayerSlim.Release();
            }
        }

        private async Task AudioFileProcessingAsync()
        {
            if (this._processingChannel is null) return;
            await foreach (string file in this._processingChannel.Reader.ReadAllAsync())
            {
                await this.AudioFileProcessingAsync(file);
            }
        }

        private async Task AudioFileProcessingAsync(string file)
        {
            if (this._audioSetting is null)
            {
                this.Logger.LogError("The audio player is not built yet.");
                return;
            }

            string fileName = Path.GetFileName(file);

            this.Logger.LogDebug("Start processing audio file: {file}.", fileName);

            try
            {
                this.PlayingMusicName = fileName;
                await this._urlAudioPlayer.LoadAsync(file, this._audioSetting.SampleRate, this._audioSetting.Channels, this._audioSetting.FrameDuration);

                this.Logger.LogDebug("Loaded audio file: {file}, start playing.", fileName);
                this._urlAudioPlayer.Play(true);

                this.Logger.LogDebug("Completed processing audio file: {file}.", fileName);
            }
            catch (OperationCanceledException)
            {
                this.PlayingMusicName = null;
                this.Logger.LogDebug("Canceled playing audio file: {file}.", fileName);
            }
            catch (Exception ex)
            {
                this.PlayingMusicName = null;
                this.Logger.LogError(ex, "Error processing audio file: {file}.", fileName);
            }
            finally
            {
                this._cancellationTokenSource?.Dispose();
            }
        }
        private void FireAudioData(float[] pcmData, bool isFirst, bool isLast)
        {
            this.OnAudioData?.Invoke(pcmData, isFirst, isLast);
        }

        public override void Dispose()
        {
            this._urlAudioPlayer.OnAudioDataAvailable -= this.FireAudioData;
            this._urlAudioPlayer.Dispose();
            this._audioPlayerSlim.Dispose();
        }
    }
}
