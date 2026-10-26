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

        // Allow medical staff from Asistent to Director:
        // asistent -> stagiar -> rezident -> specialist -> primar -> inspector -> director
        return lower.Contains("director") ||
               lower.Contains("inspector") ||
               lower.Contains("primar") ||
               lower.Contains("speciali") ||
               lower.Contains("reziden") ||
               lower.Contains("stagiar") ||
               lower.Contains("asisten");
    }

    public static int GetRankOrder(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return 99;
        var lower = rank.ToLowerInvariant();
        if (lower.Contains("director"))
        {
            if (lower.Contains("adjunct")) return 2;
            return 1;
        }
        if (lower.Contains("inspector")) return 3;
        if (lower.Contains("primar")) return 4;
        if (lower.Contains("speciali")) return 5;
        if (lower.Contains("reziden")) return 6;
        if (lower.Contains("stagiar")) return 7;
        if (lower.Contains("asisten")) return 8;
        return 99;
    }

    public static string GetRankColor(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return "#00E5FF";
        var lower = rank.ToLowerInvariant();
        if (lower.Contains("director")) return "#FF2D55"; // Crimson / Red-Pink (Highest rank)
        if (lower.Contains("inspector")) return "#F43F5E"; // Rose / Coral
        if (lower.Contains("primar")) return "#A855F7"; // Purple
        if (lower.Contains("speciali")) return "#00E5FF"; // Bright Cyan
        if (lower.Contains("reziden")) return "#818CF8"; // Indigo
        if (lower.Contains("stagiar")) return "#34D399"; // Emerald
        if (lower.Contains("asisten")) return "#F59E0B"; // Amber
        return "#38BDF8"; // Sky Blue
    }
}
