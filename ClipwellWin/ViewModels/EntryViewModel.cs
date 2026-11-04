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

    public long PinOrder
    {
        get => Entry.PinOrder;
        set { Entry.PinOrder = value; OnPropertyChanged(); }
    }

    public int UseCount => Entry.UseCount;
    public DateTime? LastUsedAt => Entry.LastUsedAt;

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

    // Zweite Zeile im Popup: für URLs die echte URL statt nochmals den Titel
    public string SublineText
    {
        get
        {
            if (Entry.Type == EntryType.Url)
            {
                var url = Entry.Content ?? "";
                return url.Length > 200 ? url[..200] : url;
            }
            return PreviewText;
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
            if (diff.TotalDays < 7)
            {
                var days = (int)diff.TotalDays;
                return days == 1 ? "vor 1 Tag" : $"vor {days} Tagen";
            }
            return Entry.Timestamp.ToString("dd.MM.yyyy");
        }
    }

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnail != null) return _thumbnail;
            var data = Entry.ThumbnailData ?? Entry.ImageData;
            if (data == null) return null;
            _thumbnail = LoadBitmapImage(data, 80);
            return _thumbnail;
        }
    }

    private static BitmapImage? LoadBitmapImage(byte[] data, int decodeWidth)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = new MemoryStream(data);
            img.DecodePixelWidth = decodeWidth;
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
            _favicon = Entry.UrlFavicon == null ? null : LoadBitmapImage(Entry.UrlFavicon, 16);
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
                return Entry.ContentKind; // BadgeText ist immer "IMG"; nur zeigen wenn ContentKind gesetzt
            if (Entry.Type == EntryType.Code)
                return null;
            // Nur anzeigen wenn es sich von BadgeText unterscheidet (z.B. "TEXT" + "Python", nicht "MARKDOWN" + "MARKDOWN")
            var detail = Entry.ContentKind ?? Entry.Language;
            return detail == BadgeText ? null : detail;
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

    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(SublineText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailBadgeText));
    }
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

    public void MarkUsed(DateTime usedAt)
    {
        Entry.UseCount++;
        Entry.LastUsedAt = usedAt;
        OnPropertyChanged(nameof(UseCount));
        OnPropertyChanged(nameof(LastUsedAt));
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
