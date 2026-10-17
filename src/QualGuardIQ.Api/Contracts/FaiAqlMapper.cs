using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Modules.Fai;
using DomainAttachment = QualGuardIQ.Domain.Shared.Attachment;

namespace QualGuardIQ.Api.Contracts;

/// <summary>Maps the domain to the v1 wire contract and back. Kept in one place so the contract and the domain can evolve separately.</summary>
public static class FaiAqlMapper
{
    // ---------- enums ----------

    public static FaiTypeCode Code(FaiType t) => t switch
    {
        FaiType.Full => FaiTypeCode.Full, FaiType.Delta => FaiTypeCode.Delta, FaiType.Requalification => FaiTypeCode.Requalification, _ => FaiTypeCode.Partial
    };
    public static FaiType Domain(FaiTypeCode t) => t switch
    {
        FaiTypeCode.Full => FaiType.Full, FaiTypeCode.Delta => FaiType.Delta, FaiTypeCode.Requalification => FaiType.Requalification, _ => FaiType.Partial
    };
    public static FaiStatusCode Code(FaiStatus s, int faiRevision = 1) => s switch
    {
        FaiStatus.Draft => FaiStatusCode.Draft,
        FaiStatus.InProgress => faiRevision > 1 ? FaiStatusCode.Reopened : FaiStatusCode.InspectionInProgress,
        FaiStatus.Review => FaiStatusCode.Review,
        FaiStatus.Returned => FaiStatusCode.ReturnedForCorrection,
        FaiStatus.PendingApproval => FaiStatusCode.ApprovalPending,
        FaiStatus.Approved => FaiStatusCode.Approved,
        FaiStatus.Rejected => FaiStatusCode.Rejected,
        FaiStatus.Closed => FaiStatusCode.Closed,
        _ => FaiStatusCode.Canceled
    };
    public static FaiStatus[] Domain(FaiStatusCode s) => s switch
    {
        FaiStatusCode.Draft => [FaiStatus.Draft], FaiStatusCode.InspectionInProgress or FaiStatusCode.Reopened => [FaiStatus.InProgress], FaiStatusCode.Review => [FaiStatus.Review],
        FaiStatusCode.ReturnedForCorrection => [FaiStatus.Returned], FaiStatusCode.ApprovalPending => [FaiStatus.PendingApproval], FaiStatusCode.Approved => [FaiStatus.Approved],
        FaiStatusCode.Rejected => [FaiStatus.Rejected], FaiStatusCode.Closed => [FaiStatus.Closed], _ => [FaiStatus.Cancelled]
    };
    public static FaiReason Reason(string reason) =>
        Enum.TryParse<FaiReason>(reason?.Trim(), true, out var r) ? r : throw new AppException($"Reason must be one of: {string.Join(", ", Enum.GetNames<FaiReason>())}.");

    public static CharacteristicTypeCode Code(CharacteristicClass c) => c switch
    {
        CharacteristicClass.Critical => CharacteristicTypeCode.Critical, CharacteristicClass.Key => CharacteristicTypeCode.Key,
        CharacteristicClass.Significant or CharacteristicClass.Special => CharacteristicTypeCode.Significant, CharacteristicClass.Major => CharacteristicTypeCode.Major,
        CharacteristicClass.Minor => CharacteristicTypeCode.Minor, _ => CharacteristicTypeCode.General
    };
    public static CharacteristicClass Domain(CharacteristicTypeCode c) => Enum.Parse<CharacteristicClass>(c.ToString());
    public static DataTypeCode Code(CharDataType d) => Enum.Parse<DataTypeCode>(d.ToString());
    public static CharDataType Domain(DataTypeCode d) => Enum.Parse<CharDataType>(d.ToString());
    public static ResultCode Code(CharResult r) => r switch { CharResult.Pass => ResultCode.Pass, CharResult.Fail => ResultCode.Fail, _ => ResultCode.NotApplicable };

