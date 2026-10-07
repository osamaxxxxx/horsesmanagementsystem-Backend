using System.Text;
using HorseVillage.Data;
using HorseVillage.Dtos;
using HorseVillage.Hubs;
using HorseVillage.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Controllers;

[Route("api/plots")]
[Authorize]
public class PlotsController(AppDbContext db, SyncNotifier notifier, IConfiguration configuration) : ApiControllerBase
{
    private const int Capacity = 1000;

    // Fields each non-admin role may change through PATCH.
    private static readonly string[] PresidentFields =
        [nameof(PlotPatchRequest.Status), nameof(PlotPatchRequest.Notes), nameof(PlotPatchRequest.RequestsAndDecisions)];
    private static readonly string[] InspectorFields =
        [nameof(PlotPatchRequest.Status), nameof(PlotPatchRequest.BuildingPermits)];

    private static readonly Dictionary<PlotStatus, string> StatusLabels = new()
    {
        [PlotStatus.ACTIVE] = "عقد ساري ومنتظم",
        [PlotStatus.WITHDRAWN] = "مسحوبة / قرار إلغاء",
        [PlotStatus.IN_PROGRESS] = "رخص قيد الإجراء",
        [PlotStatus.SWAPPED] = "تبديل / تنازل قسيمة",
        [PlotStatus.COMPLETED] = "مكتملة البناء بالكامل"
    };

    [HttpGet]
    public async Task<PagedResult<PlotDto>> List([FromQuery] PlotQuery query, CancellationToken ct)
    {
        var filtered = Filter(query);
        var total = await filtered.CountAsync(ct);
        var plots = await filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<PlotDto>(plots.Select(PlotDto.From).ToList(), total, query.Page, query.PageSize);
    }

    [HttpGet("stats")]
    public async Task<PlotStatsDto> Stats(CancellationToken ct)
    {
        var counts = await db.Plots.GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        int Of(PlotStatus status) => counts.GetValueOrDefault(status);

        return new PlotStatsDto(Capacity, counts.Values.Sum(), Of(PlotStatus.ACTIVE), Of(PlotStatus.IN_PROGRESS),
            Of(PlotStatus.COMPLETED), Of(PlotStatus.WITHDRAWN), Of(PlotStatus.SWAPPED));
    }

    [HttpGet("export")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<FileContentResult> Export([FromQuery] PlotQuery query, CancellationToken ct)
    {
        var plots = await Filter(query).ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("م,اسم المنتفع,تاريخ القرعة,رقم العقد,تاريخ انتهاء العقد,اسم المربط,رقم القسيمة,بيانات التواصل,تراخيص وإتمام البناء,الطلبات والقرارات,مخاطبات الإدارة,الملاحظات,الحالة");
        foreach (var p in plots)
        {
            csv.AppendJoin(',', p.SerialNo, Cell(p.BeneficiaryName), Cell(p.LotteryDate?.ToString("yyyy-MM-dd")), Cell(p.ContractNo),
                Cell(p.ContractExpiryDate?.ToString("yyyy-MM-dd")), Cell(p.StudName), Cell(p.PlotNo), Cell(p.ContactInfo),
                Cell(p.BuildingPermits), Cell(p.RequestsAndDecisions), Cell(p.OfficialLetters), Cell(p.Notes), Cell(StatusLabels[p.Status]));
            csv.AppendLine();
        }

        // UTF-8 with BOM so Excel reads the Arabic text correctly.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(csv.ToString())];
        return File(bytes, "text/csv; charset=utf-8", $"سجل_قسائم_قرية_الخيول_العقدة_{Today():yyyy-MM-dd}.csv");

        static string Cell(string? text) => $"\"{(text ?? "").Replace("\"", "\"\"").ReplaceLineEndings(" ")}\"";
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlotDto>> Get(Guid id, CancellationToken ct)
    {
        var plot = await db.Plots.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return plot is null ? NotFound() : PlotDto.From(plot);
    }

