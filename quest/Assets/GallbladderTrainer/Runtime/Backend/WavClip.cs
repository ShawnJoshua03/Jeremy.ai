using System;
using UnityEngine;

namespace GallbladderTrainer.Backend
{
    /// <summary>
    /// Decodes a RIFF/WAVE body (PCM 16-bit or IEEE float 32-bit) into an AudioClip in memory, so the response body
    /// stays readable for 503 fallbacks and no temp files are written on the headset.
    /// </summary>
    public static class WavClip
    {
        public static AudioClip Create(byte[] d, string name)
        {
            if (d == null || d.Length < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F'
                || d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E') return null;
            int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLen = 0;
            int p = 12;
            while (p + 8 <= d.Length)
            {
                string id = new string(new[] { (char)d[p], (char)d[p + 1], (char)d[p + 2], (char)d[p + 3] });
                int len = BitConverter.ToInt32(d, p + 4);
                int body = p + 8;
                if (id == "fmt " && body + 16 <= d.Length)
                {
                    format = BitConverter.ToInt16(d, body);
                    channels = BitConverter.ToInt16(d, body + 2);
                    rate = BitConverter.ToInt32(d, body + 4);
                    bits = BitConverter.ToInt16(d, body + 14);
                    if (format == -2 && len >= 26) format = BitConverter.ToInt16(d, body + 24); // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    dataStart = body;
                    // Streaming encoders may write 0 or 0xFFFFFFFF as the length; take the rest of the buffer.
                    dataLen = len <= 0 || body + len > d.Length ? d.Length - body : len;
                    break;
                }
                if (len < 0) break;
                p = body + len + (len & 1);
            }
            if (dataStart < 0 || channels <= 0 || rate <= 0) return null;

            float[] samples;
            if (format == 1 && bits == 16)
            {
                int n = dataLen / 2;
                samples = new float[n];
                for (int i = 0; i < n; i++) samples[i] = BitConverter.ToInt16(d, dataStart + i * 2) / 32768f;
            }
            else if (format == 3 && bits == 32)
            {
                int n = dataLen / 4;
                samples = new float[n];
                for (int i = 0; i < n; i++) samples[i] = BitConverter.ToSingle(d, dataStart + i * 4);
            }
            else return null;

            int frames = samples.Length / channels;
            if (frames <= 0) return null;
            if (samples.Length != frames * channels) Array.Resize(ref samples, frames * channels);
            var clip = AudioClip.Create(name, frames, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
