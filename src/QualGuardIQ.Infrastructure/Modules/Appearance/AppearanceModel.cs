using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Appearance;

/// <summary>The palette a form family uses: palette1 (green and blue), palette2 (slate and blue) or custom with its own edge and accent colours.</summary>
public sealed record FamilyPalette(string Palette, string? Edge = null, string? Accent = null);

/// <summary>Appearance values as stored and edited. Null values inherit (personal from organization, organization from the built-in look).</summary>
public sealed record AppearanceValues(string? Mode, string? PrimaryColor, string? AccentColor, Dictionary<string, FamilyPalette>? FormPalettes);

/// <summary>Built-in look, validation and resolution of appearance settings.</summary>
public static partial class AppearanceModel
{
    public const string Blue = "#1257C4", Green = "#0E8550", Slate = "#334155";
    public static readonly string[] Modes = ["light", "dark", "system"];
    public static readonly string[] Palettes = ["palette1", "palette2", "custom"];

    /// <summary>Palette colours: the edge (section bars, badges, rules) and the accent (numbers, callouts, links).</summary>
    public static (string Edge, string Accent) Colours(string palette) => palette == "palette1" ? (Green, Blue) : (Slate, Blue);

    /// <summary>Checklists, investigations and plans use the green-and-blue palette; certificates, reports, worksheets and registers the slate-and-blue one.</summary>
    public static string DefaultPalette(string family) => family is "checklist" or "investigation" or "plan" ? "palette1" : "palette2";

    public static Dictionary<string, FamilyPalette> DefaultFormPalettes() => FormThemes.Themes.ToDictionary(f => f, f => new FamilyPalette(DefaultPalette(f)));

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex Hex();

    public static void Validate(AppearanceValues v)
    {
        if (v.Mode is not null && !Modes.Contains(v.Mode)) throw new AppException("Mode must be light, dark or system.");
        if (v.PrimaryColor is not null && !Hex().IsMatch(v.PrimaryColor)) throw new AppException("The primary colour must be a hex colour such as #1257C4.");
        if (v.AccentColor is not null && !Hex().IsMatch(v.AccentColor)) throw new AppException("The accent colour must be a hex colour such as #0E8550.");
        foreach (var (family, p) in v.FormPalettes ?? [])
        {
            if (!FormThemes.Themes.Contains(family)) throw new AppException($"Unknown form family '{family}'.");
            if (!Palettes.Contains(p.Palette)) throw new AppException($"The {family} palette must be palette1, palette2 or custom.");
            if (p.Palette == "custom" && (p.Edge is null || p.Accent is null)) throw new AppException($"Pick both custom colours for the {family} family.");
            if (p.Edge is not null && !Hex().IsMatch(p.Edge) || p.Accent is not null && !Hex().IsMatch(p.Accent)) throw new AppException($"The {family} colours must be hex colours.");
        }
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AppearanceValues? Read(AppearanceSetting? s) => s is null ? null
        : new(s.Mode, s.PrimaryColor, s.AccentColor, JsonSerializer.Deserialize<Dictionary<string, FamilyPalette>>(s.FormPalettes, Json) ?? []);

    /// <summary>Writes values onto a row; custom colours are kept only for custom palettes so the audit trail shows real changes.</summary>
    public static void Write(AppearanceSetting s, AppearanceValues v)
    {
        s.Mode = v.Mode; s.PrimaryColor = v.PrimaryColor?.ToUpperInvariant(); s.AccentColor = v.AccentColor?.ToUpperInvariant();
        var palettes = (v.FormPalettes ?? []).OrderBy(x => Array.IndexOf(FormThemes.Themes, x.Key))
            .ToDictionary(x => x.Key, x => x.Value.Palette == "custom" ? x.Value with { Edge = x.Value.Edge?.ToUpperInvariant(), Accent = x.Value.Accent?.ToUpperInvariant() } : new FamilyPalette(x.Value.Palette));
        s.FormPalettes = JsonSerializer.Serialize(palettes, Json);
    }

    /// <summary>Edge and accent colours a form family prints with: the organization's choice, else the built-in palette (printed copies follow the organization, not a person).</summary>
    public static async Task<(string Edge, string Accent)> PrintColoursAsync(AppDbContext db, string family, CancellationToken ct)
    {
        var org = Read(await db.AppearanceSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == null, ct));
        var p = org?.FormPalettes?.GetValueOrDefault(family) ?? new FamilyPalette(DefaultPalette(family));
        return p.Palette == "custom" && p.Edge is not null && p.Accent is not null ? (p.Edge, p.Accent) : Colours(p.Palette);
    }
}
