using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ClipwellWin.ViewModels;

public enum UrlPreviewState { Unknown, Loading, Loaded, Failed }

public class EntryViewModel : ViewModelBase
{
    public ClipboardEntry Entry { get; }

    public EntryViewModel(ClipboardEntry entry) => Entry = entry;

    public long Id => Entry.Id;
    public EntryType Type => Entry.Type;
    public string? Content => Entry.Content;
    public string? Language => Entry.Language;
    public string? HexColor => Entry.HexColor;
    public string? ContentKind => Entry.ContentKind;
    public string? DetectionReason => Entry.DetectionReason;
    public bool IsPinned
    {
        get => Entry.IsPinned;
        set { Entry.IsPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(GroupLabel)); }
    }

    public string GroupLabel
    {
        get
        {
            if (Entry.IsPinned) return "Gepinnt";
            var today = DateTime.Today;
            var d = Entry.Timestamp.Date;
            if (d == today)              return "Heute";
            if (d == today.AddDays(-1))  return "Gestern";
            if (d > today.AddDays(-7))   return "Diese Woche";
            return "Früher";
        }
    }
    public DateTime Timestamp => Entry.Timestamp;

    private UrlPreviewState _urlState = UrlPreviewState.Unknown;
    public UrlPreviewState UrlState
    {
        get => _urlState;
        set
        {
            if (_urlState == value) return;
            _urlState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUrlLoading));
            OnPropertyChanged(nameof(IsUrlFailed));
        }
    }
    public bool IsUrlLoading => _urlState == UrlPreviewState.Loading;
    public bool IsUrlFailed  => _urlState == UrlPreviewState.Failed;

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set { if (_isHovered != value) { _isHovered = value; OnPropertyChanged(); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string PreviewText
    {
        get
        {
            var text = Entry.PreviewText;
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > 200 ? text[..200] : text;
        }
    }

    public string RelativeTime
    {
        get
        {
            var diff = DateTime.Now - Entry.Timestamp;
            if (diff.TotalSeconds < 60) return "gerade eben";
            if (diff.TotalMinutes < 60) return $"vor {(int)diff.TotalMinutes} Min.";
            if (diff.TotalHours < 24) return $"vor {(int)diff.TotalHours} Std.";
            if (diff.TotalDays < 7) return $"vor {(int)diff.TotalDays} Tagen";
            return Entry.Timestamp.ToString("dd.MM.yyyy");
        }
    }

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnail != null || Entry.ImageData == null) return _thumbnail;
            _thumbnail = LoadThumbnail(Entry.ImageData);
            return _thumbnail;
        }
    }

    private static BitmapImage? LoadThumbnail(byte[] data)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(data);
            img.DecodePixelWidth = 80;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private BitmapImage? _favicon;
    private byte[]? _lastFavicon;
    public BitmapImage? Favicon
    {
        get
        {
            if (Entry.UrlFavicon == _lastFavicon) return _favicon;
            _lastFavicon = Entry.UrlFavicon;
            _favicon = Entry.UrlFavicon == null ? null : LoadImage(Entry.UrlFavicon);
            return _favicon;
        }
    }

    public string? UrlTitle => Entry.UrlTitle;

    public string BadgeText => ContentKindService.PrimaryBadge(Entry);

    public string? DetailBadgeText
    {
        get
        {
            if (Entry.Type == EntryType.Url)
                return Entry.ContentKind;
            if (Entry.Type == EntryType.Image)
                return Entry.ContentKind ?? "IMG";
            return Entry.ContentKind ?? Entry.Language;
        }
    }

    private (int w, int h)? _imagePixels;
    public (int Width, int Height) ImagePixels
    {
        get
        {
            if (_imagePixels.HasValue) return _imagePixels.Value;
            if (Entry.ImageData == null) return (0, 0);
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = new MemoryStream(Entry.ImageData);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                _imagePixels = (img.PixelWidth, img.PixelHeight);
            }
            catch { _imagePixels = (0, 0); }
            return _imagePixels.Value;
        }
    }

    public long ImageFileSizeBytes => Entry.ImageData?.Length ?? 0;
    public string ImageSizeLabel
    {
        get
        {
            var bytes = ImageFileSizeBytes;
            if (bytes == 0) return "";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024.0):F1} MB";
        }
    }

    private static BitmapImage? LoadImage(byte[] data)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(data);
            img.DecodePixelWidth = 16;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    private Brush? _hexBrush;
    private bool _hexBrushLoaded;
    public Brush? HexBrush
    {
        get
        {
            if (_hexBrushLoaded) return _hexBrush;
            _hexBrushLoaded = true;
            if (string.IsNullOrEmpty(HexColor)) return null;
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexColor));
                b.Freeze();
                _hexBrush = b;
            }
            catch { }
            return _hexBrush;
        }
    }

    public void RefreshPreview() => OnPropertyChanged(nameof(PreviewText));
    public void RefreshTimestamp()
    {
        OnPropertyChanged(nameof(Timestamp));
        OnPropertyChanged(nameof(RelativeTime));
        OnPropertyChanged(nameof(GroupLabel));
    }

    public void SetTimestamp(DateTime timestamp)
    {
        Entry.Timestamp = timestamp;
        RefreshTimestamp();
    }

    public void SetType(EntryType type, string? language, string? kind, string reason)
    {
        Entry.Type = type;
        Entry.Language = language;
        Entry.ContentKind = kind;
        Entry.DetectionReason = reason;
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(ContentKind));
        OnPropertyChanged(nameof(DetectionReason));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailBadgeText));
    }

    public void SetContent(string content, string? kind, string reason, string? language = null)
    {
        Entry.Content = content;
        Entry.Language = language;
        Entry.ContentKind = kind;
        Entry.DetectionReason = reason;
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(ContentKind));
        OnPropertyChanged(nameof(DetectionReason));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailBadgeText));
    }

    public void RefreshFavicon()
    {
        _lastFavicon = null;
        _favicon = null;
        OnPropertyChanged(nameof(Favicon));
        OnPropertyChanged(nameof(UrlTitle));
        OnPropertyChanged(nameof(PreviewText));
    }
}
