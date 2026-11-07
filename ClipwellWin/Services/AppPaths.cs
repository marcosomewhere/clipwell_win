using System.IO;

namespace ClipwellWin.Services;

public static class AppPaths
{
    public static readonly string DataDir = ResolveDataDir(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public const string RecordingsFolderName = "recordings";
    public const string NotesFolderName = "notes";
    public const string ModelsFolderName = "models";

    public static string NotesDir => Path.Combine(DataDir, NotesFolderName);
    public static string KbNotesDir => Path.Combine(DataDir, "kb", NotesFolderName);
    public static string ModelsDir => Path.Combine(DataDir, ModelsFolderName);
    public static string TasksFile => Path.Combine(DataDir, "tasks.json");
    /// <summary>Hand-over folder of the Outlook add-in.</summary>
    public static string InboxDir => Path.Combine(DataDir, "inbox");
    /// <summary>Installed copy of the Outlook add-in, separate from the program folder so Outlook never locks the build.</summary>
    public static string OutlookAddinDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipwell", "outlook");

    public static string ResolveDataDir(string applicationDataDir)
        => Path.Combine(applicationDataDir, "Clipwell");
}
