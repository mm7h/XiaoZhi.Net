using System;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models
{
    /// <summary>
    /// Per-session state for Silero VAD v4 model inference.
    /// Each client session should have its own instance for lock-free concurrent access.
    /// </summary>
    internal sealed class SileroModelState
    {
        /// <summary>
        /// Hidden state tensor for LSTM (2 layers x 1 batch x 64 units for 16kHz, or 128 for 8kHz).
        /// Shape: [2, 1, 64] for 16kHz or [2, 1, 128] for 8kHz
        /// </summary>
        public float[] HiddenState { get; private set; }

        /// <summary>
        /// Cell state tensor for LSTM.
        /// Shape: [2, 1, 64] for 16kHz or [2, 1, 128] for 8kHz
        /// </summary>
        public float[] CellState { get; private set; }

        /// <summary>
        /// Last sample rate used for validation.
        /// </summary>
        public int LastSampleRate { get; set; }

        private readonly int _stateSize;

        /// <summary>
        /// Creates a new model state for the specified sample rate.
        /// </summary>
        /// <param name="sampleRate">Sample rate (8000 or 16000)</param>
        public SileroModelState(int sampleRate)
        {
            // Silero VAD v4: state size depends on sample rate
            // 16kHz: 64 units, 8kHz: 128 units (2 layers x 1 batch x units)
            this._stateSize = sampleRate == 16000 ? 64 : 128;
            this.HiddenState = new float[2 * 1 * this._stateSize];
            this.CellState = new float[2 * 1 * this._stateSize];
            this.LastSampleRate = sampleRate;
        }

        /// <summary>
        /// Updates the hidden state with new values from model output.
        /// </summary>
        public void UpdateHiddenState(float[] newState)
        {
            if (newState.Length != this.HiddenState.Length)
            {
                throw new ArgumentException(string.Format(Lang.SileroModelState_UpdateHiddenState_SizeMismatch, this.HiddenState.Length, newState.Length));
            }
            Array.Copy(newState, this.HiddenState, newState.Length);
        }

        /// <summary>
        /// Updates the cell state with new values from model output.
        /// </summary>
        public void UpdateCellState(float[] newState)
        {
            if (newState.Length != this.CellState.Length)
            {
                throw new ArgumentException(string.Format(Lang.SileroModelState_UpdateCellState_SizeMismatch, this.CellState.Length, newState.Length));
            }
            Array.Copy(newState, this.CellState, newState.Length);
        }

        /// <summary>
        /// Resets the model state to initial values (zeros).
        /// Call this when starting a new audio stream or after errors.
        /// </summary>
        public void Reset()
        {
            Array.Clear(this.HiddenState, 0, this.HiddenState.Length);
            Array.Clear(this.CellState, 0, this.CellState.Length);
        }
    }
}
