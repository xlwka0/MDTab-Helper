using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MDHelper.Models;

namespace MDHelper.Services;

public sealed class ChatSequenceService
{
    private readonly SendInputService _input;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChatSequenceService(SendInputService input)
    {
        _input = input;
    }

    public Task RunAsync(KeybindSettings settings, KeybindEntry entry, CancellationToken cancellationToken)
    {
        return RunAsync(settings, entry, null, cancellationToken);
    }

    public async Task RunAsync(
        KeybindSettings settings,
        KeybindEntry entry,
        Func<bool>? isGameFocused,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _input.ReleaseModifiers();

            var lines = entry.Script
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                cancellationToken.ThrowIfCancellationRequested();
                if (isGameFocused != null && !isGameFocused())
                {
                    throw new OperationCanceledException("Fereastra jocului nu mai este activă. Comanda a fost oprită.");
                }

                // 1) Press Chat Open Key (default: T)
                await _input.KeyPressAsync((ushort)settings.ChatOpenVirtualKey);
                await DelayWithCheckAsync(Math.Max(0, settings.DelayAfterChatOpenMs), isGameFocused, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (isGameFocused != null && !isGameFocused())
                {
                    throw new OperationCanceledException("Fereastra jocului nu mai este activă. Comanda a fost oprită.");
                }

                // 2) Type text line
                await _input.TextAsync(line);
                await DelayWithCheckAsync(Math.Max(0, settings.DelayAfterTextMs), isGameFocused, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (isGameFocused != null && !isGameFocused())
                {
                    throw new OperationCanceledException("Fereastra jocului nu mai este activă. Comanda a fost oprită.");
                }

                // 3) Press Enter
                await _input.KeyPressAsync(0x0D);
                await DelayWithCheckAsync(Math.Max(0, settings.DelayAfterEnterMs), isGameFocused, cancellationToken);

                // 4) Wait between lines if more lines remain
                if (i < lines.Count - 1)
                {
                    await DelayWithCheckAsync(Math.Max(0, settings.DelayMs), isGameFocused, cancellationToken);
                }
            }
        }
        finally
        {
            _input.ReleaseModifiers();
            _gate.Release();
        }
    }

    private static async Task DelayWithCheckAsync(int delayMs, Func<bool>? isGameFocused, CancellationToken cancellationToken)
    {
        if (delayMs <= 0) return;

        int remaining = delayMs;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isGameFocused != null && !isGameFocused())
            {
                throw new OperationCanceledException("Fereastra jocului nu mai este activă. Comanda a fost oprită.");
            }

            int step = Math.Min(remaining, 35);
            await Task.Delay(step, cancellationToken);
            remaining -= step;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (isGameFocused != null && !isGameFocused())
        {
            throw new OperationCanceledException("Fereastra jocului nu mai este activă. Comanda a fost oprită.");
        }
    }
}
