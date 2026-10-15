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

    public async Task RunAsync(KeybindSettings settings, KeybindEntry entry, CancellationToken cancellationToken)
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

                // 1) press T
                await _input.KeyPressAsync((ushort)settings.ChatOpenVirtualKey);
                await Task.Delay(Math.Max(0, settings.DelayAfterChatOpenMs), cancellationToken);

                // 2) type text
                await _input.TextAsync(line);
                await Task.Delay(Math.Max(0, settings.DelayAfterTextMs), cancellationToken);

                // press Enter
                await _input.KeyPressAsync(0x0D);
                await Task.Delay(Math.Max(0, settings.DelayAfterEnterMs), cancellationToken);

                // 3) wait 3 sec (or configured) between lines
                if (i < lines.Count - 1)
                {
                    await Task.Delay(Math.Max(0, settings.DelayMs), cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
