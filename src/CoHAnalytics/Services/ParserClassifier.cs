using System.Globalization;
using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Fixture-backed structural classifier with no gameplay or identity state.</summary>
public sealed partial class ParserClassifier : IParserClassifier
{
    private static readonly string[] SupportedSystemPrefixes =
    [
        "You are ",
        "You have ",
        "You may ",
        "You gain ",
        "You received ",
        "You hit ",
        "You heal ",
        "You activated ",
        "You got ",
        "You paid ",
        "You bid ",
        "Your ",
        "Entering ",
        "Using global chat handle @",
        "HIT "
    ];
    private readonly ParserClassificationOptions _options;

    public ParserClassifier(ParserClassificationOptions? options = null)
    {
        _options = options ?? new ParserClassificationOptions();
        _options.Validate();
    }

    public ParserEvent Classify(ParserRawEvent rawEvent)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);

        if (rawEvent.LineStatus != ParserLineStatus.Complete)
        {
            return Create(rawEvent, ParserEventKind.Malformed, ParserClassificationStatus.Malformed, "line_status_malformed");
        }

        var rawLine = rawEvent.RawLine;
        if (!ParserLineEnvelope.TryResolve(
                rawLine,
                rawEvent.SourceId.LogDate,
                out var envelopeKind,
                out var sourceTimestamp,
                out var bodyStart))
        {
            return Create(
                rawEvent,
                ParserEventKind.Malformed,
                ParserClassificationStatus.Malformed,
                "timestamp_invalid");
        }

        var body = rawLine[bodyStart..];
        var chat = ChatStructure().Match(body);
        if (chat.Success
            && chat.Groups["channel"].Length <= _options.MaximumChannelLength
            && chat.Groups["speaker"].Length <= _options.MaximumSpeakerLength)
        {
            var kind = IsSystemChannel(chat.Groups["channel"].Value)
                ? ParserEventKind.SystemLine
                : ParserEventKind.ChatLine;
            return Create(rawEvent, kind, ParserClassificationStatus.Recognized, kind == ParserEventKind.ChatLine ? "channel_chat" : "system_channel", sourceTimestamp);
        }

        var welcome = WelcomeAttribution().Match(body);
        if (TryCreateEvidence(welcome, bodyStart, ParserStructuralEvidenceKind.WelcomeAttribution, out var welcomeEvidence))
        {
            return Create(rawEvent, ParserEventKind.PotentialIdentityEvidence, ParserClassificationStatus.Recognized, "welcome_attribution", sourceTimestamp, welcomeEvidence);
        }

        var action = SystemAttributedAction().Match(body);
        if (TryCreateEvidence(action, bodyStart, ParserStructuralEvidenceKind.SystemAttributedAction, out var actionEvidence))
        {
            return Create(rawEvent, ParserEventKind.PotentialIdentityEvidence, ParserClassificationStatus.Recognized, "system_attributed_action", sourceTimestamp, actionEvidence);
        }

        if (IsSupportedSystemStructure(body))
        {
            return Create(rawEvent, ParserEventKind.SystemLine, ParserClassificationStatus.Recognized, "system_prefix", sourceTimestamp);
        }

        if (envelopeKind == ParserLineEnvelopeKind.Untimestamped)
        {
            return Create(rawEvent, ParserEventKind.Unknown, ParserClassificationStatus.Unknown, "unknown");
        }

        return Create(rawEvent, ParserEventKind.TimestampedLine, ParserClassificationStatus.Recognized, "timestamped", sourceTimestamp);
    }

    private bool TryCreateEvidence(
        Match match,
        int bodyStart,
        ParserStructuralEvidenceKind kind,
        out ParserStructuralEvidence? evidence)
    {
        evidence = null;
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"];
        if (name.Length == 0 || name.Length > _options.MaximumCandidateNameLength)
        {
            return false;
        }

        evidence = new ParserStructuralEvidence
        {
            CandidateName = name.Value,
            EvidenceTextStart = bodyStart + name.Index,
            EvidenceTextLength = name.Length,
            EvidenceKind = kind,
            AttributionStrength = ParserAttributionStrength.Strong
        };
        return true;
    }

    private static bool IsSystemChannel(string channel) =>
        channel.Equals("NPC", StringComparison.OrdinalIgnoreCase)
        || channel.Equals("Caption", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedSystemStructure(string body) =>
        SupportedSystemPrefixes.Any(prefix => body.StartsWith(prefix, StringComparison.Ordinal))
        || SuperGroupMessageOfDay().IsMatch(body);

    private static ParserEvent Create(
        ParserRawEvent raw,
        ParserEventKind kind,
        ParserClassificationStatus status,
        string ruleId,
        DateTime? timestamp = null,
        ParserStructuralEvidence? evidence = null) =>
        new()
        {
            ContextId = raw.ContextId,
            SourceId = raw.SourceId,
            SourceSegmentId = raw.SourceSegmentId,
            BindingGeneration = raw.BindingGeneration,
            SourceTransitionKind = raw.SourceTransitionKind,
            Sequence = raw.Sequence,
            ObservedAt = raw.ObservedAt,
            RawLine = raw.RawLine,
            SourceByteStart = raw.SourceByteStart,
            SourceByteEnd = raw.SourceByteEnd,
            LineStatus = raw.LineStatus,
            EventKind = kind,
            ClassificationStatus = status,
            ClassificationRuleId = ruleId,
            SourceTimestamp = timestamp,
            StructuralEvidence = evidence
        };

    [GeneratedRegex(@"^\[(?<channel>[^\]\r\n]+)\] (?<speaker>[^:\r\n]+): .*$", RegexOptions.CultureInvariant)]
    private static partial Regex ChatStructure();

    [GeneratedRegex(@"^Welcome to City of Heroes, (?<name>[^!\r\n]+)!$", RegexOptions.CultureInvariant)]
    private static partial Regex WelcomeAttribution();

    [GeneratedRegex(@"^(?<name>[^!\r\n]+?)(?: hits| heals) you with their\b.*$", RegexOptions.CultureInvariant)]
    private static partial Regex SystemAttributedAction();

    [GeneratedRegex(@"^\[SuperGroup\] .+ Message of the Day --", RegexOptions.CultureInvariant)]
    private static partial Regex SuperGroupMessageOfDay();
}
