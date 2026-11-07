using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MerkautomatOutlook;

/// <summary>
/// Outlook COM add-in with a single context-menu entry: the selected mails are written as JSON into the Merkautomat
/// inbox folder (%APPDATA%\Clipwell\inbox), which Merkautomat turns into tasks. Nothing else is read or changed.
/// </summary>
[ComVisible(true)]
[Guid(ClassId)]
[ProgId(ProgIdName)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class Connect : IDTExtensibility2, IRibbonExtensibility
{
    public const string ClassId = "6E4B2D7A-3C1F-4F8E-9B5A-1D2C3E4F5A6B";
    public const string ProgIdName = "Merkautomat.OutlookAddin";
    private const string AddinKey = @"Software\Microsoft\Office\Outlook\Addins\" + ProgIdName;
    private const int OlMail = 43;

    // Same marshaling as the interface, so late-bound IDispatch calls on the class interface behave identically.
    public void OnConnection([MarshalAs(UnmanagedType.IDispatch)] object application, int connectMode,
        [MarshalAs(UnmanagedType.IDispatch)] object addInInst,
        [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom) { }
    public void OnDisconnection(int removeMode, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom) { }
    public void OnAddInsUpdate([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom) { }
    public void OnStartupComplete([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom) { }
    public void OnBeginShutdown([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom) { }

    public string GetCustomUI(string ribbonId)
        => ribbonId == "Microsoft.Outlook.Explorer" ? RibbonXml : "";

    private const string RibbonXml = """
        <customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui">
          <contextMenus>
            <contextMenu idMso="ContextMenuMailItem">
              <button id="MerkautomatTaskSingle" label="Aufgabe in Merkautomat erstellen" imageMso="NewTask" onAction="OnCreateTask" insertAfterMso="Copy" />
            </contextMenu>
            <contextMenu idMso="ContextMenuMultipleItems">
              <button id="MerkautomatTaskMultiple" label="Aufgaben in Merkautomat erstellen" imageMso="NewTask" onAction="OnCreateTask" insertAfterMso="Copy" />
            </contextMenu>
          </contextMenus>
        </customUI>
        """;

    /// <summary>Ribbon callback (called by name through IDispatch).</summary>
    public void OnCreateTask(IRibbonControl control)
    {
        object? selection = null;
        try
        {
            selection = control.Context;
            var mails = ReadMails(selection);
            if (mails.Count == 0)
            {
                MessageBox.Show("In der Auswahl ist keine Mail.", "Merkautomat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var inbox = InboxDirectory();
            foreach (var mail in mails) MailHandover.Write(inbox, mail, DateTime.Now);
            // Lets Merkautomat bring its window to the front although Outlook is the foreground process.
            AllowSetForegroundWindow(-1);
            StartMerkautomatIfNeeded();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Die Aufgabe konnte nicht an Merkautomat übergeben werden:\n" + ex.Message, "Merkautomat",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (selection != null && Marshal.IsComObject(selection)) Marshal.ReleaseComObject(selection);
        }
    }

    private static List<MailData> ReadMails(object? selection)
    {
        var result = new List<MailData>();
        if (selection == null) return result;
        dynamic items = selection;
        int count = items.Count;
        for (var i = 1; i <= Math.Min(count, MailHandover.MaxMailsPerClick); i++)
        {
            dynamic item = items.Item(i);
            try
            {
                if ((int)item.Class != OlMail) continue;
                string? storeId = null;
                try { storeId = (string)item.Parent.StoreID; }
                catch (COMException) { }
                result.Add(new MailData(
                    (string?)item.Subject ?? "",
                    (string?)item.SenderName ?? "",
                    (DateTime)item.ReceivedTime,
                    (string?)item.Body ?? "",
                    (string?)item.EntryID ?? "",
                    storeId));
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        return result;
    }

    private static string InboxDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipwell", "inbox");

    /// <summary>Merkautomat picks the files up at its next start; when its path is known, start it right away.</summary>
    private static void StartMerkautomatIfNeeded()
    {
        if (Process.GetProcessesByName("Merkautomat").Length > 0) return;
        using var key = Registry.CurrentUser.OpenSubKey(AddinKey);
        if (key?.GetValue("MerkautomatPath") is string path && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) });
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
