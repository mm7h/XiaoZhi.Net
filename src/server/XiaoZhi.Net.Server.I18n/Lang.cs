using System;
using System.Globalization;
using System.Resources;

namespace XiaoZhi.Net.Server.I18n
{
    public class Lang
    {
        private static readonly Lazy<ResourceManager> s_resourceMan = new Lazy<ResourceManager>(() =>
            new ResourceManager("XiaoZhi.Net.Server.I18n.Lang", typeof(Lang).Assembly));

        private static CultureInfo? s_resourceCulture;

        public static ResourceManager ResourceManager => s_resourceMan.Value;

        public static CultureInfo Culture
        {
            set { s_resourceCulture = value; }
        }

        public static string Audio2TextHandler_Build_AsrNotConfigured => ResourceManager.GetString("Audio2TextHandler_Build_AsrNotConfigured", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_Handle_ProcessFailed => ResourceManager.GetString("Audio2TextHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_ConvertFailed => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_ConvertFailed", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_NoSpeak => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_NoSpeak", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_SpeakText => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_SpeakText", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_Handle_Cancelled => ResourceManager.GetString("Audio2TextHandler_Handle_Cancelled", s_resourceCulture) ?? "";

        public static string ServerBuilder_Initialize_ConfigNull => ResourceManager.GetString("ServerBuilder_Initialize_ConfigNull", s_resourceCulture) ?? "";
        public static string ServerBuilder_WithPlugin_PluginNameNull => ResourceManager.GetString("ServerBuilder_WithPlugin_PluginNameNull", s_resourceCulture) ?? "";
        public static string ServerBuilder_WithPlugin_FunctionsNull => ResourceManager.GetString("ServerBuilder_WithPlugin_FunctionsNull", s_resourceCulture) ?? "";
        public static string ServerBuilder_BuildComponents_ResourceLoadFailed => ResourceManager.GetString("ServerBuilder_BuildComponents_ResourceLoadFailed", s_resourceCulture) ?? "";
        public static string ServerBuilder_BuildComponents_ProviderBuildFailed => ResourceManager.GetString("ServerBuilder_BuildComponents_ProviderBuildFailed", s_resourceCulture) ?? "";

        public static string AudioProcessorHandler_Build_AudioProcessorNotConfigured => ResourceManager.GetString("AudioProcessorHandler_Build_AudioProcessorNotConfigured", s_resourceCulture) ?? "";
        public static string AudioProcessorHandler_Handle_AudioProcessorNotBuilt => ResourceManager.GetString("AudioProcessorHandler_Handle_AudioProcessorNotBuilt", s_resourceCulture) ?? "";
        public static string AudioProcessorHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioProcessorHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string AudioProcessorHandler_Handle_Cancelled => ResourceManager.GetString("AudioProcessorHandler_Handle_Cancelled", s_resourceCulture) ?? "";
        public static string AudioProcessorHandler_OnMixedAudioDataAvailable_WriteFailed => ResourceManager.GetString("AudioProcessorHandler_OnMixedAudioDataAvailable_WriteFailed", s_resourceCulture) ?? "";

        public static string AudioSendHandler_Build_AudioProcessorNotConfigured => ResourceManager.GetString("AudioSendHandler_Build_AudioProcessorNotConfigured", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Build_AudioEncoderNotConfigured => ResourceManager.GetString("AudioSendHandler_Build_AudioEncoderNotConfigured", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_AudioProcessorNotConfigured => ResourceManager.GetString("AudioSendHandler_Handle_AudioProcessorNotConfigured", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_AudioEncoderNotConfigured => ResourceManager.GetString("AudioSendHandler_Handle_AudioEncoderNotConfigured", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_FirstFrame => ResourceManager.GetString("AudioSendHandler_Handle_FirstFrame", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_LastFrame => ResourceManager.GetString("AudioSendHandler_Handle_LastFrame", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioSendHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string AudioSendHandler_Handle_Cancelled => ResourceManager.GetString("AudioSendHandler_Handle_Cancelled", s_resourceCulture) ?? "";

        public static string AudioReceiveHandler_Build_VadNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Build_VadNotConfigured", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Build_AudioDecoderNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Build_AudioDecoderNotConfigured", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_VadNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Handle_VadNotConfigured", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_AudioDecoderNotConfigured => ResourceManager.GetString("AudioReceiveHandler_Handle_AudioDecoderNotConfigured", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_PacketIgnored => ResourceManager.GetString("AudioReceiveHandler_Handle_PacketIgnored", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_ProcessFailed => ResourceManager.GetString("AudioReceiveHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort => ResourceManager.GetString("AudioReceiveHandler_HandleVoiceDetected_VoiceTooShort", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_Handle_Cancelled => ResourceManager.GetString("AudioReceiveHandler_Handle_Cancelled", s_resourceCulture) ?? "";

        public static string BaseHandler_CheckWorkflowValid_StaleWorkflow => ResourceManager.GetString("BaseHandler_CheckWorkflowValid_StaleWorkflow", s_resourceCulture) ?? "";
        public static string BaseHandler_OnSessionCtsTokenChanged_CtsAlreadyDisposed => ResourceManager.GetString("BaseHandler_OnSessionCtsTokenChanged_CtsAlreadyDisposed", s_resourceCulture) ?? "";
        public static string BaseHandler_OnTokenCanceled_TokenCanceled => ResourceManager.GetString("BaseHandler_OnTokenCanceled_TokenCanceled", s_resourceCulture) ?? "";

        public static string DialogueHandler_Build_LlmNotConfigured => ResourceManager.GetString("DialogueHandler_Build_LlmNotConfigured", s_resourceCulture) ?? "";
        public static string DialogueHandler_Handle_LlmNotConfigured => ResourceManager.GetString("DialogueHandler_Handle_LlmNotConfigured", s_resourceCulture) ?? "";
        public static string DialogueHandler_Handle_LlmCallTime => ResourceManager.GetString("DialogueHandler_Handle_LlmCallTime", s_resourceCulture) ?? "";
        public static string DialogueHandler_Handle_ProcessFailed => ResourceManager.GetString("DialogueHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string DialogueHandler_OnTokenGenerated_ResponseText => ResourceManager.GetString("DialogueHandler_OnTokenGenerated_ResponseText", s_resourceCulture) ?? "";
        public static string DialogueHandler_OnBeforeTokenGenerate_Thinking => ResourceManager.GetString("DialogueHandler_OnBeforeTokenGenerate_Thinking", s_resourceCulture) ?? "";
        public static string DialogueHandler_Handle_Cancelled => ResourceManager.GetString("DialogueHandler_Handle_Cancelled", s_resourceCulture) ?? "";
        public static string DialogueHandler_Handle_McpToolsNotReady => ResourceManager.GetString("DialogueHandler_Handle_McpToolsNotReady", s_resourceCulture) ?? "";
        public static string DialogueHandler_OnTokenGenerating_WriteFailed => ResourceManager.GetString("DialogueHandler_OnTokenGenerating_WriteFailed", s_resourceCulture) ?? "";

        public static string HelloMessageHandler_Handle_InitFailed => ResourceManager.GetString("HelloMessageHandler_Handle_InitFailed", s_resourceCulture) ?? "";

        public static string Text2AudioHandler_Build_TtsNotConfigured => ResourceManager.GetString("Text2AudioHandler_Build_TtsNotConfigured", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_Build_PlayerNotConfigured => ResourceManager.GetString("Text2AudioHandler_Build_PlayerNotConfigured", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_TtsNotConfigured => ResourceManager.GetString("Text2AudioHandler_Handle_TtsNotConfigured", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_NoTtsRequired => ResourceManager.GetString("Text2AudioHandler_Handle_NoTtsRequired", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_ProcessFailed => ResourceManager.GetString("Text2AudioHandler_Handle_ProcessFailed", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_PlayerNotBuilt => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_PlayerNotBuilt", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_InvalidBindCode => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_InvalidBindCode", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_OnBeforeProcessing_Started => ResourceManager.GetString("Text2AudioHandler_OnBeforeProcessing_Started", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_OnProcessed_Completed => ResourceManager.GetString("Text2AudioHandler_OnProcessed_Completed", s_resourceCulture) ?? "";

        public static string Text2AudioHandler_CheckBindDevice_BindCodeFormatError => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_BindCodeFormatError", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_BindDevicePrompt => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_BindDevicePrompt", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_CheckBindDevice_VersionNotFound => ResourceManager.GetString("Text2AudioHandler_CheckBindDevice_VersionNotFound", s_resourceCulture) ?? "";
        public static string Text2AudioHandler_Handle_Cancelled => ResourceManager.GetString("Text2AudioHandler_Handle_Cancelled", s_resourceCulture) ?? "";

        public static string TextHandler_Handle_ReceivedText => ResourceManager.GetString("TextHandler_Handle_ReceivedText", s_resourceCulture) ?? "";
        public static string TextHandler_Handle_InvalidType => ResourceManager.GetString("TextHandler_Handle_InvalidType", s_resourceCulture) ?? "";
        public static string TextHandler_HandleAbortMessage_Received => ResourceManager.GetString("TextHandler_HandleAbortMessage_Received", s_resourceCulture) ?? "";
        public static string TextHandler_HandleAbortMessage_Cancelled => ResourceManager.GetString("TextHandler_HandleAbortMessage_Cancelled", s_resourceCulture) ?? "";
        public static string TextHandler_HandleListen_ModeSetting => ResourceManager.GetString("TextHandler_HandleListen_ModeSetting", s_resourceCulture) ?? "";
        public static string TextHandler_HandleIotDescriptors_ClientNotInit => ResourceManager.GetString("TextHandler_HandleIotDescriptors_ClientNotInit", s_resourceCulture) ?? "";
        public static string TextHandler_HandleMcp_ClientNotFound => ResourceManager.GetString("TextHandler_HandleMcp_ClientNotFound", s_resourceCulture) ?? "";

        public static string AudioPacketHelper_PcmBytesToFloat_UnsupportedBitDepth => ResourceManager.GetString("AudioPacketHelper_PcmBytesToFloat_UnsupportedBitDepth", s_resourceCulture) ?? "";
        public static string AudioPacketHelper_Float2PcmBytes_UnsupportedFormat => ResourceManager.GetString("AudioPacketHelper_Float2PcmBytes_UnsupportedFormat", s_resourceCulture) ?? "";
        public static string AudioPacketHelper_WriteSample_UnsupportedBitDepth => ResourceManager.GetString("AudioPacketHelper_WriteSample_UnsupportedBitDepth", s_resourceCulture) ?? "";

        public static string CodeTimer_Dispose_JobFinished => ResourceManager.GetString("CodeTimer_Dispose_JobFinished", s_resourceCulture) ?? "";

        public static string MarkdownCleaner_ReplaceTableBlock_SingleLineTable => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_SingleLineTable", s_resourceCulture) ?? "";
        public static string MarkdownCleaner_ReplaceTableBlock_TableHeader => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_TableHeader", s_resourceCulture) ?? "";
        public static string MarkdownCleaner_ReplaceTableBlock_RowContent => ResourceManager.GetString("MarkdownCleaner_ReplaceTableBlock_RowContent", s_resourceCulture) ?? "";

        #region HandlerManager
        public static string HandlerManager_InitializePrivateConfig_BuildPipelineFailed => ResourceManager.GetString("HandlerManager_InitializePrivateConfig_BuildPipelineFailed", s_resourceCulture) ?? "";
        public static string HandlerManager_BuildHandlersWorkflow_BuiltWorkflow => ResourceManager.GetString("HandlerManager_BuildHandlersWorkflow_BuiltWorkflow", s_resourceCulture) ?? "";
        public static string HandlerManager_ScheduleOnAbort_Aborted => ResourceManager.GetString("HandlerManager_ScheduleOnAbort_Aborted", s_resourceCulture) ?? "";
        #endregion

        #region ProviderManager
        public static string ProviderManager_BuildComponent_ProviderBuildFailed => ResourceManager.GetString("ProviderManager_BuildComponent_ProviderBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_FFmpegInstalled => ResourceManager.GetString("ProviderManager_BuildComponent_FFmpegInstalled", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_FFmpegNotFound => ResourceManager.GetString("ProviderManager_BuildComponent_FFmpegNotFound", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildComponent_BuildComponentsFailed => ResourceManager.GetString("ProviderManager_BuildComponent_BuildComponentsFailed", s_resourceCulture) ?? "";

        public static string ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_RemoteServiceUnavailable", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_NoPrivateConfig => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_NoPrivateConfig", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateVadBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateVadInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateVadInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericVadBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericVadBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericVadInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericVadInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateAsrBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateAsrInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateAsrInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericAsrBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericAsrInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericAsrInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateLlmBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateLlmInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateLlmInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericLlmBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericLlmInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericLlmInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateTtsBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_PrivateTtsInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_PrivateTtsInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericTtsBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_GenericTtsInitialized => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_GenericTtsInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_DeviceNotFound => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_DeviceNotFound", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_DeviceNotBinded => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_DeviceNotBinded", s_resourceCulture) ?? "";
        public static string ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed => ResourceManager.GetString("ProviderManager_InitializePrivateConfig_LoadPrivateConfigFailed", s_resourceCulture) ?? "";

        public static string ProviderManager_LoadAgentMemory_LoadFailed => ResourceManager.GetString("ProviderManager_LoadAgentMemory_LoadFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_SaveAgentMemory_LlmNotInitialized => ResourceManager.GetString("ProviderManager_SaveAgentMemory_LlmNotInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_SaveAgentMemory_MemorySaved => ResourceManager.GetString("ProviderManager_SaveAgentMemory_MemorySaved", s_resourceCulture) ?? "";
        public static string ProviderManager_SaveAgentMemory_SaveMemoryFailed => ResourceManager.GetString("ProviderManager_SaveAgentMemory_SaveMemoryFailed", s_resourceCulture) ?? "";

        public static string ProviderManager_BuildAudioResampler_ResamplingRequired => ResourceManager.GetString("ProviderManager_BuildAudioResampler_ResamplingRequired", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioResampler_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioResampler_BuildFailed", s_resourceCulture) ?? "";

        public static string ProviderManager_BuildAudioDecoder_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioDecoder_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioEncoder_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioEncoder_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildIoT_BuildFailed => ResourceManager.GetString("ProviderManager_BuildIoT_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildMCP_BuildFailed => ResourceManager.GetString("ProviderManager_BuildMCP_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioPlayer_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioPlayer_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildAudioProcessor_BuildFailed => ResourceManager.GetString("ProviderManager_BuildAudioProcessor_BuildFailed", s_resourceCulture) ?? "";

        public static string ProviderManager_RegisterGlobalProviders_VadBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_VadBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_VadInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_VadInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_AsrBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_AsrBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_AsrInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_AsrInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_LlmInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_LlmInitialized", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_TtsBuildFailed => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_TtsBuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_RegisterGlobalProviders_TtsInitialized => ResourceManager.GetString("ProviderManager_RegisterGlobalProviders_TtsInitialized", s_resourceCulture) ?? "";
        #endregion

        #region SocketSession
        public static string SocketSession_SendAsync_SendingJson => ResourceManager.GetString("SocketSession_SendAsync_SendingJson", s_resourceCulture) ?? "";
        public static string SocketSession_SendTtsMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendTtsMessageAsync_SessionNotInitialized", s_resourceCulture) ?? "";
        public static string SocketSession_SendSttMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendSttMessageAsync_SessionNotInitialized", s_resourceCulture) ?? "";
        public static string SocketSession_SendLlmMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendLlmMessageAsync_SessionNotInitialized", s_resourceCulture) ?? "";
        public static string SocketSession_SendAbortMessageAsync_SessionNotInitialized => ResourceManager.GetString("SocketSession_SendAbortMessageAsync_SessionNotInitialized", s_resourceCulture) ?? "";
        public static string SocketSession_OnSessionClosedAsync_ClientOffline => ResourceManager.GetString("SocketSession_OnSessionClosedAsync_ClientOffline", s_resourceCulture) ?? "";
        #endregion

        #region AuthenticationVerification
        public static string AuthenticationVerification_VerifyAsync_DeviceIdNotFound => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_DeviceIdNotFound", s_resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_NewDeviceConnected => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_NewDeviceConnected", s_resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_AuthFailed => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_AuthFailed", s_resourceCulture) ?? "";
        public static string AuthenticationVerification_VerifyAsync_IpNotFound => ResourceManager.GetString("AuthenticationVerification_VerifyAsync_IpNotFound", s_resourceCulture) ?? "";
        #endregion

        #region MessageDispatch
        public static string MessageDispatch_DispatchAsync_ArgumentNull => ResourceManager.GetString("MessageDispatch_DispatchAsync_ArgumentNull", s_resourceCulture) ?? "";
        #endregion

        #region ServerStatusMiddleware
        public static string ServerStatusMiddleware_Start_ServerStarted => ResourceManager.GetString("ServerStatusMiddleware_Start_ServerStarted", s_resourceCulture) ?? "";
        public static string ServerStatusMiddleware_Start_NoListeningOptions => ResourceManager.GetString("ServerStatusMiddleware_Start_NoListeningOptions", s_resourceCulture) ?? "";
        public static string ServerStatusMiddleware_Shutdown_ShuttingDown => ResourceManager.GetString("ServerStatusMiddleware_Shutdown_ShuttingDown", s_resourceCulture) ?? "";
        #endregion

        #region SessionContainerMiddleware
        public static string SessionContainerMiddleware_RegisterSession_LoginFailed => ResourceManager.GetString("SessionContainerMiddleware_RegisterSession_LoginFailed", s_resourceCulture) ?? "";
        #endregion

        #region BaseSherpaAsr
        public static string BaseSherpaAsr_RegisterDevice_Registered => ResourceManager.GetString("BaseSherpaAsr_RegisterDevice_Registered", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaAsr_UnregisterDevice_Unregistered", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_ProviderNotBuilt", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_UnexpectedError", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaAsr_Processing_ProviderNotBuilt", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_ErrorLoop => ResourceManager.GetString("BaseSherpaAsr_Processing_ErrorLoop", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Dispose_WaitFailed => ResourceManager.GetString("BaseSherpaAsr_Dispose_WaitFailed", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_AudioSaved => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_AudioSaved", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_AudioNotSaved => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_AudioNotSaved", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_ConvertSpeechTextAsync_RequestCancelled => ResourceManager.GetString("BaseSherpaAsr_ConvertSpeechTextAsync_RequestCancelled", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_RequestCancelledBeforeProcessing => ResourceManager.GetString("BaseSherpaAsr_Processing_RequestCancelledBeforeProcessing", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_RequestCancelledAfterDecoding => ResourceManager.GetString("BaseSherpaAsr_Processing_RequestCancelledAfterDecoding", s_resourceCulture) ?? "";
        public static string BaseSherpaAsr_Processing_ResultProcessingError => ResourceManager.GetString("BaseSherpaAsr_Processing_ResultProcessingError", s_resourceCulture) ?? "";
        #endregion

        #region Paraformer
        public static string Paraformer_Build_Built => ResourceManager.GetString("Paraformer_Build_Built", s_resourceCulture) ?? "";
        public static string Paraformer_Build_InvalidSettings => ResourceManager.GetString("Paraformer_Build_InvalidSettings", s_resourceCulture) ?? "";
        #endregion

        #region SenseVoice
        public static string SenseVoice_Build_Built => ResourceManager.GetString("SenseVoice_Build_Built", s_resourceCulture) ?? "";
        public static string SenseVoice_Build_InvalidSettings => ResourceManager.GetString("SenseVoice_Build_InvalidSettings", s_resourceCulture) ?? "";
        #endregion

        #region WebSocketClient
        public static string WebSocketClient_CloseAsync_CloseFailed => ResourceManager.GetString("WebSocketClient_CloseAsync_CloseFailed", s_resourceCulture) ?? "";
        #endregion

        #region DefaultOpusDecoder
        public static string DefaultOpusDecoder_Build_Built => ResourceManager.GetString("DefaultOpusDecoder_Build_Built", s_resourceCulture) ?? "";
        public static string DefaultOpusDecoder_Build_InvalidSettings => ResourceManager.GetString("DefaultOpusDecoder_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string DefaultOpusDecoder_DecodeAsync_NotBuilt => ResourceManager.GetString("DefaultOpusDecoder_DecodeAsync_NotBuilt", s_resourceCulture) ?? "";
        #endregion

        #region DefaultOpusEncoder
        public static string DefaultOpusEncoder_Build_Built => ResourceManager.GetString("DefaultOpusEncoder_Build_Built", s_resourceCulture) ?? "";
        public static string DefaultOpusEncoder_Build_InvalidSettings => ResourceManager.GetString("DefaultOpusEncoder_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string DefaultOpusEncoder_EncodeAsync_NotBuilt => ResourceManager.GetString("DefaultOpusEncoder_EncodeAsync_NotBuilt", s_resourceCulture) ?? "";
        #endregion

        #region DefaultResampler
        public static string DefaultResampler_Build_Built => ResourceManager.GetString("DefaultResampler_Build_Built", s_resourceCulture) ?? "";
        public static string DefaultResampler_Build_InvalidSettings => ResourceManager.GetString("DefaultResampler_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string DefaultResampler_ResampleAsync_NotBuilt => ResourceManager.GetString("DefaultResampler_ResampleAsync_NotBuilt", s_resourceCulture) ?? "";
        #endregion

        #region FileMusicPlayer
        public static string FileMusicPlayer_Build_FFmpegInitFailed => ResourceManager.GetString("FileMusicPlayer_Build_FFmpegInitFailed", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_PlayAsync_NotBuilt => ResourceManager.GetString("FileMusicPlayer_PlayAsync_NotBuilt", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_PlayAsync_NoFiles => ResourceManager.GetString("FileMusicPlayer_PlayAsync_NoFiles", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_PauseAsync_Skip => ResourceManager.GetString("FileMusicPlayer_PauseAsync_Skip", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_ResumeAsync_Skip => ResourceManager.GetString("FileMusicPlayer_ResumeAsync_Skip", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_StopAsync_Skip => ResourceManager.GetString("FileMusicPlayer_StopAsync_Skip", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Canceled => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Canceled", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Start => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Start", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Playing => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Playing", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Completed => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Completed", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_PlaybackCanceled => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_PlaybackCanceled", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_AudioFileProcessingAsync_Error => ResourceManager.GetString("FileMusicPlayer_AudioFileProcessingAsync_Error", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_Dispose_Timeout => ResourceManager.GetString("FileMusicPlayer_Dispose_Timeout", s_resourceCulture) ?? "";
        public static string FileMusicPlayer_Dispose_Error => ResourceManager.GetString("FileMusicPlayer_Dispose_Error", s_resourceCulture) ?? "";
        #endregion

        #region IoTClient
        public static string IoTClient_RegisterIoTTools_FunctionDescription => ResourceManager.GetString("IoTClient_RegisterIoTTools_FunctionDescription", s_resourceCulture) ?? "";
        public static string IoTClient_RegisterIoTTools_PluginDescription => ResourceManager.GetString("IoTClient_RegisterIoTTools_PluginDescription", s_resourceCulture) ?? "";
        public static string IoTClient_SetIoTPropertyStatusValue_SetStatus => ResourceManager.GetString("IoTClient_SetIoTPropertyStatusValue_SetStatus", s_resourceCulture) ?? "";
        public static string IoTClient_SendIoTMessageAsync_MessageNull => ResourceManager.GetString("IoTClient_SendIoTMessageAsync_MessageNull", s_resourceCulture) ?? "";
        public static string IoTClient_RegisterIoTTools_KernelNull => ResourceManager.GetString("IoTClient_RegisterIoTTools_KernelNull", s_resourceCulture) ?? "";
        #endregion

        #region DefaultAudioProcessor
        public static string DefaultAudioProcessor_Build_Initialized => ResourceManager.GetString("DefaultAudioProcessor_Build_Initialized", s_resourceCulture) ?? "";
        public static string DefaultAudioProcessor_Build_InvalidSettings => ResourceManager.GetString("DefaultAudioProcessor_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string DefaultAudioProcessor_FireOnMixedAudioData_InvokeError => ResourceManager.GetString("DefaultAudioProcessor_FireOnMixedAudioData_InvokeError", s_resourceCulture) ?? "";
        #endregion

        #region NotificationPlayer
        public static string NotificationPlayer_Build_FFmpegInitFailed => ResourceManager.GetString("NotificationPlayer_Build_FFmpegInitFailed", s_resourceCulture) ?? "";
        public static string NotificationPlayer_PlayBindCodeAsync_NotBuilt => ResourceManager.GetString("NotificationPlayer_PlayBindCodeAsync_NotBuilt", s_resourceCulture) ?? "";
        public static string NotificationPlayer_PlayBindCodeAsync_StreamNull => ResourceManager.GetString("NotificationPlayer_PlayBindCodeAsync_StreamNull", s_resourceCulture) ?? "";
        public static string NotificationPlayer_PlayNotFoundAsync_NotBuilt => ResourceManager.GetString("NotificationPlayer_PlayNotFoundAsync_NotBuilt", s_resourceCulture) ?? "";
        public static string NotificationPlayer_PlayNotFoundAsync_StreamNull => ResourceManager.GetString("NotificationPlayer_PlayNotFoundAsync_StreamNull", s_resourceCulture) ?? "";
        public static string NotificationPlayer_StopAsync_Skip => ResourceManager.GetString("NotificationPlayer_StopAsync_Skip", s_resourceCulture) ?? "";
        #endregion

        #region ChatAgent
        public static string ChatAgent_Build_Built => ResourceManager.GetString("ChatAgent_Build_Built", s_resourceCulture) ?? "";
        public static string ChatAgent_Build_BuiltFailed => ResourceManager.GetString("ChatAgent_Build_BuiltFailed", s_resourceCulture) ?? "";
        public static string ChatAgent_Build_BuildPluginsBuilt => ResourceManager.GetString("ChatAgent_Build_BuildPluginsBuilt", s_resourceCulture) ?? "";
        public static string ChatAgent_Build_BuildPluginsFailed => ResourceManager.GetString("ChatAgent_Build_BuildPluginsFailed", s_resourceCulture) ?? "";
        public static string ChatAgent_RegisterDevice_PluginRegistered => ResourceManager.GetString("ChatAgent_RegisterDevice_PluginRegistered", s_resourceCulture) ?? "";
        public static string ChatAgent_GenerateChatResponseAsync_AgentNotBuilt => ResourceManager.GetString("ChatAgent_GenerateChatResponseAsync_AgentNotBuilt", s_resourceCulture) ?? "";
        #endregion

        #region BaseAgent
        public static string BaseAgent_RegisterDevice_Registered => ResourceManager.GetString("BaseAgent_RegisterDevice_Registered", s_resourceCulture) ?? "";
        public static string BaseAgent_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseAgent_UnregisterDevice_Unregistered", s_resourceCulture) ?? "";
        public static string BaseAgent_CheckDeviceRegistered_NotRegistered => ResourceManager.GetString("BaseAgent_CheckDeviceRegistered_NotRegistered", s_resourceCulture) ?? "";
        #endregion

        #region EmotionAgent
        public static string EmotionAgent_Build_Built => ResourceManager.GetString("EmotionAgent_Build_Built", s_resourceCulture) ?? "";
        public static string EmotionAgent_Build_BuildFailed => ResourceManager.GetString("EmotionAgent_Build_BuildFailed", s_resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_AgentNotBuilt => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_AgentNotBuilt", s_resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_UserCanceled => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_UserCanceled", s_resourceCulture) ?? "";
        public static string EmotionAgent_AnalyzeEmotionAsync_UnexpectedError => ResourceManager.GetString("EmotionAgent_AnalyzeEmotionAsync_UnexpectedError", s_resourceCulture) ?? "";
        #endregion

        #region MCPToolFunctionFilter
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_McpClientNotInit", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_SubMcpClientNotFound", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailed => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailed", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailedDetail => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeMcpFailedDetail", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTClientNotInit => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTClientNotInit", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_ReturnTypeNotSpecified => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_ReturnTypeNotSpecified", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTSuccess => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTSuccess", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvalidIoTName => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvalidIoTName", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailed => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailed", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailedDetail => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_InvokeIoTFailedDetail", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_FunctionCancelled => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_FunctionCancelled", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_McpCancelled => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_McpCancelled", s_resourceCulture) ?? "";
        public static string MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTCancelled => ResourceManager.GetString("MCPToolFunctionFilter_OnFunctionInvocationAsync_IoTCancelled", s_resourceCulture) ?? "";
        #endregion

        #region BaseMcpClient
        public static string BaseMcpClient_HandleMcpMessageAsync_KernelNotReady => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_KernelNotReady", s_resourceCulture) ?? "";
        #endregion

        #region BaseMcpClient
        public static string BaseMcpClient_HandleMcpMessageAsync_CallResult => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_CallResult", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_InitMessage => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_InitMessage", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ServerInfo => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ServerInfo", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_InvalidServerInfo", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolListMessage => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolListMessage", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolAdded => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolAdded", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ToolCount => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ToolCount", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_MoreTools => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_MoreTools", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ClientReady => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ClientReady", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_GetToolsFailed", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ClientRequest => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ClientRequest", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ErrorResponse => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ErrorResponse", s_resourceCulture) ?? "";
        public static string BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception => ResourceManager.GetString("BaseMcpClient_HandleMcpMessageAsync_ErrorResponse_Exception", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_ToolError => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_ToolError", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_TimeoutEx => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_TimeoutEx", s_resourceCulture) ?? "";

        public static string BaseMcpClient_SendMcpInitializeAsync_SendingInit => ResourceManager.GetString("BaseMcpClient_SendMcpInitializeAsync_SendingInit", s_resourceCulture) ?? "";
        public static string BaseMcpClient_SendMcpNotificationAsync_SendingNotification => ResourceManager.GetString("BaseMcpClient_SendMcpNotificationAsync_SendingNotification", s_resourceCulture) ?? "";
        public static string BaseMcpClient_RequestToolsListAsync_RequestTools => ResourceManager.GetString("BaseMcpClient_RequestToolsListAsync_RequestTools", s_resourceCulture) ?? "";
        public static string BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor => ResourceManager.GetString("BaseMcpClient_RequestToolsListAsync_RequestToolsWithCursor", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallTool => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallTool", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_Timeout => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_Timeout", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallSuccess => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallSuccess", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_WaitTimeout => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_WaitTimeout", s_resourceCulture) ?? "";
        public static string BaseMcpClient_CallMcpToolAsync_CallFailed => ResourceManager.GetString("BaseMcpClient_CallMcpToolAsync_CallFailed", s_resourceCulture) ?? "";
        public static string BaseMcpClient_AddTool_ToolExists => ResourceManager.GetString("BaseMcpClient_AddTool_ToolExists", s_resourceCulture) ?? "";
        #endregion

        #region McpEndpointClient
        public static string McpEndpointClient_Build_UrlEmpty => ResourceManager.GetString("McpEndpointClient_Build_UrlEmpty", s_resourceCulture) ?? "";
        public static string McpEndpointClient_Build_InvalidSettings => ResourceManager.GetString("McpEndpointClient_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string McpEndpointClient_OnOpen_Connected => ResourceManager.GetString("McpEndpointClient_OnOpen_Connected", s_resourceCulture) ?? "";
        #endregion

        #region DeviceMcpClient
        public static string DeviceMcpClient_SendMcpInitializeAsync_SendingInit => ResourceManager.GetString("DeviceMcpClient_SendMcpInitializeAsync_SendingInit", s_resourceCulture) ?? "";
        #endregion

        #region GenericOpenAI
        public static string GenericOpenAI_Build_InvalidSettings => ResourceManager.GetString("GenericOpenAI_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string GenericOpenAI_Build_AgentSettingMissing => ResourceManager.GetString("GenericOpenAI_Build_AgentSettingMissing", s_resourceCulture) ?? "";
        public static string GenericOpenAI_StartDialogueAsync_NotBuilt => ResourceManager.GetString("GenericOpenAI_StartDialogueAsync_NotBuilt", s_resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_EmotionDetected => ResourceManager.GetString("GenericOpenAI_ChatAsync_EmotionDetected", s_resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_UnexpectedError => ResourceManager.GetString("GenericOpenAI_ChatAsync_UnexpectedError", s_resourceCulture) ?? "";
        public static string GenericOpenAI_ChatAsync_Cancelled => ResourceManager.GetString("GenericOpenAI_ChatAsync_Cancelled", s_resourceCulture) ?? "";
        public static string GenericOpenAI_ChatByStreamingAsync_Cancelled => ResourceManager.GetString("GenericOpenAI_ChatByStreamingAsync_Cancelled", s_resourceCulture) ?? "";
        #endregion

        #region DefaultRag
        public static string DefaultRag_Load_EmbeddingModelMissing => ResourceManager.GetString("DefaultRag_Load_EmbeddingModelMissing", s_resourceCulture) ?? "";
        public static string DefaultRag_Load_DocumentDirectoryInvalid => ResourceManager.GetString("DefaultRag_Load_DocumentDirectoryInvalid", s_resourceCulture) ?? "";
        public static string DefaultRag_Load_Loaded => ResourceManager.GetString("DefaultRag_Load_Loaded", s_resourceCulture) ?? "";
        public static string DefaultRag_Load_LoadFailed => ResourceManager.GetString("DefaultRag_Load_LoadFailed", s_resourceCulture) ?? "";
        public static string DefaultRag_Create_NotReady => ResourceManager.GetString("DefaultRag_Create_NotReady", s_resourceCulture) ?? "";
        public static string DefaultRag_SearchAsync_QueryReceived => ResourceManager.GetString("DefaultRag_SearchAsync_QueryReceived", s_resourceCulture) ?? "";
        public static string DefaultRag_RetrieveAsync_EmbeddingGeneratorNotInitialized => ResourceManager.GetString("DefaultRag_RetrieveAsync_EmbeddingGeneratorNotInitialized", s_resourceCulture) ?? "";
        public static string DefaultRag_RetrieveAsync_EmbeddingCountInvalid => ResourceManager.GetString("DefaultRag_RetrieveAsync_EmbeddingCountInvalid", s_resourceCulture) ?? "";
        #endregion

        #region BaseHuoshanTTS
        public static string BaseHuoshanTTS_SaveAudioFile_FileSaved => ResourceManager.GetString("BaseHuoshanTTS_SaveAudioFile_FileSaved", s_resourceCulture) ?? "";
        public static string BaseHuoshanTTS_SaveAudioFile_SaveFailed => ResourceManager.GetString("BaseHuoshanTTS_SaveAudioFile_SaveFailed", s_resourceCulture) ?? "";
        #endregion

        #region HuoshanHttpV3TTS
        public static string HuoshanHttpV3TTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanHttpV3TTS_Build_ConfigIncomplete", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_Build_Built => ResourceManager.GetString("HuoshanHttpV3TTS_Build_Built", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_Build_Failed => ResourceManager.GetString("HuoshanHttpV3TTS_Build_Failed", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_DevNotReg => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_DevNotReg", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_MissingSentenceId => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_MissingSentenceId", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_RequestFailed => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_RequestFailed", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_ApiError => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_ApiError", s_resourceCulture) ?? "";
        public static string HuoshanHttpV3TTS_SynthesisAsync_GeneralFailed => ResourceManager.GetString("HuoshanHttpV3TTS_SynthesisAsync_GeneralFailed", s_resourceCulture) ?? "";
        #endregion

        #region HuoshanHttpTTS
        public static string HuoshanHttpTTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanHttpTTS_Build_ConfigIncomplete", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_Build_Built => ResourceManager.GetString("HuoshanHttpTTS_Build_Built", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_Build_Failed => ResourceManager.GetString("HuoshanHttpTTS_Build_Failed", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_ModelNotBuilt => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_ModelNotBuilt", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_DevNotReg => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_DevNotReg", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_MissingSentenceId => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_MissingSentenceId", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_RequestFailed => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_RequestFailed", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_ApiError => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_ApiError", s_resourceCulture) ?? "";
        public static string HuoshanHttpTTS_SynthesisAsync_GeneralFailed => ResourceManager.GetString("HuoshanHttpTTS_SynthesisAsync_GeneralFailed", s_resourceCulture) ?? "";
        #endregion

        #region HuoshanBidirectionTTS
        public static string HuoshanBidirectionTTS_SynthesisAsync_ClientNotInit => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_ClientNotInit", s_resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_MissingIds => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_MissingIds", s_resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_Canceled => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_Canceled", s_resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_SynthesisAsync_Failed => ResourceManager.GetString("HuoshanBidirectionTTS_SynthesisAsync_Failed", s_resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_Dispose_FinishError => ResourceManager.GetString("HuoshanBidirectionTTS_Dispose_FinishError", s_resourceCulture) ?? "";
        public static string HuoshanBidirectionTTS_Dispose_Disposed => ResourceManager.GetString("HuoshanBidirectionTTS_Dispose_Disposed", s_resourceCulture) ?? "";
        #endregion

        #region SileroNative
        public static string SileroNative_Build_UnsupportedSampleRate => ResourceManager.GetString("SileroNative_Build_UnsupportedSampleRate", s_resourceCulture) ?? "";
        public static string SileroNative_Build_Built => ResourceManager.GetString("SileroNative_Build_Built", s_resourceCulture) ?? "";
        public static string SileroNative_Build_InvalidSettings => ResourceManager.GetString("SileroNative_Build_InvalidSettings", s_resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_ProviderNotBuilt => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_ProviderNotBuilt", s_resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_VoiceStopped => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_VoiceStopped", s_resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_UserCanceled => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_UserCanceled", s_resourceCulture) ?? "";
        public static string SileroNative_AnalysisVoiceAsync_UnexpectedError => ResourceManager.GetString("SileroNative_AnalysisVoiceAsync_UnexpectedError", s_resourceCulture) ?? "";
        public static string SileroNative_CheckLongTermSilence_Detected => ResourceManager.GetString("SileroNative_CheckLongTermSilence_Detected", s_resourceCulture) ?? "";
        #endregion

        #region Kokoro
        public static string Kokoro_Build_Built => ResourceManager.GetString("Kokoro_Build_Built", s_resourceCulture) ?? "";
        public static string Kokoro_Build_InvalidSettings => ResourceManager.GetString("Kokoro_Build_InvalidSettings", s_resourceCulture) ?? "";
        #endregion

        #region BaseSherpaTts
        public static string BaseSherpaTts_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaTts_UnregisterDevice_Unregistered", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_ProviderNotBuilt => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_ProviderNotBuilt", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_MissingIds => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_MissingIds", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_FileSaved => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_FileSaved", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_SaveFailed => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_SaveFailed", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_Generated => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_Generated", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_CallbackNotRegistered => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_CallbackNotRegistered", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_UserCanceled => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_UserCanceled", s_resourceCulture) ?? "";
        public static string BaseSherpaTts_SynthesisAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaTts_SynthesisAsync_UnexpectedError", s_resourceCulture) ?? "";
        #endregion

        #region HuoshanStreamTTS
        public static string HuoshanStreamTTS_Build_ConfigIncomplete => ResourceManager.GetString("HuoshanStreamTTS_Build_ConfigIncomplete", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_Build_Built => ResourceManager.GetString("HuoshanStreamTTS_Build_Built", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_Build_Failed => ResourceManager.GetString("HuoshanStreamTTS_Build_Failed", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_ConnectAsync_ClientNotInitLog => ResourceManager.GetString("HuoshanStreamTTS_ConnectAsync_ClientNotInitLog", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_ConnectAsync_ClientNotInitEx => ResourceManager.GetString("HuoshanStreamTTS_ConnectAsync_ClientNotInitEx", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_WaitForEventAsync_Timeout => ResourceManager.GetString("HuoshanStreamTTS_WaitForEventAsync_Timeout", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnOpen_Connected => ResourceManager.GetString("HuoshanStreamTTS_OnOpen_Connected", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnClose_Closed => ResourceManager.GetString("HuoshanStreamTTS_OnClose_Closed", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnClose_ClosedEx => ResourceManager.GetString("HuoshanStreamTTS_OnClose_ClosedEx", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnError_Error => ResourceManager.GetString("HuoshanStreamTTS_OnError_Error", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnError_ErrorEx => ResourceManager.GetString("HuoshanStreamTTS_OnError_ErrorEx", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ParseFailed => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ParseFailed", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_AppendFailed => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_AppendFailed", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ServerFailure => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ServerFailure", s_resourceCulture) ?? "";
        public static string HuoshanStreamTTS_OnBinaryMessage_ServerError => ResourceManager.GetString("HuoshanStreamTTS_OnBinaryMessage_ServerError", s_resourceCulture) ?? "";
        #endregion

        #region BaseProvider
        public static string BaseProvider_RegisterDevice_Registered => ResourceManager.GetString("BaseProvider_RegisterDevice_Registered", s_resourceCulture) ?? "";
        public static string BaseProvider_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseProvider_UnregisterDevice_Unregistered", s_resourceCulture) ?? "";
        public static string BaseProvider_CheckDeviceRegistered_NotRegistered => ResourceManager.GetString("BaseProvider_CheckDeviceRegistered_NotRegistered", s_resourceCulture) ?? "";
        public static string BaseProvider_CheckModelExist_NotFound => ResourceManager.GetString("BaseProvider_CheckModelExist_NotFound", s_resourceCulture) ?? "";
        public static string BaseProvider_ReplaceMacDelimiters_DeviceIdNull => ResourceManager.GetString("BaseProvider_ReplaceMacDelimiters_DeviceIdNull", s_resourceCulture) ?? "";
        #endregion

        #region Silero
        public static string Silero_Build_Built => ResourceManager.GetString("Silero_Build_Built", s_resourceCulture) ?? "";
        public static string Silero_Build_InvalidSettings => ResourceManager.GetString("Silero_Build_InvalidSettings", s_resourceCulture) ?? "";
        #endregion

        #region BaseSherpaVad
        public static string BaseSherpaVad_Build_UnsupportedSampleRate => ResourceManager.GetString("BaseSherpaVad_Build_UnsupportedSampleRate", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_RegisterDevice_Registered => ResourceManager.GetString("BaseSherpaVad_RegisterDevice_Registered", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_UnregisterDevice_Unregistered => ResourceManager.GetString("BaseSherpaVad_UnregisterDevice_Unregistered", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_ResetSessionState_Reset => ResourceManager.GetString("BaseSherpaVad_ResetSessionState_Reset", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_VoiceStopped", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_UserCanceled => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_UserCanceled", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_UnexpectedError", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_CheckLongTermSilence_Detected => ResourceManager.GetString("BaseSherpaVad_CheckLongTermSilence_Detected", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_VadNotBuilt", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound => ResourceManager.GetString("BaseSherpaVad_AnalysisVoiceAsync_SessionStateNotFound", s_resourceCulture) ?? "";
        #endregion

        #region SileroModelState
        public static string SileroModelState_UpdateHiddenState_SizeMismatch => ResourceManager.GetString("SileroModelState_UpdateHiddenState_SizeMismatch", s_resourceCulture) ?? "";
        public static string SileroModelState_UpdateCellState_SizeMismatch => ResourceManager.GetString("SileroModelState_UpdateCellState_SizeMismatch", s_resourceCulture) ?? "";
        #endregion

        #region SileroOnnx
        public static string SileroOnnx_Load_Loaded => ResourceManager.GetString("SileroOnnx_Load_Loaded", s_resourceCulture) ?? "";
        public static string SileroOnnx_Load_InvalidModel => ResourceManager.GetString("SileroOnnx_Load_InvalidModel", s_resourceCulture) ?? "";
        public static string SileroOnnx_Infer_Disposed => ResourceManager.GetString("SileroOnnx_Infer_Disposed", s_resourceCulture) ?? "";
        public static string SileroOnnx_Infer_SessionNotInitialized => ResourceManager.GetString("SileroOnnx_Infer_SessionNotInitialized", s_resourceCulture) ?? "";
        public static string SileroOnnx_Infer_SampleCountMismatch => ResourceManager.GetString("SileroOnnx_Infer_SampleCountMismatch", s_resourceCulture) ?? "";
        public static string SileroOnnx_ValidateInput_SamplesEmpty => ResourceManager.GetString("SileroOnnx_ValidateInput_SamplesEmpty", s_resourceCulture) ?? "";
        public static string SileroOnnx_ValidateInput_UnsupportedSampleRate => ResourceManager.GetString("SileroOnnx_ValidateInput_UnsupportedSampleRate", s_resourceCulture) ?? "";
        public static string SileroOnnx_ValidateInput_SampleRateChanged => ResourceManager.GetString("SileroOnnx_ValidateInput_SampleRateChanged", s_resourceCulture) ?? "";
        public static string SileroOnnx_Dispose_Disposed => ResourceManager.GetString("SileroOnnx_Dispose_Disposed", s_resourceCulture) ?? "";
        #endregion

        #region MusicProvider
        public static string MusicProvider_Load_PathNotSet => ResourceManager.GetString("MusicProvider_Load_PathNotSet", s_resourceCulture) ?? "";
        public static string MusicProvider_Load_PathNotExist => ResourceManager.GetString("MusicProvider_Load_PathNotExist", s_resourceCulture) ?? "";
        public static string MusicProvider_UpdateMusicFiles_SettingsNotInitialized => ResourceManager.GetString("MusicProvider_UpdateMusicFiles_SettingsNotInitialized", s_resourceCulture) ?? "";
        #endregion

        #region DefaultDeviceBinding
        public static string DefaultDeviceBinding_Load_BindCodePromptNotExist => ResourceManager.GetString("DefaultDeviceBinding_Load_BindCodePromptNotExist", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_Load_BindNotFoundNotExist => ResourceManager.GetString("DefaultDeviceBinding_Load_BindNotFoundNotExist", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_Load_DigitFilesCountError => ResourceManager.GetString("DefaultDeviceBinding_Load_DigitFilesCountError", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_Load_InvalidDigitFile => ResourceManager.GetString("DefaultDeviceBinding_Load_InvalidDigitFile", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_Load_InvalidResourceLoading => ResourceManager.GetString("DefaultDeviceBinding_Load_InvalidResourceLoading", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_GetDeviceNotFoundAudioStream_NotLoaded => ResourceManager.GetString("DefaultDeviceBinding_GetDeviceNotFoundAudioStream_NotLoaded", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_GetDeviceBindCodeAudioStream_InvalidBindCode => ResourceManager.GetString("DefaultDeviceBinding_GetDeviceBindCodeAudioStream_InvalidBindCode", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_GetDeviceBindCodeAudioStream_PromptNotLoaded => ResourceManager.GetString("DefaultDeviceBinding_GetDeviceBindCodeAudioStream_PromptNotLoaded", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_GetDeviceBindCodeAudioStream_DigitNotLoaded => ResourceManager.GetString("DefaultDeviceBinding_GetDeviceBindCodeAudioStream_DigitNotLoaded", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_CombinedStream_ListEmpty => ResourceManager.GetString("DefaultDeviceBinding_CombinedStream_ListEmpty", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_CombinedStream_FirstFileInvalid => ResourceManager.GetString("DefaultDeviceBinding_CombinedStream_FirstFileInvalid", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_CombinedStream_FileInvalid => ResourceManager.GetString("DefaultDeviceBinding_CombinedStream_FileInvalid", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_CombinedStream_BufferOverflow => ResourceManager.GetString("DefaultDeviceBinding_CombinedStream_BufferOverflow", s_resourceCulture) ?? "";
        public static string DefaultDeviceBinding_CombinedStream_InvalidOrigin => ResourceManager.GetString("DefaultDeviceBinding_CombinedStream_InvalidOrigin", s_resourceCulture) ?? "";
        #endregion

        #region BaseOnnxModel
        public static string BaseOnnxModel_CheckModelExist_NotFound => ResourceManager.GetString("BaseOnnxModel_CheckModelExist_NotFound", s_resourceCulture) ?? "";
        #endregion

        #region ManageApiClient
        public static string ManageApiClient_LoadConfigFromApi_UnknownException => ResourceManager.GetString("ManageApiClient_LoadConfigFromApi_UnknownException", s_resourceCulture) ?? "";
        #endregion

        #region StreamingAudio
        public static string BaseHuoshanASR_Build_ConfigIncomplete => ResourceManager.GetString("BaseHuoshanASR_Build_ConfigIncomplete", s_resourceCulture) ?? "";
        public static string BaseHuoshanASR_Build_SegmentDurationInvalid => ResourceManager.GetString("BaseHuoshanASR_Build_SegmentDurationInvalid", s_resourceCulture) ?? "";
        public static string BaseHuoshanASR_Build_Built => ResourceManager.GetString("BaseHuoshanASR_Build_Built", s_resourceCulture) ?? "";
        public static string BaseHuoshanASR_Build_Failed => ResourceManager.GetString("BaseHuoshanASR_Build_Failed", s_resourceCulture) ?? "";
        public static string BaseHuoshanASR_FinishUtterance_WaitForFinalResultFailed => ResourceManager.GetString("BaseHuoshanASR_FinishUtterance_WaitForFinalResultFailed", s_resourceCulture) ?? "";
        public static string BaseHuoshanASR_AbortSynchronously_Failed => ResourceManager.GetString("BaseHuoshanASR_AbortSynchronously_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_ConfigIncomplete => ResourceManager.GetString("AliyunRealtimeASR_Build_ConfigIncomplete", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_EndpointInvalid => ResourceManager.GetString("AliyunRealtimeASR_Build_EndpointInvalid", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_UnsupportedRegion => ResourceManager.GetString("AliyunRealtimeASR_Build_UnsupportedRegion", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_SegmentDurationInvalid => ResourceManager.GetString("AliyunRealtimeASR_Build_SegmentDurationInvalid", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_Built => ResourceManager.GetString("AliyunRealtimeASR_Build_Built", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_Build_Failed => ResourceManager.GetString("AliyunRealtimeASR_Build_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_FinishUtterance_WaitForFinalResultFailed => ResourceManager.GetString("AliyunRealtimeASR_FinishUtterance_WaitForFinalResultFailed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeASR_AbortSynchronously_Failed => ResourceManager.GetString("AliyunRealtimeASR_AbortSynchronously_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_ConfigInvalid => ResourceManager.GetString("AliyunRealtimeTTS_Build_ConfigInvalid", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_UnsupportedSampleRate => ResourceManager.GetString("AliyunRealtimeTTS_Build_UnsupportedSampleRate", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_InvalidAudioParameter => ResourceManager.GetString("AliyunRealtimeTTS_Build_InvalidAudioParameter", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_Built => ResourceManager.GetString("AliyunRealtimeTTS_Build_Built", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_Failed => ResourceManager.GetString("AliyunRealtimeTTS_Build_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_EndpointInvalid => ResourceManager.GetString("AliyunRealtimeTTS_Build_EndpointInvalid", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Build_UnsupportedRegion => ResourceManager.GetString("AliyunRealtimeTTS_Build_UnsupportedRegion", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Synthesis_NotRegistered => ResourceManager.GetString("AliyunRealtimeTTS_Synthesis_NotRegistered", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Synthesis_NotBuilt => ResourceManager.GetString("AliyunRealtimeTTS_Synthesis_NotBuilt", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Synthesis_ClientNotInitialized => ResourceManager.GetString("AliyunRealtimeTTS_Synthesis_ClientNotInitialized", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Synthesis_MissingFirstSegment => ResourceManager.GetString("AliyunRealtimeTTS_Synthesis_MissingFirstSegment", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Task_Failed => ResourceManager.GetString("AliyunRealtimeTTS_Task_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Task_Failed_Default => ResourceManager.GetString("AliyunRealtimeTTS_Task_Failed_Default", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Abort_Failed => ResourceManager.GetString("AliyunRealtimeTTS_Abort_Failed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Start_ConnectionFailed => ResourceManager.GetString("AliyunRealtimeTTS_Start_ConnectionFailed", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_Append_NotAcceptingText => ResourceManager.GetString("AliyunRealtimeTTS_Append_NotAcceptingText", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_WebSocket_UnexpectedClose => ResourceManager.GetString("AliyunRealtimeTTS_WebSocket_UnexpectedClose", s_resourceCulture) ?? "";
        public static string AliyunRealtimeTTS_WebSocket_Error => ResourceManager.GetString("AliyunRealtimeTTS_WebSocket_Error", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_ConfigInvalid => ResourceManager.GetString("AliyunHttpTTS_Build_ConfigInvalid", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_EndpointInvalid => ResourceManager.GetString("AliyunHttpTTS_Build_EndpointInvalid", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_UnsupportedSampleRate => ResourceManager.GetString("AliyunHttpTTS_Build_UnsupportedSampleRate", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_InvalidAudioParameter => ResourceManager.GetString("AliyunHttpTTS_Build_InvalidAudioParameter", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_Built => ResourceManager.GetString("AliyunHttpTTS_Build_Built", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Build_Failed => ResourceManager.GetString("AliyunHttpTTS_Build_Failed", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_NotRegistered => ResourceManager.GetString("AliyunHttpTTS_Synthesis_NotRegistered", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_NotBuilt => ResourceManager.GetString("AliyunHttpTTS_Synthesis_NotBuilt", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_MissingSentenceId => ResourceManager.GetString("AliyunHttpTTS_Synthesis_MissingSentenceId", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_RequestFailed => ResourceManager.GetString("AliyunHttpTTS_Synthesis_RequestFailed", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_DownloadFailed => ResourceManager.GetString("AliyunHttpTTS_Synthesis_DownloadFailed", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_ApiError => ResourceManager.GetString("AliyunHttpTTS_Synthesis_ApiError", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_InvalidResponse => ResourceManager.GetString("AliyunHttpTTS_Synthesis_InvalidResponse", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_NoAudio => ResourceManager.GetString("AliyunHttpTTS_Synthesis_NoAudio", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_Synthesis_Failed => ResourceManager.GetString("AliyunHttpTTS_Synthesis_Failed", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_SaveAudioFile_FileSaved => ResourceManager.GetString("AliyunHttpTTS_SaveAudioFile_FileSaved", s_resourceCulture) ?? "";
        public static string AliyunHttpTTS_SaveAudioFile_SaveFailed => ResourceManager.GetString("AliyunHttpTTS_SaveAudioFile_SaveFailed", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_OnSpeechTextConverted_StaleResult => ResourceManager.GetString("Audio2TextHandler_OnSpeechTextConverted_StaleResult", s_resourceCulture) ?? "";
        public static string Audio2TextHandler_ObserveSpeechResult_Failed => ResourceManager.GetString("Audio2TextHandler_ObserveSpeechResult_Failed", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_OnVoiceDetected_DispatchFailed => ResourceManager.GetString("AudioReceiveHandler_OnVoiceDetected_DispatchFailed", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_HandleManualStop_DispatchFailed => ResourceManager.GetString("AudioReceiveHandler_HandleManualStop_DispatchFailed", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_AbortStreamingUtterance_Failed => ResourceManager.GetString("AudioReceiveHandler_AbortStreamingUtterance_Failed", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_QueueStreamingOperation_QueueOverflow => ResourceManager.GetString("AudioReceiveHandler_QueueStreamingOperation_QueueOverflow", s_resourceCulture) ?? "";
        public static string AudioReceiveHandler_SendStreamingOperationSafelyAsync_Failed => ResourceManager.GetString("AudioReceiveHandler_SendStreamingOperationSafelyAsync_Failed", s_resourceCulture) ?? "";
        public static string HelloMessageHandler_Handle_UnsupportedAudioParameters => ResourceManager.GetString("HelloMessageHandler_Handle_UnsupportedAudioParameters", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildOutputAudioResampler_ResamplingRequired => ResourceManager.GetString("ProviderManager_BuildOutputAudioResampler_ResamplingRequired", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildOutputAudioResampler_BuildFailed => ResourceManager.GetString("ProviderManager_BuildOutputAudioResampler_BuildFailed", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildInputAudioResampler_ResamplingRequired => ResourceManager.GetString("ProviderManager_BuildInputAudioResampler_ResamplingRequired", s_resourceCulture) ?? "";
        public static string ProviderManager_BuildInputAudioResampler_BuildFailed => ResourceManager.GetString("ProviderManager_BuildInputAudioResampler_BuildFailed", s_resourceCulture) ?? "";
        public static string SileroNative_Build_NonCanonicalSampleRate => ResourceManager.GetString("SileroNative_Build_NonCanonicalSampleRate", s_resourceCulture) ?? "";
        public static string BaseSherpaVad_Build_NonCanonicalSampleRate => ResourceManager.GetString("BaseSherpaVad_Build_NonCanonicalSampleRate", s_resourceCulture) ?? "";
        public static string DefaultOpusDecoder_DecodeAsync_SampleCountMismatch => ResourceManager.GetString("DefaultOpusDecoder_DecodeAsync_SampleCountMismatch", s_resourceCulture) ?? "";
        #endregion

    }
}
