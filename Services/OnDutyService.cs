using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
    public string? ErrorMessage { get; set; }
    public string Title { get; set; } = "Personal la Datorie";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<OnDutyDoctor> AllDoctors { get; set; } = new();
    public List<OnDutyGroup> Groups { get; set; } = new();
    public int TotalCount => AllDoctors.Count;
    public BandagesStockInfo Bandages { get; set; } = new();
}

public sealed class OnDutyService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string WebhookUrl = "https://discord.com/api/webhooks/1559981949685076089/F8INgAQjFKfqSfuQ5GnVYZEbUneq4a-Bn9bzc5Qk503rW8gIRXLRZtUkPVMdi-k9hbsm/messages/1564305453792632913";
    private const string BandagesWebhookUrl = "https://discord.com/api/webhooks/1564303786883682465/De5T-8DzZP4-l2a3Je1QfU_gQnXaiYqqEbIX3mN0NfDxBF_CIkPglWPPij5DoRuNykcj/messages/1564305172795101210";

    // Regex to match lines like: "• [**M-207**] **Daria Power** (`24357`)" or "  [M-101] **Livius** (`25`)"
    private static readonly Regex DoctorRegex = new(
        @"(?:•|\*|-)?\s*(?:\[\*{0,2}([^*\]]+)\*{0,2}\]\s*)?\*\*([^*]+)\*\*\s*\(`?\s*(\d+)\s*`?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Regex to strip count "(3)" from field name like "Medici Specialiști (3)" or "Medici Primari (1)"
    private static readonly Regex RankStripRegex = new(
        @"\s*\(\d+\)\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex BoldValueRegex = new(
        @"\*\*([^*]+)\*\*",
        RegexOptions.Compiled
    );

    static OnDutyService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MDTab-Helper/2.6.1");
    }

    public async Task<OnDutyResult> FetchOnDutyDataAsync(CancellationToken cancellationToken = default)
    {
        var result = new OnDutyResult();
        var bandagesTask = FetchBandagesStockAsync(cancellationToken);

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

                    // Filter: strictly only medical staff from Asistent to Director
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

            // Group by rank and order
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

    public async Task<BandagesStockInfo> FetchBandagesStockAsync(CancellationToken cancellationToken = default)
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
