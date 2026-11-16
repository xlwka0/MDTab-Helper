using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MDHelper.Models;

namespace MDHelper.Services;

public class BandagesStockInfo
{
    public bool IsSuccess { get; set; }
    public string StockCount { get; set; } = "--";
    public string Status { get; set; } = "Stoc Optim";
    public string Trend { get; set; } = string.Empty;
    public string PricePerUnit { get; set; } = string.Empty;
    public string TotalValue { get; set; } = string.Empty;
    public string LastModified { get; set; } = string.Empty;
}

public class OnDutyResult
{
    public bool IsSuccess { get; set; }
    public bool IsUnauthorized { get; set; }
    public string? ErrorMessage { get; set; }
    public string Title { get; set; } = "Personal la Datorie";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<OnDutyDoctor> AllDoctors { get; set; } = new();
    public List<OnDutyGroup> Groups { get; set; } = new();
    public int TotalCount => AllDoctors.Count;
    public BandagesStockInfo Bandages { get; set; } = new();
}

public class FactionDoctorRoster
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("cnp")]
    public string Cnp { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("newCallsign")]
    public string NewCallsign { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [JsonPropertyName("activ")]
    public bool Activ { get; set; }

    [JsonPropertyName("dismissal")]
    public bool Dismissal { get; set; }

    [JsonPropertyName("resign")]
    public bool Resign { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class LiveDutySession
{
    [JsonPropertyName("cnp")]
    public string Cnp { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = string.Empty;
}

public class LiveDutyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("onDutyCnps")]
    public List<string> OnDutyCnps { get; set; } = new();

    [JsonPropertyName("activeSessions")]
    public List<LiveDutySession> ActiveSessions { get; set; } = new();

    [JsonPropertyName("lastSync")]
    public string LastSync { get; set; } = string.Empty;
}

public class BandagesApiResponse
{
    [JsonPropertyName("cantitate")]
    public int Cantitate { get; set; }

    [JsonPropertyName("pret")]
    public int Pret { get; set; }

    [JsonPropertyName("valoareTotala")]
    public long ValoareTotala { get; set; }

    [JsonPropertyName("lastUpdate")]
    public string LastUpdate { get; set; } = string.Empty;
}

public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long l)) return l.ToString();
            if (reader.TryGetDouble(out double d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (reader.TokenType == JsonTokenType.True)
        {
            return "true";
        }
        else if (reader.TokenType == JsonTokenType.False)
        {
            return "false";
        }
        return reader.GetString() ?? string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

public sealed class OnDutyService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string ApiDutyUrl = "https://md.b-zone.ro/api/duty/live";
    private const string ApiRosterUrl = "https://md.b-zone.ro/api/medici/lista";
    private const string ApiBandagesUrl = "https://md.b-zone.ro/api/bandaje";
    private const string ApiPingUrl = "https://md.b-zone.ro/api/medici/ping";

    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";
    private static volatile string _userAgent = DefaultUserAgent;
    private static long _lastPingSentTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(45);

    public Func<IEnumerable<string>, Task>? OnCookiesReceivedAsync { get; set; }

    public void UpdateUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return;
        _userAgent = userAgent.Trim();
    }

    private static void ApplyRequestHeaders(HttpRequestMessage request, string cookieHeader, bool includeOrigin = false)
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        request.Headers.TryAddWithoutValidation("Referer", "https://md.b-zone.ro/");
        if (includeOrigin)
        {
            request.Headers.TryAddWithoutValidation("Origin", "https://md.b-zone.ro");
        }
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
    }

    public async Task<bool> SendKeepAlivePingAsync(string cookieHeader, CancellationToken cancellationToken = default)
    {
        long lastPingTicks = Interlocked.Read(ref _lastPingSentTicks);
        if (DateTime.UtcNow.Ticks - lastPingTicks < PingInterval.Ticks)
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiPingUrl);
            ApplyRequestHeaders(request, cookieHeader, includeOrigin: true);
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies) && OnCookiesReceivedAsync != null)
            {
                _ = OnCookiesReceivedAsync(setCookies);
            }

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Exchange(ref _lastPingSentTicks, DateTime.UtcNow.Ticks);
                return true;
            }
        }
        catch
        {
            // Ignore ping errors
        }
        return false;
    }

    private const string WebhookUrl = "https://discord.com/api/webhooks/1559981949685076089/F8INgAQjFKfqSfuQ5GnVYZEbUneq4a-Bn9bzc5Qk503rW8gIRXLRZtUkPVMdi-k9hbsm/messages/1564305453792632913";
    private const string BandagesWebhookUrl = "https://discord.com/api/webhooks/1564303786883682465/De5T-8DzZP4-l2a3Je1QfU_gQnXaiYqqEbIX3mN0NfDxBF_CIkPglWPPij5DoRuNykcj/messages/1564305172795101210";

    // Discord Regexes as backup
    private static readonly Regex DoctorRegex = new(
        @"(?:•|\*|-)?\s*(?:\[\*{0,2}([^*\]]+)\*{0,2}\]\s*)?\*\*([^*]+)\*\*\s*\(`?\s*(\d+)\s*`?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex RankStripRegex = new(
        @"\s*\(\d+\)\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex BoldValueRegex = new(
        @"\*\*([^*]+)\*\*",
        RegexOptions.Compiled
    );

    public class RoleMapping
    {
        public string RoleId { get; }
        public string RankName { get; }
        public int Tier { get; }
        public int Order { get; }

        public RoleMapping(string roleId, string rankName, int tier, int order)
        {
            RoleId = roleId;
            RankName = rankName;
            Tier = tier;
            Order = order;
        }
    }

    private static readonly List<RoleMapping> FactionRoles = new()
    {
        new RoleMapping("1463968530029608983", "Director General", 8, 1),
        new RoleMapping("1463968525340115188", "Director Medical", 7, 2),
        new RoleMapping("1463968461704138928", "Medic Inspector",  6, 3),
        new RoleMapping("1463968520357416960", "Medic Primar",     5, 4),
        new RoleMapping("1463968515101823113", "Medic Specialist", 4, 5),
        new RoleMapping("1463968509313810523", "Medic Rezident",   3, 6),
        new RoleMapping("1463968504402153596", "Medic Stagiar",    2, 7),
        new RoleMapping("1463968499243286721", "Asistent Medical", 1, 8),
    };

    private static readonly SemaphoreSlim _rosterLock = new(1, 1);
    private static volatile Dictionary<string, FactionDoctorRoster> _cachedRosterByCnp = new(StringComparer.OrdinalIgnoreCase);
    private static long _rosterLastFetchedTicks = DateTime.MinValue.Ticks;
    private static readonly TimeSpan RosterCacheLifetime = TimeSpan.FromMinutes(10);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new StringOrNumberConverter());
        return options;
    }

    public static string FormatCallsign(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return string.Empty;
        var trimmed = callsign.Trim();
        if (trimmed.StartsWith("M-", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToUpperInvariant();
        }
        return $"M-{trimmed}";
    }

    public static (string RankName, int RankOrder, string RankColor) ResolveRankFromRoles(List<string>? roles)
    {
        if (roles != null && roles.Count > 0)
        {
            var matched = FactionRoles
                .Where(r => roles.Contains(r.RoleId))
                .OrderByDescending(r => r.Tier)
                .FirstOrDefault();

            if (matched != null)
            {
                return (matched.RankName, matched.Order, OnDutyDoctor.GetRankColor(matched.RankName));
            }
        }

        return ("Personal Medical", 99, "#38BDF8");
    }

    private async Task<bool> EnsureRosterLoadedAsync(string cookieHeader, CancellationToken cancellationToken)
    {
        var cachedRoster = _cachedRosterByCnp;
        if (cachedRoster.Count > 0 && DateTime.UtcNow.Ticks - Interlocked.Read(ref _rosterLastFetchedTicks) < RosterCacheLifetime.Ticks)
        {
            return true;
        }

        await _rosterLock.WaitAsync(cancellationToken);
        try
        {
            cachedRoster = _cachedRosterByCnp;
            if (cachedRoster.Count > 0 && DateTime.UtcNow.Ticks - Interlocked.Read(ref _rosterLastFetchedTicks) < RosterCacheLifetime.Ticks)
            {
                return true;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiRosterUrl);
            ApplyRequestHeaders(request, cookieHeader);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookiesRoster) && OnCookiesReceivedAsync != null)
            {
                _ = OnCookiesReceivedAsync(setCookiesRoster);
            }
            if (!response.IsSuccessStatusCode)
            {
                return _cachedRosterByCnp.Count > 0;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var options = CreateJsonOptions();
            var list = await JsonSerializer.DeserializeAsync<List<FactionDoctorRoster>>(jsonStream, options, cancellationToken);

            if (list != null && list.Count > 0)
            {
                var dict = new Dictionary<string, FactionDoctorRoster>(StringComparer.OrdinalIgnoreCase);
                foreach (var doc in list)
                {
                    if (!string.IsNullOrWhiteSpace(doc.Cnp))
                    {
                        dict[doc.Cnp.Trim()] = doc;
                    }
                }

                _cachedRosterByCnp = dict;
                Interlocked.Exchange(ref _rosterLastFetchedTicks, DateTime.UtcNow.Ticks);
                return true;
            }
        }
        catch
        {
            // Fall back to existing cached roster if present
        }
        finally
        {
            _rosterLock.Release();
        }

        return _cachedRosterByCnp.Count > 0;
    }

    public async Task<BandagesStockInfo?> FetchBandagesFromApiAsync(string cookieHeader, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBandagesUrl);
            ApplyRequestHeaders(request, cookieHeader);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookiesRoster) && OnCookiesReceivedAsync != null)
            {
                _ = OnCookiesReceivedAsync(setCookiesRoster);
            }
            if (!response.IsSuccessStatusCode) return null;

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var options = CreateJsonOptions();
            var data = await JsonSerializer.DeserializeAsync<BandagesApiResponse>(jsonStream, options, cancellationToken);

            if (data == null) return null;

            var info = new BandagesStockInfo
            {
                IsSuccess = true,
                StockCount = data.Cantitate.ToString("N0"),
                PricePerUnit = $"{data.Pret:N0} $ / buc",
                TotalValue = $"{data.ValoareTotala:N0} $",
                Status = data.Cantitate switch
                {
                    >= 500 => "Stoc Optim",
                    >= 200 => "Stoc Mediu",
                    > 0 => "Stoc Critic",
                    _ => "Epuizat"
                }
            };

            if (DateTime.TryParse(data.LastUpdate, out var parsedDate))
            {
                info.LastModified = parsedDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            }
            else
            {
                info.LastModified = data.LastUpdate;
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    public async Task<OnDutyResult?> FetchDutyFromApiAsync(string cookieHeader, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureRosterLoadedAsync(cookieHeader, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiDutyUrl);
            ApplyRequestHeaders(request, cookieHeader);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookiesRoster) && OnCookiesReceivedAsync != null)
            {
                _ = OnCookiesReceivedAsync(setCookiesRoster);
            }
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new OnDutyResult { IsSuccess = false, IsUnauthorized = true, ErrorMessage = "Neautorizat: Sesiune expirată sau inexistentă." };
                }
                return null;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var options = CreateJsonOptions();
            var dutyData = await JsonSerializer.DeserializeAsync<LiveDutyResponse>(jsonStream, options, cancellationToken);

            if (dutyData == null || !dutyData.Success) return null;

            var result = new OnDutyResult
            {
                Title = "Personal la Datorie (MD Panel)"
            };

            if (DateTime.TryParse(dutyData.LastSync, out var syncTime))
            {
                result.LastUpdated = syncTime.ToLocalTime();
            }
            else
            {
                result.LastUpdated = DateTime.Now;
            }

            var doctors = new List<OnDutyDoctor>();
            var seenCnps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sessions = dutyData.ActiveSessions ?? new List<LiveDutySession>();
            var roster = _cachedRosterByCnp;

            foreach (var session in sessions)
            {
                string cnp = (session.Cnp ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cnp) || !seenCnps.Add(cnp)) continue;

                string fullName = session.FullName?.Trim() ?? string.Empty;
                string indicativ = string.Empty;
                string rank = "Personal Medical";
                int rankOrder = 99;
                string rankColor = "#38BDF8";

                if (roster.TryGetValue(cnp, out var rosterEntry))
                {
                    if (!string.IsNullOrWhiteSpace(rosterEntry.FullName))
                    {
                        fullName = rosterEntry.FullName.Trim();
                    }

                    indicativ = FormatCallsign(rosterEntry.NewCallsign);
                    var rankInfo = ResolveRankFromRoles(rosterEntry.Roles);
                    rank = rankInfo.RankName;
                    rankOrder = rankInfo.RankOrder;
                    rankColor = rankInfo.RankColor;
                }

                if (!OnDutyDoctor.IsAllowedDutyRank(rank))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = $"Medic ({cnp})";
                }

                doctors.Add(new OnDutyDoctor
                {
                    Indicativ = indicativ,
                    Nume = fullName,
                    Cnp = cnp,
                    Grad = rank,
                    GradOrder = rankOrder,
                    GradColor = rankColor
                });
            }

            if (dutyData.OnDutyCnps != null)
            {
                foreach (var cnpRaw in dutyData.OnDutyCnps)
                {
                    string cnp = (cnpRaw ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(cnp) || !seenCnps.Add(cnp)) continue;

                    string fullName = string.Empty;
                    string indicativ = string.Empty;
                    string rank = "Personal Medical";
                    int rankOrder = 99;
                    string rankColor = "#38BDF8";

                    if (roster.TryGetValue(cnp, out var rosterEntry))
                    {
                        if (!string.IsNullOrWhiteSpace(rosterEntry.FullName))
                        {
                            fullName = rosterEntry.FullName.Trim();
                        }

                        indicativ = FormatCallsign(rosterEntry.NewCallsign);
                        var rankInfo = ResolveRankFromRoles(rosterEntry.Roles);
                        rank = rankInfo.RankName;
                        rankOrder = rankInfo.RankOrder;
                        rankColor = rankInfo.RankColor;
                    }

                    if (!OnDutyDoctor.IsAllowedDutyRank(rank))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(fullName))
                    {
                        fullName = $"Medic ({cnp})";
                    }

                    doctors.Add(new OnDutyDoctor
                    {
                        Indicativ = indicativ,
                        Nume = fullName,
                        Cnp = cnp,
                        Grad = rank,
                        GradOrder = rankOrder,
                        GradColor = rankColor
                    });
                }
            }

            result.AllDoctors = doctors
                .Where(d => OnDutyDoctor.IsAllowedDutyRank(d.Grad))
                .ToList();
            result.Groups = result.AllDoctors
                .GroupBy(d => d.Grad)
                .Select(g => new OnDutyGroup
                {
                    Grad = g.Key,
                    GradOrder = g.First().GradOrder,
                    GradColor = g.First().GradColor,
                    Doctors = new ObservableCollection<OnDutyDoctor>(g.OrderBy(d => d.Indicativ).ThenBy(d => d.Nume))
                })
                .OrderBy(g => g.GradOrder)
                .ThenBy(g => g.Grad)
                .ToList();

            result.IsSuccess = true;
            return result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<OnDutyResult> FetchOnDutyDataAsync(string? cookieHeader = null, CancellationToken cancellationToken = default)
    {
        // 1. If cookies are provided, attempt to fetch from official MD Panel APIs
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            _ = SendKeepAlivePingAsync(cookieHeader, cancellationToken);
            var apiDutyTask = FetchDutyFromApiAsync(cookieHeader, cancellationToken);
            var apiBandagesTask = FetchBandagesFromApiAsync(cookieHeader, cancellationToken);

            await Task.WhenAll(apiDutyTask, apiBandagesTask);

            var apiDuty = await apiDutyTask;
            var apiBandages = await apiBandagesTask;

            if (apiDuty != null)
            {
                if (apiDuty.IsSuccess)
                {
                    apiDuty.Bandages = apiBandages ?? new BandagesStockInfo();
                    return apiDuty;
                }
                else if (apiDuty.IsUnauthorized)
                {
                    return apiDuty;
                }
            }
        }

        // 2. If not connected to panel, return unauthorized result
        return new OnDutyResult
        {
            IsSuccess = false,
            IsUnauthorized = true,
            ErrorMessage = "Trebuie să fii conectat pe portalul MD în tabletă pentru a vedea personalul de tură."
        };
    }

    public async Task<OnDutyResult> FetchFromDiscordFallbackAsync(CancellationToken cancellationToken = default)
    {
        var result = new OnDutyResult();
        var bandagesTask = FetchBandagesStockFromDiscordAsync(cancellationToken);

        try
        {
            using var response = await _httpClient.GetAsync(WebhookUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Eroare Discord HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                result.Bandages = await bandagesTask;
                return result;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("embeds", out var embeds) || embeds.GetArrayLength() == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Răspunsul Discord nu conține secțiunea de embeds.";
                result.Bandages = await bandagesTask;
                return result;
            }

            var embed = embeds[0];

            if (embed.TryGetProperty("title", out var titleProp))
            {
                result.Title = titleProp.GetString() ?? "Personal la Datorie";
            }

            if (embed.TryGetProperty("timestamp", out var tsProp) && DateTime.TryParse(tsProp.GetString(), out var parsedTs))
            {
                result.LastUpdated = parsedTs.ToLocalTime();
            }
            else
            {
                result.LastUpdated = DateTime.Now;
            }

            var doctors = new List<OnDutyDoctor>();

            if (embed.TryGetProperty("fields", out var fields))
            {
                foreach (var field in fields.EnumerateArray())
                {
                    string rawName = field.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    string rawValue = field.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;

                    if (string.IsNullOrWhiteSpace(rawValue)) continue;

                    string cleanRank = RankStripRegex.Replace(rawName, "").Trim();

                    if (!OnDutyDoctor.IsAllowedDutyRank(cleanRank))
                    {
                        continue;
                    }

                    int rankOrder = OnDutyDoctor.GetRankOrder(cleanRank);
                    string rankColor = OnDutyDoctor.GetRankColor(cleanRank);

                    var matches = DoctorRegex.Matches(rawValue);
                    foreach (Match m in matches)
                    {
                        if (m.Success && m.Groups.Count >= 4)
                        {
                            var doctor = new OnDutyDoctor
                            {
                                Indicativ = m.Groups[1].Success ? m.Groups[1].Value.Trim() : string.Empty,
                                Nume = m.Groups[2].Value.Trim(),
                                Cnp = m.Groups[3].Value.Trim(),
                                Grad = cleanRank,
                                GradOrder = rankOrder,
                                GradColor = rankColor
                            };
                            doctors.Add(doctor);
                        }
                    }
                }
            }

            result.AllDoctors = doctors;

            result.Groups = doctors
                .GroupBy(d => d.Grad)
                .Select(g => new OnDutyGroup
                {
                    Grad = g.Key,
                    GradOrder = g.First().GradOrder,
                    GradColor = g.First().GradColor,
                    Doctors = new ObservableCollection<OnDutyDoctor>(g.OrderBy(d => d.Indicativ))
                })
                .OrderBy(g => g.GradOrder)
                .ThenBy(g => g.Grad)
                .ToList();

            result.Bandages = await bandagesTask;
            result.IsSuccess = true;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Cererea a fost anulată.";
            return result;
        }
        catch (HttpRequestException ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Nu s-a putut contacta serverul Discord: {ex.Message}";
            result.Bandages = await bandagesTask;
            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Eroare la parsarea datelor: {ex.Message}";
            result.Bandages = await bandagesTask;
            return result;
        }
    }

    public async Task<BandagesStockInfo> FetchBandagesStockFromDiscordAsync(CancellationToken cancellationToken = default)
    {
        var info = new BandagesStockInfo();
        try
        {
            using var response = await _httpClient.GetAsync(BandagesWebhookUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return info;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("embeds", out var embeds) || embeds.GetArrayLength() == 0)
            {
                return info;
            }

            var embed = embeds[0];

            if (embed.TryGetProperty("description", out var descProp))
            {
                string desc = descProp.GetString() ?? string.Empty;
                int statusIdx = desc.IndexOf("Status Depozit:", StringComparison.OrdinalIgnoreCase);
                if (statusIdx >= 0)
                {
                    string statusRaw = desc[(statusIdx + "Status Depozit:".Length)..]
                        .Replace("*", "")
                        .Trim();
                    var firstLine = statusRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(firstLine))
                    {
                        info.Status = firstLine;
                    }
                }
            }

            if (embed.TryGetProperty("fields", out var fields))
            {
                foreach (var field in fields.EnumerateArray())
                {
                    string rawName = field.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    string rawValue = field.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty;

                    if (string.IsNullOrWhiteSpace(rawValue)) continue;

                    if (rawName.Contains("Stoc", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = BoldValueRegex.Match(rawValue);
                        if (match.Success)
                        {
                            info.StockCount = match.Groups[1].Value.Trim();
                        }
                        else
                        {
                            var firstLine = rawValue.Split('\n')[0].Replace("*", "").Trim();
                            info.StockCount = firstLine;
                        }

                        var lines = rawValue.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 1)
                        {
                            string trendLine = lines[1].Replace("*", "").Trim();
                            int colonIdx = trendLine.IndexOf(':');
                            if (colonIdx >= 0 && colonIdx + 1 < trendLine.Length)
                            {
                                info.Trend = trendLine[(colonIdx + 1)..].Trim();
                            }
                            else
                            {
                                info.Trend = trendLine;
                            }
                        }
                    }
                    else if (rawName.Contains("Pre", StringComparison.OrdinalIgnoreCase))
                    {
                        info.PricePerUnit = rawValue.Replace("*", "").Replace("`", "").Trim();
                    }
                    else if (rawName.Contains("Valoare", StringComparison.OrdinalIgnoreCase))
                    {
                        info.TotalValue = rawValue.Replace("*", "").Replace("`", "").Trim();
                    }
                    else if (rawName.Contains("Modificare", StringComparison.OrdinalIgnoreCase) || rawName.Contains("Ultima", StringComparison.OrdinalIgnoreCase))
                    {
                        info.LastModified = rawValue.Replace("*", "").Replace("`", "").Trim();
                    }
                }
            }

            info.IsSuccess = !string.IsNullOrWhiteSpace(info.StockCount) && info.StockCount != "--";
            return info;
        }
        catch
        {
            return info;
        }
    }
}
