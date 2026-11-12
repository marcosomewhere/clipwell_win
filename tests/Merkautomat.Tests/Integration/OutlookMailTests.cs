using System.IO;
using Merkautomat.Services;
using MerkautomatOutlook;
using Microsoft.Win32;
using Xunit;

namespace Merkautomat.Tests;

public sealed class MailInboxTests : IDisposable
{
    [Fact]
    public void HandoverUsesConfiguredLocalFolderOrTheProgramFolderAndNeverAppData()
    {
        Assert.Equal(@"D:\Tool\data\inbox", MailHandover.ResolveInboxDirectory(@"D:\Tool\data", @"C:\Other\Merkautomat.exe"));
        Assert.Equal(@"C:\Tools\Merkautomat\data\inbox", MailHandover.ResolveInboxDirectory(null, @"C:\Tools\Merkautomat\Merkautomat.exe"));
        Assert.Throws<InvalidOperationException>(() => MailHandover.ResolveInboxDirectory(null, "relative.exe"));
    }
    private static readonly DateTime Now = new(2026, 11, 7, 20, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "merkautomat-inbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void HandoverFromTheAddin_RoundTripsSpecialCharacters()
    {
        var mail = new MailData("AW: Angebot \"Q4\"", "Müller, Anna", new DateTime(2026, 11, 6, 14, 5, 0),
            "Hallo\r\n\tbitte prüfen: C:\\Pfad\\datei\u0001.txt", "00000000ABCDEF", "STORE1");

        var file = MailHandover.Write(_dir, mail, Now);
        Assert.DoesNotContain("Angebot", File.ReadAllText(file));
        var parsed = MailInbox.Parse(MailInbox.Decode(File.ReadAllBytes(file)));

        Assert.Equal(file, Assert.Single(MailInbox.PendingFiles(_dir)));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Equal("AW: Angebot \"Q4\"", parsed.Subject);
        Assert.Equal("Müller, Anna", parsed.Sender);
        Assert.Equal(new DateTime(2026, 11, 6, 14, 5, 0), parsed.Received);
        Assert.Equal("Hallo\r\n\tbitte prüfen: C:\\Pfad\\datei\u0001.txt", parsed.Body);
        Assert.Equal("00000000ABCDEF", parsed.EntryId);
    }

    [Fact]
    public void Handover_ShortensVeryLongBodies()
    {
        var json = MailHandover.ToJson(new MailData("s", "a", Now, new string('x', MailHandover.MaxBody + 500), "id", null));
        Assert.Equal(MailHandover.MaxBody, MailInbox.Parse(json).Body.Length);
    }

    [Fact]
    public void ToTask_UsesSubjectWithoutReplyPrefixesAndKeepsTheMailAsSource()
    {
        var task = MailInbox.ToTask(new InboxMail("AW: WG: Angebot prüfen", "Anna Müller", new DateTime(2026, 11, 6, 14, 5, 0),
            "Bitte bis Freitag.\r\n\r\n\r\n\r\nGruß", "ENTRY1"), Now);

        Assert.Equal("Angebot prüfen", task.Title);
        Assert.Equal("mail:ENTRY1", task.SourceKey);
        Assert.Equal("Mail von Anna Müller: AW: WG: Angebot prüfen", task.SourceTitle);
        Assert.Equal("Von: Anna Müller\nEmpfangen: 06.11.2026 14:05\nBetreff: AW: WG: Angebot prüfen\n\nBitte bis Freitag.\n\nGruß", task.SourceText);
        Assert.Equal(Now, task.Created);
        Assert.False(task.Done);
    }

    [Fact]
    public void ToTask_WithoutSubjectAndWithLongBody()
    {
        var task = MailInbox.ToTask(new InboxMail("", "", DateTime.MinValue, new string('a', 9000), ""), Now);

        Assert.Equal("Mail ohne Betreff", task.Title);
        Assert.StartsWith("mail:", task.SourceKey);
        Assert.Equal(TaskStore.MaxSourceText + 1, task.SourceText!.Length);
    }

    [Fact]
    public void CleanBody_RemovesNewsletterFillersIndentationAndBlankRuns()
    {
        var body = "Sign in now\r\n\u034F \u200C \u00AD \u034F\u00A0\u200C\r\n \r\n\t\r\n   <http://example.com/x>  \r\n\r\n\r\n\r\nGruß\uFEFF";

        Assert.Equal("Sign in now\n\n<http://example.com/x>\n\nGruß", MailInbox.CleanBody(body));
    }

    [Fact]
    public void LinkText_FindsBracketedAndPlainLinks()
    {
        var segments = LinkText.Split("Start <https://email.gitbook.com/e/c/eyJlbWFpbF9pZCI6ImRnVDRpUWNCQU9MRDdnYmh3LTRHQWFHMkxuNUN0cWpU> und https://example.com/docs. Ende");

        Assert.Equal(["Start ", "email.gitbook.com/…", " und ", "example.com/docs", ". Ende"], segments.Select(s => s.Text));
        Assert.Equal("https://email.gitbook.com/e/c/eyJlbWFpbF9pZCI6ImRnVDRpUWNCQU9MRDdnYmh3LTRHQWFHMkxuNUN0cWpU", segments[1].Link!.AbsoluteUri);
        Assert.Equal("https://example.com/docs", segments[3].Link!.AbsoluteUri);
        Assert.Null(segments[0].Link);
    }

    [Fact]
    public void LinkText_IgnoresOtherSchemesAndPlainText()
    {
        Assert.Equal([new LinkSegment("kein Link, file://C:/x und ftp://a.b")], LinkText.Split("kein Link, file://C:/x und ftp://a.b"));
        Assert.Empty(LinkText.Split(""));
        Assert.Equal("example.com", LinkText.Label(new Uri("https://www.example.com/")));
    }

    [Theory]
    [InlineData("AW: Termin", "Termin")]
    [InlineData("RE: Fwd: WG: Termin", "Termin")]
    [InlineData("AW[2]: Termin", "Termin")]
    [InlineData("Awesome: Termin", "Awesome: Termin")]
    [InlineData("Termin: AW", "Termin: AW")]
    public void SubjectWithoutPrefixes(string subject, string expected)
        => Assert.Equal(expected, MailInbox.SubjectWithoutPrefixes(subject));

    [Theory]
    [InlineData("kein json")]
    [InlineData("{\"kind\":\"etwas-anderes\",\"subject\":\"x\"}")]
    public void Parse_RejectsForeignOrBrokenFiles(string json)
        => Assert.Throws<InvalidDataException>(() => MailInbox.Parse(json));
}

public sealed class OutlookAddinRegistrationTests : IDisposable
{
    private readonly string _testKey = @"Software\MerkautomatTests\" + Guid.NewGuid().ToString("N");
    private readonly string _installDir = Path.Combine(Path.GetTempPath(), "merkautomat-outlook-" + Guid.NewGuid().ToString("N"));
    private readonly RegistryKey _root;

