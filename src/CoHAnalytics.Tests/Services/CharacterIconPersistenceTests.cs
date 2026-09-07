using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterIconPersistenceTests
{
    [Fact]
    public void Icon_assignments_are_independent_and_survive_reload()
    {
        var directory = CreateDirectory();
        var writer = CreateRepository(directory);
        var alpha = writer.EstablishTrustedFromWelcome("acct", "Alpha Hero").RecordId!;
        var beta = writer.EstablishTrustedFromWelcome("acct", "Beta Hero").RecordId!;

        Assert.True(writer.SetIconReference(
            alpha,
            CharacterIconReference.BuiltIn("default-male-03")).IsSuccess);
        Assert.True(writer.SetIconReference(
            beta,
            CharacterIconReference.BuiltIn("default-female-06")).IsSuccess);

        var reader = CreateRepository(directory);

        Assert.Equal("default-male-03", reader.TryGetRecord(alpha)!.IconReference!.IconId);
        Assert.Equal("default-female-06", reader.TryGetRecord(beta)!.IconReference!.IconId);
    }

    [Fact]
    public void Rename_preserves_icon_on_stable_character_identity()
    {
        var directory = CreateDirectory();
        var repository = CreateRepository(directory);
        var established = repository.EstablishTrustedFromWelcome("acct", "Original Hero");
        var recordId = established.RecordId!;
        repository.SetIconReference(
            recordId,
            CharacterIconReference.BuiltIn("default-female-02"));

        var renamed = repository.RecordTrustedObservedDisplayName(recordId, "Renamed Hero");

        Assert.True(renamed.IsSuccess);
        Assert.Equal(recordId, renamed.RecordId);
        var reloaded = CreateRepository(directory).TryGetRecord(recordId);
        Assert.Equal("Renamed Hero", reloaded!.CurrentDisplayName);
        Assert.Equal("default-female-02", reloaded.IconReference!.IconId);
    }

    [Fact]
    public void Custom_icon_assignment_survives_reload_and_character_rename()
    {
        var directory = CreateDirectory();
        var repository = CreateRepository(directory);
        var recordId = repository.EstablishTrustedFromWelcome("acct", "Original Hero").RecordId!;
        var customId = "custom-" + new string('a', 64);

        Assert.True(repository.SetIconReference(
            recordId,
            CharacterIconReference.Custom(customId)).IsSuccess);
        Assert.True(repository.RecordTrustedObservedDisplayName(recordId, "Renamed Hero").IsSuccess);

        var reloaded = CreateRepository(directory).TryGetRecord(recordId);
        Assert.NotNull(reloaded);
        Assert.Equal("Renamed Hero", reloaded.CurrentDisplayName);
        Assert.Equal(CharacterIconKind.Custom, reloaded.IconReference!.Kind);
        Assert.Equal(customId, reloaded.IconReference.IconId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_icon_reference_is_rejected(string iconId)
    {
        var repository = CreateRepository(CreateDirectory());
        var recordId = repository.EstablishTrustedFromWelcome("acct", "Alpha Hero").RecordId!;

        var result = repository.SetIconReference(
            recordId,
            CharacterIconReference.BuiltIn(iconId));

        Assert.Equal(CharacterRepositoryOutcome.InvalidIconReference, result.Outcome);
        Assert.Null(repository.TryGetRecord(recordId)!.IconReference);
    }

    private static CharacterRepository CreateRepository(string directory) =>
        new(new CharacterRepositoryOptions { DataDirectory = directory });

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-character-icons",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
