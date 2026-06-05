using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                _view.Refresh();
        }
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
        var toDelete = Entries.Where(e => e.IsSelected).ToList();
        foreach (var vm in toDelete)
        {
            _db.Delete(vm.Id);
            Entries.Remove(vm);
        }
        IsBulkMode = false;
    }

    public void PinSelected(bool pin)
    {
        foreach (var vm in Entries.Where(e => e.IsSelected))
        {
            vm.IsPinned = pin;
            _db.SetPinned(vm.Id, pin);
        }
        _view.Refresh();
        OnPropertyChanged(nameof(SelectedCount));
    }

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
        _view = CollectionViewSource.GetDefaultView(Entries);
        _view.Filter = Filter;
        _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.IsPinned), ListSortDirection.Descending));
        _view.SortDescriptions.Add(new SortDescription(nameof(EntryViewModel.Timestamp), ListSortDirection.Descending));
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntryViewModel.GroupLabel)));
    }

    public void LoadFromDb()
    {
        Entries.Clear();
        foreach (var entry in _db.LoadAll())
            Entries.Add(new EntryViewModel(entry));
    }

    public void AddEntry(ClipboardEntry entry)
    {
        if (Entries.Count > 0)
        {
            var last = Entries.OrderByDescending(e => e.Timestamp).First();
            if (IsDuplicateOfLatest(last.Entry, entry)) return;
        }

        var id = _db.Insert(entry);
        entry.Id = id;
        Entries.Insert(0, new EntryViewModel(entry));

        _db.Purge(_maxHistoryItems());

        if (_maxAgeInDays() > 0)
            _db.PurgeByAge(_maxAgeInDays());

        var dbIds = _db.LoadAll().Select(e => e.Id).ToHashSet();
        var toRemove = Entries.Where(e => !dbIds.Contains(e.Id)).ToList();
        foreach (var r in toRemove) Entries.Remove(r);
    }

    private static bool IsDuplicateOfLatest(ClipboardEntry latest, ClipboardEntry next)
    {
        if (latest.Type != next.Type) return false;
        if (next.Type == EntryType.Image)
            return latest.ImageData != null
                && next.ImageData != null
                && latest.ImageData.SequenceEqual(next.ImageData);

        return string.Equals(latest.Content, next.Content, StringComparison.Ordinal);
    }

    public void TogglePin(EntryViewModel vm)
    {
        vm.IsPinned = !vm.IsPinned;
        _db.SetPinned(vm.Id, vm.IsPinned);
        _view.Refresh();
    }

    public void Delete(EntryViewModel vm)
    {
        _db.Delete(vm.Id);
        Entries.Remove(vm);
    }

    public void SecureDelete(EntryViewModel vm, DatabaseService db)
    {
        Entries.Remove(vm);
        _ = System.Threading.Tasks.Task.Run(() => db.SecureDelete(vm.Id));
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
        var reason = $"Manuell als {type} behandelt.";
        vm.SetType(type, language, reason);
        _db.SetType(vm.Id, type, language, reason);
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
        OnPropertyChanged(nameof(TypeFilter));
        OnPropertyChanged(nameof(ShowPinnedOnly));
        OnPropertyChanged(nameof(SearchText));
        _view.Refresh();
    }

    private bool Filter(object obj)
    {
        if (obj is not EntryViewModel vm) return false;

        if (_typeFilter.HasValue && vm.Type != _typeFilter.Value) return false;

        if (_showPinnedOnly && !vm.IsPinned) return false;

        var q = _searchText.Trim();
        if (string.IsNullOrWhiteSpace(q)) return true;

        // Präfix-Syntax: type:url  kind:json  domain:github.com  pinned:true
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

        return (vm.Content?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Entry.OcrText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Entry.UrlTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (vm.Language?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
