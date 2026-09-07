namespace CoHAnalytics.Orchestration.Contributors;

internal static class ContributorFactKeys
{
    internal static class HomecomingInstallation
    {
        public const string Configured = "installation.homecoming.configured";
        public const string Root = "installation.homecoming.root";
        public const string Launcher = "installation.homecoming.launcher";
        public const string DiscoverySource = "installation.homecoming.discovery_source";
    }

    internal static class HomecomingRuntime
    {
        public const string ClientCount = "runtime.homecoming.client_count";
        public const string IsRunning = "runtime.homecoming.is_running";
        public const string Status = "runtime.homecoming.status";
    }

    internal static class Accounts
    {
        public const string Count = "accounts.count";
        public const string WithLogsCount = "accounts.with_logs_count";
        public const string WithHistoricalLogsCount = "accounts.with_historical_logs_count";
        public const string LatestLogTimestamp = "accounts.latest_log_timestamp";
    }

    internal static class LogActivity
    {
        public const string AccountCount = "log.account_count";
        public const string LogsFolderCount = "log.logs_folder_count";
        public const string CandidateCount = "log.candidate_count";
        public const string GrowingCount = "log.growing_count";
        public const string InactiveCount = "log.inactive_count";
        public const string HistoricalCount = "log.historical_count";
        public const string UnavailableCount = "log.unavailable_count";
        public const string RolloverCandidateCount = "log.rollover_candidate_count";
        public const string LastGrowthAt = "log.last_growth_at";
    }

    internal static class MonitoringSessionManager
    {
        public const string ContextCount = "monitoring.context_count";
        public const string ActiveContextCount = "monitoring.active_context_count";
        public const string SuspendedContextCount = "monitoring.suspended_context_count";
        public const string WaitingContextCount = "monitoring.waiting_context_count";
        public const string ErrorContextCount = "monitoring.error_context_count";
        public const string RuntimeAvailable = "monitoring.runtime_available";
        public const string PendingOfferCount = "monitoring.pending_offer_count";
        public const string ClaimedSourceCount = "monitoring.claimed_source_count";
        public const string UnclaimedGrowingSourceCount = "monitoring.unclaimed_growing_source_count";
        public const string AmbiguousSourceCount = "monitoring.ambiguous_source_count";
        public const string DeclinedSourceCount = "monitoring.declined_source_count";
        public const string RolloverCount = "monitoring.rollover_count";
        public const string LastSourceTransitionAt = "monitoring.last_source_transition_at";
    }

    internal static class Parser
    {
        public const string WorkerCount = "parser.worker_count";
        public const string ReadingCount = "parser.reading_count";
        public const string WaitingCount = "parser.waiting_count";
        public const string SuspendedCount = "parser.suspended_count";
        public const string FaultedCount = "parser.faulted_count";
        public const string TotalBytesRead = "parser.total_bytes_read";
        public const string TotalLinesProcessed = "parser.total_lines_processed";
        public const string TotalClassifiedLines = "parser.total_classified_lines";
        public const string RecognizedLineCount = "parser.recognized_line_count";
        public const string UnknownLineCount = "parser.unknown_line_count";
        public const string MalformedLineCount = "parser.malformed_line_count";
        public const string PotentialIdentityEvidenceCount = "parser.potential_identity_evidence_count";
        public const string LastEventAt = "parser.last_event_at";
        public const string LastClassifiedEventAt = "parser.last_classified_event_at";
        public const string ActiveContextCount = "parser.active_context_count";
    }

    internal static class Session
    {
        public const string ActiveCount = "session.active_count";
        public const string SuspendedCount = "session.suspended_count";
        public const string NeedsAttentionCount = "session.needs_attention_count";
        public const string RetentionOverflowCount = "session.retention_overflow_count";
        public const string IncompleteCount = "session.incomplete_count";
        public const string CommittedEventCount = "session.committed_event_count";
        public const string LastCommittedEventAt = "session.last_committed_event_at";
    }

    internal static class Identity
    {
        public const string ResolvedCount = "identity.resolved_count";
        public const string UnresolvedCount = "identity.unresolved_count";
        public const string CandidateCount = "identity.candidate_count";
        public const string ConflictedCount = "identity.conflicted_count";
        public const string RequiredCount = "identity.required_count";
        public const string ConfirmedCount = "identity.confirmed_count";
        public const string InferredCount = "identity.inferred_count";
        public const string UnknownCount = "identity.unknown_count";
    }

    internal static class MidsInstallation
    {
        public const string Configured = "installation.mids.configured";
        public const string Root = "installation.mids.root";
        public const string Version = "installation.mids.version";
        public const string DatabaseAvailable = "database.mids-homecoming.available";
        public const string DatabaseVersion = "database.mids-homecoming.version";
    }
}
