using Merkautomat.ViewModels;

namespace Merkautomat;

public partial class App
{
    // Entries saved before Markdown was detected separately (e.g. chat answers stored as TypeScript).
    private void RecheckMarkdownEntries()
    {
        if (_popupVm == null) return;
        var snapshot = _popupVm.Entries.Select(e => e.Entry).ToList();
        _ = Task.Run(() =>
        {
            try
            {
                var ids = PopupViewModel.FindMarkdownCandidates(snapshot);
                if (ids.Count > 0) Dispatcher.Invoke(() => _popupVm.MarkAsMarkdown(ids));
            }
            catch (Exception ex) { LogCrash(ex); }
        });
    }
}
