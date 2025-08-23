using Microsoft.Extensions.Logging;
using MP3Sharp;
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Dtos;
using XiaoZhi.Net.Server.Common.Enums;
using XiaoZhi.Net.Server.Helpers;

namespace XiaoZhi.Net.Server.Providers.AudioPlayer
{
    internal class FileAudioPlayer : BaseProvider<FileAudioPlayer, AudioPlayerConfig>, IAudioPlayer
    {
        private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        private readonly SemaphoreSlim _audioPlayerSlim = new SemaphoreSlim(1, 1);

        private Channel<string>? _processingChannel;
        private CancellationTokenSource? _cancellationTokenSource;
        private int _frameDurationMs = 60; // 每帧的时长，单位毫秒

        public override string ProviderType => "audio player";

        public override string ModelName => nameof(FileAudioPlayer);

        public PlayingStatus PlayingStatus { get; private set; }

        public event Action<string>? OnBeforeProcessing;
        public event Action<string, float[]>? OnProcessing; // use Memory then to span?
        public event Action<string, bool>? OnProcessed;

        public FileAudioPlayer(ILogger<FileAudioPlayer> logger) : base(logger)
        {

        }

        public override bool Build(AudioPlayerConfig settings)
        {
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

        public async Task PlayAsync(params string[] files)
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

                foreach (string file in files)
                {
                    if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                    {
                        this.Logger.LogWarning("Invalid audio file path: {filePath}.", file);
                        continue;
                    }
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
            if (this.PlayingStatus == PlayingStatus.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the pausing.", this.PlayingStatus);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();


                this._pauseEvent.Reset();


            }
            finally
            {
                this.PlayingStatus = PlayingStatus.Paused;
                this._audioPlayerSlim.Release();
            }
        }

        public async Task ResumeAsync()
        {
            if (this.PlayingStatus == PlayingStatus.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the resuming.", this.PlayingStatus);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();


                this._pauseEvent.Set();


            }
            finally
            {
                this.PlayingStatus = PlayingStatus.Playing;
                this._audioPlayerSlim.Release();
            }
        }

        public async Task StopAsync()
        {
            if (this.PlayingStatus == PlayingStatus.Idle)
            {
                this.Logger.LogInformation("The audio player status is {status}, skip the stopping.", this.PlayingStatus);
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();

                this._cancellationTokenSource?.Cancel();


            }
            finally
            {
                this.PlayingStatus = PlayingStatus.Idle;
                this._audioPlayerSlim.Release();
            }
        }

        public async Task SeekAsync(long positionMs)
        {
            if (this.PlayingStatus == PlayingStatus.Idle)
            {
                return;
            }
            try
            {
                await this._audioPlayerSlim.WaitAsync();




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
            this._cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = this._cancellationTokenSource.Token;

            var mp3Stream = new MP3Stream(file);
            string fileName = Path.GetFileName(file);

            this.OnBeforeProcessing?.Invoke(fileName);

            int sampleRate = mp3Stream.Frequency;
            int channels = mp3Stream.ChannelCount;

            this.Logger.LogDebug("Start processing audio file: {file}, sample rate: {sampleRate}, channels: {channels}.", fileName, sampleRate, channels);

            int bufferSize = (sampleRate * this._frameDurationMs / 1000) * 2 * channels; // 每帧的字节数

            byte[] pcmData = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                this.PlayingStatus = PlayingStatus.Playing;

                int bytesRead;
                while ((bytesRead = await mp3Stream.ReadAsync(pcmData, 0, pcmData.Length)) > 0)
                {
                    this._pauseEvent.Wait(token);

                    this.OnProcessing?.Invoke(fileName, pcmData.Bytes2Float());
                    await Task.Delay(this._frameDurationMs, token);

                    token.ThrowIfCancellationRequested();
                }
                this.OnProcessed?.Invoke(fileName, true);
                this.Logger.LogDebug("Completed processing audio file: {file}.", fileName);
            }
            catch (OperationCanceledException)
            {
                this.OnProcessed?.Invoke(fileName, false);
                this.Logger.LogDebug("Canceled playing audio file: {file}.", fileName);
            }
            catch (Exception ex)
            {
                this.OnProcessed?.Invoke(fileName, false);
                this.Logger.LogError(ex, "Error processing audio file: {file}.", fileName);
            }
            finally
            {
                this.PlayingStatus = PlayingStatus.Idle;
                mp3Stream.Dispose();
                this._cancellationTokenSource.Dispose();
                ArrayPool<byte>.Shared.Return(pcmData);
            }
        }

        public override void Dispose()
        {
            this._audioPlayerSlim.Dispose();
            this._pauseEvent.Dispose();
        }
    }
}
