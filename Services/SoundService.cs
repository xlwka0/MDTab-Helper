using System;
using System.Runtime.InteropServices;

namespace MDHelper.Services;

public static class SoundService
{
    public static bool Enabled { get; set; } = true;

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    // Permanently pinned WAV buffers in memory for instant, zero-lag native WinMM playback
    private static readonly IntPtr OverlayShowPtr = CreatePinnedSmoothToneWav(startFreq: 523.25, endFreq: 659.25, durationMs: 135, volume: 0.20);
    private static readonly IntPtr OverlayHidePtr = CreatePinnedSmoothToneWav(startFreq: 659.25, endFreq: 493.88, durationMs: 135, volume: 0.22);
    private static readonly IntPtr KeybindStartPtr = CreatePinnedSmoothToneWav(startFreq: 880.00, endFreq: 880.00, durationMs: 45, volume: 0.15);
    private static readonly IntPtr KeybindFinishPtr = CreatePinnedSmoothToneWav(startFreq: 783.99, endFreq: 1046.50, durationMs: 120, volume: 0.20);
    private static readonly IntPtr KeybindStopPtr = CreatePinnedSmoothToneWav(startFreq: 240.00, endFreq: 190.00, durationMs: 150, volume: 0.32);

    private static long _lastOverlaySoundTicks = 0;

    /// <summary>
    /// Synthesizes a single continuous, uninterrupted tone (with optional smooth frequency glide)
    /// and pins it permanently in memory so winmm.dll PlaySound plays it with zero stutter or cutoffs.
    /// </summary>
    private static IntPtr CreatePinnedSmoothToneWav(double startFreq, double endFreq, int durationMs, double volume)
    {
        const int sampleRate = 44100;
        const int warmupMs = 25;   // Silent lead-in wakes up idle audio drivers smoothly
        const int tailMs = 35;     // Silent tail prevents driver cutoff at the end

        int warmupSamples = sampleRate * warmupMs / 1000;
        int toneSamples = sampleRate * durationMs / 1000;
        int tailSamples = sampleRate * tailMs / 1000;
        int totalSamples = warmupSamples + toneSamples + tailSamples;

        byte[] pcmData = new byte[totalSamples * 2];

        int attackSamples = Math.Max(1, sampleRate * 16 / 1000);
        int releaseSamples = Math.Max(1, toneSamples - attackSamples);

        double phase = 0.0;

        for (int i = 0; i < toneSamples; i++)
        {
            double progress = (double)i / toneSamples;

            // Smooth S-curve frequency transition (no abrupt step or gap in the middle)
            double smoothStep = progress * progress * (3.0 - 2.0 * progress);
            double currentFreq = startFreq + (endFreq - startFreq) * smoothStep;

            phase += 2.0 * Math.PI * currentFreq / sampleRate;

            // Warm sine wave + subtle second harmonic
            double sample = (Math.Sin(phase) + 0.12 * Math.Sin(2.0 * phase)) / 1.12;

            // Continuous bell envelope: smooth raised-cosine attack, smooth natural decay (never dips in the middle)
            double envelope;
            if (i < attackSamples)
            {
                double a = (double)i / attackSamples;
                envelope = 0.5 * (1.0 - Math.Cos(Math.PI * a));
            }
            else
            {
                double r = (double)(i - attackSamples) / releaseSamples;
                envelope = 0.5 * (1.0 + Math.Cos(Math.PI * r));
            }

            short val = (short)(Math.Clamp(sample * volume * envelope, -0.98, 0.98) * short.MaxValue);
            int pcmIdx = (warmupSamples + i) * 2;
            pcmData[pcmIdx] = (byte)(val & 0xFF);
            pcmData[pcmIdx + 1] = (byte)((val >> 8) & 0xFF);
        }

        byte[] wavBytes = new byte[44 + pcmData.Length];
        int totalDataLen = pcmData.Length;
        int totalWriteLen = totalDataLen + 36;

        wavBytes[0] = 0x52; wavBytes[1] = 0x49; wavBytes[2] = 0x46; wavBytes[3] = 0x46; // RIFF
        wavBytes[4] = (byte)(totalWriteLen & 0xff);
        wavBytes[5] = (byte)((totalWriteLen >> 8) & 0xff);
        wavBytes[6] = (byte)((totalWriteLen >> 16) & 0xff);
        wavBytes[7] = (byte)((totalWriteLen >> 24) & 0xff);
        wavBytes[8] = 0x57; wavBytes[9] = 0x41; wavBytes[10] = 0x56; wavBytes[11] = 0x45; // WAVE
        wavBytes[12] = 0x66; wavBytes[13] = 0x6d; wavBytes[14] = 0x74; wavBytes[15] = 0x20; // fmt
        wavBytes[16] = 16; wavBytes[17] = 0; wavBytes[18] = 0; wavBytes[19] = 0; // Subchunk1Size (16)
        wavBytes[20] = 1; wavBytes[21] = 0; // AudioFormat = 1 (PCM)
        wavBytes[22] = 1; wavBytes[23] = 0; // NumChannels = 1 (Mono)
        wavBytes[24] = (byte)(sampleRate & 0xff);
        wavBytes[25] = (byte)((sampleRate >> 8) & 0xff);
        wavBytes[26] = (byte)((sampleRate >> 16) & 0xff);
        wavBytes[27] = (byte)((sampleRate >> 24) & 0xff);
        int byteRate = sampleRate * 2;
        wavBytes[28] = (byte)(byteRate & 0xff);
        wavBytes[29] = (byte)((byteRate >> 8) & 0xff);
        wavBytes[30] = (byte)((byteRate >> 16) & 0xff);
        wavBytes[31] = (byte)((byteRate >> 24) & 0xff);
        wavBytes[32] = 2; wavBytes[33] = 0; // BlockAlign (2)
        wavBytes[34] = 16; wavBytes[35] = 0; // BitsPerSample (16)
        wavBytes[36] = 0x64; wavBytes[37] = 0x61; wavBytes[38] = 0x74; wavBytes[39] = 0x61; // data
        wavBytes[40] = (byte)(totalDataLen & 0xff);
        wavBytes[41] = (byte)((totalDataLen >> 8) & 0xff);
        wavBytes[42] = (byte)((totalDataLen >> 16) & 0xff);
        wavBytes[43] = (byte)((totalDataLen >> 24) & 0xff);

        Buffer.BlockCopy(pcmData, 0, wavBytes, 44, pcmData.Length);

        var handle = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }

    private static void PlayNative(IntPtr wavPtr)
    {
        if (!Enabled || wavPtr == IntPtr.Zero) return;
        try
        {
            PlaySound(wavPtr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch
        {
            // Ignore audio play errors
        }
    }

    public static void PlayOverlayShow()
    {
        long now = Environment.TickCount64;
        if (now - _lastOverlaySoundTicks < 60) return;
        _lastOverlaySoundTicks = now;
        PlayNative(OverlayShowPtr);
    }

    public static void PlayOverlayHide()
    {
        long now = Environment.TickCount64;
        if (now - _lastOverlaySoundTicks < 60) return;
        _lastOverlaySoundTicks = now;
        PlayNative(OverlayHidePtr);
    }

    public static void PlayKeybindStart()
    {
        PlayNative(KeybindStartPtr);
    }

    public static void PlayKeybindFinish()
    {
        PlayNative(KeybindFinishPtr);
    }

    public static void PlayKeybindStop()
    {
        PlayNative(KeybindStopPtr);
    }
}