    public static SamplingTypeCode Code(SamplingType t) => Enum.Parse<SamplingTypeCode>(t.ToString());
    public static SamplingType Domain(SamplingTypeCode t) => Enum.Parse<SamplingType>(t.ToString());
    public static SeverityCode Code(SamplingMode m) => Enum.Parse<SeverityCode>(m.ToString());
    public static SamplingMode Domain(SeverityCode m) => Enum.Parse<SamplingMode>(m.ToString());
    public static DefectClassCode Code(Severity s) => Enum.Parse<DefectClassCode>(s.ToString());
    public static Severity Domain(DefectClassCode s) => Enum.Parse<Severity>(s.ToString());
    /// <summary>Superseded revisions are Obsolete; a revision deactivated without a successor is Inactive.</summary>
    public static PlanStatusCode Code(AqlSamplingPlan p, bool inactive) => p.Status switch
    {
        PlanStatus.Draft => PlanStatusCode.Draft, PlanStatus.Active => PlanStatusCode.Active, _ => inactive ? PlanStatusCode.Inactive : PlanStatusCode.Obsolete
    };

    public static AqlStatusCode Code(AqlInspection a) => a.Status switch
    {
        AqlInspectionStatus.Sampling => AqlStatusCode.InProgress,
        AqlInspectionStatus.Submitted => AqlStatusCode.Submitted,
        AqlInspectionStatus.Decided or AqlInspectionStatus.Approved => a.OverallResult == AqlDecision.Reject ? AqlStatusCode.Rejected : AqlStatusCode.Accepted,
        AqlInspectionStatus.Closed => AqlStatusCode.Closed,
        _ => AqlStatusCode.Canceled
    };
    public static AqlResultCode Code(AqlDecision? d) => d switch { AqlDecision.Accept => AqlResultCode.Accepted, AqlDecision.Reject => AqlResultCode.Rejected, _ => AqlResultCode.Pending };
    public static EvaluationCode Eval(AqlDecision d) => d switch { AqlDecision.Accept => EvaluationCode.Accepted, AqlDecision.Reject => EvaluationCode.Rejected, _ => EvaluationCode.NextStage };

    public static LinkedRecordTypeCode LinkType(string type) => type switch
    {
        "Ncr" => LinkedRecordTypeCode.NCR, "Capa" => LinkedRecordTypeCode.CAPA, "Scar" => LinkedRecordTypeCode.SCAR, "Product" => LinkedRecordTypeCode.Product, "Part" => LinkedRecordTypeCode.Part,
        "Drawing" => LinkedRecordTypeCode.Drawing, "Specification" => LinkedRecordTypeCode.Specification, "Supplier" => LinkedRecordTypeCode.Supplier, "Customer" => LinkedRecordTypeCode.Customer,
        "ChangeRequest" => LinkedRecordTypeCode.ChangeRequest, "InspectionPlan" => LinkedRecordTypeCode.InspectionPlan, "Fai" => LinkedRecordTypeCode.FAI, "AqlInspection" => LinkedRecordTypeCode.AQLInspection,
        "Inspection" => LinkedRecordTypeCode.Inspection, "Document" => LinkedRecordTypeCode.Document, "Risk" => LinkedRecordTypeCode.Risk, "Complaint" => LinkedRecordTypeCode.Complaint, "Audit" => LinkedRecordTypeCode.Audit,
        _ => LinkedRecordTypeCode.Other
    };

    // ---------- records ----------

    public static FAI ToDto(FaiRecord x) => new(x.Id, x.Number, Code(x.Type), x.Reason.ToString(), x.ProductId, x.PartId, x.PartRevision, x.DrawingId, x.DrawingRevision, x.SpecificationId,
        x.SupplierId, x.CustomerId, x.SiteId, x.DepartmentId, x.LotNumber, x.BatchNumber, x.SerialNumber, x.InspectorId, x.ReviewerId, x.ApproverId, Code(x.Status, x.FaiRevision),
        x.InspectionStartedAt, x.InspectionCompletedAt, x.ApprovedAt, x.Comments, x.CreatedAt, x.CreatedById ?? Guid.Empty, x.UpdatedAt == default ? null : x.UpdatedAt, x.UpdatedById, x.RowVersion,
        x.Title, x.Type.ToString(), x.FaiRevision, x.DueOn, x.CustomerApprovalRequired, x.ChangeId, x.BaseFaiId);

