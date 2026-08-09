using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Resources;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class MediaToolAdapter : IMediaTool
    {
        private readonly Session _session;
        private readonly IMusicFileProvider _musicFileProvider;

        public MediaToolAdapter(Session session, IMusicFileProvider musicFileProvider)
        {
            this._session = session;
            this._musicFileProvider = musicFileProvider;
        }

        public float Volume
        {
            get => this._session.PrivateProvider.AudioPlayerClient?.MusicPlayer.Volume ?? 0f;
            set
            {
                if (this._session.PrivateProvider.AudioPlayerClient is not null)
                {
                    this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.Volume = value;
                }
            }
        }
        public bool HasMusicFiles => this._musicFileProvider.HasMusicFiles;
        public IReadOnlyDictionary<string, string> MusicFiles => this._musicFileProvider.MusicFiles;
        public string MusicFolderPath => this._musicFileProvider.MusicFolderPath;
        public bool IsPlaying => this._session.PrivateProvider.AudioPlayerClient?.MusicPlayer.IsPlaying ?? false;
        public bool IsPaused => this._session.PrivateProvider.AudioPlayerClient?.MusicPlayer.PlaybackState == Media.Abstractions.Common.Enums.PlaybackState.Paused;
        public string? PlayingMusicName => this._session.PrivateProvider.AudioPlayerClient?.MusicPlayer.PlayingMusicName;
        public bool UpdateMusicFiles() => this._musicFileProvider.UpdateMusicFiles();
        public bool UpdateMusicFiles(string newMusicFolderPath) => this._musicFileProvider.UpdateMusicFiles(newMusicFolderPath);

        public async ValueTask PlayAsync(string musicName)
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            CancellationToken cancellationToken = this._session.PrivateProvider.Token;
            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.PlayAsync(cancellationToken, musicName);
        }

        public async ValueTask PauseAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            CancellationToken cancellationToken = this._session.PrivateProvider.Token;
            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.PauseAsync(cancellationToken);
        }

        public async ValueTask ResumeAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            CancellationToken cancellationToken = this._session.PrivateProvider.Token;
            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.ResumeAsync(cancellationToken);
        }

        public async ValueTask StopAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            CancellationToken cancellationToken = this._session.PrivateProvider.Token;
            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.StopAsync(cancellationToken);
        }

        public async ValueTask SeekAsync(TimeSpan position)
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            CancellationToken cancellationToken = this._session.PrivateProvider.Token;
            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.SeekAsync(position, cancellationToken);
        }
    }
}
