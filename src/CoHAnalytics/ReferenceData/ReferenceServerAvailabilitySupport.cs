namespace CoHAnalytics.ReferenceData;

internal static class ReferenceServerAvailabilitySupport
{
    internal static bool TryGetHomecomingStatus(
        IReadOnlyList<ReferenceServerAvailability> availability,
        out ReferenceServerAvailabilityStatus status)
    {
        status = default;
        ReferenceServerAvailability? match = null;

        foreach (var entry in availability)
        {
            if (!string.Equals(entry.ServerKey, ReferenceServerKey.Homecoming, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                status = default;
                return false;
            }

            match = entry;
        }

        if (match is null)
        {
            return false;
        }

        status = match.Status;
        return true;
    }

    internal static bool IsCurrentHomecoming(IReadOnlyList<ReferenceServerAvailability> availability) =>
        TryGetHomecomingStatus(availability, out var status)
        && status == ReferenceServerAvailabilityStatus.Current;

    internal static bool IsHistoricalHomecoming(IReadOnlyList<ReferenceServerAvailability> availability) =>
        TryGetHomecomingStatus(availability, out var status)
        && status == ReferenceServerAvailabilityStatus.Historical;

    internal static bool MatchesQueryScope(
        IReadOnlyList<ReferenceServerAvailability> availability,
        ReferenceCatalogQueryScope queryScope)
    {
        if (queryScope == ReferenceCatalogQueryScope.AllHomecomingIdentities)
        {
            return true;
        }

        return IsCurrentHomecoming(availability);
    }

    internal static ReferenceServerAvailability CreateHomecoming(
        ReferenceServerAvailabilityStatus status) =>
        new()
        {
            ServerKey = ReferenceServerKey.Homecoming,
            Status = status
        };
}
