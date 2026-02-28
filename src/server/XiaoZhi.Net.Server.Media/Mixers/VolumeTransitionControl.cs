using System.Diagnostics;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Mixers
{
    internal class VolumeTransitionControl
    {
        private float _startVolume = 0.0f;
        private float _currentVolume = 0.0f;
        private float _targetVolume = 0.0f;
        private long _transitionStartTicks = 0;
        private long _transitionDurationTicks = 0;
        private VolumeTransitionCurve _transitionCurve = VolumeTransitionCurve.Logarithmic;
        private bool _isInitialized = false;

        public float CurrentVolume => _currentVolume;
        public float TargetVolume => _targetVolume;
        public bool IsTransitioning => _transitionDurationTicks > 0 && Stopwatch.GetTimestamp() - _transitionStartTicks < _transitionDurationTicks;

        public void StartTransition(float newTargetVolume, int durationMs, VolumeTransitionCurve curve)
        {
            if (!_isInitialized)
            {
                _currentVolume = newTargetVolume;
                _targetVolume = newTargetVolume;
                _startVolume = newTargetVolume;
                _isInitialized = true;
                return;
            }

            if (Math.Abs(_currentVolume - newTargetVolume) > 0.01f)
            {
                _startVolume = _currentVolume;
                _targetVolume = newTargetVolume;
                _transitionStartTicks = Stopwatch.GetTimestamp();
                _transitionDurationTicks = durationMs > 0
                    ? (long)(durationMs / 1000.0 * Stopwatch.Frequency)
                    : 0;
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

            var elapsedTicks = Stopwatch.GetTimestamp() - _transitionStartTicks;
            var progress = Math.Min(1.0f, (float)((double)elapsedTicks / _transitionDurationTicks));

            if (progress >= 1.0f)
            {
                _currentVolume = _targetVolume;
                _startVolume = _targetVolume;
                return _currentVolume;
            }

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
