using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ClipwellWin.Services;

public enum OutlookAddinState { NotInstalled, Active, DisabledByOutlook, FileMissing }

/// <summary>
/// Registers the Outlook COM add-in (project MerkautomatOutlook) for the current user only: COM class under
/// HKCU\Software\Classes and the add-in entry under HKCU\Software\Microsoft\Office\Outlook\Addins. No admin rights.
/// The DLL is copied to a fixed folder first, so a running Outlook never locks the build output.
/// </summary>
public static class OutlookAddin
{
    // Must match MerkautomatOutlook.Connect (checked by a test).
    public const string ClassId = "{6E4B2D7A-3C1F-4F8E-9B5A-1D2C3E4F5A6B}";
    public const string ProgId = "Merkautomat.OutlookAddin";
    public const string ClassName = "MerkautomatOutlook.Connect";
    public const string DllName = "MerkautomatOutlook.dll";
    public const string AddinKey = @"Software\Microsoft\Office\Outlook\Addins\" + ProgId;
    private const string ClassesKey = @"Software\Classes\";
    private const string ManagedCategory = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}";

    /// <summary>The add-in DLL that ships next to Merkautomat.exe.</summary>
    public static string BundledDll(string baseDirectory) => Path.Combine(baseDirectory, "outlook", DllName);

    /// <summary>The per-user 64-bit HKCU view that 64-bit Outlook reads.</summary>
    public static RegistryKey OpenUserRoot() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    /// <exception cref="IOException">The installed DLL is locked because Outlook is running.</exception>
    public static void Install(RegistryKey root, string bundledDll, string installDirectory, string merkautomatExe)
    {
        if (!File.Exists(bundledDll)) throw new FileNotFoundException("Das Outlook-Add-in fehlt neben Merkautomat.exe.", bundledDll);
        Directory.CreateDirectory(installDirectory);
        var dll = Path.Combine(installDirectory, DllName);
        File.Copy(bundledDll, dll, overwrite: true);
        var assembly = AssemblyName.GetAssemblyName(dll);
        var version = assembly.Version?.ToString() ?? "1.0.0.0";

        using (var progId = root.CreateSubKey(ClassesKey + ProgId))
        {
            progId.SetValue("", ClassName);
            using var clsid = progId.CreateSubKey("CLSID");
            clsid.SetValue("", ClassId);
        }
        using (var cls = root.CreateSubKey(ClassesKey + @"CLSID\" + ClassId))
        {
            cls.SetValue("", ClassName);
            using (var prog = cls.CreateSubKey("ProgId")) prog.SetValue("", ProgId);
            using (cls.CreateSubKey(@"Implemented Categories\" + ManagedCategory)) { }
            using var server = cls.CreateSubKey("InprocServer32");
            WriteServer(server, assembly.FullName, dll);
            server.SetValue("", "mscoree.dll");
            server.SetValue("ThreadingModel", "Both");
            using var versioned = server.CreateSubKey(version);
            WriteServer(versioned, assembly.FullName, dll);
        }
        using (var addin = root.CreateSubKey(AddinKey))
        {
            addin.SetValue("FriendlyName", "Merkautomat");
            addin.SetValue("Description", "Kontextmenü \"Aufgabe in Merkautomat erstellen\" für Mails");
            addin.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
            addin.SetValue("MerkautomatPath", merkautomatExe);
        }
    }

    private static void WriteServer(RegistryKey key, string assemblyName, string dll)
    {
        key.SetValue("Class", ClassName);
        key.SetValue("Assembly", assemblyName);
        key.SetValue("RuntimeVersion", "v4.0.30319");
        key.SetValue("CodeBase", new Uri(dll).AbsoluteUri);
    }

    /// <summary>Removes all registry entries; the DLL is deleted when Outlook does not hold it.</summary>
    public static void Uninstall(RegistryKey root, string installDirectory)
    {
        root.DeleteSubKeyTree(AddinKey, throwOnMissingSubKey: false);
        root.DeleteSubKeyTree(ClassesKey + @"CLSID\" + ClassId, throwOnMissingSubKey: false);
        root.DeleteSubKeyTree(ClassesKey + ProgId, throwOnMissingSubKey: false);
        try { File.Delete(Path.Combine(installDirectory, DllName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static OutlookAddinState State(RegistryKey root, string installDirectory)
    {
        using var addin = root.OpenSubKey(AddinKey);
        if (addin == null) return OutlookAddinState.NotInstalled;
        if (!File.Exists(Path.Combine(installDirectory, DllName))) return OutlookAddinState.FileMissing;
        // Outlook sets LoadBehavior to 2 when it switched the add-in off (error or slow start).
        return addin.GetValue("LoadBehavior") is int behavior && (behavior & 1) == 0
            ? OutlookAddinState.DisabledByOutlook : OutlookAddinState.Active;
    }

    /// <summary>Asks Outlook to load the add-in again at its next start after it had switched it off.</summary>
    public static void Reenable(RegistryKey root)
    {
        using var addin = root.OpenSubKey(AddinKey, writable: true);
        addin?.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
    }
}
