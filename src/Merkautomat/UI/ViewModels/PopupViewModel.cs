using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat.ViewModels;

public class PopupViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly Func<int> _maxHistoryItems;
    private readonly Func<int> _maxAgeInDays;

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    private ICollectionView _view;
    public ICollectionView FilteredEntries => _view;

    private string _searchText = "";
    private HashSet<long>? _ftsMatchIds;
    private Regex? _searchRegex;

    public ObservableCollection<string> SearchHistory { get; } = [];

    public void AddToSearchHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        var existing = SearchHistory.FirstOrDefault(s => s.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (existing != null) SearchHistory.Remove(existing);
        SearchHistory.Insert(0, query);
        while (SearchHistory.Count > 5) SearchHistory.RemoveAt(SearchHistory.Count - 1);
    }

    private bool _isRegexSearch;
    public bool IsRegexSearch
    {
        get => _isRegexSearch;
        set
        {
            if (Set(ref _isRegexSearch, value))
            {
                UpdateSearchHelpers(_searchText);
                _view.Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                UpdateSearchHelpers(value);
                _view.Refresh();
            }
        }
    }

    private void UpdateSearchHelpers(string query)
    {
        UpdateSearchRegex(query);
        UpdateFtsMatchIds(query);
    }

    private void UpdateSearchRegex(string query)
    {
        _searchRegex = null;
        if (!_isRegexSearch) return;

        var q = query.Trim();
        if (string.IsNullOrWhiteSpace(q)) return;

        try
        {
            _searchRegex = new Regex(
                q,
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromMilliseconds(50));
        }
        // An unfinished pattern while typing is normal; the search then matches nothing until it is valid.
        catch (ArgumentException)
        {
            _searchRegex = null;
        }
    }

    private void UpdateFtsMatchIds(string query)
    {
        var q = query.Trim();
        if (Entries.Count <= 1000
            || _isRegexSearch
            || string.IsNullOrWhiteSpace(q)
            || q.StartsWith("type:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("kind:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
            || q.StartsWith("pinned:", StringComparison.OrdinalIgnoreCase))
        {
            _ftsMatchIds = null;
            _ftsQuery++;
            return;
        }
        // Until the FTS result arrives the linear scan filters; hits of the previous query must not linger.
        _ftsMatchIds = null;
        _ = QueryFtsAsync(q, ++_ftsQuery);
    }

    private int _ftsQuery;

    private async Task QueryFtsAsync(string query, int version)
    {
        HashSet<long>? ids = null;
        try
        {
            await _runDb(() => ids = _db.SearchFts(query));
        }
        catch (Exception ex)
        {
            App.LogError("Volltextsuche", ex);
            return;
        }
        if (version != _ftsQuery || ids == null) return;
        _ftsMatchIds = ids;
        _view.Refresh();
    }

    private EntryType? _typeFilter;
    public EntryType? TypeFilter
    {
        get => _typeFilter;
        set
        {
            if (_typeFilter == value) return;
            _typeFilter = value;
            OnPropertyChanged();
            _view.Refresh();
        }
    }

    private string _sortMode = "Neueste";
    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (Set(ref _sortMode, value))
            {
                OnPropertyChanged(nameof(SortModeLabel));
                OnPropertyChanged(nameof(IsSortCustom));
                ApplySortMode();
            }
        }
    }

    public string SortModeLabel => _sortMode switch
    {
        "Häufig" => "Häufig genutzt",
        "Selten" => "Selten genutzt",
        var m     => m,
    };

    public bool IsSortCustom => _sortMode != "Neueste";

    private bool _showPinnedOnly;
    public bool ShowPinnedOnly
    {
        get => _showPinnedOnly;
        set
        {
            if (Set(ref _showPinnedOnly, value))
                _view.Refresh();
        }
    }

    private bool _showMarkdownOnly;
    /// <summary>Chip "MD": only Markdown entries (badge MD); they stay visible under "Text" as well.</summary>
    public bool ShowMarkdownOnly
    {
        get => _showMarkdownOnly;
        set
        {
            if (Set(ref _showMarkdownOnly, value))
                _view.Refresh();
        }
    }


    private bool _isBulkMode;
    public bool IsBulkMode
    {
        get => _isBulkMode;
        set
        {
            if (Set(ref _isBulkMode, value))
            {
                if (!value)
                    foreach (var vm in Entries) vm.IsSelected = false;
                OnPropertyChanged(nameof(SelectedCount));
            }
        }
    }

    public int SelectedCount => Entries.Count(e => e.IsSelected);

    public void NotifySelectionChanged() => OnPropertyChanged(nameof(SelectedCount));

    public void ToggleSelectAll()
    {
        var visible = Entries.Where(e => _view.Filter == null || _view.Filter(e)).ToList();
        bool selectAll = !visible.All(e => e.IsSelected);
        foreach (var vm in visible)
            vm.IsSelected = selectAll;
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void DeleteEntries(List<EntryViewModel> entries)
    {
        foreach (var vm in entries)
        {
            _db.Delete(vm.Id);
            Entries.Remove(vm);
            EntryDeleted?.Invoke(vm.Id);
        }
        IsBulkMode = false;
    }

    public string CopySelectedTexts(string separator = "\n---\n")
    {
        return string.Join(separator, Entries
            .Where(e => e.IsSelected)
            .Select(e => e.Content ?? e.Entry.OcrText ?? "")
            .Where(t => !string.IsNullOrEmpty(t)));
    }

    public void PinSelected(bool pin)
    {
        foreach (var vm in Entries.Where(e => e.IsSelected))
        {
            var pinOrder = _db.SetPinned(vm.Id, pin);
            vm.IsPinned = pin;
            vm.PinOrder = pinOrder;
        }
        _view.Refresh();
        OnPropertyChanged(nameof(SelectedCount));
    }

    public List<EntryViewModel> GetSelectedViewModels()
        => Entries.Where(e => e.IsSelected).ToList();

    private EntryViewModel? _selected;
    public EntryViewModel? SelectedEntry
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>Runs database work on the calling thread; for tests, which have no dispatcher to return to.</summary>
    public static readonly Func<Action, Task> InlineDatabase = work => { work(); return Task.CompletedTask; };

    // Database calls wait on DatabaseService's gate, which a backup or import can hold for seconds; the popup must
    // keep reacting meanwhile, so additions and FTS queries run here instead of on the UI thread.
    private readonly Func<Action, Task> _runDb;
    private readonly SemaphoreSlim _addGate = new(1, 1);

    public PopupViewModel(DatabaseService db, Func<int>? maxHistoryItems = null, Func<int>? maxAgeInDays = null,
        Func<Action, Task>? runDatabase = null)
    {
        _runDb = runDatabase ?? (work => Task.Run(work));
        _db = db;
        _maxHistoryItems = maxHistoryItems ?? (() => 500);
        _maxAgeInDays = maxAgeInDays ?? (() => 0);
        _view = CreateView();
        ApplySortMode();
    }

    private ICollectionView CreateView()
    {
        var view = new ListCollectionView(Entries);
        view.Filter = Filter;
        return view;
    }

    private void ApplySortMode()
    {
        var liveView = _view as ICollectionViewLiveShaping;
        using (_view.DeferRefresh())
        {
            _view.SortDescriptions.Clear();
            _view.GroupDescriptions.Clear();
            if (liveView != null)
            {
                liveView.LiveSortingProperties.Clear();
                liveView.LiveGroupingProperties.Clear();
            }

            _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.IsPinned), ListSortDirection.Descending));
            _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.PinSortKey), ListSortDirection.Descending));

            switch (_sortMode)
            {
                case "Älteste":
                    _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.Timestamp), ListSortDirection.Ascending));
                    _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntryViewModel.GroupLabel)));
                    break;
                case "Häufig":
                    _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.UseCount), ListSortDirection.Descending));
                    break;
                case "Selten":
                    _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.UseCount), ListSortDirection.Ascending));
                    break;
                default: // "Neueste"
                    _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.Timestamp), ListSortDirection.Descending));
                    _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntryViewModel.GroupLabel)));
                    break;
            }

            if (liveView != null)
            {
                liveView.IsLiveSorting = true;
                liveView.LiveSortingProperties.Add(nameof(EntryViewModel.IsPinned));
                liveView.LiveSortingProperties.Add(nameof(EntryViewModel.PinSortKey));
                if (_sortMode is "Häufig" or "Selten")
                {
                    liveView.LiveSortingProperties.Add(nameof(EntryViewModel.UseCount));
                }
                else
                {
                    liveView.LiveSortingProperties.Add(nameof(EntryViewModel.Timestamp));
                    liveView.IsLiveGrouping = true;
                    liveView.LiveGroupingProperties.Add(nameof(EntryViewModel.GroupLabel));
                }
            }
        }
    }

    public void LoadFromDb()
    {
        Entries.Clear();
        foreach (var entry in _db.LoadAllLite())
            Entries.Add(new EntryViewModel(entry));
    }

    /// <summary>
    /// Stores a new entry or refreshes its duplicate. Must be called on the UI thread; it returns there after the
    /// database work, and additions run one at a time so a quick second copy cannot slip past the duplicate check.
    /// </summary>
    public async Task AddEntryAsync(ClipboardEntry entry)
    {
        entry.Metadata = KnowledgeMetadata.ForClipboard(entry);
        await _addGate.WaitAsync();
        try
        {
            var duplicates = Entries
                .Where(e => IsDuplicate(e.Entry, entry))
                .OrderByDescending(e => e.IsPinned)
                .ThenByDescending(e => e.Timestamp)
                .ToList();
            if (duplicates.Count > 0)
            {
                await RefreshDuplicateAsync(entry, duplicates);
                return;
            }

            var maxItems = _maxHistoryItems();
            var maxAge = _maxAgeInDays();
            HashSet<long>? remaining = null;
            await _runDb(() =>
            {
                entry.Id = _db.Insert(entry);
                var purged = _db.Purge(maxItems);
                var agePurged = maxAge > 0 ? _db.PurgeByAge(maxAge) : 0;
                if (purged + agePurged > 0) remaining = _db.LoadAllIds();
            });

            Entries.Insert(0, new EntryViewModel(entry));
            if (remaining != null)
            {
                var toRemove = Entries.Where(e => !remaining.Contains(e.Id)).ToList();
                foreach (var r in toRemove) Entries.Remove(r);
            }
        }
        finally
        {
            _addGate.Release();
        }
    }

    private async Task RefreshDuplicateAsync(ClipboardEntry entry, List<EntryViewModel> duplicates)
    {
        var kept = duplicates[0];
        var extra = duplicates.Skip(1).ToList();
        entry.Id = kept.Id;
        var pin = entry.IsPinned && !kept.IsPinned;
        // The same text copied again from a web page brings fresh formatting and source.
        var refreshHtml = entry.HtmlContent != null || entry.SourceUrl != null;
        var sourceUrl = entry.SourceUrl ?? kept.Entry.SourceUrl;
        var previous = KnowledgeMetadata.ForClipboard(kept.Entry);
        var incoming = KnowledgeMetadata.ForClipboard(entry);
        var metadata = previous with
        {
            Application = previous.Application ?? incoming.Application,
            WindowTitle = previous.WindowTitle ?? incoming.WindowTitle,
            SourceUrl = previous.SourceUrl ?? incoming.SourceUrl,
            SourcePath = previous.SourcePath ?? incoming.SourcePath,
        };

        long pinOrder = 0;
        await _runDb(() =>
        {
            if (pin) pinOrder = _db.SetPinned(kept.Id, true);
            _db.UpdateTimestamp(kept.Id, entry.Timestamp);
            _db.UpdateMetadata(kept.Id, metadata);
            if (refreshHtml) _db.UpdateHtml(kept.Id, entry.HtmlContent, sourceUrl);
            foreach (var duplicate in extra) _db.Delete(duplicate.Id);
        });

        if (pin)
        {
            kept.IsPinned = true;
            kept.PinOrder = pinOrder;
        }
        kept.Entry.Metadata = metadata;
        entry.Metadata = metadata;
        kept.SetTimestamp(entry.Timestamp);
        if (refreshHtml)
        {
            kept.Entry.HtmlContent = entry.HtmlContent;
            kept.Entry.HasHtml = entry.HtmlContent != null;
            kept.Entry.SourceUrl = sourceUrl;
        }
        foreach (var duplicate in extra) Entries.Remove(duplicate);
        _view.Refresh();
    }

    private static bool IsDuplicate(ClipboardEntry existing, ClipboardEntry next)
    {
        if (existing.Type != next.Type || KnowledgeMetadata.DifferentSources(existing, next)) return false;

        if (next.Type == EntryType.Image)
        {
            if (existing.ImageData == null || next.ImageData == null) return false;
            if (existing.ImageData.Length != next.ImageData.Length) return false;
            // Fast hash check before full byte comparison
            return ComputeSimpleHash(existing.ImageData) == ComputeSimpleHash(next.ImageData)
                && existing.ImageData.SequenceEqual(next.ImageData);
        }

        if (next.Type == EntryType.Url)
        {
            var a = NormalizeUrl(existing.Content);
            var b = NormalizeUrl(next.Content);
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // Text / Code: normalize whitespace
        return existing.Content != null
            && next.Content != null
            && string.Equals(
                NormalizeText(existing.Content),
                NormalizeText(next.Content),
                StringComparison.Ordinal);
    }

    private static string NormalizeText(string s) => s.Trim();

    private static string? NormalizeUrl(string? url)
    {
        if (url == null) return null;
        return url.TrimEnd('/').ToLowerInvariant();
    }

    private static long ComputeSimpleHash(byte[] data)
    {
        if (data.Length == 0) return 17;

        long h = 17;
        var step = Math.Max(1, data.Length / 64);
        for (var i = 0; i < data.Length; i += step)
            h = h * 31 + data[i];
        h = h * 31 + data[^1];
        return h;
    }

    public void TogglePin(EntryViewModel vm)
    {
        var next = !vm.IsPinned;
        var pinOrder = _db.SetPinned(vm.Id, next);
        vm.IsPinned = next;
        vm.PinOrder = pinOrder;
        ReseatEntry(vm);
    }

    /// <summary>
    /// Moves a pinned entry before or after another pinned entry and renumbers all pinned entries,
    /// including ones hidden by a pinboard search, so PinOrder values stay unique.
    /// </summary>
    public bool MovePinned(long draggedId, long targetId, bool insertAfter)
    {
        if (draggedId == targetId) return false;

        var pinned = Entries.Where(e => e.IsPinned).OrderByDescending(e => e.PinSortKey).ToList();
        var dragged = pinned.FirstOrDefault(e => e.Id == draggedId);
        var target = pinned.FirstOrDefault(e => e.Id == targetId);
        if (dragged == null || target == null) return false;

        pinned.Remove(dragged);
        pinned.Insert(pinned.IndexOf(target) + (insertAfter ? 1 : 0), dragged);

        // Gaps of 1000 leave room for NextPinOrder (max + 1) without renumbering on every pin.
        for (int i = 0; i < pinned.Count; i++)
        {
            var newOrder = (long)(pinned.Count - i) * 1000;
            if (pinned[i].PinOrder == newOrder) continue;
            pinned[i].PinOrder = newOrder;
            _db.UpdatePinOrder(pinned[i].Id, newOrder);
        }
        return true;
    }

    private void ReseatEntry(EntryViewModel vm)
    {
        var oldIndex = Entries.IndexOf(vm);
        if (oldIndex < 0) return;

        Entries.RemoveAt(oldIndex);
        var newIndex = 0;
        while (newIndex < Entries.Count && CompareForDisplay(Entries[newIndex], vm) <= 0)
            newIndex++;
        Entries.Insert(newIndex, vm);
    }

    private int CompareForDisplay(EntryViewModel a, EntryViewModel b)
    {
        var pinned = b.IsPinned.CompareTo(a.IsPinned);
        if (pinned != 0) return pinned;
        var pinOrder = b.PinSortKey.CompareTo(a.PinSortKey);
        if (pinOrder != 0) return pinOrder;
        return _sortMode switch
        {
            "Häufig"  => b.UseCount.CompareTo(a.UseCount),
            "Selten"  => a.UseCount.CompareTo(b.UseCount),
            "Älteste" => a.Timestamp.CompareTo(b.Timestamp),
            _         => b.Timestamp.CompareTo(a.Timestamp),
        };
    }

    /// <summary>The user deleted an entry; purges by the history limits do not raise it.</summary>
    public event Action<long>? EntryDeleted;

    public void Delete(EntryViewModel vm)
    {
        _db.Delete(vm.Id);
        Entries.Remove(vm);
        EntryDeleted?.Invoke(vm.Id);
    }

    public void SecureDelete(EntryViewModel vm)
    {
        Entries.Remove(vm);
        _ = System.Threading.Tasks.Task.Run(() => _db.SecureDelete(vm.Id));
        EntryDeleted?.Invoke(vm.Id);
    }

    public EntryViewModel? LatestEntry()
        => Entries.OrderByDescending(e => e.Timestamp).FirstOrDefault();

    public void SetType(EntryViewModel vm, EntryType type, string? kind = null)
    {
        string? language = type == EntryType.Code
            ? SyntaxService.DetectLanguage(vm.Content ?? "", CodeDetectionMode.Aggressive) ?? "Code"
            : null;
        // ContentKind ist ein Badge, nicht der Sprachname – separat ableiten.
        kind ??= type == EntryType.Code ? "CODE" : null;
        var reason = kind == "MARKDOWN" ? "Manuell als Markdown behandelt." : $"Manuell als {type} behandelt.";
        vm.SetType(type, language, kind, reason);
        _db.SetType(vm.Id, type, language, kind, reason);
        _view.Refresh();
    }

    public const string MarkdownRecheckReason = "Als Markdown erkannt (nachträglich geprüft).";

    /// <summary>
    /// Older text or script entries whose content is Markdown prose. Manual choices ("Manuell als ...",
    /// "... manuell bearbeitet.") and notes stay as they are. Safe to call off the UI thread.
    /// </summary>
    public static List<long> FindMarkdownCandidates(IEnumerable<ClipboardEntry> entries)
        => entries
            .Where(e => e.Type is EntryType.Text or EntryType.Code
                && !string.Equals(e.ContentKind, ContentKindService.MarkdownKind, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(e.ContentKind, "NOTE", StringComparison.OrdinalIgnoreCase)
                && e.DetectionReason?.Contains("anuell", StringComparison.Ordinal) != true
                && ContentKindService.LooksLikeMarkdownDocument(e.Content ?? ""))
            .Select(e => e.Id)
            .ToList();

    /// <summary>Stores the entries as Markdown text; returns how many changed.</summary>
    public int MarkAsMarkdown(IEnumerable<long> ids, string reason = MarkdownRecheckReason)
    {
        var wanted = ids.ToHashSet();
        var changed = 0;
        foreach (var vm in Entries.Where(e => wanted.Contains(e.Id)).ToList())
        {
            _db.SetType(vm.Id, EntryType.Text, null, ContentKindService.MarkdownKind, reason);
            vm.SetType(EntryType.Text, null, ContentKindService.MarkdownKind, reason);
            changed++;
        }
        if (changed > 0) _view.Refresh();
        return changed;
    }

    public void UpdateOcr(long id, string ocrText)
    {
        _db.UpdateOcr(id, ocrText);
        var vm = Entries.FirstOrDefault(e => e.Id == id);
        if (vm != null)
        {
            vm.Entry.OcrText = ocrText;
            vm.RefreshPreview();
        }
    }

    public void UpdateUrlPreview(long id, string? title, byte[]? favicon)
    {
        var vm = Entries.FirstOrDefault(e => e.Id == id);
        if (vm == null) return;
        vm.Entry.UrlTitle = title;
        vm.Entry.UrlFavicon = favicon;
        _db.UpdateUrlMetadata(id, title, favicon);
        vm.RefreshFavicon();
        vm.RefreshPreview();
    }

    private DateTime _groupingDate = DateTime.Today;

    public void RefreshTimestamps(IEnumerable<EntryViewModel>? entries = null)
    {
        foreach (var vm in (entries ?? Entries).ToList())
            vm.RefreshTimestamp();

        // Regrouping rebuilds the whole list; date groups only change when the day changes.
        if (DateTime.Today == _groupingDate) return;
        _groupingDate = DateTime.Today;
        _view.Refresh();
    }

    private bool Filter(object obj)
    {
        if (obj is not EntryViewModel vm) return false;

        if (_typeFilter.HasValue && vm.Type != _typeFilter.Value) return false;

        if (_showPinnedOnly && !vm.IsPinned) return false;

        if (_showMarkdownOnly && vm.BadgeText != "MD") return false;


        var q = _searchText.Trim();
        if (string.IsNullOrWhiteSpace(q)) return true;

        if (q.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            var typeStr = q[5..];
            return vm.Type.ToString().Contains(typeStr, StringComparison.OrdinalIgnoreCase);
        }
        if (q.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
        {
            var kindStr = q[5..];
            return (vm.ContentKind?.Contains(kindStr, StringComparison.OrdinalIgnoreCase) ?? false)
                || vm.BadgeText.Equals(kindStr, StringComparison.OrdinalIgnoreCase);
        }
        if (q.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
        {
            var domain = q[7..];
            return vm.Type == EntryType.Url
                && (vm.Content?.Contains(domain, StringComparison.OrdinalIgnoreCase) ?? false);
        }
        if (q.Equals("pinned:true", StringComparison.OrdinalIgnoreCase))  return vm.IsPinned;
        if (q.Equals("pinned:false", StringComparison.OrdinalIgnoreCase)) return !vm.IsPinned;

        if (_ftsMatchIds != null)
            return _ftsMatchIds.Contains(vm.Id);

        if (_isRegexSearch)
        {
            var rx = _searchRegex;
            return rx != null
                && ((vm.Content != null && rx.IsMatch(vm.Content))
                    || (vm.Entry.OcrText != null && rx.IsMatch(vm.Entry.OcrText))
                    || (vm.Entry.UrlTitle != null && rx.IsMatch(vm.Entry.UrlTitle))
                    || (vm.Language != null && rx.IsMatch(vm.Language)));
        }

        return (vm.Content?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Entry.OcrText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Entry.UrlTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Language?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