    public static FaiInput ToInput(FAICreate r, string? existingTitle = null, string? partNumber = null, uint rowVersion = 0, FaiType? keepType = null)
    {
        var type = keepType is { } k && Code(k) == r.FaiType ? k : Domain(r.FaiType);
        return new FaiInput(string.IsNullOrWhiteSpace(r.Title) ? existingTitle ?? $"{r.FaiType} FAI {partNumber}".Trim() : r.Title, type, Reason(r.Reason), r.PartId, r.PartRevision, r.DrawingId, r.DrawingRevision,
            r.SpecificationId, null, r.Process, r.SupplierId, r.CustomerId, r.ChangeId, r.InspectionPlanId, r.BaseFaiId, r.SiteId, r.DepartmentId, r.LotNumber, r.BatchNumber, r.SerialNumber,
            r.ProductionDate, r.DueDate, r.InspectorId, r.ReviewerId, r.CustomerApprovalRequired, r.Comments, r.SupplierOverrideReason, rowVersion);
    }

    public static FAICharacteristic ToDto(FaiCharacteristic c) => new(c.Id, c.FaiId, c.BalloonNumber, c.DrawingReference, Code(c.Class), c.Description, Code(c.DataType),
        c.Nominal, c.Nominal is { } n && c.LowerTolerance is { } lo ? n + lo : null, c.Nominal is { } n2 && c.UpperTolerance is { } up ? n2 + up : null,
        c.Units, c.InspectionMethod, c.RequirementSource, c.AcceptanceCriteria, c.RowVersion, c.Result.ToString(), c.MeasuredValue, c.CarriedOver, c.NcrId);

    /// <summary>Specification limits to nominal ± tolerances; a missing nominal defaults to the midpoint of the limits.</summary>
    public static CharacteristicInput2 ToInput(FAICharacteristicInput r)
    {
        decimal? nominal = r.NominalValue, lo = null, up = null;
        if (r.DataType == DataTypeCode.Numeric)
        {
            if (r.LowerSpecificationLimit is null && r.UpperSpecificationLimit is null) throw new AppException("A numeric characteristic needs at least one specification limit.");
            if (r.LowerSpecificationLimit is { } l && r.UpperSpecificationLimit is { } u && l > u) throw new AppException("The lower specification limit cannot exceed the upper limit.");
            nominal ??= r.LowerSpecificationLimit is { } a && r.UpperSpecificationLimit is { } b ? (a + b) / 2 : r.LowerSpecificationLimit ?? r.UpperSpecificationLimit;
            if (nominal < r.LowerSpecificationLimit || nominal > r.UpperSpecificationLimit) throw new AppException("The nominal value must lie within the specification limits.");
            lo = r.LowerSpecificationLimit - nominal; up = r.UpperSpecificationLimit - nominal;
        }
        else if (string.IsNullOrWhiteSpace(r.AcceptanceCriteria)) throw new AppException("Non-numeric characteristics need acceptance criteria.");
        return new CharacteristicInput2(r.BalloonNumber, r.Description, r.DrawingReference, Domain(r.CharacteristicType),
            r.DataType == DataTypeCode.Numeric ? CharacteristicKind.Variable : CharacteristicKind.Attribute, nominal, lo, up, r.UnitOfMeasure, r.InspectionMethod, r.RequirementSource, r.AcceptanceCriteria, Domain(r.DataType));
    }

    public static CharacteristicResult ToDto(FaiCharacteristicResult v, bool? calibrationValid, string? inspectorName) => new(v.Id, v.CharacteristicId, v.InspectionSequence, v.MeasuredValue,
        v.Observation, v.BooleanValue, Code(v.PassFail), v.InspectionEquipmentId, calibrationValid, v.InspectionDate, v.InspectorId, v.Notes, v.RowVersion, v.EquipmentNumber, v.Evidence, inspectorName);

