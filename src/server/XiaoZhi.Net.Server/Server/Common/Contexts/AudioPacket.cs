using SherpaOnnx;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class AudioPacket
    {

        private bool _released;

        public AudioPacket()
        {
            VadPacket = new CircularBuffer(960 * 100);
        }
        public CircularBuffer VadPacket { get; private set; }

        public void Reset()
        {
            if (!_released)
            {
                VadPacket.Reset();;
            }
        }

        public void Release()
        {
            _released = true;
            VadPacket.Dispose();
        }
    }
}