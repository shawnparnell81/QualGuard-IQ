using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// The stored content type comes from the file extension (a fixed list), never from what the browser claims, and common formats
/// must start with the bytes that format always starts with. A web page renamed to .pdf, or a program renamed to .png, is refused.
/// </summary>
public static class FileSignatures
{
    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".tif"] = "image/tiff", [".tiff"] = "image/tiff", [".txt"] = "text/plain", [".csv"] = "text/csv",
        [".json"] = "application/json", [".xml"] = "application/xml", [".zip"] = "application/zip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".doc"] = "application/msword", [".xls"] = "application/vnd.ms-excel",
        [".dwg"] = "image/vnd.dwg", [".dxf"] = "image/vnd.dxf", [".step"] = "model/step", [".stp"] = "model/step", [".igs"] = "model/iges", [".iges"] = "model/iges",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime", [".webm"] = "video/webm",
    };

    /// <summary>How many bytes from the start of the file <see cref="Matches"/> needs.</summary>
    public const int HeaderLength = 1024;

    public static string ContentTypeFor(string ext) => Types.TryGetValue(ext, out var t) ? t : "application/octet-stream";

    /// <summary>Plain-language name of the format, for the refusal message.</summary>
    public static string Describe(string ext) => ext.TrimStart('.').ToUpperInvariant();

    /// <summary>True when the first bytes fit the extension. Formats without a fixed signature (text, CAD) always pass.</summary>
    public static bool Matches(string ext, ReadOnlySpan<byte> head) => ext.ToLowerInvariant() switch
    {
        // PDF readers accept a little leading junk before the %PDF marker
        ".pdf" => head.IndexOf("%PDF-"u8) is >= 0 || head.IndexOf("%PDF"u8) is >= 0 and < 16,
        ".png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
        ".webp" => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        ".tif" or ".tiff" => head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8),
        ".docx" or ".xlsx" or ".pptx" or ".zip" => head.StartsWith("PK\x03\x04"u8) || head.StartsWith("PK\x05\x06"u8),
        ".doc" or ".xls" => head.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
        ".mp4" or ".m4v" or ".mov" => head.Length >= 8 && IsMediaBox(head[4..8]),
        ".webm" => head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        _ => true
    };

    static readonly string[] MediaBoxes = ["ftyp", "moov", "mdat", "wide", "free", "skip", "pnot"];
    static bool IsMediaBox(ReadOnlySpan<byte> box) => MediaBoxes.Contains(Encoding.ASCII.GetString(box));
}