    public OutlookAddinRegistrationTests() => _root = Registry.CurrentUser.CreateSubKey(_testKey);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\MerkautomatTests"))
            if (parent is { SubKeyCount: 0, ValueCount: 0 }) Registry.CurrentUser.DeleteSubKey(@"Software\MerkautomatTests", throwOnMissingSubKey: false);
        if (Directory.Exists(_installDir)) Directory.Delete(_installDir, recursive: true);
    }

    [Fact]
    public void BuildShipsTheAddinNextToTheProgram()
        => Assert.True(File.Exists(OutlookAddin.BundledDll(AppContext.BaseDirectory)));

    [Fact]
    public void ClassIdMatchesTheAddin()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Merkautomat.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "MerkautomatOutlook", "Connect.cs"));

        Assert.Contains($"ClassId = \"{OutlookAddin.ClassId.Trim('{', '}')}\"", source);
        Assert.Contains($"ProgIdName = \"{OutlookAddin.ProgId}\"", source);
        Assert.Contains("namespace MerkautomatOutlook;", source);
        Assert.Contains("public sealed class Connect", source);
    }

    /// <summary>
    /// Outlook passes SAFEARRAY(VARIANT)* to every IDTExtensibility2 method. A plain "ref Array" is marshaled as an
    /// _Array interface pointer instead and crashes Outlook inside clr.dll at startup.
    /// </summary>
    [Fact]
    public void AddinInterfacesMarshalOutlookArraysAsSafeArrays()
    {
        var connect = System.Reflection.Assembly.LoadFrom(OutlookAddin.BundledDll(AppContext.BaseDirectory))
            .GetType("MerkautomatOutlook.Connect", throwOnError: true)!;
        var arrays = new[] { typeof(IDTExtensibility2), connect }
            .SelectMany(t => t.GetMethods())
            .Where(m => m.Name.StartsWith("On", StringComparison.Ordinal) && m.Name != "OnCreateTask")
            .SelectMany(m => m.GetParameters().Where(p => p.ParameterType.GetElementType() == typeof(Array) || p.ParameterType == typeof(Array))
                .Select(p => (Method: m, Parameter: p)))
            .ToList();

        Assert.Equal(10, arrays.Count);
        foreach (var (method, parameter) in arrays)
        {
            var marshal = parameter.GetCustomAttributes(typeof(System.Runtime.InteropServices.MarshalAsAttribute), false)
                .Cast<System.Runtime.InteropServices.MarshalAsAttribute>().SingleOrDefault();
            Assert.True(marshal is { Value: System.Runtime.InteropServices.UnmanagedType.SafeArray, SafeArraySubType: System.Runtime.InteropServices.VarEnum.VT_VARIANT },
                $"{method.DeclaringType!.Name}.{method.Name}({parameter.Name}) braucht MarshalAs(SafeArray, VT_VARIANT)");
        }
    }

    [Fact]
    public void LocationUpdateMovesTheAddinAndInboxButPreservesDisabledState()
    {
        OutlookAddin.Install(_root, OutlookAddin.BundledDll(AppContext.BaseDirectory), _installDir, @"C:\Old\Merkautomat.exe");
        using (var entry = _root.OpenSubKey(OutlookAddin.AddinKey, writable: true)!) entry.SetValue("LoadBehavior", 2, RegistryValueKind.DWord);
        var relocated = Path.Combine(_installDir, "new", "data", "outlook");
        var data = Path.GetDirectoryName(relocated)!;
        OutlookAddin.UpdateLocation(_root, OutlookAddin.BundledDll(AppContext.BaseDirectory), relocated, @"C:\New\Merkautomat.exe", data);
        Assert.Equal(OutlookAddinState.DisabledByOutlook, OutlookAddin.State(_root, relocated));
        using var updated = _root.OpenSubKey(OutlookAddin.AddinKey)!;
        Assert.Equal(data, updated.GetValue("DataDirectory"));
        Assert.Equal(@"C:\New\Merkautomat.exe", updated.GetValue("MerkautomatPath"));
        Assert.True(File.Exists(Path.Combine(relocated, OutlookAddin.DllName)));
        using var locked = new FileStream(Path.Combine(relocated, OutlookAddin.DllName), FileMode.Open, FileAccess.Read, FileShare.Read);
        OutlookAddin.UpdateLocation(_root, OutlookAddin.BundledDll(AppContext.BaseDirectory), relocated, @"C:\New\Merkautomat.exe", data);
    }

    [Fact]
    public void InstallStateAndUninstall()
    {
        Assert.Equal(OutlookAddinState.NotInstalled, OutlookAddin.State(_root, _installDir));

        OutlookAddin.Install(_root, OutlookAddin.BundledDll(AppContext.BaseDirectory), _installDir, @"C:\Programme\Merkautomat.exe");

        var dll = Path.Combine(_installDir, OutlookAddin.DllName);
        Assert.True(File.Exists(dll));
        Assert.Equal(OutlookAddinState.Active, OutlookAddin.State(_root, _installDir));
        using (var addin = _root.OpenSubKey(OutlookAddin.AddinKey)!)
        {
            Assert.Equal(3, addin.GetValue("LoadBehavior"));
            Assert.Equal(@"C:\Programme\Merkautomat.exe", addin.GetValue("MerkautomatPath"));
        }
        using (var server = _root.OpenSubKey($@"Software\Classes\CLSID\{OutlookAddin.ClassId}\InprocServer32")!)
        {
            Assert.Equal("mscoree.dll", server.GetValue(""));
            Assert.Equal(OutlookAddin.ClassName, server.GetValue("Class"));
            Assert.Equal(new Uri(dll).AbsoluteUri, server.GetValue("CodeBase"));
            Assert.StartsWith("MerkautomatOutlook, Version=", (string)server.GetValue("Assembly")!);
        }
        using (var progId = _root.OpenSubKey($@"Software\Classes\{OutlookAddin.ProgId}\CLSID")!)
            Assert.Equal(OutlookAddin.ClassId, progId.GetValue(""));

        using (var addin = _root.OpenSubKey(OutlookAddin.AddinKey, writable: true)!) addin.SetValue("LoadBehavior", 2, RegistryValueKind.DWord);
        Assert.Equal(OutlookAddinState.DisabledByOutlook, OutlookAddin.State(_root, _installDir));
        OutlookAddin.Reenable(_root);
        Assert.Equal(OutlookAddinState.Active, OutlookAddin.State(_root, _installDir));

        OutlookAddin.Uninstall(_root, _installDir);
        Assert.Equal(OutlookAddinState.NotInstalled, OutlookAddin.State(_root, _installDir));
        Assert.Null(_root.OpenSubKey($@"Software\Classes\CLSID\{OutlookAddin.ClassId}"));
        Assert.False(File.Exists(dll));
    }
}
