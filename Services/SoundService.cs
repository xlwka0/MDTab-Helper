using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace MDHelper.Services;

public static class SoundService
{
    public static bool Enabled { get; set; } = true;

    private static void PlayTone(double frequency, int durationMs, double volume = 0.3)
    {
        if (!Enabled) return;

        Task.Run(() =>
        {
            try
            {
                int sampleRate = 44100;
                int numSamples = sampleRate * durationMs / 1000;
                byte[] pcmData = new byte[numSamples * 2]; // 16-bit mono

                for (int i = 0; i < numSamples; i++)
                {
                    double t = (double)i / sampleRate;
                    
                    // Base tone: pure sine wave for warmest sound
                    double sample = Math.Sin(2 * Math.PI * frequency * t);

                    // Add a tiny second harmonic for richer analog texture
                    sample = (sample + 0.15 * Math.Sin(4 * Math.PI * frequency * t)) / 1.15;

                    // Envelope: smooth fade-in (15ms) and fade-out (30ms) to prevent clicks
                    double envelope = 1.0;
                    int attackSamples = sampleRate * 15 / 1000;
                    int decaySamples = sampleRate * 30 / 1000;

                    if (i < attackSamples)
                    {
                        envelope = (double)i / attackSamples;
                    }
                    else if (i > numSamples - decaySamples)
                    {
                        envelope = (double)(numSamples - i) / decaySamples;
                    }

                    short val = (short)(sample * volume * envelope * short.MaxValue);
                    pcmData[i * 2] = (byte)(val & 0xFF);
                    pcmData[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
                }

                // RIFF-WAVE header (44 bytes)
                byte[] wavHeader = new byte[44];
                int totalDataLen = pcmData.Length;
                int totalWriteLen = totalDataLen + 36;

                wavHeader[0] = 0x52; wavHeader[1] = 0x49; wavHeader[2] = 0x46; wavHeader[3] = 0x46; // RIFF
                wavHeader[4] = (byte)(totalWriteLen & 0xff);
                wavHeader[5] = (byte)((totalWriteLen >> 8) & 0xff);
                wavHeader[6] = (byte)((totalWriteLen >> 16) & 0xff);
                wavHeader[7] = (byte)((totalWriteLen >> 24) & 0xff);
                wavHeader[8] = 0x57; wavHeader[9] = 0x41; wavHeader[10] = 0x56; wavHeader[11] = 0x45; // WAVE
                wavHeader[12] = 0x66; wavHeader[13] = 0x6d; wavHeader[14] = 0x74; wavHeader[15] = 0x20; // fmt
                wavHeader[16] = 16; wavHeader[17] = 0; wavHeader[18] = 0; wavHeader[19] = 0; // Subchunk1Size (16)
                wavHeader[20] = 1; wavHeader[21] = 0; // AudioFormat = 1 (PCM)
                wavHeader[22] = 1; wavHeader[23] = 0; // NumChannels = 1 (Mono)
                wavHeader[24] = (byte)(sampleRate & 0xff);
                wavHeader[25] = (byte)((sampleRate >> 8) & 0xff);
                wavHeader[26] = (byte)((sampleRate >> 16) & 0xff);
                wavHeader[27] = (byte)((sampleRate >> 24) & 0xff);
                int byteRate = sampleRate * 2;
                wavHeader[28] = (byte)(byteRate & 0xff);
                wavHeader[29] = (byte)((byteRate >> 8) & 0xff);
                wavHeader[30] = (byte)((byteRate >> 16) & 0xff);
                wavHeader[31] = (byte)((byteRate >> 24) & 0xff);
                wavHeader[32] = 2; wavHeader[33] = 0; // BlockAlign (2)
                wavHeader[34] = 16; wavHeader[35] = 0; // BitsPerSample (16)
                wavHeader[36] = 0x64; wavHeader[37] = 0x61; wavHeader[38] = 0x74; wavHeader[39] = 0x61; // data
                wavHeader[40] = (byte)(totalDataLen & 0xff);
                wavHeader[41] = (byte)((totalDataLen >> 8) & 0xff);
                wavHeader[42] = (byte)((totalDataLen >> 16) & 0xff);
                wavHeader[43] = (byte)((totalDataLen >> 24) & 0xff);

                byte[] wavBytes = new byte[44 + pcmData.Length];
                Buffer.BlockCopy(wavHeader, 0, wavBytes, 0, 44);
                Buffer.BlockCopy(pcmData, 0, wavBytes, 44, pcmData.Length);

                using (var ms = new MemoryStream(wavBytes))
                using (var player = new SoundPlayer(ms))
                {
                    player.PlaySync();
                }
            }
            catch
            {
                // Ignore audio play errors
            }
        });
    }

    public static void PlayOverlayShow()
    {
        // Gentle rising major third chime
        PlayTone(523.25, 90, 0.2); // C5
        Task.Delay(85).ContinueWith(_ => PlayTone(659.25, 140, 0.2)); // E5
    }

    public static void PlayOverlayHide()
    {
        // Gentle falling major third chime
        PlayTone(659.25, 90, 0.25); // E5
        Task.Delay(85).ContinueWith(_ => PlayTone(523.25, 140, 0.25)); // C5
    }

    public static void PlayKeybindStart()
    {
        // Very short soft pop
        PlayTone(880.00, 40, 0.15); // A5
    }

    public static void PlayKeybindFinish()
    {
        // Playful double chime
        PlayTone(783.99, 70, 0.2); // G5
        Task.Delay(65).ContinueWith(_ => PlayTone(1046.50, 110, 0.2)); // C6
    }

    public static void PlayKeybindStop()
    {
        // Deep warm thud
        PlayTone(220.00, 150, 0.35); // A3
    }
}
