using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;
using XiaoZhi.Net.Server.FFmpeg.Abstractions;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Dtos;
using XiaoZhi.Net.Server.FFmpeg.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.FFmpeg.Mixers
{
    internal sealed unsafe class FFmpegAudioMixer : IAudioMixer
    {
        #region 私有字段

        private readonly ILogger<FFmpegAudioMixer> _logger;
        
        // 音频流管理
        private readonly Dictionary<AudioType, AudioStreamContext> _audioStreams;
        private readonly Dictionary<AudioType, IntPtr> _audioFifos; // AVAudioFifo*
        private readonly Dictionary<AudioType, VolumeTransitionControl> _volumeStates;
        private readonly Dictionary<AudioType, float> _volumeLevels;
        private readonly Dictionary<AudioType, int> _priorities;
        private readonly object _streamLock = new();
        
        // FFmpeg Filter Graph相关
        private AVFilterGraph* _filterGraph;
        private AVFilterContext* _sinkFilterCtx;
        private readonly Dictionary<AudioType, IntPtr> _sourceFilterCtxs; // AVFilterContext*
        private AVFilterInOut** _filterInputs;
        private AVFilterInOut* _filterOutput;
        
        // 音频格式参数
        private int _outputSampleRate;
        private int _outputChannels;
        private int _frameDuration;
        private int _frameSampleCount;
        private AVSampleFormat _sampleFormat;
        private ulong _channelLayout;
        
        // 配置和状态管理
        private AudioMixerConfig _config = new();
        private bool _initialized;
        private bool _disposed;
        private volatile bool _isMixing = false;
        private volatile bool _shouldStop = false;
        private readonly object _filterLock = new();
        
        // 处理线程和同步
        private Thread? _mixingThread;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ManualResetEventSlim _dataAvailableEvent = new(false);
        
        // 统计信息
        private AudioMixerStats _currentStats = new();
        private long _pts = 0;
        private int _totalStreamCount = 0;
        private int _completedStreamCount = 0;
        
        // 输出事件标志
        private bool _hasEmittedFirst = false;
        private bool _hasEmittedLast = false;
        private bool _draining = false;
        
        // 严格时序控制
        private bool _enabledStrictTiming = false;
        private readonly Queue<float[]> _pendingOutputData = new();
        private DateTime _lastOutputTime = DateTime.MinValue;

        #endregion

        #region 事件和属性

        public event Action<AudioMixerState>? StateChanged;
        public event Action<float[], bool, bool>? OnMixedAudioDataAvailable;
        public event Action<AudioMixerStats>? OnStatsUpdated;

        public bool IsInitialized => _initialized;
        public int OutputSampleRate => _outputSampleRate;
        public int OutputChannels => _outputChannels;
        public int FrameDuration => _frameDuration;

        #endregion

        #region 构造函数

        public FFmpegAudioMixer(ILogger<FFmpegAudioMixer> logger)
        {
            _logger = logger;
            _audioStreams = new Dictionary<AudioType, AudioStreamContext>();
            _audioFifos = new Dictionary<AudioType, IntPtr>();
            _sourceFilterCtxs = new Dictionary<AudioType, IntPtr>();
            _volumeStates = new Dictionary<AudioType, VolumeTransitionControl>();
            _volumeLevels = new Dictionary<AudioType, float>();
            _priorities = new Dictionary<AudioType, int>();
            
            // 初始化音频类型默认配置
            InitializeAudioTypes();
            
            // 初始化FFmpeg日志级别
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
        }

        #endregion

        #region 初始化和配置

        private void InitializeAudioTypes()
        {
            var audioTypes = new[] { AudioType.TTS, AudioType.Music, AudioType.SystemNotification };

            foreach (var audioType in audioTypes)
            {
                switch (audioType)
                {
                    case AudioType.SystemNotification:
                        _volumeLevels[audioType] = 1.0f;
                        _priorities[audioType] = 1;
                        break;
                    case AudioType.TTS:
                        _volumeLevels[audioType] = 0.9f;
                        _priorities[audioType] = 2;
                        break;
                    case AudioType.Music:
                        _volumeLevels[audioType] = 0.3f;
                        _priorities[audioType] = 3;
                        break;
                    default:
                        _volumeLevels[audioType] = 0.5f;
                        _priorities[audioType] = 4;
                        break;
                }
            }
        }

        private void UpdateVolumeLevelsFromConfig()
        {
            _volumeLevels[AudioType.SystemNotification] = _config.SystemNotificationVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.TTS] = _config.TTSVolumeConfig.BaseVolume;
            _volumeLevels[AudioType.Music] = _config.MusicVolumeConfig.BaseVolume;
        }

        public bool Initialize(int outputSampleRate, int outputChannels, int frameDuration, AudioMixerConfig? config = null)
        {
            lock (_filterLock)
            {
                try
                {
                    if (_initialized)
                    {
                        _logger.LogWarning("FFmpegAudioMixer is already initialized");
                        return true;
                    }

                    if (config is not null)
                    {
                        _config = config;
                    }

                    _outputSampleRate = outputSampleRate;
                    _outputChannels = outputChannels;
                    _frameDuration = frameDuration;
                    _frameSampleCount = outputSampleRate * frameDuration / 1000 * outputChannels;
                    // 使用打包浮点格式，便于与托管 float[] 互操作
                    _sampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLT;
                    _channelLayout = outputChannels == 2 ? ffmpeg.AV_CH_LAYOUT_STEREO : ffmpeg.AV_CH_LAYOUT_MONO;

                    // 更新音量配置
                    UpdateVolumeLevelsFromConfig();
                    
                    // 设置严格时序控制
                    _enabledStrictTiming = _config.EnabledStrictTiming;

                    _initialized = true;
                    SetState(AudioMixerState.Idle);

                    _logger.LogInformation("FFmpeg audio mixer initialized successfully with SampleRate={SampleRate}, Channels={Channels}, FrameDuration={FrameDuration}ms",
                        outputSampleRate, outputChannels, frameDuration);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize FFmpeg audio mixer");
                    return false;
                }
            }
        }

        #endregion

        #region 音频流管理

        public void AddAudioData(AudioType audioType, float[] audioData)
        {
            if (!_initialized || _disposed || audioData == null || audioData.Length == 0)
                return;

            lock (_streamLock)
            {
                // 获取或创建音频流上下文
                if (!_audioStreams.TryGetValue(audioType, out var streamContext))
                {
                    streamContext = CreateAudioStreamContext(audioType);
                    _audioStreams[audioType] = streamContext;
                    _totalStreamCount++;
                    
                    // 重新配置filter graph
                    ReconfigureFilterGraph();
                }

                // 写入数据到FIFO缓冲区
                WriteToAudioFifo(audioType, audioData);
                
                // 标记有数据可用
                _dataAvailableEvent.Set();
                
                // 启动混音处理
                if (!_isMixing)
                {
                    StartMixingProcess();
                }
            }
        }

        public void StopAudioStream(AudioType audioType)
        {
            lock (_streamLock)
            {
                if (_audioStreams.TryGetValue(audioType, out var streamContext))
                {
                    streamContext.IsStopping = true;
                    _logger.LogDebug("Marked audio stream {AudioType} for stopping", audioType);
                }
            }
        }

        private AudioStreamContext CreateAudioStreamContext(AudioType audioType)
        {
            var context = new AudioStreamContext
            {
                AudioType = audioType,
                IsActive = true,
                IsStopping = false,
                ProcessedFrameCount = 0,
                SourceClosed = false
            };

            // 创建FIFO缓冲区
            var fifo = ffmpeg.av_audio_fifo_alloc(_sampleFormat, _outputChannels, _frameSampleCount * 30);
            if (fifo == null)
            {
                throw new InvalidOperationException($"Failed to allocate audio FIFO for {audioType}");
            }
            
            _audioFifos[audioType] = (IntPtr)fifo;
            
            _logger.LogDebug("Created audio stream context for {AudioType}", audioType);
            return context;
        }

        private void WriteToAudioFifo(AudioType audioType, float[] audioData)
        {
            if (!_audioFifos.TryGetValue(audioType, out var fifoPtr))
                return;

            try
            {
                // 应用音量控制（避免每次写入都重启过渡）
                var volumeState = GetOrCreateVolumeState(audioType);
                var targetVolume = _volumeLevels.GetValueOrDefault(audioType, 0.5f);

                float currentVolume;
                if (!_config.EnableSmoothVolumeControl)
                {
                    currentVolume = targetVolume;
                }
                else
                {
                    const float eps = 0.0001f;
                    if (!volumeState.IsTransitioning && Math.Abs(volumeState.TargetVolume - targetVolume) > eps)
                    {
                        volumeState.StartTransition(targetVolume, _config.VolumeTransitionDurationMs, _config.TransitionCurve);
                    }
                    currentVolume = volumeState.UpdateAndGetCurrentVolume();
                }

                // 应用音量到音频数据
                var processedData = new float[audioData.Length];
                for (int i = 0; i < audioData.Length; i++)
                {
                    processedData[i] = audioData[i] * currentVolume;
                }

                // 转换float数组为FFmpeg格式（打包格式）
                var dataPtr = Marshal.AllocHGlobal(processedData.Length * sizeof(float));
                Marshal.Copy(processedData, 0, dataPtr, processedData.Length);
                
                var samples = processedData.Length / _outputChannels;
                var fifo = (AVAudioFifo*)fifoPtr;
                var tmp = dataPtr; // 需要一个一级指针数组的地址
                var ret = ffmpeg.av_audio_fifo_write(fifo, (void**)&tmp, samples);
                
                Marshal.FreeHGlobal(dataPtr);
                
                if (ret < 0)
                {
                    _logger.LogError("Failed to write audio data to FIFO for {AudioType}, error: {Error}", 
                        audioType, GetFFmpegErrorString(ret));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing audio data to FIFO for {AudioType}", audioType);
            }
        }

        #endregion

        #region Filter Graph管理

        private void ReconfigureFilterGraph()
        {
            try
            {
                CleanupFilterGraph();
                CreateFilterGraph();
                _logger.LogDebug("Filter graph reconfigured with {StreamCount} streams", _audioStreams.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconfigure filter graph");
            }
        }

        private void CreateFilterGraph()
        {
            if (_audioStreams.Count == 0)
                return;

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            if (_filterGraph == null)
            {
                throw new InvalidOperationException("Failed to allocate filter graph");
            }

            // 创建输出sink filter
            CreateSinkFilter();
            
            // 为每个音频流创建source filter
            foreach (var audioType in _audioStreams.Keys)
            {
                CreateSourceFilter(audioType);
            }
            
            // 配置filter graph
            ConfigureFilterGraph();
        }

        private void CreateSinkFilter()
        {
            var sinkFilter = ffmpeg.avfilter_get_by_name("abuffersink");
            if (sinkFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffersink filter");
            }

            AVFilterContext* sinkCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sinkCtx, sinkFilter, "out", null, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create sink filter: {GetFFmpegErrorString(ret)}");
            }
            _sinkFilterCtx = sinkCtx;

            // 设置输出格式参数
            SetSinkFilterParameters();
        }

        private void SetSinkFilterParameters()
        {
            // 设置采样率
            var sr = _outputSampleRate;
            var ret = ffmpeg.av_opt_set_bin(_sinkFilterCtx, "sample_rates",
                (byte*)&sr, sizeof(int), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (ret < 0)
            {
                _logger.LogWarning("Failed to set sample rate for sink filter: {Error}", GetFFmpegErrorString(ret));
            }

            // 设置声道布局
            var cl = _channelLayout;
            ret = ffmpeg.av_opt_set_bin(_sinkFilterCtx, "channel_layouts",
                (byte*)&cl, sizeof(ulong), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (ret < 0)
            {
                _logger.LogWarning("Failed to set channel layout for sink filter: {Error}", GetFFmpegErrorString(ret));
            }

            // 设置采样格式
            var sf = _sampleFormat;
            ret = ffmpeg.av_opt_set_bin(_sinkFilterCtx, "sample_fmts",
                (byte*)&sf, sizeof(AVSampleFormat), ffmpeg.AV_OPT_SEARCH_CHILDREN);
            if (ret < 0)
            {
                _logger.LogWarning("Failed to set sample format for sink filter: {Error}", GetFFmpegErrorString(ret));
            }
        }

        private void CreateSourceFilter(AudioType audioType)
        {
            var sourceFilter = ffmpeg.avfilter_get_by_name("abuffer");
            if (sourceFilter == null)
            {
                throw new InvalidOperationException("Failed to get abuffer filter");
            }

            var filterName = $"in{(int)audioType}";
            var args = $"time_base=1/{_outputSampleRate}:sample_rate={_outputSampleRate}:" +
                      $"sample_fmt={ffmpeg.av_get_sample_fmt_name(_sampleFormat)}:channel_layout=0x{_channelLayout:X}";

            AVFilterContext* sourceCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&sourceCtx, sourceFilter, filterName, args, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create source filter for {audioType}: {GetFFmpegErrorString(ret)}");
            }

            _sourceFilterCtxs[audioType] = (IntPtr)sourceCtx;
        }

        private void ConfigureFilterGraph()
        {
            if (_audioStreams.Count == 1)
            {
                // 单个输入流，直接连接
                var first = _sourceFilterCtxs.Values.First();
                var sourceCtx = (AVFilterContext*)first;
                var ret = ffmpeg.avfilter_link(sourceCtx, 0u, _sinkFilterCtx, 0u);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link single source to sink: {GetFFmpegErrorString(ret)}");
                }
            }
            else
            {
                // 多个输入流，使用amix filter
                CreateAmixFilter();
            }

            // 配置filter graph
            var configRet = ffmpeg.avfilter_graph_config(_filterGraph, null);
            if (configRet < 0)
            {
                throw new InvalidOperationException($"Failed to configure filter graph: {GetFFmpegErrorString(configRet)}");
            }
        }

        private void CreateAmixFilter()
        {
            var amixFilter = ffmpeg.avfilter_get_by_name("amix");
            if (amixFilter == null)
            {
                throw new InvalidOperationException("Failed to get amix filter");
            }

            var inputCount = _audioStreams.Count;
            var args = $"inputs={inputCount}";

            AVFilterContext* amixCtx = null;
            var ret = ffmpeg.avfilter_graph_create_filter(&amixCtx, amixFilter, "amix", args, null, _filterGraph);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to create amix filter: {GetFFmpegErrorString(ret)}");
            }

            // 连接所有输入源到amix
            uint inputIndex = 0;
            foreach (var src in _sourceFilterCtxs.Values)
            {
                var sourceCtx = (AVFilterContext*)src;
                ret = ffmpeg.avfilter_link(sourceCtx, 0u, amixCtx, inputIndex++);
                if (ret < 0)
                {
                    throw new InvalidOperationException($"Failed to link source to amix: {GetFFmpegErrorString(ret)}");
                }
            }

            // 连接amix到sink
            ret = ffmpeg.avfilter_link(amixCtx, 0u, _sinkFilterCtx, 0u);
            if (ret < 0)
            {
                throw new InvalidOperationException($"Failed to link amix to sink: {GetFFmpegErrorString(ret)}");
            }
        }

        private void CleanupFilterGraph()
        {
            if (_filterGraph != null)
            {
                var graph = _filterGraph;
                ffmpeg.avfilter_graph_free(&graph);
                _filterGraph = null;
            }
            
            _sourceFilterCtxs.Clear();
            _sinkFilterCtx = null;
        }

        #endregion

        #region 混音处理

        private void StartMixingProcess()
        {
            if (_isMixing || _disposed)
                return;

            _isMixing = true;
            _shouldStop = false;
            _hasEmittedFirst = false;
            _hasEmittedLast = false;
            _draining = false;
            SetState(AudioMixerState.Mixing);
            
            _mixingThread = new Thread(MixingThreadProc)
            {
                Name = "FFmpegAudioMixer",
                IsBackground = true
            };
            _mixingThread.Start();
            
            _logger.LogDebug("Started mixing process");
        }

        private void MixingThreadProc()
        {
            try
            {
                while (!_shouldStop && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (!ProcessMixing())
                    {
                        // 如果没有活跃流且尚未发出最后一帧，则尝试排空
                        if (_audioStreams.Count == 0 && !_hasEmittedLast && _filterGraph != null)
                        {
                            DrainSinkAndEmitLast();
                        }

                        // 等待数据或检查是否应该停止
                        _dataAvailableEvent.Wait(TimeSpan.FromMilliseconds(50), _cancellationTokenSource.Token);
                        _dataAvailableEvent.Reset();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in mixing thread");
            }
            finally
            {
                _isMixing = false;
                SetState(AudioMixerState.Idle);
                _logger.LogDebug("Mixing thread stopped");
            }
        }

        private bool ProcessMixing()
        {
            lock (_streamLock)
            {
                if (_filterGraph == null)
                    return false;

                bool hasProcessedData = false;
                
                // 处理每个活跃的音频流
                foreach (var kvp in _audioStreams.ToList())
                {
                    var audioType = kvp.Key;
                    var streamContext = kvp.Value;
                    
                    if (!streamContext.IsActive)
                        continue;

                    // 从FIFO读取数据并推送到filter
                    if (ProcessAudioStream(audioType, streamContext))
                    {
                        hasProcessedData = true;
                    }
                }

                // 从filter graph获取混音后的数据
                if (hasProcessedData)
                {
                    RetrieveMixedAudioData();
                }

                // 清理已完成的流
                CleanupCompletedStreams();
                
                return hasProcessedData;
            }
        }

        private bool ProcessAudioStream(AudioType audioType, AudioStreamContext streamContext)
        {
            if (!_audioFifos.TryGetValue(audioType, out var fifoPtr) || 
                !_sourceFilterCtxs.TryGetValue(audioType, out var sourceCtxPtr))
                return false;

            var fifo = (AVAudioFifo*)fifoPtr;
            var sourceCtx = (AVFilterContext*)sourceCtxPtr;

            var availableSamples = ffmpeg.av_audio_fifo_size(fifo);
            if (availableSamples < _frameSampleCount / _outputChannels && !streamContext.IsStopping)
                return false;

            try
            {
                // 创建音频帧
                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                    return false;

                // 设置帧参数
                frame->nb_samples = Math.Min(availableSamples, _frameSampleCount / _outputChannels);

                AVChannelLayout ch;
                ffmpeg.av_channel_layout_from_mask(&ch, _channelLayout);
                frame->ch_layout = ch;
                frame->format = (int)_sampleFormat;
                frame->sample_rate = _outputSampleRate;
                frame->pts = _pts;

                // 分配帧缓冲区
                var ret = ffmpeg.av_frame_get_buffer(frame, 0);
                if (ret < 0)
                {
                    ffmpeg.av_frame_free(&frame);
                    return false;
                }

                // 从FIFO读取数据（打包格式，单平面）
                void** planes = stackalloc void*[1];
                planes[0] = frame->data[0];
                ret = ffmpeg.av_audio_fifo_read(fifo, planes, frame->nb_samples);
                if (ret <= 0)
                {
                    ffmpeg.av_frame_free(&frame);
                    return false;
                }

                int nbSamples = frame->nb_samples; // 捕获以避免释放后访问

                // 推送到filter
                ret = ffmpeg.av_buffersrc_add_frame_flags(sourceCtx, frame, 0);
                ffmpeg.av_frame_free(&frame);

                if (ret < 0)
                {
                    _logger.LogError("Failed to add frame to filter for {AudioType}: {Error}", 
                        audioType, GetFFmpegErrorString(ret));
                    return false;
                }

                streamContext.ProcessedFrameCount++;
                _pts += nbSamples;
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio stream {AudioType}", audioType);
                return false;
            }
        }

        private void RetrieveMixedAudioData()
        {
            if (_sinkFilterCtx == null)
                return;

            try
            {
                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                    return;

                var ret = ffmpeg.av_buffersink_get_frame(_sinkFilterCtx, frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    ffmpeg.av_frame_free(&frame);
                    return;
                }

                if (ret < 0)
                {
                    _logger.LogError("Failed to get frame from sink filter: {Error}", GetFFmpegErrorString(ret));
                    ffmpeg.av_frame_free(&frame);
                    return;
                }

                var mixedData = ConvertFrameToFloatArrayInternal(frame);
                ffmpeg.av_frame_free(&frame);

                if (mixedData != null && mixedData.Length > 0)
                {
                    // 更新统计信息
                    UpdateStatistics(mixedData);
                    
                    if (_enabledStrictTiming)
                    {
                        // 严格时序控制：缓存数据等待合适的输出时间
                        _pendingOutputData.Enqueue(mixedData);
                        OutputPendingDataWithTiming();
                    }
                    else
                    {
                        // 立即输出
                        var isFirst = !_hasEmittedFirst;
                        OnMixedAudioDataAvailable?.Invoke(mixedData, isFirst, false);
                        _hasEmittedFirst = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving mixed audio data");
            }
        }

        private void DrainSinkAndEmitLast()
        {
            if (_sinkFilterCtx == null || _hasEmittedLast)
                return;

            try
            {
                while (true)
                {
                    var frame = ffmpeg.av_frame_alloc();
                    if (frame == null) break;

                    var ret = ffmpeg.av_buffersink_get_frame(_sinkFilterCtx, frame);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        ffmpeg.av_frame_free(&frame);
                        break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                    {
                        ffmpeg.av_frame_free(&frame);
                        if (!_hasEmittedLast)
                        {
                            // 如果启用了严格时序控制，先刷新待处理数据
                            if (_enabledStrictTiming)
                            {
                                FlushPendingOutputData();
                            }
                            
                            var isFirst = !_hasEmittedFirst;
                            OnMixedAudioDataAvailable?.Invoke(Array.Empty<float>(), isFirst, true);
                            _hasEmittedFirst = true;
                            _hasEmittedLast = true;
                            _shouldStop = true;
                        }
                        break;
                    }
                    if (ret < 0)
                    {
                        ffmpeg.av_frame_free(&frame);
                        break;
                    }

                    var mixedData = ConvertFrameToFloatArrayInternal(frame);
                    ffmpeg.av_frame_free(&frame);

                    if (mixedData != null && mixedData.Length > 0)
                    {
                        UpdateStatistics(mixedData);
                        
                        if (_enabledStrictTiming)
                        {
                            // 严格时序控制：缓存数据等待合适的输出时间
                            _pendingOutputData.Enqueue(mixedData);
                            OutputPendingDataWithTiming();
                        }
                        else
                        {
                            // 立即输出
                            var isFirst = !_hasEmittedFirst;
                            OnMixedAudioDataAvailable?.Invoke(mixedData, isFirst, false);
                            _hasEmittedFirst = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error draining sink");
            }
        }

        private void CleanupCompletedStreams()
        {
            var streamsToRemove = new List<AudioType>();
            
            foreach (var kvp in _audioStreams)
            {
                var audioType = kvp.Key;
                var streamContext = kvp.Value;
                
                if (streamContext.IsStopping && _audioFifos.TryGetValue(audioType, out var fifoPtr))
                {
                    var fifo = (AVAudioFifo*)fifoPtr;
                    var remainingSamples = ffmpeg.av_audio_fifo_size(fifo);
                    if (remainingSamples == 0)
                    {
                        // 关闭 source，通知 EOF
                        if (_sourceFilterCtxs.TryGetValue(audioType, out var srcPtr) && !streamContext.SourceClosed)
                        {
                            ffmpeg.av_buffersrc_close((AVFilterContext*)srcPtr, _pts, 0);
                            streamContext.SourceClosed = true;
                        }
                        streamsToRemove.Add(audioType);
                    }
                }
            }
            
            foreach (var audioType in streamsToRemove)
            {
                RemoveAudioStream(audioType);
            }
        }

        private void RemoveAudioStream(AudioType audioType)
        {
            // 移除音频流上下文
            _audioStreams.Remove(audioType);
            
            // 清理FIFO
            if (_audioFifos.TryGetValue(audioType, out var fifoPtr))
            {
                ffmpeg.av_audio_fifo_free((AVAudioFifo*)fifoPtr);
                _audioFifos.Remove(audioType);
            }
            
            // 不立即重建filter graph，避免丢帧；仅移除源引用
            _sourceFilterCtxs.Remove(audioType);
            
            _completedStreamCount++;
            _logger.LogDebug("Removed audio stream {AudioType}", audioType);
            
            // 如果没有活跃流了，进入排空阶段
            if (_audioStreams.Count == 0)
            {
                _draining = true;
            }
        }

        #endregion

        #region 统计和状态管理

        private void UpdateStatistics(float[] audioData)
        {
            float sumSquares = 0;
            float peak = 0;
            
            for (int i = 0; i < audioData.Length; i++)
            {
                var sample = Math.Abs(audioData[i]);
                sumSquares += audioData[i] * audioData[i];
                if (sample > peak) peak = sample;
            }
            
            _currentStats.CurrentRms = (float)Math.Sqrt(sumSquares / audioData.Length);
            _currentStats.CurrentPeak = peak;
            _currentStats.CurrentGainDb = 20 * (float)Math.Log10(Math.Max(_currentStats.CurrentRms, 1e-10f));
            _currentStats.ActiveStreamCount = _audioStreams.Count(kvp => kvp.Value.IsActive);
            
            OnStatsUpdated?.Invoke(_currentStats);
        }

        private void SetState(AudioMixerState newState)
        {
            StateChanged?.Invoke(newState);
            _logger.LogDebug("FFmpeg audio mixer state changed to {State}", newState);
        }

        public AudioMixerStats GetCurrentStats()
        {
            return new AudioMixerStats
            {
                CurrentRms = _currentStats.CurrentRms,
                CurrentPeak = _currentStats.CurrentPeak,
                CurrentGainDb = _currentStats.CurrentGainDb,
                ActiveStreamCount = _currentStats.ActiveStreamCount,
                DelayCompensation = new Dictionary<AudioType, float>()
            };
        }

        #endregion

        #region 工具方法

        private VolumeTransitionControl GetOrCreateVolumeState(AudioType audioType)
        {
            if (!_volumeStates.TryGetValue(audioType, out var volumeState))
            {
                volumeState = new VolumeTransitionControl();
                _volumeStates[audioType] = volumeState;
            }
            return volumeState;
        }

        private void OutputPendingDataWithTiming()
        {
            if (!_pendingOutputData.Any())
                return;

            var now = DateTime.UtcNow;
            var frameDurationMs = _frameDuration;

            // 如果是第一次输出，记录当前时间作为基准
            if (_lastOutputTime == DateTime.MinValue)
            {
                _lastOutputTime = now;
            }

            // 检查是否到了下一个输出时间点
            var expectedNextOutputTime = _lastOutputTime.AddMilliseconds(frameDurationMs);
            if (now >= expectedNextOutputTime)
            {
                if (_pendingOutputData.TryDequeue(out var audioData))
                {
                    var isFirst = !_hasEmittedFirst;
                    OnMixedAudioDataAvailable?.Invoke(audioData, isFirst, false);
                    _hasEmittedFirst = true;
                    _lastOutputTime = now;

                    _logger.LogDebug("Output mixed audio data with strict timing: {DataLength} samples", audioData.Length);
                }
            }
        }

        private void FlushPendingOutputData()
        {
            // 在停止时，输出所有待处理的数据
            while (_pendingOutputData.TryDequeue(out var audioData))
            {
                var isFirst = !_hasEmittedFirst;
                OnMixedAudioDataAvailable?.Invoke(audioData, isFirst, false);
                _hasEmittedFirst = true;
            }
        }

        public void ClearAllBuffers()
        {
            lock (_streamLock)
            {
                foreach (var fifoPtr in _audioFifos.Values)
                {
                    ffmpeg.av_audio_fifo_reset((AVAudioFifo*)fifoPtr);
                }
                
                _logger.LogDebug("Cleared all audio buffers");
            }
        }

        private string GetFFmpegErrorString(int error)
        {
            const int size = 1024;
            byte* buf = stackalloc byte[size];
            ffmpeg.av_strerror(error, buf, (ulong)size);
            return Marshal.PtrToStringAnsi((IntPtr)buf) ?? $"FFmpeg error {error}";
        }

        private float[]? ConvertFrameToFloatArrayInternal(AVFrame* frame)
        {
            try
            {
                var sampleCount = frame->nb_samples * _outputChannels;
                var result = new float[sampleCount];
                Marshal.Copy((IntPtr)frame->data[0], result, 0, sampleCount);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting frame to float array");
                return null;
            }
        }

        #endregion

        #region 资源清理

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_filterLock)
            {
                _shouldStop = true;
                _cancellationTokenSource.Cancel();
                
                // 等待混音线程结束
                _mixingThread?.Join(TimeSpan.FromSeconds(5));
                
                // 清理FFmpeg资源
                CleanupFilterGraph();
                
                foreach (var fifoPtr in _audioFifos.Values)
                {
                    ffmpeg.av_audio_fifo_free((AVAudioFifo*)fifoPtr);
                }
                _audioFifos.Clear();
                
                _audioStreams.Clear();
                _pendingOutputData.Clear();
                _dataAvailableEvent.Dispose();
                _cancellationTokenSource.Dispose();
                
                _disposed = true;
                _initialized = false;
            }
            
            _logger.LogInformation("FFmpeg audio mixer disposed");
        }

        #endregion

        private class AudioStreamContext
        {
            public AudioType AudioType { get; set; }
            public bool IsActive { get; set; }
            public bool IsStopping { get; set; }
            public int ProcessedFrameCount { get; set; }
            public bool SourceClosed { get; set; }
        }
    }

    
}