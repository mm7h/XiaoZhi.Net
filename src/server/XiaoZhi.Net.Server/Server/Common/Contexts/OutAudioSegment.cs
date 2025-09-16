using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Common.Contexts
{
    internal class OutAudioSegment : OutSegment
    {
        private float[] _audioData = null!;

        public OutAudioSegment() : base()
        {
        }

        /// <summary>
        /// 段落内容
        /// </summary>
        public float[] AudioData => this._audioData;

        public AudioType AudioType { get; private set; }

        // 为对象池提供初始化方法
        public void Initialize(float[] audioData, AudioType audioType, string content, bool isFirstSegment, bool isLastSegment)
        {
            this._audioData = audioData;
            this.AudioType = audioType;
            base.Initialize(content, isFirstSegment, isLastSegment);
        }

        public override void Reset()
        {
            this._audioData = null!;
            this.AudioType = AudioType.None;
            base.Reset();
        }
    }
}
