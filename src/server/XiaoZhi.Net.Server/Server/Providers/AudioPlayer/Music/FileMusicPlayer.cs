using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Media.Abstractions;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer.Music
{
    internal class FileMusicPlayer : BaseProvider<FileMusicPlayer, AudioSetting>, IMusicPlayer
    {
        private readonly SemaphoreSlim _audioPlayerSlim = new SemaphoreSlim(1, 1);
        private readonly IUrlAudioPlayer _urlAudioPlayer;

        private Channel<string>? _processingChannel;
        private CancellationTokenSource? _processingCts;
        private Task? _processingTask;
        private CancellationTokenSource? _cancellationTokenSource;
        private AudioSetting? _audioSetting;

        public override string ProviderType => "audio player";

        public override string ModelName => nameof(FileMusicPlayer);

        public PlaybackState PlaybackState => this._urlAudioPlayer.State;

        public bool IsPlaying => this._urlAudioPlayer.State is PlaybackState.Playing or PlaybackState.Buffering;

        public string? PlayingMusicName { get; private set; }

        public float Volume
        {
            get => this._urlAudioPlayer.Volume;
            set => this._urlAudioPlayer.Volume = value;
        }

        public event Action<float[], bool, bool>? OnAudioData;

        public FileMusicPlayer(IUrlAudioPlayer urlAudioPlayer, ILogger<FileMusicPlayer> logger) : base(logger)
        {
            this._urlAudioPlayer = urlAudioPlayer;
            this._urlAudioPlayer.OnAudioDataAvailable += this.FireAudioData;
        }

        public override bool Build(AudioSetting audioSetting)
        {
            if (!this._urlAudioPlayer.CheckFFmpegInstalled())
            {
                this.Logger.LogError(Lang.FileMusicPlayer_Build_FFmpegInitFailed);
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

            this._processingCts = new CancellationTokenSource();
            this._processingTask = Task.Run(() => this.AudioFileProcessingAsync(this._processingCts.Token));

            return true;
        }

        public async Task PlayAsync(CancellationToken cancellationToken = default, params string[] files)
        {
            if (this._processingChannel is null)
            {
                this.Logger.LogError(Lang.FileMusicPlayer_PlayAsync_NotBuilt);
                return;
            }
            if (files is null || files.Length == 0)
            {
                this.Logger.LogWarning(Lang.FileMusicPlayer_PlayAsync_NoFiles);
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
                this.Logger.LogInformation(Lang.FileMusicPlayer_PauseAsync_Skip, this.PlaybackState);
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
                this.Logger.LogInformation(Lang.FileMusicPlayer_ResumeAsync_Skip, PlaybackState);
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
                this.Logger.LogInformation(Lang.FileMusicPlayer_StopAsync_Skip, PlaybackState);
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

        private async Task AudioFileProcessingAsync(CancellationToken cancellationToken)
        {
            if (this._processingChannel is null) return;
            try
            {
                await foreach (string file in this._processingChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    await this.AudioFileProcessingAsync(file, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Canceled);
            }
            finally
            {
                var playbackCts = Interlocked.Exchange(ref this._cancellationTokenSource, null);
                playbackCts?.Dispose();

                var processingCts = Interlocked.Exchange(ref this._processingCts, null);
                processingCts?.Dispose();
            }
        }

        private async Task AudioFileProcessingAsync(string file, CancellationToken cancellationToken)
        {
            if (this._audioSetting is null)
            {
                this.Logger.LogError(Lang.FileMusicPlayer_PlayAsync_NotBuilt);
                return;
            }

            string fileName = Path.GetFileName(file);

            this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Start, fileName);

            try
            {
                this.PlayingMusicName = fileName;
                cancellationToken.ThrowIfCancellationRequested();
                await this._urlAudioPlayer.LoadAsync(file, this._audioSetting.SampleRate, this._audioSetting.Channels, this._audioSetting.FrameDuration);

                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Playing, fileName);
                this._urlAudioPlayer.Play(true);

                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Completed, fileName);
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_PlaybackCanceled, fileName);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, Lang.FileMusicPlayer_AudioFileProcessingAsync_Error, fileName);
            }
            finally
            {
                this.PlayingMusicName = null;
                this._cancellationTokenSource?.Dispose();
            }
        }
        private void FireAudioData(float[] pcmData, bool isFirst, bool isLast)
        {
            this.OnAudioData?.Invoke(pcmData, isFirst, isLast);
        }

        public override void Dispose()
        {
            this._processingChannel?.Writer.TryComplete();

            var processingCts = Interlocked.Exchange(ref this._processingCts, null);
            processingCts?.Cancel();

            this._urlAudioPlayer.OnAudioDataAvailable -= this.FireAudioData;

            if (this._processingTask is { } task)
            {
                try
                {
                    if (!task.Wait(TimeSpan.FromSeconds(3)))
                    {
                        this.Logger.LogWarning(Lang.FileMusicPlayer_Dispose_Timeout);
                        // 任务超时未完成，手动释放
                        processingCts?.Dispose();
                    }
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
                {
                    // 任务被取消是预期行为，忽略
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, Lang.FileMusicPlayer_Dispose_Error);
                    processingCts?.Dispose();
                }
            }
            else
            {
                processingCts?.Dispose();
            }

            this._urlAudioPlayer.Dispose();
            this._cancellationTokenSource?.Dispose();
            this._audioPlayerSlim.Dispose();
        }
    }
}
