using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserClassifierTests
{
    private readonly ParserClassifier _classifier = new();

    [Fact]
    public void Unknown_line_is_preserved_without_failure()
    {
        var raw = Raw("unrecognized plain text");
        var result = _classifier.Classify(raw);

        Assert.Equal(ParserEventKind.Unknown, result.EventKind);
        Assert.Equal(ParserClassificationStatus.Unknown, result.ClassificationStatus);
        Assert.Equal(raw.RawLine, result.RawLine);
    }

    [Fact]
    public void Untimestamped_welcome_line_classifies_as_identity_evidence()
    {
        const string line = "Welcome to City of Heroes, Dawn's Vanguard!";
        var result = _classifier.Classify(Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        Assert.Equal("welcome_attribution", result.ClassificationRuleId);
        Assert.Null(result.SourceTimestamp);
        Assert.Equal("Dawn's Vanguard", result.StructuralEvidence!.CandidateName);
    }

    [Fact]
    public void Full_timestamp_welcome_line_classifies_as_identity_evidence()
    {
        const string line = "2026-08-07 03:57:00 Welcome to City of Heroes, Dawn's Vanguard!";
        var result = _classifier.Classify(Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        Assert.Equal("Dawn's Vanguard", result.StructuralEvidence!.CandidateName);
        Assert.Equal(new DateTime(2026, 8, 7, 3, 57, 0, DateTimeKind.Unspecified), result.SourceTimestamp);
    }

    [Fact]
    public void Bracket_timestamp_welcome_line_classifies_as_identity_evidence()
    {
        const string line = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!";
        var result = _classifier.Classify(Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        Assert.Equal("welcome_attribution", result.ClassificationRuleId);
        var evidence = Assert.IsType<ParserStructuralEvidence>(result.StructuralEvidence);
        Assert.Equal("Dawn's Vanguard", evidence.CandidateName);
        Assert.Equal(new DateTime(2026, 8, 4, 3, 57, 0, DateTimeKind.Unspecified), result.SourceTimestamp);
    }

    [Fact]
    public void Bracket_timestamp_supports_ordinary_character_names()
    {
        const string line = "[12:05] Welcome to City of Heroes, Example Hero!";
        var result = _classifier.Classify(Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        Assert.Equal("Example Hero", result.StructuralEvidence!.CandidateName);
        Assert.Equal(new DateTime(2026, 8, 4, 12, 5, 0, DateTimeKind.Unspecified), result.SourceTimestamp);
    }

    [Fact]
    public void Invalid_bracket_timestamp_is_malformed()
    {
        var result = _classifier.Classify(Raw("[99:99] Welcome to City of Heroes, Example Hero!"));

        Assert.Equal(ParserEventKind.Malformed, result.EventKind);
        Assert.Equal("timestamp_invalid", result.ClassificationRuleId);
    }

    [Fact]
    public void Exact_local_timestamp_format_is_parsed_without_time_zone_inference()
    {
        var result = _classifier.Classify(Raw("2026-08-04 06:27:10 structurally timestamped"));

        Assert.Equal(ParserEventKind.TimestampedLine, result.EventKind);
        Assert.Equal(new DateTime(2026, 8, 4, 6, 27, 10, DateTimeKind.Unspecified), result.SourceTimestamp);
        Assert.Equal(DateTimeKind.Unspecified, result.SourceTimestamp!.Value.Kind);
    }

    [Theory]
    [InlineData("2026-08-04 06:27:10 Entering Bayside Docks.")]
    [InlineData("2026-08-04 06:27:10 You are now ready.")]
    [InlineData("2026-08-04 06:27:10 Your status changed.")]
    [InlineData("2026-08-04 06:27:10 [NPC] Werfer Jaeger: To arms!")]
    public void Supported_explicit_system_structures_classify_as_system(string line)
    {
        Assert.Equal(ParserEventKind.SystemLine, _classifier.Classify(Raw(line)).EventKind);
    }

    [Fact]
    public void Bracketed_channel_speaker_delimiter_classifies_chat()
    {
        var result = _classifier.Classify(Raw("2026-08-04 06:28:14 [General] Echo Reaver: hello"));

        Assert.Equal(ParserEventKind.ChatLine, result.EventKind);
        Assert.Null(result.StructuralEvidence);
    }

    [Fact]
    public void Colon_alone_is_not_chat_and_ambiguous_brackets_remain_broad()
    {
        Assert.Equal(
            ParserEventKind.TimestampedLine,
            _classifier.Classify(Raw("2026-08-04 06:28:14 Random Name: hello")).EventKind);
        Assert.Equal(
            ParserEventKind.TimestampedLine,
            _classifier.Classify(Raw("2026-08-04 06:28:14 [06:28] formatted notice")).EventKind);
    }

    [Fact]
    public void Invalid_timestamp_and_noncomplete_line_are_malformed_but_preserved()
    {
        var invalidTimestamp = Raw("2026-99-04 06:27:10 content");
        var invalidResult = _classifier.Classify(invalidTimestamp);
        Assert.Equal(ParserEventKind.Malformed, invalidResult.EventKind);
        Assert.Equal(invalidTimestamp.RawLine, invalidResult.RawLine);

        var tooLarge = Raw("", ParserLineStatus.TooLarge);
        var tooLargeResult = _classifier.Classify(tooLarge);
        Assert.Equal(ParserClassificationStatus.Malformed, tooLargeResult.ClassificationStatus);
        Assert.Equal(ParserEventKind.Malformed, tooLargeResult.EventKind);
    }

    [Fact]
    public void All_raw_provenance_is_preserved_exactly()
    {
        var raw = Raw("2026-08-04 06:28:14 [General] Speaker: text") with
        {
            BindingGeneration = 9,
            Sequence = 42,
            SourceByteStart = 100,
            SourceByteEnd = 155
        };
        var result = _classifier.Classify(raw);

        Assert.Equal(raw.ContextId, result.ContextId);
        Assert.Equal(raw.SourceId, result.SourceId);
        Assert.Equal(raw.SourceSegmentId, result.SourceSegmentId);
        Assert.Equal(raw.BindingGeneration, result.BindingGeneration);
        Assert.Equal(raw.Sequence, result.Sequence);
        Assert.Equal(raw.ObservedAt, result.ObservedAt);
        Assert.Equal(raw.RawLine, result.RawLine);
        Assert.Equal(raw.SourceByteStart, result.SourceByteStart);
        Assert.Equal(raw.SourceByteEnd, result.SourceByteEnd);
        Assert.Equal(raw.LineStatus, result.LineStatus);
    }

    internal static ParserRawEvent Raw(string line, ParserLineStatus status = ParserLineStatus.Complete) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create("acct", "Account", @"C:\fake\chatlog 2026-08-04.txt", new DateOnly(2026, 8, 4)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = 1,
            ObservedAt = new DateTimeOffset(2026, 8, 4, 6, 28, 14, TimeSpan.Zero),
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length + 2,
            LineStatus = status
        };
}
