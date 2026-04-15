using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace Butubukuai
{
    /// <summary>
    /// 音频管理类，负责获取麦克风列表和采集实时音频
    /// </summary>
    public class AudioManager
    {
        private WaveInEvent? _waveIn;

        /// <summary>
        /// 当音量发生变化时触发的事件，返回归一化后的音量 (0.0 ~ 1.0)
        /// </summary>
        public event EventHandler<float>? OnVolumeChanged;

        /// <summary>
        /// 获取系统麦克风列表
        /// </summary>
        /// <returns>麦克风设备名称列表</returns>
        public List<string> GetMicrophoneList()
        {
            var micList = new List<string>();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceInfo = WaveIn.GetCapabilities(i);
                micList.Add(deviceInfo.ProductName);
            }
            return micList;
        }

        /// <summary>
        /// 开始从指定设备采集音频
        /// </summary>
        /// <param name="deviceNumber">设备编号</param>
        public void StartRecording(int deviceNumber)
        {
            if (_waveIn != null)
            {
                StopRecording();
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                // 设置标准的音频格式: 采样率 44.1kHz, 单声道, 16位深度
                WaveFormat = new WaveFormat(44100, 1)
            };

            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.StartRecording();
        }

        /// <summary>
        /// 停止音频采集
        /// </summary>
        public void StopRecording()
        {
            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= WaveIn_DataAvailable;
                _waveIn.Dispose();
                _waveIn = null;
            }
            
            // 确保停止后将音量重置为 0
            OnVolumeChanged?.Invoke(this, 0f);
        }

        /// <summary>
        /// 处理音频数据到达事件
        /// </summary>
        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            // 在这段缓冲区内计算音量峰值
            float maxVolume = 0;

            // e.Buffer 包含的是 16-bit PCM 数据 (每个采样点 2 字节)
            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                // 将 2 字节组合成一个 16位整数 (short)
                short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);

                // 归一化处理得到 0 ~ 1.0 的百分比
                float sample32 = Math.Abs(sample / 32768f);
                if (sample32 > maxVolume)
                {
                    maxVolume = sample32;
                }
            }

            // 触发音量变化事件
            OnVolumeChanged?.Invoke(this, maxVolume);
        }
    }
}
