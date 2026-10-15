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

public class OnDutyResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string Title { get; set; } = "Personal la Datorie";
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<OnDutyDoctor> AllDoctors { get; set; } = new();
    public List<OnDutyGroup> Groups { get; set; } = new();
    public int TotalCount => AllDoctors.Count;
}

public sealed class OnDutyService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string WebhookUrl = "https://discord.com/api/webhooks/1559981949685076089/F8INgAQjFKfqSfuQ5GnVYZEbUneq4a-Bn9bzc5Qk503rW8gIRXLRZtUkPVMdi-k9hbsm/messages/1559982022036824108";

    // Regex to match lines like: "• [**M-207**] **Daria Power** (`24357`)" or "  [**M-207**] **Daria Power** (`24357`)" or "**Livius Demedici** (`25`)"
    private static readonly Regex DoctorRegex = new(
        @"(?:•|\*|-)?\s*(?:\[\*\*([^*\]]+)\*\*\]\s*)?\*\*([^*]+)\*\*\s*\(`?(\d+)`?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Regex to strip count "(3)" from field name like "Medici Specialiști (3)"
    private static readonly Regex RankStripRegex = new(
        @"\s*\(\d+\)\s*$",
        RegexOptions.Compiled
    );

    static OnDutyService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MDHelper/2.5");
    }

    public async Task<OnDutyResult> FetchOnDutyDataAsync(CancellationToken cancellationToken = default)
    {
        var result = new OnDutyResult();

        try
        {
            using var response = await _httpClient.GetAsync(WebhookUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"Eroare Discord HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                return result;
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

            var root = doc.RootElement;
            if (!root.TryGetProperty("embeds", out var embeds) || embeds.GetArrayLength() == 0)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "Răspunsul Discord nu conține secțiunea de embeds.";
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
            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Eroare la parsarea datelor: {ex.Message}";
            return result;
        }
    }
}
