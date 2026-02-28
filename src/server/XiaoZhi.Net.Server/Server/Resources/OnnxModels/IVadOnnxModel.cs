using XiaoZhi.Net.Server.Resources.OnnxModels.VAD.Models;

namespace XiaoZhi.Net.Server.Resources.OnnxModels
{
    internal interface IVadOnnxModel : IOnnxModel
    {
        float Infer(float[] audioSamples, int sampleRate, SileroModelState modelState);
    }
}
