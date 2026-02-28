using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XiaoZhi.Net.Server.I18n;
using XiaoZhi.Net.Server.Providers.VAD.Native;
using XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models;

namespace XiaoZhi.Net.Server.Resources.OnnxModels.VAD
{
    /// <summary>
    /// Silero VAD v4 ONNX model wrapper.
    /// This class holds the ONNX InferenceSession and should be registered as a Singleton.
    /// The Infer() method is thread-safe when each caller provides their own SileroModelState.
    /// 
    /// Model inputs (v4):
    /// - input: [batch_size, chunk_samples] - Audio samples (512 for 16kHz, 256 for 8kHz)
    /// - sr: [1] - Sample rate as int64
    /// - h: [2, batch_size, 64] - Hidden state (for 16kHz) or [2, batch_size, 128] (for 8kHz)
    /// - c: [2, batch_size, 64] - Cell state (for 16kHz) or [2, batch_size, 128] (for 8kHz)
    /// 
    /// Model outputs (v4):
    /// - output: [batch_size, 1] - Speech probability
    /// - hn: [2, batch_size, 64/128] - New hidden state
    /// - cn: [2, batch_size, 64/128] - New cell state
    /// </summary>
    internal sealed class SileroOnnx : BaseOnnxModel<SileroOnnx>, IVadOnnxModel
    {
        private readonly object _sessionLock = new object();
        
        private InferenceSession? _session;
        private bool _disposed;

        private static readonly int[] SUPPORTED_SAMPLE_RATES = [8000, 16000];

        public SileroOnnx(ILogger<SileroOnnx> logger) : base(logger)
        {
        }

        public override string ModelType => "vad";
        public override string ModelName => nameof(SileroNative);

        public override bool Load(ModelSetting settings)
        {
            if (this.CheckModelExist())
            {
                SessionOptions sessionOptions = new SessionOptions
                {
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 1,
                    EnableCpuMemArena = true,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                string modelPath = Path.Combine(this.ModelFileFoler, "model.onnx");
                this._session = new InferenceSession(modelPath, sessionOptions);
                this.Logger.LogInformation(Lang.SileroOnnx_Load_Loaded, this.ModelName);
                return true;
            }
            else
            {
                this.Logger.LogError(Lang.SileroOnnx_Load_InvalidModel, this.ModelName);
                return false;
            }
        }

        /// <summary>
        /// Creates a new model state for a client session.
        /// Each client should have its own state instance for lock-free concurrent access.
        /// </summary>
        /// <param name="sampleRate">Sample rate (8000 or 16000)</param>
        public static SileroModelState CreateModelState(int sampleRate)
        {
            return new SileroModelState(sampleRate);
        }

        /// <summary>
        /// Run inference on the model with the given audio samples.
        /// Multiple callers can use this method concurrently with their own SileroModelState.
        /// </summary>
        /// <param name="audioSamples">Audio samples as float array (normalized to [-1, 1])</param>
        /// <param name="sampleRate">Sample rate (8000 or 16000)</param>
        /// <param name="modelState">Per-client model state for lock-free concurrent access</param>
        /// <returns>Speech probability (0.0 to 1.0)</returns>
        public float Infer(float[] audioSamples, int sampleRate, SileroModelState modelState)
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(SileroOnnx), string.Format(Lang.SileroOnnx_Infer_Disposed, nameof(SileroOnnx)));
            }

            if (this._session is null)
            {
                throw new InvalidOperationException(Lang.SileroOnnx_Infer_SessionNotInitialized);
            }

            this.ValidateInput(audioSamples, sampleRate, modelState);

            int expectedSamples = sampleRate == 16000 ? 512 : 256;
            if (audioSamples.Length != expectedSamples)
            {
                throw new ArgumentException(string.Format(Lang.SileroOnnx_Infer_SampleCountMismatch, expectedSamples, sampleRate, audioSamples.Length));
            }

            int stateSize = sampleRate == 16000 ? 64 : 128;
            const int batchSize = 1;

            // Prepare input tensors
            var inputTensor = new DenseTensor<float>(audioSamples, new int[] { batchSize, audioSamples.Length });
            var srTensor = new DenseTensor<long>(new long[] { sampleRate }, new int[] { 1 });
            var hTensor = new DenseTensor<float>(modelState.HiddenState, new int[] { 2, batchSize, stateSize });
            var cTensor = new DenseTensor<float>(modelState.CellState, new int[] { 2, batchSize, stateSize });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("sr", srTensor),
                NamedOnnxValue.CreateFromTensor("h", hTensor),
                NamedOnnxValue.CreateFromTensor("c", cTensor)
            };

            // Run inference (session.Run is thread-safe for reading, but we lock to be extra safe)
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs;
            lock (_sessionLock)
            {
                outputs = this._session.Run(inputs);
            }

            using (outputs)
            {
                // Extract output probability
                var outputTensor = outputs.First(o => o.Name == "output").AsTensor<float>();
                float speechProb = outputTensor[0];

                // Extract and update hidden state
                var hnTensor = outputs.First(o => o.Name == "hn").AsTensor<float>();
                float[] newHiddenState = new float[2 * batchSize * stateSize];
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < batchSize; j++)
                    {
                        for (int k = 0; k < stateSize; k++)
                        {
                            newHiddenState[i * batchSize * stateSize + j * stateSize + k] = hnTensor[i, j, k];
                        }
                    }
                }
                modelState.UpdateHiddenState(newHiddenState);

                // Extract and update cell state
                var cnTensor = outputs.First(o => o.Name == "cn").AsTensor<float>();
                float[] newCellState = new float[2 * batchSize * stateSize];
                for (int i = 0; i < 2; i++)
                {
                    for (int j = 0; j < batchSize; j++)
                    {
                        for (int k = 0; k < stateSize; k++)
                        {
                            newCellState[i * batchSize * stateSize + j * stateSize + k] = cnTensor[i, j, k];
                        }
                    }
                }
                modelState.UpdateCellState(newCellState);

                return speechProb;
            }
        }

        private void ValidateInput(float[] audioSamples, int sampleRate, SileroModelState modelState)
        {
            if (audioSamples == null || audioSamples.Length == 0)
            {
                throw new ArgumentException(Lang.SileroOnnx_ValidateInput_SamplesEmpty);
            }

            if (!SUPPORTED_SAMPLE_RATES.Contains(sampleRate))
            {
                throw new ArgumentException(string.Format(Lang.SileroOnnx_ValidateInput_UnsupportedSampleRate, sampleRate));
            }

            if (modelState.LastSampleRate != sampleRate)
            {
                this.Logger.LogDebug(Lang.SileroOnnx_ValidateInput_SampleRateChanged, modelState.LastSampleRate, sampleRate);
                modelState.Reset();
                modelState.LastSampleRate = sampleRate;
            }
        }

        public override void Dispose()
        {
            if (!this._disposed)
            {
                this._session?.Dispose();
                this._disposed = true;
                this.Logger.LogInformation(Lang.SileroOnnx_Dispose_Disposed);
            }
            GC.SuppressFinalize(this);
        }
    }
}
