using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HorseVillage.Data;
using HorseVillage.Dtos;
using HorseVillage.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Controllers;

[Route("api/audit-logs")]
[Authorize(Roles = Roles.SuperAdmin)]
public class AuditLogsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AuditLogDto>> List(
        [FromQuery] Guid? plotId,
        [FromQuery] string? actionType,
        CancellationToken ct,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 50)
    {
        var logs = db.AuditLogs.AsNoTracking();
        if (plotId is not null) logs = logs.Where(a => a.PlotId == plotId);
        if (!string.IsNullOrWhiteSpace(actionType)) logs = logs.Where(a => a.ActionType == actionType);

        var total = await logs.CountAsync(ct);
        var rows = await logs.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.Id, a.PlotId, a.ActionType, a.PerformedById, PerformedByName = a.PerformedBy!.FullName, a.Details, a.CreatedAt })
            .ToListAsync(ct);

        var items = rows.Select(a => new AuditLogDto(a.Id, a.PlotId, a.ActionType, a.PerformedById, a.PerformedByName,
            JsonSerializer.Deserialize<JsonElement>(a.Details), a.CreatedAt)).ToList();
        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }
}
