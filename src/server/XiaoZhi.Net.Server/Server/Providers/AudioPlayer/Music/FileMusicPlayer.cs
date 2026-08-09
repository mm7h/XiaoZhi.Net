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
        private readonly MusicProviderSetting _musicProviderSetting;
        private readonly IUrlAudioPlayer _urlAudioPlayer;

        private Channel<MusicFileRequest>? _processingChannel;
        private CancellationTokenSource? _processingCts;
        private CancellationTokenSource? _activePlaybackCts;
        private Task? _processingTask;
        private AudioSetting? _audioSetting;
        private long _playbackGeneration;

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

        public FileMusicPlayer(
            IUrlAudioPlayer urlAudioPlayer,
            XiaoZhiConfig config,
            ILogger<FileMusicPlayer> logger)
            : base(logger)
        {
            this._urlAudioPlayer = urlAudioPlayer;
            this._musicProviderSetting = config.MusicProviderSetting;
            this._urlAudioPlayer.OnAudioDataAvailable += this.FireAudioData;
        }

        public override bool Build(AudioSetting audioSetting)
        {
            if (this._musicProviderSetting.CommandTimeout <= TimeSpan.Zero
                || this._musicProviderSetting.StopTimeout <= TimeSpan.Zero)
            {
                return false;
            }

            if (!this._urlAudioPlayer.CheckFFmpegInstalledAsync().GetAwaiter().GetResult())
            {
                this.Logger.LogError(Lang.FileMusicPlayer_Build_FFmpegInitFailed);
                return false;
            }

            this._audioSetting = audioSetting;
            BoundedChannelOptions boundedChannelOptions = new(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true
            };
            this._processingChannel = Channel.CreateBounded<MusicFileRequest>(boundedChannelOptions);

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

            bool lockAcquired = false;
            try
            {
                await this.WaitForAudioPlayerLockAsync(
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                if (this.PlaybackState != PlaybackState.Idle
                    || Volatile.Read(ref this._activePlaybackCts) is not null)
                {
                    await this.StopCoreAsync(cancellationToken).ConfigureAwait(false);
                }

                long generation = Volatile.Read(ref this._playbackGeneration);
                foreach (string file in files)
                {
                    MusicFileRequest request = new(file, generation);
                    await ExecuteWithTimeoutAsync(
                        token => this._processingChannel.Writer.WriteAsync(request, token).AsTask(),
                        this._musicProviderSetting.CommandTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    this._audioPlayerSlim.Release();
                }
            }
        }

        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            bool lockAcquired = false;
            try
            {
                await this.WaitForAudioPlayerLockAsync(
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                if (this.PlaybackState == PlaybackState.Idle)
                {
                    this.Logger.LogInformation(Lang.FileMusicPlayer_PauseAsync_Skip, this.PlaybackState);
                    return;
                }

                await ExecuteWithTimeoutAsync(
                    token => this._urlAudioPlayer.PauseAsync(token),
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lockAcquired)
                {
                    this._audioPlayerSlim.Release();
                }
            }
        }

        public async Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            bool lockAcquired = false;
            try
            {
                await this.WaitForAudioPlayerLockAsync(
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                if (this.PlaybackState == PlaybackState.Idle)
                {
                    this.Logger.LogInformation(Lang.FileMusicPlayer_ResumeAsync_Skip, this.PlaybackState);
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // PlayAsync 在暂停状态会同步切换为继续播放，并返回同一个播放完成任务。
                // 该任务已由后台音乐处理循环等待，Function Tool 不应再次等待整首歌。
                _ = this._urlAudioPlayer.PlayAsync(cancellationToken);
            }
            finally
            {
                if (lockAcquired)
                {
                    this._audioPlayerSlim.Release();
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            bool lockAcquired = false;
            try
            {
                await this.WaitForAudioPlayerLockAsync(
                    this._musicProviderSetting.StopTimeout,
                    cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                await this.StopCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lockAcquired)
                {
                    this._audioPlayerSlim.Release();
                }
            }
        }

        public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            bool lockAcquired = false;
            try
            {
                await this.WaitForAudioPlayerLockAsync(
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                lockAcquired = true;

                if (this.PlaybackState == PlaybackState.Idle)
                {
                    return;
                }

                await ExecuteWithTimeoutAsync(
                    token => this._urlAudioPlayer.SeekAsync(position, token),
                    this._musicProviderSetting.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lockAcquired)
                {
                    this._audioPlayerSlim.Release();
                }
            }
        }

        private async Task StopCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._playbackGeneration);
            this.CancelActivePlayback();

            if (this.PlaybackState == PlaybackState.Idle)
            {
                return;
            }

            await ExecuteWithTimeoutAsync(
                token => this._urlAudioPlayer.StopAsync(token),
                this._musicProviderSetting.StopTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task AudioFileProcessingAsync(CancellationToken cancellationToken)
        {
            if (this._processingChannel is null)
            {
                return;
            }

            try
            {
                await foreach (MusicFileRequest request in this._processingChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    await this.AudioFileProcessingAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Canceled);
            }
            finally
            {
                var processingCts = Interlocked.Exchange(ref this._processingCts, null);
                processingCts?.Dispose();
            }
        }

        private async Task AudioFileProcessingAsync(MusicFileRequest request, CancellationToken processingCancellationToken)
        {
            if (this._audioSetting is null || !this.IsCurrentGeneration(request.Generation))
            {
                return;
            }

            string fileName = Path.GetFileName(request.File);
            using CancellationTokenSource playbackCts = CancellationTokenSource.CreateLinkedTokenSource(processingCancellationToken);
            Volatile.Write(ref this._activePlaybackCts, playbackCts);

            try
            {
                this.PlayingMusicName = fileName;
                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Start, fileName);

                playbackCts.Token.ThrowIfCancellationRequested();
                if (!this.IsCurrentGeneration(request.Generation))
                {
                    return;
                }

                bool loaded = await this._urlAudioPlayer.LoadAsync(
                    request.File,
                    this._audioSetting.SampleRate,
                    this._audioSetting.Channels,
                    this._audioSetting.FrameDuration,
                    playbackCts.Token).ConfigureAwait(false);

                if (!loaded || playbackCts.IsCancellationRequested || !this.IsCurrentGeneration(request.Generation))
                {
                    return;
                }

                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Playing, fileName);
                await this._urlAudioPlayer.PlayAsync(playbackCts.Token).ConfigureAwait(false);
                this.Logger.LogDebug(Lang.FileMusicPlayer_AudioFileProcessingAsync_Completed, fileName);
            }
            catch (OperationCanceledException) when (
                playbackCts.IsCancellationRequested
                || processingCancellationToken.IsCancellationRequested)
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
                Interlocked.CompareExchange(ref this._activePlaybackCts, null, playbackCts);
            }
        }

        private async Task WaitForAudioPlayerLockAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (!await this._audioPlayerSlim.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException("等待音乐播放器控制锁超时。");
            }
        }

        private static async Task ExecuteWithTimeoutAsync(
            Func<CancellationToken, Task> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await operation(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException("音乐播放器操作超时。");
            }
        }

        private void CancelActivePlayback()
        {
            CancellationTokenSource? activePlaybackCts = Volatile.Read(ref this._activePlaybackCts);
            if (activePlaybackCts is null)
            {
                return;
            }

            try
            {
                activePlaybackCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 音乐处理循环刚完成并释放 CTS 时，停止请求无需重复取消。
            }
        }

        private bool IsCurrentGeneration(long generation)
        {
            return generation == Volatile.Read(ref this._playbackGeneration);
        }

        private void FireAudioData(float[] pcmData, bool isFirst, bool isLast)
        {
            this.OnAudioData?.Invoke(pcmData, isFirst, isLast);
        }

        public override void Dispose()
        {
            this.CancelActivePlayback();
            Interlocked.Increment(ref this._playbackGeneration);
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
            this._audioPlayerSlim.Dispose();
        }

        private readonly record struct MusicFileRequest(string File, long Generation);
    }
}