    public static ResultInput2 ToInput(CharacteristicResultInput r) => new(r.MeasuredValue, r.TextValue, r.Result switch { ResultCode.Pass => true, ResultCode.Fail => false, _ => null },
        r.EquipmentId, r.Evidence, r.Notes, r.BooleanValue, r.Result == ResultCode.NotApplicable, r.Sequence);

    public static Attachment ToDto(DomainAttachment a) => new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.CreatedAt, a.CreatedById ?? Guid.Empty, a.Category, a.Version);

    public static AQLPlan ToDto(AqlSamplingPlan p, int rows, bool inactive) => new(p.Id, p.PlanName, p.Description, Code(p.InspectionType), Code(p.InspectionSeverity), p.Revision, Code(p, inactive), p.EffectiveDate, p.RowVersion,
        p.RejectOnCritical, p.ClearanceNumber, p.SamplingFrequency, p.ChangeReason, p.ActivatedAt, rows);

    public static PlanInput ToInput(AQLPlanInput r, IReadOnlyList<PlanDetailInput>? details = null) => new(r.Name, Domain(r.InspectionType), Domain(r.Severity), r.Description, r.RejectOnCritical ?? true,
        r.ClearanceNumber, r.SamplingFrequency, r.ChangeReason, details, (uint)(r.Version ?? 0), r.Revision, r.EffectiveDate);

    public static AQLPlanDetail ToDto(AqlPlanDetail d) => new(d.Id, d.PlanId, d.LotSizeMin, d.LotSizeMax, d.InspectionLevel, d.AqlLevel, d.SampleSize, d.AcceptQuantity, d.RejectQuantity,
        d.DefectClass is { } c ? Code(c) : null, d.Stage, d.CodeLetter);
    public static PlanDetailInput ToInput(AQLPlanDetailInput r) => new(r.LotSizeMin, r.LotSizeMax, r.InspectionLevel, r.AqlLevel, r.Stage ?? 1, r.SampleSize, r.AcceptanceNumber, r.RejectionNumber,
        r.CodeLetter, r.DefectClass is { } c ? Domain(c) : null);

    public static IReadOnlyList<AqlThreshold> Thresholds(SampleCalc c) => c.Thresholds.Count > 0
        ? c.Thresholds.Select(t => new AqlThreshold(Code(t.DefectClass), t.Stages[0].Ac, t.Stages[0].Re, t.Aql, Stages(t.Stages))).ToList()
        : [new AqlThreshold(null, c.Stages[0].Ac, c.Stages[0].Re, 0, Stages(c.Stages))];
    private static List<AqlStageDto> Stages(IEnumerable<AqlStage> s) => s.Select((x, i) => new AqlStageDto(i + 1, x.N, x.Ac, x.Re)).ToList();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<AqlThreshold> Thresholds(AqlInspection a)
    {
        var byClass = AqlService.ClassStagesOf(a);
        if (byClass.Count > 0)
        {
            var aqls = new Dictionary<Severity, decimal?> { [Severity.Critical] = a.AqlLevelCritical, [Severity.Major] = a.AqlLevelMajor, [Severity.Minor] = a.AqlLevelMinor };
            return byClass.OrderByDescending(k => k.Key).Select(kv => { var s = kv.Value[Math.Min(a.Stage, kv.Value.Count) - 1]; return new AqlThreshold(Code(kv.Key), s.Ac, s.Re, aqls[kv.Key] ?? 0, Stages(kv.Value)); }).ToList();
        }
        var stages = JsonSerializer.Deserialize<List<AqlStage>>(a.Stages, Json) ?? [];
        return stages.Count == 0 ? [] : [new AqlThreshold(null, a.AcceptanceQuantity, a.RejectionQuantity, a.AqlLevel, Stages(stages))];
    }

    public static AQLInspection ToDto(AqlInspection a) => new(a.Id, a.InspectionNumber, a.PlanId, a.SupplierId, a.CustomerId, a.ProductId, a.PartId ?? Guid.Empty, a.PartRevision, a.PurchaseOrder, a.WorkOrder,
        a.LotNumber, a.LotSize, a.SampleSize, a.InspectionLevel, a.AqlLevelCritical, a.AqlLevelMajor, a.AqlLevelMinor, a.CriticalDefects, a.MajorDefects, a.MinorDefects,
        Code(a), Code(a.OverallResult), a.InspectorId, a.InspectionDate, a.Notes, a.RowVersion, a.Status.ToString(), a.InspectionId, a.PlanLabel,
        a.ClassStages is null ? a.AqlLevel : null, a.CodeLetter, Code(a.Severity), a.Stage, a.HundredPercent, Thresholds(a), a.Rationale, a.SamplingRecommendation, a.ApproverId, a.ApprovedAt);

    public static AqlLotInput ToInput(AQLInspectionInput r) => new(r.PlanId, r.InspectionId, r.PartId, r.PartRevision, r.SupplierId, r.CustomerId, r.ProductId, r.PurchaseOrder, r.WorkOrder,
        r.LotNumber, r.LotSize, r.InspectionLevel, r.AqlLevel, r.AqlLevelCritical, r.AqlLevelMajor, r.AqlLevelMinor, r.Notes, r.Version is { } v ? (uint)v : null);

    public static DefectCategory ToDto(AqlDefectCategory c) => new(c.Id, c.DefectCode, Code(c.DefectType), c.Description, c.IsActive, c.Category);

    public static InspectionDefect ToDto(AqlInspectionDefect d, AqlDefectCategory c) => new(d.Id, d.InspectionId, d.DefectCategoryId, d.QuantityFound, d.SampleUnitNumber, d.Description,
        d.AttachmentIds is null ? [] : JsonSerializer.Deserialize<List<Guid>>(d.AttachmentIds, Json) ?? [], c.DefectCode, Code(c.DefectType), d.Stage);

    public static EvaluationResult ToDto(AqlInspection a, AqlDecision decision, IReadOnlyList<ClassOutcome> classes, DateTimeOffset at) => new(Eval(decision), at,
        classes.Select(o => new EvaluationDetail(o.DefectClass is { } c ? Code(c) : null, o.Defects, o.Ac, o.Re, Eval(o.Decision), o.Rationale)).ToList(), a.Stage, a.SampleSize);

    // ---------- audit ----------

    /// <summary>Audit-trail rows have 64-bit ids; they are carried as UUIDs with the id in the last 8 bytes (big-endian).</summary>
    public static Guid AuditId(long id)
    {
        Span<byte> b = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(b[8..], id);
        return new Guid(b, bigEndian: true);
    }

    public static AuditEvent ToDto(AuditEntry e)
    {
        JsonElement? prev = null, next = null;
        if (!string.IsNullOrWhiteSpace(e.Changes))
        {
            try
            {
                using var doc = JsonDocument.Parse(e.Changes);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var p = new Dictionary<string, JsonElement>(); var n = new Dictionary<string, JsonElement>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                        if (prop.Value.TryGetProperty("from", out var f)) p[prop.Name] = f.Clone();
                        if (prop.Value.TryGetProperty("to", out var t)) n[prop.Name] = t.Clone();
                    }
                    if (p.Count > 0) prev = JsonSerializer.SerializeToElement(p);
                    if (n.Count > 0) next = JsonSerializer.SerializeToElement(n);
                }
            }
            catch (JsonException) { }
        }
        return new AuditEvent(AuditId(e.Id), e.Action, e.UserId ?? Guid.Empty, e.OccurredAt, prev, next, e.Reason, e.CorrelationId, e.EntityType, e.EntityId, e.UserName, e.Description);
    }

    /// <summary>Stable id for a pending approval step that has no decision yet (request id + step order).</summary>
    public static Guid PendingStepId(Guid requestId, int step)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes($"{requestId:N}:{step}"));
        h[6] = (byte)((h[6] & 0x0F) | 0x80); h[8] = (byte)((h[8] & 0x3F) | 0x80); // RFC 9562 v8
        return new Guid(h.AsSpan(0, 16), bigEndian: true);
    }

    public static PageMetadata Meta(int page, int pageSize, int total) => new(page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    public static (int Page, int Size) Clamp(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));
}
