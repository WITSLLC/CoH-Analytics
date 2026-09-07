using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public override long TimestampFrequency => 1_000;

    public void Advance(TimeSpan delta)
    {
        _utcNow += delta;
        _timestamp += (long)delta.TotalMilliseconds;
    }

    public void Set(DateTimeOffset value) => _utcNow = value;
}

internal class FakeContributor : IApplicationContributor
{
    private ApplicationContribution _contribution;
    private Func<CancellationToken, Task<ApplicationContribution>>? _pullOverride;

    public FakeContributor(
        ApplicationContributorDescriptor descriptor,
        ApplicationContribution? contribution = null)
    {
        Descriptor = descriptor;
        _contribution = contribution ?? ApplicationContribution.Empty(descriptor.ProviderId, DateTimeOffset.UtcNow) with
        {
            Health = ContributorHealth.Ready,
            Activity = ContributorActivity.Inactive
        };
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public int PullCount { get; private set; }

    public List<string> LifecycleLog { get; } = [];

    public event EventHandler? ContributionChanged;

    public void SetContribution(ApplicationContribution contribution) =>
        _contribution = contribution.WithImmutableCollections();

    public void SetPullOverride(Func<CancellationToken, Task<ApplicationContribution>>? pullOverride) =>
        _pullOverride = pullOverride;

    public void RaiseChanged() => ContributionChanged?.Invoke(this, EventArgs.Empty);

    public virtual Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        PullCount++;
        if (_pullOverride is not null)
        {
            return _pullOverride(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_contribution);
    }
}

internal sealed class LifecycleFakeContributor : FakeContributor, IApplicationContributorLifecycle
{
    public LifecycleFakeContributor(
        ApplicationContributorDescriptor descriptor,
        ApplicationContribution? contribution = null)
        : base(descriptor, contribution)
    {
    }

    public TimeSpan StartDelay { get; set; }

    public TimeSpan StopDelay { get; set; }

    public Exception? StartException { get; set; }

    public Exception? StopException { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        LifecycleLog.Add("Start");
        if (StartDelay > TimeSpan.Zero)
        {
            await Task.Delay(StartDelay, cancellationToken);
        }

        if (StartException is not null)
        {
            throw StartException;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        LifecycleLog.Add("Stop");
        if (StopDelay > TimeSpan.Zero)
        {
            await Task.Delay(StopDelay, cancellationToken);
        }

        if (StopException is not null)
        {
            throw StopException;
        }
    }
}

internal sealed class ActionHandlingContributor : FakeContributor, IApplicationActionHandler
{
    public ActionHandlingContributor(
        ApplicationContributorDescriptor descriptor,
        ApplicationContribution? contribution = null)
        : base(descriptor, contribution)
    {
    }

    public Task<ApplicationActionResult> HandleAsync(
        ApplicationAction action,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ApplicationActionResult.Success());
}

internal static class TestDescriptors
{
    public static ApplicationContributorDescriptor Create(
        string providerId,
        string displayName,
        IEnumerable<string>? produces = null,
        IEnumerable<string>? requires = null,
        IEnumerable<string>? optional = null,
        ApplicationContributorImportance importance = ApplicationContributorImportance.Important,
        int priority = 10,
        TimeSpan? maxAge = null,
        TimeSpan? pullTimeout = null,
        int schemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion) =>
        new(
            providerId,
            displayName,
            (produces ?? [providerId + ".capability"]).ToArray(),
            (requires ?? []).ToArray(),
            (optional ?? []).ToArray(),
            importance,
            priority,
            maxAge,
            pullTimeout)
        {
            SchemaVersion = schemaVersion
        };

    public static ApplicationContribution Ready(
        string providerId,
        DateTimeOffset observedAt,
        ContributorActivity activity = ContributorActivity.Inactive,
        IEnumerable<ApplicationIssue>? issues = null,
        IEnumerable<ApplicationFact>? facts = null,
        IEnumerable<ApplicationAction>? actions = null) =>
        new(
            providerId,
            ContributorHealth.Ready,
            activity,
            (facts ?? []).ToArray(),
            (issues ?? []).ToArray(),
            (actions ?? []).ToArray(),
            observedAt);
}
