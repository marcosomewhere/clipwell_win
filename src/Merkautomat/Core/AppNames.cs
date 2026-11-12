namespace Merkautomat.Services;

/// <summary>
/// Names shown to the user. The executable and startup entry use Merkautomat;
/// legacy migration paths remain available for existing installations.
/// </summary>
public static class AppNames
{
    public const string App = "Merkautomat";
    public const string KnowledgeBase = "Ideenkombinat";
    public const string Pinboard = "Pinstation";
    public const string VoiceNotes = "Sprechwerk";
    public const string Settings = "Kontrollstation";
    public const string MyKnowledge = "Mein Wissen";

    /// <summary>Window title in the form "Merkautomat – Bereich".</summary>
    public static string Title(string area) => $"{App} – {area}";
}
