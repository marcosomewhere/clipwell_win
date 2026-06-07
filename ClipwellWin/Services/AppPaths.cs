using System.IO;

namespace ClipwellWin.Services;

public static class AppPaths
{
    public static readonly string DataDir = ResolveDataDir(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string ResolveDataDir(string applicationDataDir)
        => Path.Combine(applicationDataDir, "Clipwell");
}
