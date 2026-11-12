using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MDHelper.Services;

/// <summary>
/// Enforces GTA RP server rules regarding macro-assisted roleplay commands.
/// Specifically addresses Rule 2.27 (Low Effort Roleplay - LER), which forbids
/// automatic/canned macros for recovering from vehicular accidents (e.g., "/me își revine ușor",
/// "/me se scutură"), while permitting legitimate medical RP concerning patients/victims.
/// </summary>
public static class RoleplayComplianceService
{
    private static readonly Regex ChatCommandPrefixRegex = new(@"^/(me|do|b|low|s|w)\s+", RegexOptions.Compiled);
    private static readonly Regex PunctuationStripRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    // Whitelist keywords indicating medical/third-party/patient context
    private static readonly string[] WhitelistKeywords =
    {
        "pacient",    // pacient, pacientul, pacienta, pacientii, pacientului
        "victim",     // victima, victimei, victimele
        "ranit",      // ranit, ranitul, ranitului, ranitii, ranita
        "cetatean",   // cetatean, cetateanul, cetateana
        "persoan",    // persoana, persoanei, persoanele
        "individ",    // individ, individul
        "omul",       // omul, omului
        "manechin",
        "targa",
        "resuscit",
        "puls",
        "tensiun"
    };

    // Observational constructs indicating the action is examining/watching an external subject
    private static readonly string[] WhitelistObservational =
    {
        "observa ca",
        "observa cum",
        "observa daca",
        "constata ca",
        "constata cum",
        "constata daca",
        "verifica daca",
        "verifica cum",
        "vede ca",
        "vede cum",
        "asteapta ca",
        "asteapta pana",
        "asista la",
        "urmareste"
    };

    // Blacklisted accident / Low Effort Roleplay phrases
    private static readonly string[] BlacklistPhrases =
    {
        "isi revine usor",
        "isi revine incet",
        "isi revine treptat",
        "isi revine din soc",
        "isi revine usor din soc",
        "isi revine din lesin",
        "se scutura de praf",
        "se scutura",
        "nu are nimic",
        "nu a patit nimic",
        "nu pateste nimic",
        "scapa teafar",
        "scapa cu bine"
    };

    /// <summary>
    /// Normalizes text by removing Romanian diacritics and converting to lowercase.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        return text
            .Replace("ă", "a").Replace("Ă", "a")
            .Replace("â", "a").Replace("Â", "a")
            .Replace("î", "i").Replace("Î", "i")
            .Replace("ș", "s").Replace("Ș", "s").Replace("ş", "s").Replace("Ş", "s")
            .Replace("ț", "t").Replace("Ț", "t").Replace("ţ", "t").Replace("Ţ", "t")
            .ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether a single command line violates LER accident roleplay rules.
    /// </summary>
    public static bool IsLineBlocked(string rawLine, out string matchedPhrase)
    {
        matchedPhrase = string.Empty;
        if (string.IsNullOrWhiteSpace(rawLine)) return false;

        string norm = Normalize(rawLine).Trim();

        // Strip leading chat command prefixes like /me, /do, /b, /low, etc.
        norm = ChatCommandPrefixRegex.Replace(norm, "").Trim();

        // Check whitelist: legitimate third-party / patient / medical observation
        bool hasWhitelistKeyword = WhitelistKeywords.Any(k => norm.Contains(k));
        bool hasWhitelistObservation = WhitelistObservational.Any(k => norm.Contains(k));

        if (hasWhitelistKeyword || hasWhitelistObservation)
        {
            return false;
        }

        // Clean punctuation to prevent bypassing with dots, commas, symbols
        string clean = PunctuationStripRegex.Replace(norm, " ");
        clean = MultiWhitespaceRegex.Replace(clean, " ").Trim();

        foreach (var phrase in BlacklistPhrases)
        {
            if (clean.Contains(phrase))
            {
                matchedPhrase = phrase;
                return true;
            }
        }

        // Standalone self "isi revine" without medical object
        if (clean == "isi revine" || clean.StartsWith("isi revine ") || clean.EndsWith(" isi revine"))
        {
            matchedPhrase = "isi revine";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inspects an entire multiline script and returns true if any line is blocked.
    /// </summary>
    public static bool HasBlockedLines(string script, out string? firstBlockedLine, out string? matchedPhrase)
    {
        firstBlockedLine = null;
        matchedPhrase = null;

        if (string.IsNullOrWhiteSpace(script)) return false;

        var lines = script
            .Replace("\r\n", "\n")
            .Split('\n');

        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (IsLineBlocked(trimmed, out string matched))
            {
                firstBlockedLine = trimmed;
                matchedPhrase = matched;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Generates a descriptive warning message for the UI.
    /// </summary>
    public static string GetWarningMessage(string blockedLine, string matchedPhrase)
    {
        return $"Linia \"{blockedLine}\" este blocată de Regulamentul Serverului (2.27 Low Effort Roleplay). Comenzile automate pentru simularea recuperării din accident (ex: \"{matchedPhrase}\") sunt interzise.";
    }
}
