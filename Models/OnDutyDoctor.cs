using System;
using System.Windows;

namespace MDHelper.Models;

public class OnDutyDoctor
{
    public string Indicativ { get; set; } = string.Empty;
    public string Nume { get; set; } = string.Empty;
    public string Cnp { get; set; } = string.Empty;
    public string Grad { get; set; } = string.Empty;
    public int GradOrder { get; set; } = 99;
    public string GradColor { get; set; } = "#00E5FF";

    public bool HasIndicativ => !string.IsNullOrWhiteSpace(Indicativ);
    public Visibility HasIndicativVisibility => HasIndicativ ? Visibility.Visible : Visibility.Collapsed;

    public string FullDisplayName => HasIndicativ 
        ? $"[{Indicativ}] {Nume} (CNP: {Cnp})" 
        : $"{Nume} (CNP: {Cnp})";

    public static bool IsAllowedDutyRank(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return false;
        var lower = rank.ToLowerInvariant();

        // Exclude inspectori, directori, conducere, sefi, etc.
        if (lower.Contains("director") || lower.Contains("conducere") || lower.Contains("manager") || lower.Contains("sef") || lower.Contains("șef") || lower.Contains("inspector"))
        {
            return false;
        }

        // Only allow medical staff from Asistent to Primar
        return lower.Contains("primar") ||
               lower.Contains("speciali") ||
               lower.Contains("reziden") ||
               lower.Contains("stagiar") ||
               lower.Contains("asistent");
    }

    public static int GetRankOrder(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return 99;
        var lower = rank.ToLowerInvariant();
        if (lower.Contains("primar")) return 1;
        if (lower.Contains("speciali")) return 2;
        if (lower.Contains("reziden")) return 3;
        if (lower.Contains("stagiar")) return 4;
        if (lower.Contains("asistent")) return 5;
        return 99;
    }

    public static string GetRankColor(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return "#00E5FF";
        var lower = rank.ToLowerInvariant();
        if (lower.Contains("primar")) return "#A855F7"; // Purple
        if (lower.Contains("speciali")) return "#00E5FF"; // Bright Cyan
        if (lower.Contains("reziden")) return "#818CF8"; // Indigo
        if (lower.Contains("stagiar")) return "#34D399"; // Emerald
        if (lower.Contains("asistent")) return "#F59E0B"; // Amber
        return "#38BDF8"; // Sky Blue
    }
}
