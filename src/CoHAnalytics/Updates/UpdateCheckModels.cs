namespace CoHAnalytics.Updates;

public enum UpdateCheckStatus
{
    Current,
    UpdateAvailable,
    UnableToCheck
}

public sealed record UpdateDownloadAction(string Label, string AssetName, string Url);

public sealed record UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }

    public required ReleaseVersion CurrentVersion { get; init; }

    public required DeploymentType DeploymentType { get; init; }

    public ReleaseVersion? RemoteVersion { get; init; }

    public string? ReleaseUrl { get; init; }

    public UpdateDownloadAction? DownloadAction { get; init; }

    public int ReleaseCount { get; init; }

    public string? FailureCode { get; init; }

    public int? HttpStatus { get; init; }

    public string? ExceptionType { get; init; }

    public int? HResult { get; init; }
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);
}
