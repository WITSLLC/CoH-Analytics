namespace CoHAnalytics.Models;

/// <summary>
/// Stable identity for one running Homecoming client process.
/// </summary>
/// <remarks>
/// Identity is <see cref="ProcessId"/> plus <see cref="ProcessStartTime"/> so PID reuse
/// after exit/relaunch is distinguishable. <see cref="ExecutablePath"/> is retained metadata.
/// </remarks>
public sealed record HomecomingProcessInstance
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartTime { get; init; }

    public required string ExecutablePath { get; init; }

    public bool Equals(HomecomingProcessInstance? other) =>
        other is not null
        && ProcessId == other.ProcessId
        && ProcessStartTime == other.ProcessStartTime;

    public override int GetHashCode() => HashCode.Combine(ProcessId, ProcessStartTime);

    /// <summary>
    /// Returns whether this observation is a more accurate identity for the same still-running
    /// process. A genuine PID reuse has a later start time; refinement corrects an initial
    /// provisional observation backward while retaining the same PID and executable.
    /// </summary>
    public bool IsMetadataRefinementOf(HomecomingProcessInstance previous) =>
        ProcessId == previous.ProcessId
        && ProcessStartTime < previous.ProcessStartTime
        && string.Equals(ExecutablePath, previous.ExecutablePath, StringComparison.OrdinalIgnoreCase);

    public static bool SequenceEqualByIdentity(
        IReadOnlyList<HomecomingProcessInstance>? left,
        IReadOnlyList<HomecomingProcessInstance>? right)
    {
        left ??= Array.Empty<HomecomingProcessInstance>();
        right ??= Array.Empty<HomecomingProcessInstance>();

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
