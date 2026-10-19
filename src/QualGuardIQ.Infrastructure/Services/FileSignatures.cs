using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// The stored content type comes from the file extension (a fixed list), never from what the browser claims, and common formats
/// must start with the bytes that format always starts with. A web page renamed to .pdf, or a program renamed to .png or .txt, is refused.
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

    /// <summary>
    /// True when the first bytes fit the extension. Formats without a fixed signature (text, CSV, JSON, XML, CAD exchange files) only
    /// pass when they don't start like a program or a known binary format; plain text also may not contain NUL bytes (except UTF-16).
    /// </summary>
    public static bool Matches(string ext, ReadOnlySpan<byte> head) => ext.ToLowerInvariant() switch
    {
        ".pdf" => head.StartsWith("%PDF"u8),
        ".png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
        ".webp" => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        ".tif" or ".tiff" => head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8),
        ".docx" or ".xlsx" or ".pptx" or ".zip" => head.StartsWith("PK\x03\x04"u8) || head.StartsWith("PK\x05\x06"u8),
        ".doc" or ".xls" => head.StartsWith(Ole),
        ".mp4" or ".m4v" or ".mov" => head.Length >= 8 && IsMediaBox(head[4..8]),
        ".webm" => head.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        ".txt" or ".csv" or ".json" or ".xml" => !IsKnownBinary(head) && IsText(head),
        // DWG files start with their version, "AC1015", "AC1032" and so on
        ".dwg" => head.StartsWith("AC"u8) && !IsKnownBinary(head),
        ".dxf" or ".step" or ".stp" or ".igs" or ".iges" => !IsKnownBinary(head),
        _ => true
    };

    static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Programs (Windows, Linux, macOS, Java, WebAssembly) and common binary, archive and document formats. Each signature is
    /// long enough (or has non-printable bytes) that ordinary text can't start with it by accident.</summary>
    static readonly byte[][] Binary =
    [
        [0x7F, 0x45, 0x4C, 0x46], [0xFE, 0xED, 0xFA, 0xCE], [0xFE, 0xED, 0xFA, 0xCF], [0xCE, 0xFA, 0xED, 0xFE], [0xCF, 0xFA, 0xED, 0xFE],
        [0xCA, 0xFE, 0xBA, 0xBE], [0x00, 0x61, 0x73, 0x6D],
        "PK\x03\x04"u8.ToArray(), "PK\x05\x06"u8.ToArray(), [0x1F, 0x8B, 0x08], [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], "Rar!\x1A\x07"u8.ToArray(),
        [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00], "%PDF-"u8.ToArray(), Ole, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A], [0xFF, 0xD8, 0xFF], "GIF87a"u8.ToArray(), "GIF89a"u8.ToArray(),
        "MSCF\0\0\0\0"u8.ToArray(), "ITSF\x03\0\0\0"u8.ToArray(),
    ];

    static bool IsKnownBinary(ReadOnlySpan<byte> head)
    {
        foreach (var sig in Binary) if (head.StartsWith(sig)) return true;
        // Windows programs: "MZ" followed by the binary DOS header (text such as "MZ-1001 notes" is fine)
        if (head.StartsWith("MZ"u8) && head[..Math.Min(head.Length, 64)].IndexOfAnyInRange((byte)0, (byte)8) >= 0) return true;
        // bzip2: "BZh", block size 1-9, then the block magic
        return head.Length >= 10 && head.StartsWith("BZh"u8) && head[3] is >= (byte)'1' and <= (byte)'9' && head[4..10].SequenceEqual("1AY&SY"u8);
    }

    /// <summary>No NUL bytes, unless the file starts with a UTF-16 byte order mark (Excel's "Unicode text" export).</summary>
    static bool IsText(ReadOnlySpan<byte> head) =>
        head.StartsWith(new byte[] { 0xFF, 0xFE }) || head.StartsWith(new byte[] { 0xFE, 0xFF }) || head.IndexOf((byte)0) < 0;

    static readonly string[] MediaBoxes = ["ftyp", "moov", "mdat", "wide", "free", "skip", "pnot"];
    static bool IsMediaBox(ReadOnlySpan<byte> box) => MediaBoxes.Contains(Encoding.ASCII.GetString(box));
}
