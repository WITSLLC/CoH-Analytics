using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Updates;

public sealed record UpdateCheckStartedDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string LocalVersion { get; init; }
    public required string DeploymentType { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.CheckStarted";
}

public sealed record UpdateCheckSkippedDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string LocalVersion { get; init; }
    public required string DeploymentType { get; init; }
    public required string Reason { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.CheckSkipped";
}

public sealed record UpdateCheckSucceededDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required int ReleaseCount { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.CheckSucceeded";
}

public sealed record UpdateAvailableDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string LocalVersion { get; init; }
    public required string RemoteVersion { get; init; }
    public required string DeploymentType { get; init; }
    public required bool HasPackageDownload { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.Available";
}

public sealed record UpdateAlreadyCurrentDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string LocalVersion { get; init; }
    public string? RemoteVersion { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.AlreadyCurrent";
}

public sealed record UpdateCheckFailedDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string FailureCode { get; init; }
    public int? HttpStatus { get; init; }
    public string? ExceptionType { get; init; }
    public int? HResult { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    internal override string EventName => "Update.CheckFailed";
}

public sealed record UpdateUserActionSelectedDiagnosticEvent : DiagnosticEvent
{
    public required string Mode { get; init; }
    public required string Action { get; init; }
    public required string RemoteVersion { get; init; }
    public required string DeploymentType { get; init; }
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;
    internal override DiagnosticCategory Category => DiagnosticCategory.Application;
    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;
    internal override string EventName => "Update.UserActionSelected";
}
