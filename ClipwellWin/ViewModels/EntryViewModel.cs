using System.Windows.Media.Imaging;
using ClipwellWin.Models;
using ClipwellWin.Services;
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
        set { Entry.IsPinned = value; OnPropertyChanged(); OnPropertyChanged(nameof(GroupLabel)); OnPropertyChanged(nameof(PinSortKey)); }
    }

    public long PinOrder
    {
        get => Entry.PinOrder;
        set { Entry.PinOrder = value; OnPropertyChanged(); OnPropertyChanged(nameof(PinSortKey)); }
    }

    // Shared by popup and pinboard: most recently pinned (or dragged to top) first; legacy pins without PinOrder fall back to their timestamp.
    public long PinSortKey => !Entry.IsPinned ? 0 : Entry.PinOrder > 0 ? Entry.PinOrder : Entry.Timestamp.Ticks;

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

    private string? _hotkeyText;
    /// <summary>Assigned global hotkey, e.g. "Ctrl+Alt+1"; null without one.</summary>
    public string? HotkeyText
    {
        get => _hotkeyText;
        set { if (_hotkeyText != value) { _hotkeyText = value; OnPropertyChanged(); } }
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

    /// <summary>
    /// Bold first line in the popup: the page title of a URL, "Bild 1280×720" for images, otherwise none
    /// (plain entries show their text in the second line only).
    /// </summary>
    public string? HeadlineText => Entry.Type switch
    {
        EntryType.Url => string.IsNullOrWhiteSpace(Entry.UrlTitle) ? null : Entry.UrlTitle,
        EntryType.Image => ImageHeadline(Entry.ImageSize),
        _ => FileContentReader.Headline(Entry.SourceUrl),
    };

    public static string ImageHeadline((int Width, int Height) size)
        => size.Width > 0 && size.Height > 0 ? $"Bild {size.Width}×{size.Height}" : "Bild";

    /// <summary>Second line: the URL itself, the recognized text of an image in quotes, otherwise the content.</summary>
    public string SublineText
    {
        get
        {
            switch (Entry.Type)
            {
                case EntryType.Url:
                    var url = Entry.Content ?? "";
                    return url.Length > 200 ? url[..200] : url;
                case EntryType.Image:
                    return OcrSubline(Entry.OcrText);
                default:
                    return PreviewText;
            }
        }
    }

    public static string OcrSubline(string? ocr)
    {
        if (string.IsNullOrWhiteSpace(ocr)) return "Kein Text erkannt";
        var flat = string.Join(' ', ocr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length > 200) flat = flat[..200];
        return $"„{flat}“";
    }

    public string RelativeTime => FormatTime(Entry.Timestamp, DateTime.Now);

    private static readonly System.Globalization.CultureInfo De = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Today relative ("vor 6 Min."), yesterday and this week with weekday and clock time, older with the date.
    /// Matches the list groups, so an entry under "Gestern" never says "vor 40 Min.".
    /// </summary>
    public static string FormatTime(DateTime timestamp, DateTime now)
    {
        var day = timestamp.Date;
        if (day >= now.Date)
        {
            var diff = now - timestamp;
            if (diff.TotalSeconds < 60) return "gerade eben";
            if (diff.TotalMinutes < 60) return $"vor {(int)diff.TotalMinutes} Min.";
            return $"vor {(int)diff.TotalHours} Std.";
        }
        if (day == now.Date.AddDays(-1)) return "Gestern, " + timestamp.ToString("HH:mm", De);
        if (day > now.Date.AddDays(-7)) return timestamp.ToString("ddd", De).TrimEnd('.') + ", " + timestamp.ToString("HH:mm", De);
        return timestamp.ToString("dd.MM.yyyy", De);
    }

    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnail != null) return _thumbnail;
            var data = Entry.ThumbnailData ?? Entry.ImageData;
            if (data == null) return null;
            _thumbnail = ImageUtils.LoadBitmap(data, 80);
            return _thumbnail;
        }
    }

    private BitmapImage? _favicon;
    private byte[]? _lastFavicon;
    public BitmapImage? Favicon
    {
        get
        {
            if (Entry.UrlFavicon == _lastFavicon) return _favicon;
            _lastFavicon = Entry.UrlFavicon;
            _favicon = Entry.UrlFavicon == null ? null : ImageUtils.LoadBitmap(Entry.UrlFavicon, 16);
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
            if (BadgeText == "MD")
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
            var img = ImageUtils.LoadBitmap(Entry.ImageData);
            _imagePixels = img == null ? (0, 0) : (img.PixelWidth, img.PixelHeight);
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

    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(HeadlineText));
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
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(SublineText));
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
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(SublineText));
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
        OnPropertyChanged(nameof(HeadlineText));
        OnPropertyChanged(nameof(SublineText));
    }
}
