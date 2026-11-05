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

    public static string ResolveDataDir(string applicationDataDir)
        => Path.Combine(applicationDataDir, "Clipwell");
}
