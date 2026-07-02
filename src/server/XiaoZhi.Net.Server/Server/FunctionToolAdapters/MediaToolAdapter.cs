using System;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;
using XiaoZhi.Net.Server.Resources;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class MediaToolAdapter : IMediaTool
    {
        private readonly Session _session;

        public MediaToolAdapter(Session session)
        {
            this._session = session;
        }

        public string BasicPath => this._musics is null ? string.Empty : AppContext.BaseDirectory;

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

        public async ValueTask PlayAsync(string musicName)
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.PlayAsync(this._session.PrivateProvider.Token, musicName);
        }

        public async ValueTask PauseAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.PauseAsync();
        }

        public async ValueTask ResumeAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.ResumeAsync();
        }

        public async ValueTask StopAsync()
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.StopAsync();
        }

        public async ValueTask SeekAsync(TimeSpan position)
        {
            if (this._session.PrivateProvider.AudioPlayerClient is null)
            {
                throw new InvalidOperationException("Audio player client is not initialized.");
            }

            await this._session.PrivateProvider.AudioPlayerClient.MusicPlayer.SeekAsync(position);
        }
    }
}