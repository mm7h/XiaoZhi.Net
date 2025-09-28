using System;
using XiaoZhi.Net.Server.Abstractions.Common.Enums;

namespace XiaoZhi.Net.Server.Providers
{
    /// <summary>
    /// ��Ƶ������
    /// </summary>
    internal interface IAudioMixer : IProvider<AudioSetting>
    {
        /// <summary>
        /// �����Ƶ�����¼�
        /// </summary>
        event Action<float[], bool, bool>? OnMixedAudioDataAvailable;

        /// <summary>
        /// ������Ƶ����
        /// </summary>
        /// <param name="audioType">��Ƶ����</param>
        /// <param name="audioData">��Ƶ����</param>
        void AddAudioData(AudioType audioType, float[] audioData);

        /// <summary>
        /// ֹͣ��Ƶ��
        /// </summary>
        /// <param name="audioType">��Ƶ����</param>
        void StopAudioStream(AudioType audioType);

        /// <summary>
        /// ������л�����
        /// </summary>
        void ClearAllBuffers();
    }
}