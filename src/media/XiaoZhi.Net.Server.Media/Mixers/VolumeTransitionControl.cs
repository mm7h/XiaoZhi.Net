using System.Diagnostics;
using XiaoZhi.Net.Server.Media.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Media.Mixers
{
    internal class VolumeTransitionControl
    {
        private float _startVolume = 0.0f;
        private long _transitionStartTicks = 0;
        private long _transitionDurationTicks = 0;
        private VolumeTransitionCurve _transitionCurve = VolumeTransitionCurve.Logarithmic;
        private bool _isInitialized = false;

        public float CurrentVolume { get; private set; } = 0.0f;
        public float TargetVolume { get; private set; } = 0.0f;
        public bool IsTransitioning => this._transitionDurationTicks > 0 && Stopwatch.GetTimestamp() - this._transitionStartTicks < this._transitionDurationTicks;

        public void StartTransition(float newTargetVolume, int durationMs, VolumeTransitionCurve curve)
        {
            if (!this._isInitialized)
            {
                this.CurrentVolume = newTargetVolume;
                this.TargetVolume = newTargetVolume;
                this._startVolume = newTargetVolume;
                this._isInitialized = true;
                return;
            }

            if (Math.Abs(this.CurrentVolume - newTargetVolume) > 0.01f)
            {
                this._startVolume = this.CurrentVolume;
                this.TargetVolume = newTargetVolume;
                this._transitionStartTicks = Stopwatch.GetTimestamp();
                this._transitionDurationTicks = durationMs > 0
                    ? (long)(durationMs / 1000.0 * Stopwatch.Frequency)
                    : 0;
                this._transitionCurve = curve;
            }
            else
            {
                this.CurrentVolume = newTargetVolume;
                this.TargetVolume = newTargetVolume;
                this._startVolume = newTargetVolume;
            }
        }

        public float UpdateAndGetCurrentVolume()
        {
            if (!this.IsTransitioning)
            {
                this.CurrentVolume = this.TargetVolume;
                return this.CurrentVolume;
            }

            var elapsedTicks = Stopwatch.GetTimestamp() - this._transitionStartTicks;
            var progress = Math.Min(1.0f, (float)((double)elapsedTicks / this._transitionDurationTicks));

            if (progress >= 1.0f)
            {
                this.CurrentVolume = this.TargetVolume;
                this._startVolume = this.TargetVolume;
                return this.CurrentVolume;
            }

            var adjustedProgress = ApplyTransitionCurve(progress, this._transitionCurve);
            this.CurrentVolume = Lerp(this._startVolume, this.TargetVolume, adjustedProgress);

            return this.CurrentVolume;
        }

        private static float ApplyTransitionCurve(float progress, VolumeTransitionCurve curve)
        {
            return curve switch
            {
                VolumeTransitionCurve.Linear => progress,
                VolumeTransitionCurve.Logarithmic => (float)(Math.Log10(1 + (9 * progress)) / Math.Log10(10)),
                VolumeTransitionCurve.Sine => (float)Math.Sin(progress * Math.PI / 2),
                VolumeTransitionCurve.Exponential => progress * progress,
                _ => progress
            };
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + ((to - from) * t);
        }
    }
}
