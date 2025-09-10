using System.Collections.Generic;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutAudioSegment
    {
        private float[] _audioData = null!;
        private Dictionary<AudioType, string?> _contents = null!;

        public OutAudioSegment() : base()
        {
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public float[] AudioData => this._audioData;

        /// <summary>
        /// 是否需要重采样
        /// </summary>
        public Dictionary<AudioType, string?> Contents => this._contents;

        /// <summary>
        /// 是否为第一段
        /// </summary>
        public bool IsFirst { get; set; }

        /// <summary>
        /// 是否为最后一段
        /// </summary>
        public bool IsLast { get; set; }

        // 为对象池提供初始化方法
        public void Initialize(float[] audioData, bool isFirst, bool isLast, Dictionary<AudioType, string?> contents)
        {
            this._audioData = audioData;
            this._contents = contents;
            this.IsFirst = isFirst;
            this.IsLast = isLast;
        }

        public void Reset()
        {
            this._audioData = null!;
            this._contents = default;
            this.IsFirst = false;
            this.IsLast = false;
        }
    }
}
