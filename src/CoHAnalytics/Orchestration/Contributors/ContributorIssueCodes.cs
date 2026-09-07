namespace CoHAnalytics.Orchestration.Contributors;

internal static class ContributorIssueCodes
{
    internal static class HomecomingInstallation
    {
        public const string NotFound = "installation.homecoming.not_found";
        public const string MultipleFound = "installation.homecoming.multiple_found";
    }

    internal static class HomecomingRuntime
    {
        public const string DetectionError = "runtime.homecoming.detection_error";
    }

    internal static class Accounts
    {
        public const string InstallationRequired = "accounts.installation_required";
        public const string DiscoveryFailed = "accounts.discovery_failed";
        public const string NoneDiscovered = "accounts.none_discovered";
    }

    internal static class LogActivity
    {
        public const string ObservationFailed = "log.observation_failed";
        public const string SourceUnavailable = "log.source_unavailable";
    }

    internal static class MonitoringSessionManager
    {
        public const string RuntimeUnavailable = "monitoring.runtime_unavailable";
        public const string ContextError = "monitoring.context_error";

        /// <summary>
        /// Reserved for a future slice. The internal test/dev context-creation seam still
        /// rejects duplicate source assignment with an exception rather than storing a
        /// claim-conflict state, and the explicit <c>ClaimSource</c> API returns an explicit
        /// domain result rather than persisting a conflict fact, so this code is not currently
        /// emitted.
        /// </summary>
        public const string SourceClaimConflict = "monitoring.source_claim_conflict";

        /// <summary>A growing source cannot be attributed automatically and needs an explicit choice.</summary>
        public const string SourceSelectionRequired = "monitoring.source_selection_required";

        /// <summary>Two or more Homecoming sessions are being monitored concurrently.</summary>
        public const string ConcurrentSessionsMonitored = "monitoring.concurrent_sessions_monitored";

        /// <summary>A claimed source is unavailable and no rollover has resolved it.</summary>
        public const string SourceUnavailable = "monitoring.source_unavailable";
    }

    internal static class Parser
    {
        public const string WorkerFault = "parser.worker_fault";
        public const string SourceUnavailable = "parser.source_unavailable";
        public const string EncodingError = "parser.encoding_error";
        public const string LineTooLarge = "parser.line_too_large";
        public const string ReadFailed = "parser.read_failed";
        public const string ClassificationFailed = "parser.classification_failed";
    }

    internal static class Session
    {
        public const string IdentitySelectionRequired = "session.identity_selection_required";
        public const string IdentityConflict = "session.identity_conflict";
        public const string RetentionOverflow = "session.retention_overflow";
        public const string ContextProcessingFailed = "session.context_processing_failed";
        public const string ManagerFailed = "session.manager_failed";
    }

    internal static class MidsInstallation
    {
        public const string NotFound = "installation.mids.not_found";
        public const string MultipleFound = "installation.mids.multiple_found";
    }

    internal static class Observations
    {
        public const string PersistenceFailed = "observations.persistence_failed";
        public const string CaptureUnavailable = "observations.capture_unavailable";
    }
}
