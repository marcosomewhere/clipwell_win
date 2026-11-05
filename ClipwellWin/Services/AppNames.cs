namespace ClipwellWin.Services;

/// <summary>
/// Names shown to the user. Technical identifiers (Clipwell.exe, %APPDATA%\Clipwell, namespaces, registry and
/// window class names) keep the original name so existing installations and data stay valid.
/// </summary>
public static class AppNames
{
    public const string App = "Merkautomat";
    public const string KnowledgeBase = "Ideenkombinat";
    public const string Pinboard = "Pinstation";
    public const string VoiceNotes = "Sprechwerk";
    public const string Settings = "Kontrollstation";

    /// <summary>Window title in the form "Merkautomat – Bereich".</summary>
    public static string Title(string area) => $"{App} – {area}";
}
