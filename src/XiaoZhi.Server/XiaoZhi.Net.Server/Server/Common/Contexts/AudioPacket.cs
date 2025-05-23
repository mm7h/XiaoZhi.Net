using SherpaOnnx;
using System;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AudioPacket
    {

        private bool _released;

        public AudioPacket()
        {
            VadPacket = new CircularBuffer(960 * 100);
            AsrPackets = new CircularBuffer(960 * 100);
            SendOpusPacketFrame = new CircularBuffer(960 * 100);
        }
        public CircularBuffer VadPacket { get; private set; }
        public CircularBuffer AsrPackets { get; private set; }
        public CircularBuffer SendOpusPacketFrame { get; private set; }

        public void Reset()
        {
            if (!_released)
            {
                VadPacket.Reset();
                AsrPackets.Reset();
                SendOpusPacketFrame.Reset();
            }
        }

        public void Release()
        {
            _released = true;
            VadPacket.Dispose();
            AsrPackets.Dispose();
            SendOpusPacketFrame.Dispose();
        }
    }
}