namespace XiaoZhi.Net.Server.I18n
{
    using System;
    using System.Globalization;
    using System.Resources;

    public class Lang
    {
        private static readonly Lazy<ResourceManager> resourceMan = new Lazy<ResourceManager>(() => 
            new ResourceManager("XiaoZhi.Net.Server.I18n.Lang", typeof(Lang).Assembly));

        private static CultureInfo? resourceCulture;

        public static ResourceManager ResourceManager => resourceMan.Value;

        public static CultureInfo Culture
        {
            set { resourceCulture = value; }
        }

        public static string Audio2TextHandler_Build_AsrNotConfigured => ResourceManager.GetString("Audio2TextHandler_Build_AsrNotConfigured", resourceCulture) ?? "";
        public static string Audio2TextHandler_Handle_ProcessFailed => ResourceManager.GetString("Audio2TextHandler_Handle_ProcessFailed", resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_ConvertFailed => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_ConvertFailed", resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_NoSpeak => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_NoSpeak", resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_SpeakText => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_SpeakText", resourceCulture) ?? "";
        
        public static string ServerBuilder_Initialize_ConfigNull => ResourceManager.GetString("ServerBuilder_Initialize_ConfigNull", resourceCulture) ?? "";
        public static string ServerBuilder_WithPlugin_PluginNameNull => ResourceManager.GetString("ServerBuilder_WithPlugin_PluginNameNull", resourceCulture) ?? "";
        public static string ServerBuilder_WithPlugin_FunctionsNull => ResourceManager.GetString("ServerBuilder_WithPlugin_FunctionsNull", resourceCulture) ?? "";
        public static string ServerBuilder_BuildComponents_ResourceLoadFailed => ResourceManager.GetString("ServerBuilder_BuildComponents_ResourceLoadFailed", resourceCulture) ?? "";
        public static string ServerBuilder_BuildComponents_ProviderBuildFailed => ResourceManager.GetString("ServerBuilder_BuildComponents_ProviderBuildFailed", resourceCulture) ?? "";

        public static string AudioProcessorHandler_Build_AudioProcessorNotConfigured => ResourceManager.GetString("AudioProcessorHandler_Build_AudioProcessorNotConfigured", resourceCulture) ?? "";
        public static string AudioProcessorHandler_Handle_AudioProcessorNotBuilt => ResourceManager.GetString("AudioProcessorHandler_Handle_AudioProcessorNotBuilt", resourceCulture) ?? "";
        public static string AudioProcessorHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioProcessorHandler_Handle_ProcessFailed", resourceCulture) ?? "";

        public static string AudioSendHandler_Build_AudioProcessorNotConfigured => ResourceManager.GetString("AudioSendHandler_Build_AudioProcessorNotConfigured", resourceCulture) ?? "";
        public static string AudioSendHandler_Build_AudioEncoderNotConfigured => ResourceManager.GetString("AudioSendHandler_Build_AudioEncoderNotConfigured", resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_AudioProcessorNotConfigured => ResourceManager.GetString("AudioSendHandler_Handle_AudioProcessorNotConfigured", resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_AudioEncoderNotConfigured => ResourceManager.GetString("AudioSendHandler_Handle_AudioEncoderNotConfigured", resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_FirstFrame => ResourceManager.GetString("AudioSendHandler_Handle_FirstFrame", resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_LastFrame => ResourceManager.GetString("AudioSendHandler_Handle_LastFrame", resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioSendHandler_Handle_ProcessFailed", resourceCulture) ?? "";

        public static string AudioReceiveHandler_Build_VadNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Build_VadNotConfigured", resourceCulture) ?? "";
        public static string AudioReceiveHandler_Build_AudioDecoderNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Build_AudioDecoderNotConfigured", resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_VadNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Handle_VadNotConfigured", resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_AudioDecoderNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Handle_AudioDecoderNotConfigured", resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_PacketIgnored => ResourceManager.GetString("AudioReceiveHandler_Handle_PacketIgnored", resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioReceiveHandler_Handle_ProcessFailed", resourceCulture) ?? "";
        public static string AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort => ResourceManager.GetString("AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort", resourceCulture) ?? "";

        public static string BaseHandler_CheckWorkflowValid_StaleWorkflow => ResourceManager.GetString("BaseHandler_CheckWorkflowValid_StaleWorkflow", resourceCulture) ?? "";
        public static string BaseHandler_OnTokenCanceled_TokenCanceled => ResourceManager.GetString("BaseHandler_OnTokenCanceled_TokenCanceled", resourceCulture) ?? "";

        public static string DialogueHandler_Build_LlmNotConfigured => ResourceManager.GetString("DialogueHandler_Build_LlmNotConfigured", resourceCulture) ?? "";
        public static string DialogueHandler_Handle_LlmNotConfigured => ResourceManager.GetString("DialogueHandler_Handle_LlmNotConfigured", resourceCulture) ?? "";
        public static string DialogueHandler_Handle_LlmCallTime => ResourceManager.GetString("DialogueHandler_Handle_LlmCallTime", resourceCulture) ?? "";
        public static string DialogueHandler_Handle_ProcessFailed => ResourceManager.GetString("DialogueHandler_Handle_ProcessFailed", resourceCulture) ?? "";
        public static string DialogueHandler_OnTokenGenerated_ResponseText => ResourceManager.GetString("DialogueHandler_OnTokenGenerated_ResponseText", resourceCulture) ?? "";
        public static string DialogueHandler_OnBeforeTokenGenerate_Thinking => ResourceManager.GetString("DialogueHandler_OnBeforeTokenGenerate_Thinking", resourceCulture) ?? "";

        public static string HelloMessageHandler_Handle_InitFailed => ResourceManager.GetString("HelloMessageHandler_Handle_InitFailed", resourceCulture) ?? "";

        public static string Text2AudioHandler_Build_TtsNotConfigured => ResourceManager.GetString("Text2AudioHandler_Build_TtsNotConfigured", resourceCulture) ?? "";
        public static string Text2AudioHandler_Build_PlayerNotConfigured => ResourceManager.GetString("Text2AudioHandler_Build_PlayerNotConfigured", resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_TtsNotConfigured => ResourceManager.GetString("Text2AudioHandler_Handle_TtsNotConfigured", resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_NoTtsRequired => ResourceManager.GetString("Text2AudioHandler_Handle_NoTtsRequired", resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_ProcessFailed => ResourceManager.GetString("Text2AudioHandler_Handle_ProcessFailed", resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_PlayerNotBuilt => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_PlayerNotBuilt", resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_InvalidBindCode => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_InvalidBindCode", resourceCulture) ?? "";
        public static string Text2AudioHandler_OnBeforeProcessing_Started => ResourceManager.GetString("Text2AudioHandler_OnBeforeProcessing_Started", resourceCulture) ?? "";
        public static string Text2AudioHandler_OnProcessed_Completed => ResourceManager.GetString("Text2AudioHandler_OnProcessed_Completed", resourceCulture) ?? "";
        
        public static string Text2AudioHandler_CheckBindDevice_BindCodeFormatError => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_BindCodeFormatError", resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_BindDevicePrompt => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_BindDevicePrompt", resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_VersionNotFound => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_VersionNotFound", resourceCulture) ?? "";

        public static string TextHandler_Handle_ReceivedText => ResourceManager.GetString("TextHandler_Handle_ReceivedText", resourceCulture) ?? "";
        public static string TextHandler_Handle_InvalidType => ResourceManager.GetString("TextHandler_Handle_InvalidType", resourceCulture) ?? "";
        public static string TextHandler_HandleAbortMessage_Received => ResourceManager.GetString("TextHandler_HandleAbortMessage_Received", resourceCulture) ?? "";
        public static string TextHandler_HandleAbortMessage_Cancelled => ResourceManager.GetString("TextHandler_HandleAbortMessage_Cancelled", resourceCulture) ?? "";
        public static string TextHandler_HandleListen_ModeSetting => ResourceManager.GetString("TextHandler_HandleListen_ModeSetting", resourceCulture) ?? "";
        public static string TextHandler_HandleIotDescriptors_ClientNotInit => ResourceManager.GetString("TextHandler_HandleIotDescriptors_ClientNotInit", resourceCulture) ?? "";
        public static string TextHandler_HandleMcp_ClientNotFound => ResourceManager.GetString("TextHandler_HandleMcp_ClientNotFound", resourceCulture) ?? "";

        public static string AudioPacketHelper_PcmBytesToFloat_UnsupportedBitDepth => ResourceManager.GetString("AudioPacketHelper_PcmBytesToFloat_UnsupportedBitDepth", resourceCulture) ?? "";
        public static string AudioPacketHelper_Float2PcmBytes_UnsupportedFormat => ResourceManager.GetString("AudioPacketHelper_Float2PcmBytes_UnsupportedFormat", resourceCulture) ?? "";
        public static string AudioPacketHelper_WriteSample_UnsupportedBitDepth => ResourceManager.GetString("AudioPacketHelper_WriteSample_UnsupportedBitDepth", resourceCulture) ?? "";

        public static string CodeTimer_Dispose_JobFinished => ResourceManager.GetString("CodeTimer_Dispose_JobFinished", resourceCulture) ?? "";

        public static string MarkdownCleaner_ReplaceTableBlock_SingleLineTable => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_SingleLineTable", resourceCulture) ?? "";
        public static string MarkdownCleaner_ReplaceTableBlock_TableHeader => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_TableHeader", resourceCulture) ?? "";
        public static string MarkdownCleaner_ReplaceTableBlock_RowContent => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_RowContent", resourceCulture) ?? "";

        #region HandlerManager
        public static string HandlerManager_InitializePrivateConfig_BuildPipelineFailed => ResourceManager.GetString("HandlerManager_InitializePrivateConfig_BuildPipelineFailed", resourceCulture) ?? "";
        public static string HandlerManager_BuildHandlersWorkflow_BuiltWorkflow => ResourceManager.GetString("HandlerManager_BuildHandlersWorkflow_BuiltWorkflow", resourceCulture) ?? "";
        public static string HandlerManager_ScheduleOnAbort_Aborted => ResourceManager.GetString("HandlerManager_ScheduleOnAbort_Aborted", resourceCulture) ?? "";
        #endregion

        #region ProviderManager
        public static string ProviderManager_BuildComponent_ProviderBuildFailed => ResourceManager.GetString("ProviderManager_BuildComponent_ProviderBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_FFmpegInstalled => ResourceManager.GetString("ProviderManager_BuildComponent_FFmpegInstalled", resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_FFmpegNotFound => ResourceManager.GetString("ProviderManager_BuildComponent_FFmpegNotFound", resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_BuildComponentsFailed => ResourceManager.GetString("ProviderManager_BuildComponent_BuildComponentsFailed", resourceCulture) ?? "";

        public static string ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_NoPrivateConfig => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_NoPrivateConfig", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateVadInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateVadInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericVadBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericVadBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericVadInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericVadInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateAsrInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateAsrInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericAsrInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericAsrInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateLlmInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateLlmInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericLlmInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericLlmInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateTtsInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateTtsInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericTtsInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericTtsInitialized", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_DeviceNotFound => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_DeviceNotFound", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_DeviceNotBinded => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_DeviceNotBinded", resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed", resourceCulture) ?? "";

        public static string ProviderManager_SaveMemory_LlmNotInitialized => ResourceManager.GetString("ProviderManager_SaveMemory_LlmNotInitialized", resourceCulture) ?? "";
        public static string ProviderManager_SaveMemory_MemorySaved => ResourceManager.GetString("ProviderManager_SaveMemory_MemorySaved", resourceCulture) ?? "";
        public static string ProviderManager_SaveMemory_SaveMemoryFailed => ResourceManager.GetString("ProviderManager_SaveMemory_SaveMemoryFailed", resourceCulture) ?? "";
        public static string ProviderManager_SaveMemory_ApiClientNotAvailable => ResourceManager.GetString("ProviderManager_SaveMemory_ApiClientNotAvailable", resourceCulture) ?? "";

        public static string ProviderManager_BuildAudioResampler_ResamplingRequired => ResourceManager.GetString("ProviderManager_BuildAudioResampler_ResamplingRequired", resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioResampler_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioResampler_BuildFailed", resourceCulture) ?? "";

        public static string ProviderManager_BuildAudioEncoder_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioEncoder_BuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_BuildIoT_BuildFailed => ResourceManager.GetString("ProviderManager_BuildIoT_BuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_BuildMCP_BuildFailed => ResourceManager.GetString("ProviderManager_BuildMCP_BuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioPlayer_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioPlayer_BuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioProcessor_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioProcessor_BuildFailed", resourceCulture) ?? "";

        public static string ProviderManager_RegisterGlobalProviders_AudioDecoderInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_AudioDecoderInitialized", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_VadBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_VadBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_VadInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_VadInitialized", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_AsrBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_AsrBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_AsrInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_AsrInitialized", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_LlmInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_LlmInitialized", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_TtsBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_TtsBuildFailed", resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_TtsInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_TtsInitialized", resourceCulture) ?? "";
        #endregion

        #region SocketSession
        public static string SocketSession_SendAsync_SendingJson => ResourceManager.GetString("SocketSession_SendAsync_SendingJson", resourceCulture) ?? "";
        public static string SocketSession_SendTtsMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendTtsMessageAsync_SessionNotInitialized", resourceCulture) ?? "";
        public static string SocketSession_SendSttMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendSttMessageAsync_SessionNotInitialized", resourceCulture) ?? "";
        public static string SocketSession_SendLlmMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendLlmMessageAsync_SessionNotInitialized", resourceCulture) ?? "";
        public static string SocketSession_SendAbortMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendAbortMessageAsync_SessionNotInitialized", resourceCulture) ?? "";
        public static string SocketSession_OnSessionClosedAsync_ClientOffline => ResourceManager.GetString("SocketSession_OnSessionClosedAsync_ClientOffline", resourceCulture) ?? "";
        #endregion

        #region AuthenticationVerification
        public static string AuthenticationVerification_VerifyAsync_DeviceIdNotFound => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_DeviceIdNotFound", resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_NewDeviceConnected => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_NewDeviceConnected", resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_AuthFailed => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_AuthFailed", resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_IpNotFound => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_IpNotFound", resourceCulture) ?? "";
        #endregion

        #region MessageDispatch
        public static string MessageDispatch_DispatchAsync_ArgumentNull => ResourceManager.GetString("MessageDispatch_DispatchAsync_ArgumentNull", resourceCulture) ?? "";
        #endregion

        #region ServerStatusMiddleware
        public static string ServerStatusMiddleware_Start_ServerStarted => ResourceManager.GetString("ServerStatusMiddleware_Start_ServerStarted", resourceCulture) ?? "";
        public static string ServerStatusMiddleware_Start_NoListeningOptions => ResourceManager.GetString("ServerStatusMiddleware_Start_NoListeningOptions", resourceCulture) ?? "";
        public static string ServerStatusMiddleware_Shutdown_ShuttingDown => ResourceManager.GetString("ServerStatusMiddleware_Shutdown_ShuttingDown", resourceCulture) ?? "";
        #endregion

        #region SessionContainerMiddleware
        public static string SessionContainerMiddleware_RegisterSession_LoginFailed => ResourceManager.GetString("SessionContainerMiddleware_RegisterSession_LoginFailed", resourceCulture) ?? "";
        #endregion

        #region BaseSherpaAsr
        public static string BaseSherpaAsr_RegisterDevice_Registered => ResourceManager.GetString("BaseSherpaAsr_RegisterDevice_Registered", resourceCulture) ?? "";
        public static string BaseSherpaAsr_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaAsr_UnregisterDevice_Unregistered", resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt", resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_UserCanceled => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_UserCanceled", resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError", resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaAsr_Processing_ProviderNotBuilt", resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_ErrorLoop => ResourceManager.GetString("BaseSherpaAsr_Processing_ErrorLoop", resourceCulture) ?? "";
        public static string BaseSherpaAsr_Dispose_WaitFailed => ResourceManager.GetString("BaseSherpaAsr_Dispose_WaitFailed", resourceCulture) ?? "";
        #endregion

        #region Paraformer
        public static string Paraformer_Build_Built => ResourceManager.GetString("Paraformer_Build_Built", resourceCulture) ?? "";
        public static string Paraformer_Build_InvalidSettings => ResourceManager.GetString("Paraformer_Build_InvalidSettings", resourceCulture) ?? "";
        #endregion

        #region SenseVoice
        public static string SenseVoice_Build_Built => ResourceManager.GetString("SenseVoice_Build_Built", resourceCulture) ?? "";
        public static string SenseVoice_Build_InvalidSettings => ResourceManager.GetString("SenseVoice_Build_InvalidSettings", resourceCulture) ?? "";
        #endregion

        #region WebSocketClient
        public static string WebSocketClient_CloseAsync_CloseFailed => ResourceManager.GetString("WebSocketClient_CloseAsync_CloseFailed", resourceCulture) ?? "";
        #endregion

        #region DefaultOpusDecoder
        public static string DefaultOpusDecoder_Build_Built => ResourceManager.GetString("DefaultOpusDecoder_Build_Built", resourceCulture) ?? "";
        public static string DefaultOpusDecoder_Build_InvalidSettings => ResourceManager.GetString("DefaultOpusDecoder_Build_InvalidSettings", resourceCulture) ?? "";
        public static string DefaultOpusDecoder_DecodeAsync_NotBuilt => ResourceManager.GetString("DefaultOpusDecoder_DecodeAsync_NotBuilt", resourceCulture) ?? "";
        #endregion

        #region DefaultOpusEncoder
        public static string DefaultOpusEncoder_Build_Built => ResourceManager.GetString("DefaultOpusEncoder_Build_Built", resourceCulture) ?? "";
        public static string DefaultOpusEncoder_Build_InvalidSettings => ResourceManager.GetString("DefaultOpusEncoder_Build_InvalidSettings", resourceCulture) ?? "";
        public static string DefaultOpusEncoder_EncodeAsync_NotBuilt => ResourceManager.GetString("DefaultOpusEncoder_EncodeAsync_NotBuilt", resourceCulture) ?? "";
        #endregion

        #region DefaultResampler
        public static string DefaultResampler_Build_Built => ResourceManager.GetString("DefaultResampler_Build_Built", resourceCulture) ?? "";
        public static string DefaultResampler_Build_InvalidSettings => ResourceManager.GetString("DefaultResampler_Build_InvalidSettings", resourceCulture) ?? "";
        public static string DefaultResampler_ResampleAsync_NotBuilt => ResourceManager.GetString("DefaultResampler_ResampleAsync_NotBuilt", resourceCulture) ?? "";
        #endregion

        #region FileMusicPlayer
        public static string FileMusicPlayer_Build_FFmpegInitFailed => ResourceManager.GetString("FileMusicPlayer_Build_FFmpegInitFailed", resourceCulture) ?? "";
        public static string FileMusicPlayer_PlayAsync_NotBuilt => ResourceManager.GetString("FileMusicPlayer_PlayAsync_NotBuilt", resourceCulture) ?? "";
        public static string FileMusicPlayer_PlayAsync_NoFiles => ResourceManager.GetString("FileMusicPlayer_PlayAsync_NoFiles", resourceCulture) ?? "";
        public static string FileMusicPlayer_PauseAsync_Skip => ResourceManager.GetString("FileMusicPlayer_PauseAsync_Skip", resourceCulture) ?? "";
        public static string FileMusicPlayer_ResumeAsync_Skip => ResourceManager.GetString("FileMusicPlayer_ResumeAsync_Skip", resourceCulture) ?? "";
        public static string FileMusicPlayer_StopAsync_Skip => ResourceManager.GetString("FileMusicPlayer_StopAsync_Skip", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Canceled => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Canceled", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Start => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Start", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Playing => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Playing", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Completed => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Completed", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_PlaybackCanceled => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_PlaybackCanceled", resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Error => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Error", resourceCulture) ?? "";
        public static string FileMusicPlayer_Dispose_Timeout => ResourceManager.GetString("FileMusicPlayer_Dispose_Timeout", resourceCulture) ?? "";
        public static string FileMusicPlayer_Dispose_Error => ResourceManager.GetString("FileMusicPlayer_Dispose_Error", resourceCulture) ?? "";
        #endregion

        #region IoTClient
        public static string IoTClient_RegisterIoTTools_FunctionDescription => ResourceManager.GetString("IoTClient_RegisterIoTTools_FunctionDescription", resourceCulture) ?? "";
        public static string IoTClient_RegisterIoTTools_PluginDescription => ResourceManager.GetString("IoTClient_RegisterIoTTools_PluginDescription", resourceCulture) ?? "";
        public static string IoTClient_SetIoTPropertyStatusValue_SetStatus => ResourceManager.GetString("IoTClient_SetIoTPropertyStatusValue_SetStatus", resourceCulture) ?? "";
        public static string IoTClient_SendIoTMessageAsync_MessageNull => ResourceManager.GetString("IoTClient_SendIoTMessageAsync_MessageNull", resourceCulture) ?? "";
        #endregion

        #region DefaultAudioProcessor
        public static string DefaultAudioProcessor_Build_Initialized => ResourceManager.GetString("DefaultAudioProcessor_Build_Initialized", resourceCulture) ?? "";
        public static string DefaultAudioProcessor_Build_InvalidSettings => ResourceManager.GetString("DefaultAudioProcessor_Build_InvalidSettings", resourceCulture) ?? "";
        #endregion

        #region NotificationPlayer
        public static string NotificationPlayer_Build_FFmpegInitFailed => ResourceManager.GetString("NotificationPlayer_Build_FFmpegInitFailed", resourceCulture) ?? "";
        public static string NotificationPlayer_PlayBindCodeAsync_NotBuilt => ResourceManager.GetString("NotificationPlayer_PlayBindCodeAsync_NotBuilt", resourceCulture) ?? "";
        public static string NotificationPlayer_PlayBindCodeAsync_StreamNull => ResourceManager.GetString("NotificationPlayer_PlayBindCodeAsync_StreamNull", resourceCulture) ?? "";
        public static string NotificationPlayer_PlayNotFoundAsync_NotBuilt => ResourceManager.GetString("NotificationPlayer_PlayNotFoundAsync_NotBuilt", resourceCulture) ?? "";
        public static string NotificationPlayer_PlayNotFoundAsync_StreamNull => ResourceManager.GetString("NotificationPlayer_PlayNotFoundAsync_StreamNull", resourceCulture) ?? "";
        public static string NotificationPlayer_StopAsync_Skip => ResourceManager.GetString("NotificationPlayer_StopAsync_Skip", resourceCulture) ?? "";
        #endregion

        #region ChatAgent
        public static string ChatAgent_Build_Built => ResourceManager.GetString("ChatAgent_Build_Built", resourceCulture) ?? "";
        public static string ChatAgent_Build_BuildPluginsFailed => ResourceManager.GetString("ChatAgent_Build_BuildPluginsFailed", resourceCulture) ?? "";
        public static string ChatAgent_Build_BuildFailed => ResourceManager.GetString("ChatAgent_Build_BuildFailed", resourceCulture) ?? "";
        public static string ChatAgent_RegisterDevice_PluginRegistered => ResourceManager.GetString("ChatAgent_RegisterDevice_PluginRegistered", resourceCulture) ?? "";
        public static string ChatAgent_GenerateChatResponseAsync_AgentNotBuilt => ResourceManager.GetString("ChatAgent_GenerateChatResponseAsync_AgentNotBuilt", resourceCulture) ?? "";
        #endregion

        #region EmotionAgent
        public static string EmotionAgent_Build_BuildFailed => ResourceManager.GetString("EmotionAgent_Build_BuildFailed", resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_AgentNotBuilt => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_AgentNotBuilt", resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_UserCanceled => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_UserCanceled", resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_UnexpectedError => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_UnexpectedError", resourceCulture) ?? "";
        #endregion

        #region MCPToolFunctionFilter
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailed => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailed", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailedDetail => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailedDetail", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTClientNotInit => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTClientNotInit", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_ReturnTypeNotSpecified => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_ReturnTypeNotSpecified", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTSuccess => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTSuccess", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvalidIoTName => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvalidIoTName", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailed => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailed", resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailedDetail => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailedDetail", resourceCulture) ?? "";
        #endregion

        #region MusicPlayer
        public static string MusicPlayer_GetMusicFilesAsync_SessionNotInit => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_SessionNotInit", resourceCulture) ?? "";
        public static string MusicPlayer_GetMusicFilesAsync_NoFilesLog => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_NoFilesLog", resourceCulture) ?? "";
        public static string MusicPlayer_GetMusicFilesAsync_NoFilesMsg => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_NoFilesMsg", resourceCulture) ?? "";
        public static string MusicPlayer_GetMusicFilesAsync_SuccessLog => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_SuccessLog", resourceCulture) ?? "";
        public static string MusicPlayer_GetMusicFilesAsync_SuccessMsg => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_SuccessMsg", resourceCulture) ?? "";
        public static string MusicPlayer_GetMusicFilesAsync_ProviderNotInit => ResourceManager.GetString("MusicPlayer_GetMusicFilesAsync_ProviderNotInit", resourceCulture) ?? "";

        public static string MusicPlayer_PlayMusic_SessionNotInit => ResourceManager.GetString("MusicPlayer_PlayMusic_SessionNotInit", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_PlayerNotInit => ResourceManager.GetString("MusicPlayer_PlayMusic_PlayerNotInit", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_NoFiles => ResourceManager.GetString("MusicPlayer_PlayMusic_NoFiles", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_MusicNameEmpty => ResourceManager.GetString("MusicPlayer_PlayMusic_MusicNameEmpty", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_FileNotFound => ResourceManager.GetString("MusicPlayer_PlayMusic_FileNotFound", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_PlayingLog => ResourceManager.GetString("MusicPlayer_PlayMusic_PlayingLog", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_SuccessMsg => ResourceManager.GetString("MusicPlayer_PlayMusic_SuccessMsg", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_FailedLog => ResourceManager.GetString("MusicPlayer_PlayMusic_FailedLog", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_FailedMsg => ResourceManager.GetString("MusicPlayer_PlayMusic_FailedMsg", resourceCulture) ?? "";
        public static string MusicPlayer_PlayMusic_ProviderNotInit => ResourceManager.GetString("MusicPlayer_PlayMusic_ProviderNotInit", resourceCulture) ?? "";

        #region BaseMcpClient
        public static string BaseMcpClient_HandleMcpMessageAsync_CallResult => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_CallResult", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_InitMessage => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_InitMessage", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ServerInfo => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ServerInfo", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolListMessage => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolListMessage", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolAdded => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolAdded", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolCount => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolCount", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_MoreTools => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_MoreTools", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ClientReady => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ClientReady", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ClientRequest => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ClientRequest", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ErrorResponse => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ErrorResponse", resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_ToolError => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_ToolError", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_TimeoutEx => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_TimeoutEx", resourceCulture) ?? "";

        public static string BaseMcpClient_SendMcpInitializeAsync_SendingInit => ResourceManager.GetString("BaseMcpClient_SendMcpInitializeAsync_SendingInit", resourceCulture) ?? "";
        public static string BaseMcpClient_SendMcpNotificationAsync_SendingNotification => ResourceManager.GetString("BaseMcpClient_SendMcpNotificationAsync_SendingNotification", resourceCulture) ?? "";
        public static string BaseMcpClient_RequestToolsListAsync_RequestTools => ResourceManager.GetString("BaseMcpClient_RequestToolsListAsync_RequestTools", resourceCulture) ?? "";
        public static string BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor => ResourceManager.GetString("BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallTool => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallTool", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_Timeout => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_Timeout", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallSuccess => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallSuccess", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_WaitTimeout => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_WaitTimeout", resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallFailed => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallFailed", resourceCulture) ?? "";
        public static string BaseMcpClient_AddTool_ToolExists => ResourceManager.GetString("BaseMcpClient_AddTool_ToolExists", resourceCulture) ?? "";
        #endregion

        #region McpEndpointClient
        public static string McpEndpointClient_Build_UrlEmpty => ResourceManager.GetString("McpEndpointClient_Build_UrlEmpty", resourceCulture) ?? "";
        public static string McpEndpointClient_Build_InvalidSettings => ResourceManager.GetString("McpEndpointClient_Build_InvalidSettings", resourceCulture) ?? "";
        public static string McpEndpointClient_OnOpen_Connected => ResourceManager.GetString("McpEndpointClient_OnOpen_Connected", resourceCulture) ?? "";
        #endregion

        #region DeviceMcpClient
        public static string DeviceMcpClient_SendMcpInitializeAsync_SendingInit => ResourceManager.GetString("DeviceMcpClient_SendMcpInitializeAsync_SendingInit", resourceCulture) ?? "";
        #endregion

        #region GenericOpenAI
        public static string GenericOpenAI_Build_InvalidSettings => ResourceManager.GetString("GenericOpenAI_Build_InvalidSettings", resourceCulture) ?? "";
        public static string GenericOpenAI_StartDialogueAsync_NotBuilt => ResourceManager.GetString("GenericOpenAI_StartDialogueAsync_NotBuilt", resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_EmotionDetected => ResourceManager.GetString("GenericOpenAI_ChatAsync_EmotionDetected", resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_UserCanceled => ResourceManager.GetString("GenericOpenAI_ChatAsync_UserCanceled", resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_UnexpectedError => ResourceManager.GetString("GenericOpenAI_ChatAsync_UnexpectedError", resourceCulture) ?? "";
        #endregion

        #region FlashMemory
        public static string FlashMemory_Build_Built => ResourceManager.GetString("FlashMemory_Build_Built", resourceCulture) ?? "";
        #endregion

        #region HuoshanHttpV3TTS
        public static string HuoshanHttpV3TTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanHttpV3TTS_Build_ConfigIncomplete", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_Build_Built => ResourceManager.GetString("HuoshanHttpV3TTS_Build_Built", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_Build_Failed => ResourceManager.GetString("HuoshanHttpV3TTS_Build_Failed", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_DevNotReg => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_DevNotReg", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_MissingSentenceId => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_MissingSentenceId", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_RequestFailed => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_RequestFailed", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_ApiError => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_ApiError", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_GeneralFailed => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_GeneralFailed", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_FileSaved => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_FileSaved", resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_FlushFailed => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_FlushFailed", resourceCulture) ?? "";
        #endregion

        #region HuoshanHttpTTS
        public static string HuoshanHttpTTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanHttpTTS_Build_ConfigIncomplete", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_Build_Built => ResourceManager.GetString("HuoshanHttpTTS_Build_Built", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_Build_Failed => ResourceManager.GetString("HuoshanHttpTTS_Build_Failed", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_ModelNotBuilt => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_ModelNotBuilt", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_DevNotReg => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_DevNotReg", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_MissingSentenceId => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_MissingSentenceId", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_RequestFailed => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_RequestFailed", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_FileSaved => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_FileSaved", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_SaveFailed => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_SaveFailed", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_ApiError => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_ApiError", resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_GeneralFailed => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_GeneralFailed", resourceCulture) ?? "";
        #endregion

        #region HuoshanBidirectionTTS
        public static string HuoshanBidirectionTTS_SynthesisAsync_ClientNotInit => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_ClientNotInit", resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_MissingIds => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_MissingIds", resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_Canceled => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_Canceled", resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_Failed => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_Failed", resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_Dispose_FinishError => ResourceManager.GetString("HuoshanBidirectionTTS_Dispose_FinishError", resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_Dispose_Disposed => ResourceManager.GetString("HuoshanBidirectionTTS_Dispose_Disposed", resourceCulture) ?? "";
        #endregion

        #region SileroNative
        public static string SileroNative_Build_UnsupportedSampleRate => ResourceManager.GetString("SileroNative_Build_UnsupportedSampleRate", resourceCulture) ?? "";
        public static string SileroNative_Build_Built => ResourceManager.GetString("SileroNative_Build_Built", resourceCulture) ?? "";
        public static string SileroNative_Build_InvalidSettings => ResourceManager.GetString("SileroNative_Build_InvalidSettings", resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_ProviderNotBuilt => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_ProviderNotBuilt", resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_VoiceStopped => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_VoiceStopped", resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_UserCanceled => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_UserCanceled", resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_UnexpectedError => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_UnexpectedError", resourceCulture) ?? "";
        public static string SileroNative_CheckLongTermSilence_Detected => ResourceManager.GetString("SileroNative_CheckLongTermSilence_Detected", resourceCulture) ?? "";
        #endregion

        #region Kokoro
        public static string Kokoro_Build_Built => ResourceManager.GetString("Kokoro_Build_Built", resourceCulture) ?? "";
        public static string Kokoro_Build_InvalidSettings => ResourceManager.GetString("Kokoro_Build_InvalidSettings", resourceCulture) ?? "";
        #endregion

        #region BaseSherpaTts
        public static string BaseSherpaTts_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaTts_UnregisterDevice_Unregistered", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_ProviderNotBuilt", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_MissingIds => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_MissingIds", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_FileSaved => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_FileSaved", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_SaveFailed => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_SaveFailed", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_Generated => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_Generated", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_CallbackNotRegistered => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_CallbackNotRegistered", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_UserCanceled => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_UserCanceled", resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_UnexpectedError", resourceCulture) ?? "";
        #endregion

        #region HuoshanStreamTTS
        public static string HuoshanStreamTTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanStreamTTS_Build_ConfigIncomplete", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_Build_Built => ResourceManager.GetString("HuoshanStreamTTS_Build_Built", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_Build_Failed => ResourceManager.GetString("HuoshanStreamTTS_Build_Failed", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_ConnectAsync_ClientNotInitLog => ResourceManager.GetString("HuoshanStreamTTS_ConnectAsync_ClientNotInitLog", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_ConnectAsync_ClientNotInitEx => ResourceManager.GetString("HuoshanStreamTTS_ConnectAsync_ClientNotInitEx", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_WaitForEventAsync_Timeout => ResourceManager.GetString("HuoshanStreamTTS_WaitForEventAsync_Timeout", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnOpen_Connected => ResourceManager.GetString("HuoshanStreamTTS_OnOpen_Connected", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnClose_Closed => ResourceManager.GetString("HuoshanStreamTTS_OnClose_Closed", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnClose_ClosedEx => ResourceManager.GetString("HuoshanStreamTTS_OnClose_ClosedEx", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnError_Error => ResourceManager.GetString("HuoshanStreamTTS_OnError_Error", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnError_ErrorEx => ResourceManager.GetString("HuoshanStreamTTS_OnError_ErrorEx", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ParseFailed => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ParseFailed", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_AppendFailed => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_AppendFailed", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ServerFailure => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ServerFailure", resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ServerError => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ServerError", resourceCulture) ?? "";
        #endregion

        #region BaseProvider
        public static string BaseProvider_RegisterDevice_Registered => ResourceManager.GetString("BaseProvider_RegisterDevice_Registered", resourceCulture) ?? "";
        public static string BaseProvider_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseProvider_UnregisterDevice_Unregistered", resourceCulture) ?? "";
        public static string BaseProvider_CheckDeviceRegistered_NotRegistered => ResourceManager.GetString("BaseProvider_CheckDeviceRegistered_NotRegistered", resourceCulture) ?? "";
        public static string BaseProvider_CheckModelExist_NotFound => ResourceManager.GetString("BaseProvider_CheckModelExist_NotFound", resourceCulture) ?? "";
        public static string BaseProvider_ReplaceMacDelimiters_DeviceIdNull => ResourceManager.GetString("BaseProvider_ReplaceMacDelimiters_DeviceIdNull", resourceCulture) ?? "";
        #endregion

        #region Silero
        public static string Silero_Build_Built => ResourceManager.GetString("Silero_Build_Built", resourceCulture) ?? "";
        public static string Silero_Build_InvalidSettings => ResourceManager.GetString("Silero_Build_InvalidSettings", resourceCulture) ?? "";
        #endregion

        #region BaseSherpaVad
        public static string BaseSherpaVad_Build_UnsupportedSampleRate => ResourceManager.GetString("BaseSherpaVad_Build_UnsupportedSampleRate", resourceCulture) ?? "";
        public static string BaseSherpaVad_RegisterDevice_Registered => ResourceManager.GetString("BaseSherpaVad_RegisterDevice_Registered", resourceCulture) ?? "";
        public static string BaseSherpaVad_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaVad_UnregisterDevice_Unregistered", resourceCulture) ?? "";
        public static string BaseSherpaVad_ResetSessionState_Reset => ResourceManager.GetString("BaseSherpaVad_ResetSessionState_Reset", resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped", resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_UserCanceled => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_UserCanceled", resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError", resourceCulture) ?? "";
        public static string BaseSherpaVad_CheckLongTermSilence_Detected => ResourceManager.GetString("BaseSherpaVad_CheckLongTermSilence_Detected", resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt", resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound", resourceCulture) ?? "";
        #endregion

        #endregion
    }
}
