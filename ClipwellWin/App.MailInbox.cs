using System.IO;
using System.Windows.Threading;
using ClipwellWin.Services;

namespace ClipwellWin;

/// <summary>Picks up mails from the Outlook add-in (inbox folder) and turns them into tasks.</summary>
public partial class App
{
    private FileSystemWatcher? _inboxWatcher;
    private DispatcherTimer? _inboxDebounce;

    private void StartMailInbox()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.InboxDir);
            _inboxDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _inboxDebounce.Tick += (_, _) => { _inboxDebounce.Stop(); ProcessMailInbox(); };
            // The add-in writes *.tmp and renames it, so a finished file shows up as a rename.
            _inboxWatcher = new FileSystemWatcher(AppPaths.InboxDir, "*.json") { NotifyFilter = NotifyFilters.FileName };
            _inboxWatcher.Created += InboxFileArrived;
            _inboxWatcher.Renamed += InboxFileArrived;
            _inboxWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogCrash(ex);
        }
        // Mails handed over while Merkautomat was not running.
        if (MailInbox.PendingFiles(AppPaths.InboxDir).Count > 0)
            Dispatcher.InvokeAsync(ProcessMailInbox, DispatcherPriority.ApplicationIdle);
    }

    private void StopMailInbox()
    {
        _inboxWatcher?.Dispose();
        _inboxDebounce?.Stop();
    }

    private void InboxFileArrived(object sender, FileSystemEventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            _inboxDebounce?.Stop();
            _inboxDebounce?.Start();
        });

    private void ProcessMailInbox()
    {
        var created = new List<KbTask>();
        var handled = new List<string>();
        foreach (var file in MailInbox.PendingFiles(AppPaths.InboxDir))
        {
            try
            {
                if (new FileInfo(file).Length > MailInbox.MaxFileBytes) throw new InvalidDataException("Übergabedatei zu groß.");
                created.Add(MailInbox.ToTask(MailInbox.Parse(MailInbox.Decode(File.ReadAllBytes(file))), DateTime.Now));
                handled.Add(file);
            }
            catch (InvalidDataException ex)
            {
                LogCrash(ex);
                MoveToInboxErrors(file);
            }
            catch (IOException)
            {
                // Still being written or locked; the next event or start picks it up.
            }
            catch (UnauthorizedAccessException ex)
            {
                LogCrash(ex);
            }
        }
        if (created.Count == 0) return;

        try
        {
            var tasks = TaskStore.Load(AppPaths.TasksFile);
            tasks.AddRange(created);
            TaskStore.Save(AppPaths.TasksFile, tasks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCrash(ex);
            return;
        }
        foreach (var file in handled)
        {
            try { SecureFile.Shred(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogCrash(ex); }
        }
        NotifyTasksChanged();
        OpenKnowledgeBase();
        _knowledgeBase?.OpenTasks(created[^1].Id);
    }

    private static void MoveToInboxErrors(string file)
    {
        try
        {
            var errors = Path.Combine(AppPaths.InboxDir, MailInbox.ErrorFolder);
            Directory.CreateDirectory(errors);
            File.Move(file, Path.Combine(errors, Path.GetFileName(file)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LogCrash(ex); }
    }
}
