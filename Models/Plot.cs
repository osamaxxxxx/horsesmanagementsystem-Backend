namespace HorseVillage.Models;

public class Plot
{
    public Guid Id { get; set; }
    public int SerialNo { get; set; }
    public string BeneficiaryName { get; set; } = "";
    public DateOnly? LotteryDate { get; set; }
    public string ContractNo { get; set; } = "";
    public DateOnly? ContractExpiryDate { get; set; }
    public string StudName { get; set; } = "";
    public string PlotNo { get; set; } = "";
    public string? ContactInfo { get; set; }
    public string? BuildingPermits { get; set; }
    public string? RequestsAndDecisions { get; set; }
    public string? OfficialLetters { get; set; }
    public string? Notes { get; set; }
    public PlotStatus Status { get; set; } = PlotStatus.ACTIVE;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public User? UpdatedBy { get; set; }
}
