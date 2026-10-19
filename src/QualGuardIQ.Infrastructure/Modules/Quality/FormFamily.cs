using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>
/// Gives forms that were installed or built before form looks existed (no PageSetup.Theme) the look of their family on screen and in print:
/// from the library template key and category they were installed from, else from the template's catalog category, else from its title.
/// </summary>
public static class FormFamily
{
    public static async Task<FormDefinition> ApplyAsync(AppDbContext db, Guid templateId, FormDefinition d, CancellationToken ct)
    {
        if (d.Page.Theme is { Length: > 0 } t && FormThemes.Themes.Contains(t)) return d;
        d.Page.Theme = await InferAsync(db, templateId, ct);
        return d;
    }

    /// <summary>The family a form template belongs to by where it came from and what it is called (ignores any look stored on a revision).</summary>
    public static async Task<string> InferAsync(AppDbContext db, Guid templateId, CancellationToken ct)
    {
        var item = await db.TemplatePackageItems.AsNoTracking().Where(i => i.DocumentId == templateId).OrderByDescending(i => i.InstalledAt)
            .Select(i => new { i.TemplateKey, i.CategoryCode }).FirstOrDefaultAsync(ct);
        if (item is not null) return FormThemes.Infer(item.TemplateKey, item.CategoryCode);
        var doc = await db.Documents.AsNoTracking().Where(x => x.Id == templateId).Select(x => new { x.Title, x.CategoryId }).FirstOrDefaultAsync(ct);
        var category = doc?.CategoryId is { } cid ? await db.TemplateCategories.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Code).FirstOrDefaultAsync(ct) : null;
        return FormThemes.Infer(doc?.Title, category);
    }
}
