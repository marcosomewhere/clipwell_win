using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using ClipwellWin.Models;
using ClipwellWin.Services;

namespace ClipwellWin.ViewModels;

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
        catch
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
            return;
        }
        _ftsMatchIds = _db.SearchFts(q);
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
        "Neueste" => "Sort",
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

    public void DeleteSelected()
    {
        DeleteEntries(Entries.Where(e => e.IsSelected).ToList());
    }

    public void DeleteEntries(List<EntryViewModel> entries)
    {
        foreach (var vm in entries)
        {
            _db.Delete(vm.Id);
            Entries.Remove(vm);
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

    public List<ClipboardEntry> GetSelectedEntries()
        => Entries.Where(e => e.IsSelected).Select(e => e.Entry).ToList();

    private EntryViewModel? _selected;
    public EntryViewModel? SelectedEntry
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public PopupViewModel(DatabaseService db, Func<int>? maxHistoryItems = null, Func<int>? maxAgeInDays = null)
    {
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

    public void AddEntry(ClipboardEntry entry)
    {
        var duplicates = Entries
            .Where(e => IsDuplicate(e.Entry, entry))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.Timestamp)
            .ToList();
        if (duplicates.Count > 0)
        {
            var kept = duplicates[0];
            entry.Id = kept.Id;
            if (entry.IsPinned && !kept.IsPinned)
            {
                var pinOrder = _db.SetPinned(kept.Id, true);
                kept.IsPinned = true;
                kept.PinOrder = pinOrder;
            }
            kept.SetTimestamp(entry.Timestamp);
            _db.UpdateTimestamp(kept.Id, entry.Timestamp);

            foreach (var duplicate in duplicates.Skip(1))
            {
                _db.Delete(duplicate.Id);
                Entries.Remove(duplicate);
            }

            _view.Refresh();
            return;
        }

        var id = _db.Insert(entry);
        entry.Id = id;
        Entries.Insert(0, new EntryViewModel(entry));

        var purged = _db.Purge(_maxHistoryItems());
        var agePurged = _maxAgeInDays() > 0 ? _db.PurgeByAge(_maxAgeInDays()) : 0;

        if (purged + agePurged > 0)
        {
            var dbIds = _db.LoadAllIds();
            var toRemove = Entries.Where(e => !dbIds.Contains(e.Id)).ToList();
            foreach (var r in toRemove) Entries.Remove(r);
        }
    }

    private static bool IsDuplicate(ClipboardEntry existing, ClipboardEntry next)
    {
        if (existing.Type != next.Type) return false;

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
        return _sortMode switch
        {
            "Häufig"  => b.UseCount.CompareTo(a.UseCount),
            "Selten"  => a.UseCount.CompareTo(b.UseCount),
            "Älteste" => a.Timestamp.CompareTo(b.Timestamp),
            _         => b.Timestamp.CompareTo(a.Timestamp),
        };
    }

    public void Delete(EntryViewModel vm)
    {
        _db.Delete(vm.Id);
        Entries.Remove(vm);
    }

    public void SecureDelete(EntryViewModel vm)
    {
        Entries.Remove(vm);
        _ = System.Threading.Tasks.Task.Run(() => _db.SecureDelete(vm.Id));
    }

    public void AddQuickNote(string text)
    {
        var entry = new ClipboardEntry
        {
            Type = EntryType.Text,
            Content = text,
            IsPinned = true,
            Timestamp = DateTime.Now,
            ContentKind = "NOTE",
            DetectionReason = "Schnellnotiz",
        };
        var id = _db.Insert(entry);
        entry.Id = id;
        Entries.Insert(0, new EntryViewModel(entry));
        _view.Refresh();
    }

    public EntryViewModel? LatestEntry()
        => Entries.OrderByDescending(e => e.Timestamp).FirstOrDefault();

    public void SetType(EntryViewModel vm, EntryType type)
    {
        string? language = type == EntryType.Code
            ? SyntaxService.DetectLanguage(vm.Content ?? "", CodeDetectionMode.Aggressive) ?? "Code"
            : null;
        // ContentKind ist ein Badge, nicht der Sprachname – separat ableiten.
        string? kind = type == EntryType.Code ? "CODE" : null;
        var reason = $"Manuell als {type} behandelt.";
        vm.SetType(type, language, kind, reason);
        _db.SetType(vm.Id, type, language, kind, reason);
        _view.Refresh();
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

    public void ClearFilters()
    {
        _typeFilter = null;
        _showPinnedOnly = false;
        _searchText = "";
        _isRegexSearch = false;
        OnPropertyChanged(nameof(TypeFilter));
        OnPropertyChanged(nameof(ShowPinnedOnly));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(IsRegexSearch));
        _view.Refresh();
    }

    public void RefreshTimestamps(IEnumerable<EntryViewModel>? entries = null)
    {
        foreach (var vm in (entries ?? Entries).ToList())
            vm.RefreshTimestamp();
        _view.Refresh();
    }

    private bool Filter(object obj)
    {
        if (obj is not EntryViewModel vm) return false;

        if (_typeFilter.HasValue && vm.Type != _typeFilter.Value) return false;

        if (_showPinnedOnly && !vm.IsPinned) return false;


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
            return vm.ContentKind?.Contains(kindStr, StringComparison.OrdinalIgnoreCase) ?? false;
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
