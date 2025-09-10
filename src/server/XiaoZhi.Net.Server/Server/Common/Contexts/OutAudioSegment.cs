using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutAudioSegment : OutSegment
    {
        private float[] _audioData = null!;
        private AudioType _audioType;
        private bool _needResample;

        public OutAudioSegment() : base()
        {
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public float[] AudioData => _audioData;

        /// <summary>
        /// 是否需要重采样
        /// </summary>
        public bool NeedResample => _needResample;

        /// <summary>
        /// 发送的音频类型
        /// </summary>
        public AudioType AudioType => _audioType;

        // 为对象池提供初始化方法
        public void Initialize(float[] audioData, AudioType audioType, bool isFirst, bool isLast, bool needResample = true)
        {
            this._audioData = audioData;
            this._audioType = audioType;
            this._needResample = needResample;
            this.IsFirst = isFirst;
            this.IsLast = isLast;
            this.SetContent(string.Empty);
        }

        public void Initialize(float[] audioData, AudioType audioType, OutSegment outSegment, bool needResample = true)
        {
            this._audioData = audioData;
            this._audioType = audioType;
            this._needResample = needResample;
            this.IsFirst = outSegment.IsFirst;
            this.IsLast = outSegment.IsLast;
            this.SetContent(outSegment.Content);
        }

        public override void Reset()
        {
            this._audioData = null!;
            this._audioType = default;
            this._needResample = default;
            base.Reset();
        }
    }
}
