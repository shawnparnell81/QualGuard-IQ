using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Notification email: the plain-text body every client can read, plus an HTML view with the QualGuard IQ logo (inline image,
/// so nothing is fetched from the internet) and a brand-blue button for the record link. All text is HTML-encoded.
/// </summary>
public static class BrandedEmail
{
    public const string LogoContentId = "qualguard-iq-logo";

    public static MailMessage Create(string from, string to, string appName, string title, string? body, string? link)
    {
        var text = $"{body}\n\n{link}".Trim();
        var msg = new MailMessage(from, to, $"[{appName}] {title}", text) { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        var html = AlternateView.CreateAlternateViewFromString(Html(appName, title, body, link), Encoding.UTF8, MediaTypeNames.Text.Html);
        var logo = new LinkedResource(new MemoryStream(BrandLogo.EmailPng), "image/png") { ContentId = LogoContentId, TransferEncoding = TransferEncoding.Base64 };
        logo.ContentType.Name = "qualguard-iq.png";
        html.LinkedResources.Add(logo);
        msg.AlternateViews.Add(html);
        return msg;
    }

    /// <summary>Adds a note to both the plain-text and the HTML view (used when a report file could not be attached).</summary>
    public static void AppendNote(MailMessage msg, string note)
    {
        msg.Body += "\n\n" + note;
        var html = msg.AlternateViews.FirstOrDefault(v => v.ContentType.MediaType == MediaTypeNames.Text.Html);
        if (html is null) return;
        if (html.ContentStream.CanSeek) html.ContentStream.Position = 0;
        var markup = new StreamReader(html.ContentStream, Encoding.UTF8).ReadToEnd();
        var resources = html.LinkedResources.ToList();
        html.LinkedResources.Clear();
        msg.AlternateViews.Remove(html);
        var marker = "<!--note-->";
        var updated = AlternateView.CreateAlternateViewFromString(markup.Replace(marker, $"<p style=\"margin:16px 0 0;color:#5B6676;font-size:13px\">{E(note)}</p>{marker}"), Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (var r in resources) updated.LinkedResources.Add(r);
        msg.AlternateViews.Add(updated);
        html.Dispose();
    }

    static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    static string Html(string appName, string title, string? body, string? link)
    {
        var paragraphs = string.Join("", (body ?? "").Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p style=\"margin:0 0 12px;line-height:1.5\">{E(p).Replace("\n", "<br>")}</p>"));
        var button = string.IsNullOrWhiteSpace(link) ? "" :
            $"<p style=\"margin:20px 0 0\"><a href=\"{E(link)}\" style=\"display:inline-block;background:{BrandLogo.BlueHex};color:#FFFFFF;text-decoration:none;font-weight:600;padding:10px 18px;border-radius:6px\">Open in {E(appName)}</a></p>" +
            $"<p style=\"margin:12px 0 0;font-size:12px;color:#5B6676;word-break:break-all\">{E(link)}</p>";
        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{E(title)}</title></head>
            <body style="margin:0;padding:0;background:#F4F6F8">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#F4F6F8;padding:24px 12px">
            <tr><td align="center">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#FFFFFF;border:1px solid #E1E5EA;border-top:4px solid {BrandLogo.BlueHex};border-radius:8px;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{BrandLogo.CharcoalHex};font-size:14px">
            <tr><td style="padding:24px 28px 8px"><img src="cid:{LogoContentId}" width="200" alt="QualGuard IQ" style="display:block;border:0;width:200px;height:auto"></td></tr>
            <tr><td style="padding:8px 28px 28px">
            <h1 style="margin:0 0 14px;font-size:18px;line-height:1.3;color:{BrandLogo.CharcoalHex}">{E(title)}</h1>
            {paragraphs}{button}<!--note-->
            </td></tr>
            </table>
            <p style="margin:14px 0 0;font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:12px;color:#5B6676">You receive this because email copies are on for your {E(appName)} notifications.</p>
            </td></tr></table>
            </body></html>
            """;
    }
}
