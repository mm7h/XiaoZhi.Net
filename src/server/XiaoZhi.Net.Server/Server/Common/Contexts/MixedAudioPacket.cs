using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class MixedAudioPacket
    {
        private float[] _audioData = null!;
        public MixedAudioPacket()
        {
        }
        public float[] Data => this._audioData;
        public bool IsFirstFrame { get; set; }
        public bool IsLastFrame { get; set; }
        public void Initialize(float[] audioData, bool isFirstFrame, bool isLastFrame)
        {
            this._audioData = audioData;
            this.IsFirstFrame = isFirstFrame;
            this.IsLastFrame = isLastFrame;
        }
        public void Reset()
        {
            this._audioData = null!;
            this.IsFirstFrame = false;
            this.IsLastFrame = false;
        }
    }
}
