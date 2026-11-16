namespace MDHelper;

/// <summary>
/// Constante globale pentru aplicația MDTab-Helper.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Variabila globală unică pentru versiunea aplicației.
    /// Modifică această valoare într-un singur loc când lansezi o versiune nouă.
    /// Se propagă automat peste tot: UI tabletă, Settings, MDTab-Tools, Updater, Telemetrie și Feedback.
    /// </summary>
    public const string AppVersion = "2.7.6";

    /// <summary>
    /// Versiunea prefixată cu 'v' pentru afișare în interfață (ex: "v2.7.5").
    /// </summary>
    public const string DisplayVersion = "v" + AppVersion;
}
