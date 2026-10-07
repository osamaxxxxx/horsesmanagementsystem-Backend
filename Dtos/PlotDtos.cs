using System.ComponentModel.DataAnnotations;
using HorseVillage.Models;

namespace HorseVillage.Dtos;

public record PlotDto(
    Guid Id,
    int SerialNo,
    string BeneficiaryName,
    DateOnly? LotteryDate,
    string ContractNo,
    DateOnly? ContractExpiryDate,
    string StudName,
    string PlotNo,
    string? ContactInfo,
    string? BuildingPermits,
    string? RequestsAndDecisions,
    string? OfficialLetters,
    string? Notes,
    PlotStatus Status,
    DateTimeOffset UpdatedAt)
{
    public static PlotDto From(Plot p) => new(
        p.Id, p.SerialNo, p.BeneficiaryName, p.LotteryDate, p.ContractNo, p.ContractExpiryDate, p.StudName, p.PlotNo,
        p.ContactInfo, p.BuildingPermits, p.RequestsAndDecisions, p.OfficialLetters, p.Notes, p.Status, p.UpdatedAt);
}

public class PlotWriteRequest
{
    [Range(1, 1000)]
    public int SerialNo { get; set; }

    [Required, StringLength(255)]
    public string BeneficiaryName { get; set; } = "";

    public DateOnly? LotteryDate { get; set; }

    [Required, StringLength(100)]
    public string ContractNo { get; set; } = "";

    public DateOnly? ContractExpiryDate { get; set; }

    [Required, StringLength(255)]
    public string StudName { get; set; } = "";

    [Required, StringLength(50)]
    public string PlotNo { get; set; } = "";

    public string? ContactInfo { get; set; }
    public string? BuildingPermits { get; set; }
    public string? RequestsAndDecisions { get; set; }
    public string? OfficialLetters { get; set; }
    public string? Notes { get; set; }
    public PlotStatus Status { get; set; } = PlotStatus.ACTIVE;
}

// Partial update: a null property means "leave unchanged".
public class PlotPatchRequest
{
    public PlotStatus? Status { get; set; }
    public string? ContactInfo { get; set; }
    public string? BuildingPermits { get; set; }
    public string? RequestsAndDecisions { get; set; }
    public string? OfficialLetters { get; set; }
    public string? Notes { get; set; }

    public IEnumerable<string> ProvidedFields()
    {
        if (Status is not null) yield return nameof(Status);
        if (ContactInfo is not null) yield return nameof(ContactInfo);
        if (BuildingPermits is not null) yield return nameof(BuildingPermits);
        if (RequestsAndDecisions is not null) yield return nameof(RequestsAndDecisions);
        if (OfficialLetters is not null) yield return nameof(OfficialLetters);
        if (Notes is not null) yield return nameof(Notes);
    }
}

public class PlotQuery
{
    public string? Search { get; set; }
    public PlotStatus? Status { get; set; }

    // serial_asc | serial_desc | plot | name
    public string Sort { get; set; } = "serial_asc";

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 1000)]
    public int PageSize { get; set; } = 100;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record PlotStatsDto(int Capacity, int Registered, int Active, int InProgress, int Completed, int Withdrawn, int Swapped);

public class SwapRequest
{
    public Guid PlotAId { get; set; }
    public Guid PlotBId { get; set; }

    [StringLength(200)]
    public string? DecisionRef { get; set; }
}

public record SwapResult(PlotDto PlotA, PlotDto PlotB);

public class LetterRequest
{
    [Required, StringLength(100)]
    public string Type { get; set; } = "";

    [Required, StringLength(100)]
    public string Reference { get; set; } = "";

    [Required, StringLength(2000)]
    public string Content { get; set; } = "";
}
