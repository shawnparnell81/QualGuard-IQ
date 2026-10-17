using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Infrastructure.Modules.Reports;

public sealed partial class ReportService
{
    private delegate void AddFn(string key, string title, string category, string perm, string description, bool usesDates, Runner run);

    /// <summary>Prompt 4 reports: executive, plant, supplier, customer, COQ, SPC, FMEA and control plan.</summary>
    private void AddAdvanced(AddFn Add, Func<string, string, string, ReportColumn> C, Func<(string, object?)[], Dictionary<string, object?>> R, Func<DateOnly, DateOnly, Func<string, Dictionary<string, object?>>, List<Dictionary<string, object?>>> Months)
    {
        Add("coq.summary", "Cost of quality by month", "Cost of quality", Permissions.CoqView, "Prevention, appraisal, internal and external failure cost per month.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.CoqEntries.AsNoTracking(), Permissions.CoqView, p, ct)).Where(x => x.IncurredOn >= f && x.IncurredOn <= t).Select(x => new { x.IncurredOn, x.Category, x.Amount }).ToListAsync(ct);
            decimal S(string mo, CoqCategory c) => rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo && r.Category == c).Sum(r => r.Amount);
            return ([C("month", "Month", "text"), C("prevention", "Prevention", "number"), C("appraisal", "Appraisal", "number"), C("internal", "Internal failure", "number"), C("external", "External failure", "number"), C("total", "Total", "number")],
                Months(f, t, mo => R([("month", mo), ("prevention", S(mo, CoqCategory.Prevention)), ("appraisal", S(mo, CoqCategory.Appraisal)), ("internal", S(mo, CoqCategory.InternalFailure)), ("external", S(mo, CoqCategory.ExternalFailure)),
                    ("total", rows.Where(r => r.IncurredOn.ToString("yyyy-MM") == mo).Sum(r => r.Amount))])), new("bar", "month", ["internal", "external"]));
        });
        Add("coq.by_party", "Cost of quality by product, supplier and customer", "Cost of quality", Permissions.CoqView, "COQ in the period attributed to each product, supplier and customer.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.CoqEntries.AsNoTracking(), Permissions.CoqView, p, ct)).Where(x => x.IncurredOn >= f && x.IncurredOn <= t)
                .Select(x => new { x.Amount, x.Category, Product = db.Products.Where(z => z.Id == x.ProductId).Select(z => z.Name).FirstOrDefault(), Supplier = db.Suppliers.Where(z => z.Id == x.SupplierId).Select(z => z.LegalName).FirstOrDefault(),
                    Customer = db.Customers.Where(z => z.Id == x.CustomerId).Select(z => z.Name).FirstOrDefault() }).ToListAsync(ct);
            var list = new List<Dictionary<string, object?>>();
            foreach (var (dim, key) in new (string, Func<dynamic, string?>)[] { ("Product", x => x.Product), ("Supplier", x => x.Supplier), ("Customer", x => x.Customer) })
                foreach (var g in rows.GroupBy(x => (string?)key(x)).Where(g => g.Key != null).OrderByDescending(g => g.Sum(x => x.Amount)))
                    list.Add(R([("dimension", dim), ("name", g.Key), ("failure", g.Where(x => x.Category is CoqCategory.InternalFailure or CoqCategory.ExternalFailure).Sum(x => x.Amount)), ("total", g.Sum(x => x.Amount))]));
            return ([C("dimension", "By", "text"), C("name", "Name", "text"), C("failure", "Failure cost", "number"), C("total", "Total", "number")], list, null);
        });
        Add("scrap.trend", "Scrap trend", "Cost of quality", Permissions.CoqView, "Scrap events, quantity and cost per month.", true, async (p, f, t, ct) =>
        {
            var rows = await (await Q(db.ScrapEvents.AsNoTracking(), Permissions.CoqView, p, ct)).Where(x => x.ScrappedOn >= f && x.ScrappedOn <= t).Select(x => new { x.ScrappedOn, x.Quantity, x.TotalCost }).ToListAsync(ct);
            return ([C("month", "Month", "text"), C("events", "Events", "number"), C("qty", "Quantity", "number"), C("cost", "Cost", "number")],
                Months(f, t, mo => { var g = rows.Where(r => r.ScrappedOn.ToString("yyyy-MM") == mo).ToList(); return R([("month", mo), ("events", g.Count), ("qty", g.Sum(x => x.Quantity)), ("cost", g.Sum(x => x.TotalCost))]); }), new("bar", "month", ["cost"]));
        });
        Add("spc.charts", "SPC chart status", "SPC", Permissions.SpcView, "Active charts with sample count, latest violation and capability (Cpk, Ppk) over the last 125 samples.", false, async (p, f, t, ct) =>
        {
            var charts = await (await Q(db.SpcCharts.AsNoTracking(), Permissions.SpcView, p, ct)).Where(c => !c.IsArchived).OrderBy(c => c.Number).ToListAsync(ct);
            var list = new List<Dictionary<string, object?>>();
            foreach (var c in charts)
            {
                var samples = (await db.SpcSamples.AsNoTracking().Where(s => s.ChartId == c.Id).OrderByDescending(s => s.TakenAt).Take(Apqp.SpcService.Window).ToListAsync(ct)).OrderBy(s => s.TakenAt).ToList();
                Apqp.SpcResult? res = null; if (samples.Count > 1) try { res = Apqp.SpcService.Compute(c, samples); } catch (ArgumentException) { }
                list.Add(R([("number", c.Number), ("title", c.Title), ("characteristic", c.Characteristic), ("type", ((Apqp.SpcChartType)c.ChartType).ToString()), ("samples", samples.Count),
                    ("violations", samples.Count(s => s.Violations != null)), ("latest", samples.LastOrDefault()?.Violations ?? (samples.Count == 0 ? "" : "In control")),
                    ("cpk", res?.Capability?.Cpk), ("ppk", res?.Capability?.Ppk), ("_url", $"/spc/{c.Id}")]));
            }
            return ([C("number", "Chart", "text"), C("title", "Title", "text"), C("characteristic", "Characteristic", "text"), C("type", "Type", "text"), C("samples", "Samples", "number"), C("violations", "Out-of-control samples", "number"), C("latest", "Latest", "text"), C("cpk", "Cpk", "number"), C("ppk", "Ppk", "number")], list, new("bar", "number", ["cpk"]));
        });
        Add("pfmea.risks", "PFMEA risk register", "FMEA", Permissions.ApqpView, "Failure modes on released PFMEAs, highest action priority and RPN first, with action status.", false, async (p, f, t, ct) =>
        {
            var pf = (await Q(db.Pfmeas.AsNoTracking(), Permissions.ApqpView, p, ct)).Where(x => x.Status == ApqpStatus.Released || x.Status == ApqpStatus.Draft);
            var rows = await db.PfmeaLines.AsNoTracking().Join(pf, l => l.PfmeaId, d => d.Id, (l, d) => new { l, d.Number, d.Revision, d.Status, d.Id }).OrderByDescending(x => x.l.Priority).ThenByDescending(x => x.l.Rpn).Take(2000).ToListAsync(ct);
            return ([C("pfmea", "PFMEA", "text"), C("status", "Status", "text"), C("mode", "Failure mode", "text"), C("s", "S", "number"), C("o", "O", "number"), C("d", "D", "number"), C("rpn", "RPN", "number"), C("ap", "Action priority", "text"), C("action", "Recommended action", "text"), C("due", "Due", "date"), C("done", "Completed", "date"), C("revisedRpn", "Revised RPN", "number")],
                rows.Select(x => R([("pfmea", $"{x.Number} rev {x.Revision}"), ("status", x.Status.ToString()), ("mode", x.l.FailureMode), ("s", x.l.Severity), ("o", x.l.Occurrence), ("d", x.l.Detection), ("rpn", x.l.Rpn), ("ap", x.l.Priority.ToString()),
                    ("action", x.l.RecommendedAction), ("due", x.l.DueOn), ("done", x.l.CompletedOn), ("revisedRpn", x.l.RevisedRpn), ("_url", $"/apqp/pfmea/{x.Id}")])).ToList(), new("bar", "mode", ["rpn"]));
        });
        Add("controlplan.lines", "Control plan characteristics", "Control plans", Permissions.ApqpView, "Characteristics on released control plans with method, sample, frequency and reaction plan.", false, async (p, f, t, ct) =>
        {
            var cp = (await Q(db.ControlPlans.AsNoTracking(), Permissions.ApqpView, p, ct)).Where(x => x.Status == ApqpStatus.Released);
            var rows = await db.ControlPlanLines.AsNoTracking().Join(cp, l => l.PlanId, d => d.Id, (l, d) => new { l, d.Number, d.Revision, d.Phase, d.Id }).OrderBy(x => x.Number).ThenBy(x => x.l.StepSequence).Take(5000).ToListAsync(ct);
            return ([C("plan", "Control plan", "text"), C("phase", "Phase", "text"), C("step", "Step", "text"), C("characteristic", "Characteristic", "text"), C("special", "Special", "text"), C("method", "Method", "text"), C("sample", "Sample", "text"), C("frequency", "Frequency", "text"), C("reaction", "Reaction plan", "text")],
                rows.Select(x => R([("plan", $"{x.Number} rev {x.Revision}"), ("phase", x.Phase.ToString()), ("step", $"{x.l.StepSequence} {x.l.ProcessStep}"), ("characteristic", x.l.Characteristic), ("special", x.l.SpecialCharacteristic), ("method", x.l.Method),
                    ("sample", x.l.SampleSize), ("frequency", x.l.Frequency), ("reaction", x.l.ReactionPlan), ("_url", $"/apqp/plans/{x.Id}")])).ToList(), null);
        });
        Add("supplier.scorecard", "Supplier scorecard and ranking (8 dimensions)", "Suppliers", Permissions.SuppliersView, "Weighted score on quality, delivery, responsiveness, cost, corrective action, audits, certifications and risk.", true, async (p, f, t, ct) =>
        {
            var rows = await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Apqp.SupplierDevelopmentService>(services).ScorecardAsync(f, t, null, ct);
            return ([C("rank", "Rank", "number"), C("supplier", "Supplier", "text"), C("overall", "Overall", "number"), C("quality", "Quality", "number"), C("delivery", "Delivery", "number"), C("responsiveness", "Responsiveness", "number"), C("cost", "Cost", "number"), C("ca", "Corrective action", "number"), C("audit", "Audits", "number"), C("cert", "Certifications", "number"), C("risk", "Risk", "number")],
                rows.Select(x => R([("rank", x.Rank), ("supplier", $"{x.Code} {x.Name}"), ("overall", x.Overall), ("quality", x.Quality), ("delivery", x.Delivery), ("responsiveness", x.Responsiveness), ("cost", x.Cost), ("ca", x.CorrectiveAction), ("audit", x.AuditResults), ("cert", x.Certifications), ("risk", x.Risk), ("_url", $"/suppliers/{x.SupplierId}")])).ToList(), new("bar", "supplier", ["overall"]));
        });
        Add("customer.quality", "Customer quality report", "Customers", Permissions.ComplaintsView, "Complaints, open complaints, overdue responses and external failure cost per customer.", true, async (p, f, t, ct) =>
        {
            var comp = await (await Q(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, p, ct)).Where(c => c.ReportedOn >= f && c.ReportedOn <= t).Select(c => new { c.CustomerId, c.Status, c.ResponseDueOn, c.ResponseSentOn }).ToListAsync(ct);
            var names = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var cost = await db.CoqEntries.AsNoTracking().Where(x => x.CustomerId != null && x.Category == CoqCategory.ExternalFailure && x.IncurredOn >= f && x.IncurredOn <= t).GroupBy(x => x.CustomerId!.Value).Select(g => new { g.Key, S = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.S, ct);
            return ([C("customer", "Customer", "text"), C("complaints", "Complaints", "number"), C("open", "Open", "number"), C("late", "Responses late", "number"), C("external", "External failure cost", "number")],
                comp.GroupBy(c => c.CustomerId).Select(g => R([("customer", names.GetValueOrDefault(g.Key)), ("complaints", g.Count()), ("open", g.Count(x => x.Status is not (ComplaintStatus.Closed or ComplaintStatus.Cancelled))),
                    ("late", g.Count(x => (x.ResponseSentOn ?? t) > x.ResponseDueOn)), ("external", cost.GetValueOrDefault(g.Key))])).OrderByDescending(x => (int)x["complaints"]!).ToList(), new("bar", "customer", ["complaints"]));
        });
        Add("plant.quality", "Plant quality report", "Performance", Permissions.NcrView, "Per site: NCRs, scrap cost, rework cost, first pass yield and calibration compliance.", true, async (p, f, t, ct) =>
        {
            var ncr = await (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(x => x.DetectedOn >= f && x.DetectedOn <= t).GroupBy(x => x.SiteId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            var ins = await db.Inspections.AsNoTracking().Where(x => x.InspectedOn >= f && x.InspectedOn <= t && x.Status != InspectionStatus.InProgress && x.Status != InspectionStatus.Cancelled).GroupBy(x => x.SiteId)
                .Select(g => new { g.Key, N = g.Count(), P = g.Count(x => x.Status == InspectionStatus.Passed || x.Status == InspectionStatus.Released) }).ToListAsync(ct);
            var scrap = await db.ScrapEvents.AsNoTracking().Where(x => x.ScrappedOn >= f && x.ScrappedOn <= t).GroupBy(x => x.SiteId).Select(g => new { g.Key, S = g.Sum(x => x.TotalCost) }).ToListAsync(ct);
            var sites = await db.Sites.AsNoTracking().Where(s => ncr.Select(x => x.Key).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            return ([C("site", "Site", "text"), C("ncrs", "NCRs", "number"), C("fpy", "First pass yield %", "number"), C("scrap", "Scrap cost", "number")],
                ncr.Select(n => { var i = ins.FirstOrDefault(x => x.Key == n.Key); return R([("site", sites.GetValueOrDefault(n.Key)), ("ncrs", n.N), ("fpy", i is null || i.N == 0 ? null : Math.Round(100m * i.P / i.N, 1)), ("scrap", scrap.FirstOrDefault(x => x.Key == n.Key)?.S ?? 0m)]); }).ToList(), new("bar", "site", ["ncrs"]));
        });
        Add("executive.summary", "Executive quality report", "Performance", Permissions.NcrView, "Open NCRs and CAPAs, complaints and NCRs per month, and cost of quality per month.", true, async (p, f, t, ct) =>
        {
            var n = await (await Q(db.Ncrs.AsNoTracking(), Permissions.NcrView, p, ct)).Where(x => x.DetectedOn >= f && x.DetectedOn <= t).Select(x => x.DetectedOn).ToListAsync(ct);
            var cq = await (await m.TryScopeAsync(db.Complaints.AsNoTracking(), Permissions.ComplaintsView, ct) ?? db.Complaints.Where(x => false)).Where(x => x.ReportedOn >= f && x.ReportedOn <= t).Select(x => x.ReportedOn).ToListAsync(ct);
            var coq = await (await m.TryScopeAsync(db.CoqEntries.AsNoTracking(), Permissions.CoqView, ct) ?? db.CoqEntries.Where(x => false)).Where(x => x.IncurredOn >= f && x.IncurredOn <= t).Select(x => new { x.IncurredOn, x.Amount }).ToListAsync(ct);
            return ([C("month", "Month", "text"), C("ncrs", "NCRs", "number"), C("complaints", "Complaints", "number"), C("coq", "Cost of quality", "number")],
                Months(f, t, mo => R([("month", mo), ("ncrs", n.Count(d => d.ToString("yyyy-MM") == mo)), ("complaints", cq.Count(d => d.ToString("yyyy-MM") == mo)), ("coq", coq.Where(x => x.IncurredOn.ToString("yyyy-MM") == mo).Sum(x => x.Amount))])), new("line", "month", ["ncrs", "complaints"]));
        });
    }

    /// <summary>Simple paginated PDF (Helvetica, landscape A4) of a report table; no external dependencies.</summary>
    public static byte[] ToPdf(ReportResult r, string title)
    {
        const int pageW = 842, pageH = 595, margin = 36, lineH = 12;
        var cols = r.Columns;
        var avail = pageW - 2 * margin;
        var colW = cols.Count == 0 ? avail : avail / cols.Count;
        int maxChars = Math.Max(4, (int)(colW / 4.6));
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        static string Ascii(string s) => new string(s.Select(c => c < 32 ? ' ' : c > 126 ? '?' : c).ToArray());
        string Cell(object? v) => v switch { null => "", DateOnly d => d.ToString("yyyy-MM-dd"), DateTimeOffset dt => dt.ToString("yyyy-MM-dd HH:mm"), decimal m => m.ToString(CultureInfo.InvariantCulture), double d => d.ToString(CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
        var header = cols.Select(c => c.Label).ToList();
        var data = r.Rows.Select(row => cols.Select(c => Cell(row.GetValueOrDefault(c.Key))).ToList()).ToList();
        var perPage = (pageH - 2 * margin - 40) / lineH - 1;
        var pages = new List<string>();
        var pageCount = Math.Max(1, (data.Count + perPage - 1) / perPage);
        for (var pg = 0; pg < pageCount; pg++)
        {
            var sb = new StringBuilder();
            sb.Append(BrandLogo.PdfOps(pageW - margin - BrandLogo.WidthForHeight(20), pageH - margin - 20, 20));
            sb.Append($"BT /F2 12 Tf {margin} {pageH - margin - 12} Td ({Esc(Ascii(title))}) Tj ET\n");
            sb.Append($"BT /F1 8 Tf {margin} {pageH - margin - 26} Td ({Esc(Ascii($"{r.Params.From:yyyy-MM-dd} to {r.Params.To:yyyy-MM-dd} - {r.Rows.Count} rows - page {pg + 1} of {pageCount}"))}) Tj ET\n");
            var y = pageH - margin - 46;
            foreach (var (row, k) in new[] { header }.Concat(data.Skip(pg * perPage).Take(perPage)).Select((x, k) => (x, k)))
            {
                var font = k == 0 ? "/F2" : "/F1";
                for (var c = 0; c < row.Count; c++)
                {
                    var text = Ascii(row[c]); if (text.Length > maxChars) text = text[..(maxChars - 1)] + "~";
                    sb.Append($"BT {font} 7 Tf {margin + c * colW} {y} Td ({Esc(text)}) Tj ET\n");
                }
                y -= lineH;
            }
            pages.Add(sb.ToString());
        }
        var objs = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "" , "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>" };
        var kids = new List<int>();
        foreach (var content in pages)
        {
            var bytes = Encoding.ASCII.GetByteCount(content);
            objs.Add($"<< /Length {bytes} >>\nstream\n{content}endstream");
            objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW} {pageH}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {objs.Count} 0 R >>");
            kids.Add(objs.Count);
        }
        objs[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>";
        var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b); }
        W("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objs.Count; i++) { offsets.Add(ms.Position); W($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = ms.Position;
        W($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
