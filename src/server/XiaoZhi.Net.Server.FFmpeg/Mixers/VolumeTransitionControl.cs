using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Mixers
{
    internal class VolumeTransitionControl
    {
        private float _startVolume = 1.0f;
        private float _currentVolume = 1.0f;
        private float _targetVolume = 1.0f;
        private DateTime _transitionStartTime = DateTime.MinValue;
        private int _transitionDurationMs = 500;
        private VolumeTransitionCurve _transitionCurve = VolumeTransitionCurve.Logarithmic;

        public float CurrentVolume => _currentVolume;
        public float TargetVolume => _targetVolume;
        public bool IsTransitioning => DateTime.Now < _transitionStartTime.AddMilliseconds(_transitionDurationMs);

        public void StartTransition(float newTargetVolume, int durationMs, VolumeTransitionCurve curve)
        {
            if (Math.Abs(_currentVolume - newTargetVolume) > 0.01f) // 只有显著变化时才开始过渡
            {
                _startVolume = _currentVolume; // 记录过渡开始时的音量
                _targetVolume = newTargetVolume;
                _transitionStartTime = DateTime.Now;
                _transitionDurationMs = durationMs;
                _transitionCurve = curve;
            }
            else
            {
                _currentVolume = newTargetVolume;
                _targetVolume = newTargetVolume;
                _startVolume = newTargetVolume;
            }
        }

        public float UpdateAndGetCurrentVolume()
        {
            if (!IsTransitioning)
            {
                _currentVolume = _targetVolume;
                return _currentVolume;
            }

            var elapsed = (DateTime.Now - _transitionStartTime).TotalMilliseconds;
            var progress = Math.Min(1.0f, (float)(elapsed / _transitionDurationMs));

            if (progress >= 1.0f)
            {
                _currentVolume = _targetVolume;
                _startVolume = _targetVolume; // 更新起始音量
                return _currentVolume;
            }

            // 应用过渡曲线
            var adjustedProgress = ApplyTransitionCurve(progress, _transitionCurve);
            _currentVolume = Lerp(_startVolume, _targetVolume, adjustedProgress);

            return _currentVolume;
        }

        private static float ApplyTransitionCurve(float progress, VolumeTransitionCurve curve)
        {
            return curve switch
            {
                VolumeTransitionCurve.Linear => progress,
                VolumeTransitionCurve.Logarithmic => (float)(Math.Log10(1 + 9 * progress) / Math.Log10(10)),
                VolumeTransitionCurve.Sine => (float)Math.Sin(progress * Math.PI / 2),
                VolumeTransitionCurve.Exponential => progress * progress,
                _ => progress
            };
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * t;
        }
    }
}
