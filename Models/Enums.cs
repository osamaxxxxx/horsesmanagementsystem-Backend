namespace HorseVillage.Models;

// Member names are the wire/database codes from the PRD, so they are stored and serialized as-is.
public enum PlotStatus
{
    ACTIVE,
    WITHDRAWN,
    IN_PROGRESS,
    SWAPPED,
    COMPLETED
}

public enum UserRole
{
    SUPER_ADMIN_REPORTER,
    PRESIDENT,
    ENGINEER_INSPECTOR
}

public static class Roles
{
    public const string SuperAdmin = nameof(UserRole.SUPER_ADMIN_REPORTER);
    public const string President = nameof(UserRole.PRESIDENT);
    public const string Inspector = nameof(UserRole.ENGINEER_INSPECTOR);
}

public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Swap = "SWAP";
    public const string Letter = "LETTER";
    public const string Revoke = "REVOKE";
    public const string Restore = "RESTORE";
}
