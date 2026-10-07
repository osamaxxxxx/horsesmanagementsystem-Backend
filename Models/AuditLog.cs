using System.Text.Json;
using HorseVillage.Infrastructure;

namespace HorseVillage.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? PlotId { get; set; }
    public string ActionType { get; set; } = "";
    public Guid? PerformedById { get; set; }
    public User? PerformedBy { get; set; }
    // JSON document describing the change.
    public string Details { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public static AuditLog Create(string action, Guid performedById, object details, Guid? plotId = null) => new()
    {
        Id = Guid.NewGuid(),
        PlotId = plotId,
        ActionType = action,
        PerformedById = performedById,
        Details = JsonSerializer.Serialize(details, JsonDefaults.Options),
        CreatedAt = DateTimeOffset.UtcNow
    };
}
