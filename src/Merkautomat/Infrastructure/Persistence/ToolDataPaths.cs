using System;
using System.IO;

namespace Merkautomat.Services;

/// <summary>Shared by the application and the net48 Outlook add-in.</summary>
public static class ToolDataPaths
{
    public static string ResolveDataDir(string applicationDirectory)
    {
        var application = Path.GetFullPath(applicationDirectory);
        var current = new DirectoryInfo(application);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Merkautomat.slnx"))
                && File.Exists(Path.Combine(current.FullName, "src", "Merkautomat", "Merkautomat.csproj")))
            {
                var buildRoot = Path.Combine(current.FullName, "src", "Merkautomat", "bin") + Path.DirectorySeparatorChar;
                if ((application.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                    .StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(current.FullName, "data");
                break;
            }
            current = current.Parent;
        }
        return Path.Combine(application, "data");
    }
}