    [HttpPost]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<PlotDto>> Create(PlotWriteRequest request, CancellationToken ct)
    {
        if (await FindConflictAsync(request, null, ct) is { } conflict) return conflict;

        var now = DateTimeOffset.UtcNow;
        var plot = new Plot { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, UpdatedById = CurrentUserId };
        Apply(request, plot);
        db.Plots.Add(plot);
        var dto = PlotDto.From(plot);
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Create, CurrentUserId, dto, plot.Id));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return DuplicateError();
        }

        await notifier.PlotUpsertedAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = plot.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<PlotDto>> Update(Guid id, PlotWriteRequest request, CancellationToken ct)
    {
        var plot = await db.Plots.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plot is null) return NotFound();
        if (await FindConflictAsync(request, id, ct) is { } conflict) return conflict;

        Apply(request, plot);
        try
        {
            return await SaveChangesAsync(plot, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return DuplicateError();
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PlotDto>> Patch(Guid id, PlotPatchRequest request, CancellationToken ct)
    {
        if (!User.IsInRole(Roles.SuperAdmin))
        {
            var allowed = User.IsInRole(Roles.President) ? PresidentFields : InspectorFields;
            var denied = request.ProvidedFields().Except(allowed).ToList();
            if (denied.Count > 0)
                return Error(StatusCodes.Status403Forbidden, "FIELD_NOT_ALLOWED",
                    $"لا تملك صلاحية تعديل الحقول: {string.Join(", ", denied)}");
        }

        var plot = await db.Plots.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plot is null) return NotFound();

        if (request.Status is { } status) plot.Status = status;
        if (request.ContactInfo is not null) plot.ContactInfo = Clean(request.ContactInfo);
        if (request.BuildingPermits is not null) plot.BuildingPermits = Clean(request.BuildingPermits);
        if (request.RequestsAndDecisions is not null) plot.RequestsAndDecisions = Clean(request.RequestsAndDecisions);
        if (request.OfficialLetters is not null) plot.OfficialLetters = Clean(request.OfficialLetters);
        if (request.Notes is not null) plot.Notes = Clean(request.Notes);

        return await SaveChangesAsync(plot, ct);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var plot = await db.Plots.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plot is null) return NotFound();

        db.Plots.Remove(plot);
        // PlotId is left empty: the row it would point at is gone. The snapshot keeps the id.
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Delete, CurrentUserId, PlotDto.From(plot)));
        await db.SaveChangesAsync(ct);

        await notifier.PlotDeletedAsync(id);
        return NoContent();
    }

    [HttpPost("swap")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<SwapResult>> Swap(SwapRequest request, CancellationToken ct)
    {
        if (request.PlotAId == request.PlotBId)
            return Error(StatusCodes.Status400BadRequest, "SAME_PLOT", "برجاء اختيار قسيمتين مختلفتين للمبادلة.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var plots = await db.Plots.Where(p => p.Id == request.PlotAId || p.Id == request.PlotBId).ToListAsync(ct);
        var a = plots.FirstOrDefault(p => p.Id == request.PlotAId);
        var b = plots.FirstOrDefault(p => p.Id == request.PlotBId);
        if (a is null || b is null) return NotFound();

        var (plotNoA, plotNoB) = (a.PlotNo, b.PlotNo);
        var decision = string.IsNullOrWhiteSpace(request.DecisionRef) ? "قرار لجنة رسمي" : request.DecisionRef.Trim();
        var now = DateTimeOffset.UtcNow;
        var stamp = Today().ToString("yyyy-MM-dd");

        // PlotNo is unique, so the exchange goes through a placeholder value.
        a.PlotNo = $"~{Guid.NewGuid():N}";
        await db.SaveChangesAsync(ct);
        b.PlotNo = plotNoA;
        await db.SaveChangesAsync(ct);
        a.PlotNo = plotNoB;

        a.OfficialLetters = AppendLine(a.OfficialLetters, $"[{stamp}]: تم تبديل القسيمة مع ({b.BeneficiaryName}) بموجب {decision}.");
        b.OfficialLetters = AppendLine(b.OfficialLetters, $"[{stamp}]: تم تبديل القسيمة مع ({a.BeneficiaryName}) بموجب {decision}.");
        foreach (var plot in plots)
        {
            plot.Status = PlotStatus.SWAPPED;
            plot.UpdatedAt = now;
            plot.UpdatedById = CurrentUserId;
        }

        var details = new
        {
            decision,
            plotA = new { a.Id, a.BeneficiaryName, from = plotNoA, to = plotNoB },
            plotB = new { b.Id, b.BeneficiaryName, from = plotNoB, to = plotNoA }
        };
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Swap, CurrentUserId, details, a.Id));
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Swap, CurrentUserId, details, b.Id));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var result = new SwapResult(PlotDto.From(a), PlotDto.From(b));
        await notifier.PlotUpsertedAsync(result.PlotA);
        await notifier.PlotUpsertedAsync(result.PlotB);
        return result;
    }

    [HttpPost("{id:guid}/letters")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<ActionResult<PlotDto>> AddLetter(Guid id, LetterRequest request, CancellationToken ct)
    {
        var plot = await db.Plots.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plot is null) return NotFound();

        var entry = $"[{Today():yyyy-MM-dd} - {request.Type.Trim()} قيد: {request.Reference.Trim()}]: {request.Content.Trim()}";
        plot.OfficialLetters = AppendLine(plot.OfficialLetters, entry);
        plot.UpdatedAt = DateTimeOffset.UtcNow;
        plot.UpdatedById = CurrentUserId;
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Letter, CurrentUserId,
            new { type = request.Type.Trim(), reference = request.Reference.Trim(), content = request.Content.Trim() }, plot.Id));
        await db.SaveChangesAsync(ct);

        var dto = PlotDto.From(plot);
        await notifier.PlotUpsertedAsync(dto);
        return dto;
    }

    private IQueryable<Plot> Filter(PlotQuery query)
    {
        var plots = db.Plots.AsNoTracking();

        if (query.Status is { } status)
            plots = plots.Where(p => p.Status == status);

        var term = query.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
            plots = plots.Where(p =>
                p.PlotNo.Contains(term) ||
                p.ContractNo.Contains(term) ||
                p.BeneficiaryName.Contains(term) ||
                p.StudName.Contains(term) ||
                p.SerialNo.ToString().Contains(term));

        return query.Sort switch
        {
            "serial_desc" => plots.OrderByDescending(p => p.SerialNo),
            "plot" => plots.OrderBy(p => p.PlotNo),
            "name" => plots.OrderBy(p => p.BeneficiaryName).ThenBy(p => p.SerialNo),
            _ => plots.OrderBy(p => p.SerialNo)
        };
    }

    // Records which fields changed, saves, and broadcasts the new state. No-op when nothing changed.
    private async Task<PlotDto> SaveChangesAsync(Plot plot, CancellationToken ct)
    {
        var changes = db.Entry(plot).Properties
            .Where(p => p.IsModified)
            .ToDictionary(p => p.Metadata.Name, p => new { old = p.OriginalValue, @new = p.CurrentValue });
        if (changes.Count == 0) return PlotDto.From(plot);

        plot.UpdatedAt = DateTimeOffset.UtcNow;
        plot.UpdatedById = CurrentUserId;
        db.AuditLogs.Add(AuditLog.Create(AuditActions.Update, CurrentUserId, changes, plot.Id));
        await db.SaveChangesAsync(ct);

        var dto = PlotDto.From(plot);
        await notifier.PlotUpsertedAsync(dto);
        return dto;
    }

    private async Task<ObjectResult?> FindConflictAsync(PlotWriteRequest request, Guid? excludeId, CancellationToken ct)
    {
        var contractNo = request.ContractNo.Trim();
        var plotNo = request.PlotNo.Trim();
        var clash = await db.Plots.AsNoTracking()
            .Where(p => p.Id != excludeId && (p.SerialNo == request.SerialNo || p.ContractNo == contractNo || p.PlotNo == plotNo))
            .Select(p => new { p.SerialNo, p.ContractNo })
            .FirstOrDefaultAsync(ct);
        if (clash is null) return null;

        if (clash.SerialNo == request.SerialNo)
            return Error(StatusCodes.Status409Conflict, "DUPLICATE_SERIAL_NO", $"الرقم التسلسلي {request.SerialNo} مستخدم في سجل آخر.");
        if (string.Equals(clash.ContractNo, contractNo, StringComparison.OrdinalIgnoreCase))
            return Error(StatusCodes.Status409Conflict, "DUPLICATE_CONTRACT_NO", $"رقم العقد {contractNo} مستخدم في سجل آخر.");
        return Error(StatusCodes.Status409Conflict, "DUPLICATE_PLOT_NO", $"رقم القسيمة {plotNo} مستخدم في سجل آخر.");
    }

    private ObjectResult DuplicateError() =>
        Error(StatusCodes.Status409Conflict, "DUPLICATE", "الرقم التسلسلي أو رقم العقد أو رقم القسيمة مستخدم في سجل آخر.");

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static void Apply(PlotWriteRequest request, Plot plot)
    {
        plot.SerialNo = request.SerialNo;
        plot.BeneficiaryName = request.BeneficiaryName.Trim();
        plot.LotteryDate = request.LotteryDate;
        plot.ContractNo = request.ContractNo.Trim();
        plot.ContractExpiryDate = request.ContractExpiryDate;
        plot.StudName = request.StudName.Trim();
        plot.PlotNo = request.PlotNo.Trim();
        plot.ContactInfo = Clean(request.ContactInfo);
        plot.BuildingPermits = Clean(request.BuildingPermits);
        plot.RequestsAndDecisions = Clean(request.RequestsAndDecisions);
        plot.OfficialLetters = Clean(request.OfficialLetters);
        plot.Notes = Clean(request.Notes);
        plot.Status = request.Status;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string AppendLine(string? existing, string line) =>
        string.IsNullOrEmpty(existing) ? line : $"{existing}\n{line}";

    // Dates stamped into the official record follow the committee's local calendar, not the server's.
    private DateOnly Today()
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["App:TimeZone"] ?? "Asia/Qatar");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            // Hosts without IANA time zone data: fall back to Arabia Standard Time (UTC+3, no daylight saving).
            return DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
        }
    }
}
