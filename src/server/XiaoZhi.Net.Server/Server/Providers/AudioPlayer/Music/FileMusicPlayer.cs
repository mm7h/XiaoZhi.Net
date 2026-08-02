using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Net.Server.Abstractions.ConfigSettings;
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
            // 若当前处于播放中或暂停状态，先完全停止当前播放，
            // 因为 UrlAudioPlayer.LoadAsync 要求 State == Idle 才能加载新曲目
            if (this.PlaybackState != PlaybackState.Idle)
            {
                await this.StopAsync();
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();

                // 使用独立 CTS，不链接 ProviderToken。
                // ProviderToken 在每次用户说话（session.Abort）时都会取消，若链接则会在
                // LLM 处理"暂停"/"继续"等指令之前意外停止音乐。
                // 音乐生命周期只由明确的 StopAsync() 指令和 Dispose() 管控。
                this._cancellationTokenSource = new CancellationTokenSource();

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
                this.Logger.LogInformation(Lang.FileMusicPlayer_ResumeAsync_Skip, this.PlaybackState);
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
                this.Logger.LogInformation(Lang.FileMusicPlayer_StopAsync_Skip, this.PlaybackState);
                return;
            }
            // 记录停止前的状态：暂停状态已由播放器内部发出 isLast=true；
            // 播放中强制停止时播放器不会自动发完成帧，需手动触发以正确关闭混音器中的 Music 流
            bool wasPlaying = this.PlaybackState is PlaybackState.Playing or PlaybackState.Buffering;
            try
            {
                await this._audioPlayerSlim.WaitAsync();
                this._urlAudioPlayer.Stop();
                this._cancellationTokenSource?.Cancel();
                if (wasPlaying)
                {
                    this.FireAudioData(Array.Empty<float>(), false, true);
                }
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
            if (this._processingChannel is null)
            {
                return;
            }
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
                // 使用原子交换置空字段，避免与 StopAsync 的 Cancel() 产生竞态：
                // Stop() 解除 Play(true) 阻塞后，内层 finally 与 StopAsync 并行执行，
                // 直接 Dispose 会导致 StopAsync 随后的 Cancel() 抛出 ObjectDisposedException
                var cts = Interlocked.Exchange(ref this._cancellationTokenSource, null);
                cts?.Dispose();
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
