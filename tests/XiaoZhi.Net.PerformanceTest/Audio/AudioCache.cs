using Concentus;
using Concentus.Enums;
using NAudio.Wave;

namespace XiaoZhi.Net.PerformanceTest.Audio;

internal sealed class AudioCache
{
    private const int FrameDurationMilliseconds = 60;
    private const int OpusBitrate = 64_000;
    private static readonly HashSet<int> s_supportedSampleRates = [8000, 12000, 16000, 24000, 48000];

    private AudioCache(IReadOnlyDictionary<string, CachedAudio> entries, CachedAudio selected)
    {
        this.Entries = entries;
        this.Selected = selected;
    }

    public IReadOnlyDictionary<string, CachedAudio> Entries { get; }
    public CachedAudio Selected { get; }

    public static AudioCache LoadFromOutputDirectory(string outputDirectory, string? selector)
    {
        string audioDirectory = Path.Combine(outputDirectory, "audios");
        if (!Directory.Exists(audioDirectory))
        {
            throw new DirectoryNotFoundException($"找不到音频目录: {audioDirectory}");
        }

        string[] paths = Directory.EnumerateFiles(audioDirectory, "*.wav", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException($"音频目录中没有 WAV 文件: {audioDirectory}");
        }

        Dictionary<string, CachedAudio> entries = new(StringComparer.OrdinalIgnoreCase);
        AudioFormat? expectedFormat = null;
        foreach (string path in paths)
        {
            CachedAudio cachedAudio = LoadAndEncode(path);
            if (expectedFormat is not null && cachedAudio.Format != expectedFormat)
            {
                throw new InvalidOperationException(
                    $"所有 WAV 必须使用相同格式。{Path.GetFileName(path)} 为 {cachedAudio.Format}，期望 {expectedFormat}。");
            }

            expectedFormat = cachedAudio.Format;
            entries.Add(cachedAudio.Name, cachedAudio);
        }

        CachedAudio selected = SelectAudio(entries, audioDirectory, selector);
        return new AudioCache(entries, selected);
    }

    private static CachedAudio SelectAudio(IReadOnlyDictionary<string, CachedAudio> entries, string audioDirectory, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            if (entries.Count == 1)
            {
                return entries.Values.Single();
            }

            throw new InvalidOperationException(
                $"audios 中有多个 WAV，请使用 --audio 指定其文件名。候选: {string.Join(", ", entries.Keys.OrderBy(name => name))}");
        }

        string candidateName = Path.GetFileName(selector);
        if (entries.TryGetValue(candidateName, out CachedAudio? cachedAudio))
        {
            return cachedAudio;
        }

        string normalizedSelector = Path.GetFullPath(Path.IsPathRooted(selector)
            ? selector
            : Path.Combine(audioDirectory, selector));
        CachedAudio? byPath = entries.Values.FirstOrDefault(audio =>
            string.Equals(audio.SourcePath, normalizedSelector, StringComparison.OrdinalIgnoreCase));
        if (byPath is not null)
        {
            return byPath;
        }

        throw new InvalidOperationException(
            $"未找到 --audio 指定的文件: {selector}。候选: {string.Join(", ", entries.Keys.OrderBy(name => name))}");
    }

    private static CachedAudio LoadAndEncode(string path)
    {
        using WaveFileReader reader = new(path);
        WaveFormat waveFormat = reader.WaveFormat;
        ValidateFormat(path, waveFormat);

        byte[] pcmBytes = ReadAllPcm(reader);
        if (pcmBytes.Length == 0)
        {
            throw new InvalidOperationException($"WAV 不包含音频样本: {path}");
        }

        int samplesPerFrame = waveFormat.SampleRate * FrameDurationMilliseconds / 1000;
        short[] allSamples = new short[pcmBytes.Length / sizeof(short)];
        Buffer.BlockCopy(pcmBytes, 0, allSamples, 0, pcmBytes.Length);

        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(
            waveFormat.SampleRate,
            waveFormat.Channels,
            OpusApplication.OPUS_APPLICATION_VOIP);
        try
        {
            encoder.Bitrate = OpusBitrate;
            List<ReadOnlyMemory<byte>> frames = EncodeFrames(encoder, allSamples, samplesPerFrame);
            AudioFormat format = new(waveFormat.SampleRate, waveFormat.Channels, waveFormat.BitsPerSample, FrameDurationMilliseconds);
            return new CachedAudio(Path.GetFileName(path), Path.GetFullPath(path), format, frames, allSamples.Length);
        }
        finally
        {
            encoder.Dispose();
        }
    }

    private static void ValidateFormat(string path, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} 必须是未压缩的 16-bit PCM 单声道 WAV，实际格式: {format.Encoding}, {format.BitsPerSample}-bit, {format.Channels} 声道。");
        }

        if (!s_supportedSampleRates.Contains(format.SampleRate))
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} 的采样率 {format.SampleRate} 不受当前服务端 Opus Hello 协议支持。");
        }
    }

    private static byte[] ReadAllPcm(WaveFileReader reader)
    {
        using MemoryStream output = new((int)Math.Min(reader.Length, int.MaxValue));
        byte[] buffer = new byte[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        if (output.Length % sizeof(short) != 0)
        {
            throw new InvalidOperationException("PCM 数据未按 16-bit 样本对齐。");
        }

        return output.ToArray();
    }

    private static List<ReadOnlyMemory<byte>> EncodeFrames(IOpusEncoder encoder, short[] allSamples, int samplesPerFrame)
    {
        List<ReadOnlyMemory<byte>> frames = new((allSamples.Length + samplesPerFrame - 1) / samplesPerFrame);
        byte[] outputBuffer = new byte[4000];
        for (int offset = 0; offset < allSamples.Length; offset += samplesPerFrame)
        {
            short[] frame = new short[samplesPerFrame];
            int copiedSamples = Math.Min(samplesPerFrame, allSamples.Length - offset);
            Array.Copy(allSamples, offset, frame, 0, copiedSamples);

            int encodedLength = encoder.Encode(frame, samplesPerFrame, outputBuffer, outputBuffer.Length);
            if (encodedLength <= 0)
            {
                throw new InvalidOperationException("Opus 编码器未生成有效帧。");
            }

            byte[] encodedFrame = new byte[encodedLength];
            Buffer.BlockCopy(outputBuffer, 0, encodedFrame, 0, encodedLength);
            frames.Add(encodedFrame);
        }

        return frames;
    }
}
