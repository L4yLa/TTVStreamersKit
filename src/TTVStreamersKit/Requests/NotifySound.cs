using IPA.Utilities;
using System;
using System.IO;
using UnityEngine;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// リクエストを追加したときの通知音。UserData\TTVStreamersKit\request.wav を鳴らす（利用者が差し替えられる）。
    /// 読めるのは非圧縮の PCM（8 / 16 / 24 / 32 ビット）と 32 ビット浮動小数点の WAV。
    /// </summary>
    internal static class NotifySound
    {
        private static AudioClip s_clip;
        private static AudioSource s_source;
        private static bool s_loaded;

        public static void Play(float volume)
        {
            try {
                if (!s_loaded) {
                    s_loaded = true;
                    s_clip = Load();
                }
                if (s_clip == null) {
                    return;
                }
                if (s_source == null) {
                    var go = new GameObject("TTVStreamersKit NotifySound");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    s_source = go.AddComponent<AudioSource>();
                    s_source.spatialBlend = 0f;
                    s_source.playOnAwake = false;
                }
                s_source.PlayOneShot(s_clip, Mathf.Clamp01(volume));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: failed to play the notify sound: {e.Message}");
            }
        }

        private static AudioClip Load()
        {
            var path = Path.Combine(UnityGame.UserDataPath, "TTVStreamersKit", "request.wav");
            if (!File.Exists(path)) {
                Plugin.Log.Warn($"Requests: the notify sound {path} was not found.");
                return null;
            }
            var clip = FromWav(File.ReadAllBytes(path));
            if (clip == null) {
                Plugin.Log.Warn("Requests: the notify sound is not a supported WAV file.");
            }
            return clip;
        }

        private static AudioClip FromWav(byte[] data)
        {
            if (data.Length < 44 || ReadTag(data, 0) != "RIFF" || ReadTag(data, 8) != "WAVE") {
                return null;
            }
            int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
            var pos = 12;
            while (pos + 8 <= data.Length) {
                var tag = ReadTag(data, pos);
                var size = BitConverter.ToInt32(data, pos + 4);
                var body = pos + 8;
                if (tag == "fmt " && size >= 16) {
                    format = BitConverter.ToUInt16(data, body);
                    channels = BitConverter.ToUInt16(data, body + 2);
                    rate = BitConverter.ToInt32(data, body + 4);
                    bits = BitConverter.ToUInt16(data, body + 14);
                    if (format == 0xFFFE && size >= 26) {
                        // WAVE_FORMAT_EXTENSIBLE: サブフォーマットの先頭2バイトが実際の形式
                        format = BitConverter.ToUInt16(data, body + 24);
                    }
                }
                else if (tag == "data") {
                    dataStart = body;
                    dataLength = Math.Min(size, data.Length - body);
                    break;
                }
                pos = body + size + (size & 1);
            }
            if (dataStart < 0 || channels <= 0 || rate <= 0 || bits <= 0) {
                return null;
            }
            var bytesPerSample = bits / 8;
            var count = dataLength / bytesPerSample;
            var samples = new float[count];
            for (var i = 0; i < count; i++) {
                var p = dataStart + i * bytesPerSample;
                if (format == 3 && bits == 32) {
                    samples[i] = BitConverter.ToSingle(data, p);
                }
                else if (format == 1 && bits == 16) {
                    samples[i] = BitConverter.ToInt16(data, p) / 32768f;
                }
                else if (format == 1 && bits == 24) {
                    samples[i] = ((data[p] << 8 | data[p + 1] << 16 | data[p + 2] << 24) >> 8) / 8388608f;
                }
                else if (format == 1 && bits == 32) {
                    samples[i] = BitConverter.ToInt32(data, p) / 2147483648f;
                }
                else if (format == 1 && bits == 8) {
                    samples[i] = (data[p] - 128) / 128f;
                }
                else {
                    return null;
                }
            }
            // SetData は使わない。Beat Saber 1.44.1 の Unity では ReadOnlySpan の多重定義があり、.NET Framework 4.8 の参照ではコンパイルできない
            var position = 0;
            return AudioClip.Create("TTVStreamersKit Notify", count / channels, channels, rate, false, buffer => {
                var length = Math.Min(buffer.Length, samples.Length - position);
                if (length > 0) {
                    Array.Copy(samples, position, buffer, 0, length);
                    position += length;
                }
                for (var i = Math.Max(length, 0); i < buffer.Length; i++) {
                    buffer[i] = 0f;
                }
            });
        }

        private static string ReadTag(byte[] data, int offset) =>
            offset + 4 <= data.Length ? System.Text.Encoding.ASCII.GetString(data, offset, 4) : "";
    }
}
