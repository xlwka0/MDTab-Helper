using System;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using MDHelper.Models;

namespace MDHelper.Services;

/// <summary>
/// Lightweight background telemetry service.
/// Sends an anonymized ping every 3 minutes to keep the Discord Live Dashboard updated.
/// Zero personal data is transmitted (only an anonymized local random GUID and app version).
/// </summary>
public class TelemetryService : IDisposable
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly KeybindSettings _settings;
    private readonly Action _saveConfigCallback;
    private DispatcherTimer? _heartbeatTimer;
    private bool _disposed = false;

    // Fallback/Default Worker URL (can be customized via settings/config.json)
    public const string DefaultTelemetryUrl = "https://mdtab-telemetry.xlwka.workers.dev";

    public TelemetryService(KeybindSettings settings, Action saveConfigCallback)
    {
        _settings = settings;
        _saveConfigCallback = saveConfigCallback;
        EnsureInstallationId();
    }

    private void EnsureInstallationId()
    {
        if (string.IsNullOrWhiteSpace(_settings.TelemetryInstallationId))
        {
            _settings.TelemetryInstallationId = Guid.NewGuid().ToString("N")[..16];
            _saveConfigCallback?.Invoke();
        }
    }

    public void Start()
    {
        if (_disposed) return;

        // Initial ping after 5 seconds of launch
        var initialTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        initialTimer.Tick += (s, e) =>
        {
            initialTimer.Stop();
            _ = SendPingAsync(isClosing: false);
        };
        initialTimer.Start();

        // Recurring heartbeat every 3 minutes
        _heartbeatTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(3)
        };
        _heartbeatTimer.Tick += (s, e) => _ = SendPingAsync(isClosing: false);
        _heartbeatTimer.Start();
    }

    public async Task SendPingAsync(bool isClosing = false)
    {
        string endpoint = !string.IsNullOrWhiteSpace(_settings.TelemetryEndpoint)
            ? _settings.TelemetryEndpoint.Trim()
            : DefaultTelemetryUrl;

        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!endpoint.EndsWith("/api/ping", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = endpoint.TrimEnd('/') + "/api/ping";
        }

        try
        {
            var payload = new
            {
                id = _settings.TelemetryInstallationId,
                version = AppConstants.DisplayVersion,
                closing = isClosing
            };

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync(endpoint, content).ConfigureAwait(false);
        }
        catch
        {
            // Silently ignore network or telemetry unreachable errors
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatTimer?.Stop();
        _heartbeatTimer = null;

        // Send farewell ping non-blocking with 1-second deadline
        try
        {
            Task.Run(() => SendPingAsync(isClosing: true)).Wait(1000);
        }
        catch { }
    }
}
