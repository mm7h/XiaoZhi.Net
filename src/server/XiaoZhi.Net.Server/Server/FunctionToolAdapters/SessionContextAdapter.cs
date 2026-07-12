using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Common.Contexts;

namespace XiaoZhi.Net.Server.Abstractions.FunctionTools
{
    internal sealed class SessionContextAdapter : ISessionContext
    {
        private readonly Session _session;

        public SessionContextAdapter(Session session)
        {
            this._session = session;
        }

        public string DeviceId => this._session.DeviceId;

        public string SessionId => this._session.SessionId;

        public DateTimeOffset LoginTime => this._session.LoginTime;

        public DateTimeOffset LastActiveTime => this._session.LastActivityTime;

        public EndPoint LocalEndPoint => this._session.LocalEndPoint;

        public EndPoint RemoteEndPoint => this._session.EndPoint;

        public async ValueTask SendAsync(string textMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this._session.SendOutter.SendAsync(textMessage);
        }

        public async ValueTask SendAsync(ReadOnlySequence<byte> pcmData, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this._session.PrivateProvider.AudioEncoder is null)
            {
                throw new InvalidOperationException("Audio encoder is not initialized.");
            }

            byte[] pcmBuffer = pcmData.ToArray();
            float[] samples = new float[pcmBuffer.Length / sizeof(float)];
            Buffer.BlockCopy(pcmBuffer, 0, samples, 0, pcmBuffer.Length);

            byte[] opusPacket = await this._session.PrivateProvider.AudioEncoder.EncodeAsync(samples, cancellationToken);
            await this._session.SendOutter.SendAsync(opusPacket);
        }
    }
}