namespace CoHAnalytics.Services.Diagnostics;

public sealed record DiagnosticsReportCreationResult
{
    public required bool Succeeded { get; init; }

    public string? DestinationPath { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureMessage { get; init; }

    public static DiagnosticsReportCreationResult Success(string destinationPath) =>
        new()
        {
            Succeeded = true,
            DestinationPath = destinationPath
        };

    public static DiagnosticsReportCreationResult Failure(string failureCode, string failureMessage) =>
        new()
        {
            Succeeded = false,
            FailureCode = failureCode,
            FailureMessage = failureMessage
        };
}
