namespace ClipwellWin.Models;

public enum EntryType
{
    Text = 0,
    Image = 1,
    Url = 2,
    Code = 3,
    Color = 4,
}

public class ClipboardEntry
{
    public long Id { get; set; }
    public EntryType Type { get; set; }
    public string? Content { get; set; }
    public byte[]? ImageData { get; set; }
    public string? OcrText { get; set; }
    public string? Language { get; set; }
    public string? UrlTitle { get; set; }
    public byte[]? UrlFavicon { get; set; }
    public string? HexColor { get; set; }
    public string? ContentKind { get; set; }
    public string? DetectionReason { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsPinned { get; set; }

    public string PreviewText => Type switch
    {
        EntryType.Image => OcrText?.Length > 0 ? OcrText : "(Bild)",
        EntryType.Url => UrlTitle?.Length > 0 ? UrlTitle : Content ?? "",
        _ => Content ?? ""
    };
}
