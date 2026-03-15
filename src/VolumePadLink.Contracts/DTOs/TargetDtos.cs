namespace VolumePadLink.Contracts.DTOs;

public static class TargetKinds
{
    public const string Master = "Master";
    public const string SessionById = "SessionById";
    public const string SessionByLogicalApp = "SessionByLogicalApp";
}

public static class MeterSourceKinds
{
    public const string Master = "Master";
    public const string ActiveTarget = "ActiveTarget";
    public const string SessionById = "SessionById";
    public const string SessionByLogicalApp = "SessionByLogicalApp";
}

public sealed record ActiveTargetDto(string Kind, string? SessionId, string? LogicalApp);

public sealed record MeterSourceDto(string Kind, string? SessionId, string? LogicalApp);
