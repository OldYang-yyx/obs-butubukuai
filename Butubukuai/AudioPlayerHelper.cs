using System;
using System.IO;
using NAudio.Wave;
using System.Threading.Tasks;

namespace Butubukuai
{
    public static class AudioPlayerHelper
    {
        public static void PlaySound(string filePath)
        {
            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    {
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }

                    using var audioFile = new AudioFileReader(filePath);
                    using var outputDevice = new WaveOutEvent();
                    outputDevice.Init(audioFile);
                    outputDevice.Play();
                    
                    while (outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"播放音频异常: {ex.Message}");
                    System.Media.SystemSounds.Beep.Play(); // Fallback
                }
            });
        }
    }
}
