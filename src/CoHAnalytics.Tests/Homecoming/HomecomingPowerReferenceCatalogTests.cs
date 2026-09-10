using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingPowerReferenceCatalogTests
{
    public static IEnumerable<object[]> BlueDevilPowers =>
    [
        Row("Brute_Melee", "Fiery_Melee", "Scorch", "Fiery Melee", "Scorch", "FieryFray_TargetedLightMelee.tga", false, false, HomecomingPowerType.Click),
        Row("Brute_Melee", "Fiery_Melee", "Breath_of_Fire", "Fiery Melee", "Breath of Fire", "FieryFray_BreathingFire.tga", false, false, HomecomingPowerType.Click),
        Row("Brute_Melee", "Fiery_Melee", "Fire_Sword_Circle", "Fiery Melee", "Fire Sword Circle", "FieryFray_FireSwordCircle.tga", false, false, HomecomingPowerType.Click),
        Row("Brute_Defense", "Fiery_Aura", "Blazing_Aura", "Fiery Aura", "Blazing Aura", "FlamingShield_FieryAura.tga", false, false, HomecomingPowerType.Toggle),
        Row("Brute_Defense", "Fiery_Aura", "Temperature_Protection", "Fiery Aura", "Temperature Protection", "FlamingShield_TemperatureProtection.tga", false, false, HomecomingPowerType.Auto),
        Row("Pool", "Leaping", "Long_Jump", "Leaping", "Super Jump", "Jump_LongJump.tga", false, false, HomecomingPowerType.Toggle),
        Row("Pool", "Leaping", "Double_Jump", "Leaping", "Double Jump", "Jump_HighJump.tga", true, true, HomecomingPowerType.Toggle),
        Row("Pool", "Leaping", "Combat_Jumping", "Leaping", "Combat Jumping", "Jump_CombatJump.tga", false, false, HomecomingPowerType.Toggle),
        Row("Pool", "Speed", "Hasten", "Speed", "Hasten", "SuperSpeed_AcceleratedCombat.tga", false, false, HomecomingPowerType.Click),
        Row("Pool", "Fighting", "Boxing", "Fighting", "Boxing", "Fighting_Boxing.tga", false, false, HomecomingPowerType.Click),
        Row("Epic", "Brute_Mu_Mastery", "Electrifying_Fences", "Mu Mastery", "Electrifying Fences", "Arachnos_Patron_RangedAoEImmobilize.tga", false, false, HomecomingPowerType.Click),
        Row("Inherent", "Inherent", "Brawl", "Inherent", "Brawl", "Inherent_Brawl.tga", true, true, HomecomingPowerType.Click),
        Row("Inherent", "Inherent", "prestige_DVD_Glidep", "Inherent", "Prestige Power Slide", "Inherent_Sprint.tga", true, true, HomecomingPowerType.Toggle),
        Row("Inherent", "Fitness", "Stamina", "Inherent Fitness", "Stamina", "Fitness_Stamina.tga", true, true, HomecomingPowerType.Auto),
        Row("Redirects", "Inherents", "Fury_Proc", "Inherents", "Fury", "BattleAxe_Taunt.tga", false, true, HomecomingPowerType.GlobalBoost)
    ];

    [Theory]
    [MemberData(nameof(BlueDevilPowers))]
    public void TryResolve_FullIdentity_ReturnsCanonicalPresentation(
        string categoryId,
        string powersetId,
        string powerId,
        string expectedPowersetDisplayName,
        string expectedPowerDisplayName,
        string expectedIconIdentity,
        bool expectedAutoIssued,
        bool expectedFree,
        HomecomingPowerType expectedPowerType)
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();

        var resolved = catalog.TryResolve(categoryId, powersetId, powerId, out var power);

        Assert.True(resolved);
        Assert.Equal(categoryId, power.CategoryId);
        Assert.Equal(powersetId, power.PowersetId);
        Assert.Equal(powerId, power.PowerId);
        Assert.Equal(expectedPowersetDisplayName, power.PowersetDisplayName);
        Assert.Equal(expectedPowerDisplayName, power.PowerDisplayName);
        Assert.Equal(expectedIconIdentity, power.IconIdentity);
        Assert.Equal(expectedAutoIssued, power.IsAutoIssued);
        Assert.Equal(expectedFree, power.IsFree);
        Assert.Equal(expectedPowerType, power.PowerType);
        Assert.True(catalog.IsLoaded);
    }

    [Fact]
    public void TryResolve_SameDisplayNameAcrossArchetypes_KeepsFullIdentitiesDistinct()
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();

        Assert.True(catalog.TryResolve("Brute_Melee", "Fiery_Melee", "Scorch", out var brute));
        Assert.True(catalog.TryResolve("Tanker_Melee", "Fiery_Melee", "Scorch", out var tanker));

        Assert.Equal("Scorch", brute.PowerDisplayName);
        Assert.Equal(brute.PowerDisplayName, tanker.PowerDisplayName);
        Assert.Equal("FieryFray_TargetedLightMelee.tga", brute.IconIdentity);
        Assert.Equal("FieryFray_Scorch.tga", tanker.IconIdentity);
    }

    [Theory]
    [InlineData("Brute_Melee", "Fiery_Melee", "Missing")]
    [InlineData("Brute_Melee", "Missing", "Scorch")]
    [InlineData("Missing", "Fiery_Melee", "Scorch")]
    [InlineData("Brute.Melee", "Fiery_Melee", "Scorch")]
    [InlineData("", "Fiery_Melee", "Scorch")]
    public void TryResolve_MissingOrInvalidFullIdentity_ReturnsFalse(
        string categoryId,
        string powersetId,
        string powerId)
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();

        Assert.False(catalog.TryResolve(categoryId, powersetId, powerId, out _));
    }

    private static object[] Row(
        string categoryId,
        string powersetId,
        string powerId,
        string powersetDisplayName,
        string powerDisplayName,
        string iconIdentity,
        bool isAutoIssued,
        bool isFree,
        HomecomingPowerType powerType) =>
        [
            categoryId,
            powersetId,
            powerId,
            powersetDisplayName,
            powerDisplayName,
            iconIdentity,
            isAutoIssued,
            isFree,
            powerType
        ];
}

internal sealed class HomecomingPowerReferenceFixture : IDisposable
{
    private static readonly Dictionary<string, string> PowerDisplayNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> PowersetDisplayNames = new(StringComparer.Ordinal);

    private static readonly SyntheticPowerPresentationRecord[] Powers =
    [
        Power("Brute_Melee.Fiery_Melee.Scorch", "Scorch", "FieryFray_TargetedLightMelee.tga"),
        Power("Tanker_Melee.Fiery_Melee.Scorch", "Scorch", "FieryFray_Scorch.tga"),
        Power("Brute_Melee.Fiery_Melee.Breath_of_Fire", "Breath of Fire", "FieryFray_BreathingFire.tga"),
        Power("Brute_Melee.Fiery_Melee.Fire_Sword_Circle", "Fire Sword Circle", "FieryFray_FireSwordCircle.tga"),
        Power("Brute_Defense.Fiery_Aura.Blazing_Aura", "Blazing Aura", "FlamingShield_FieryAura.tga", powerType: 2),
        Power("Brute_Defense.Fiery_Aura.Temperature_Protection", "Temperature Protection", "FlamingShield_TemperatureProtection.tga", powerType: 1),
        Power("Pool.Leaping.Long_Jump", "Super Jump", "Jump_LongJump.tga", powerType: 2),
        Power("Pool.Leaping.Double_Jump", "Double Jump", "Jump_HighJump.tga", true, true, 2),
        Power("Pool.Leaping.Combat_Jumping", "Combat Jumping", "Jump_CombatJump.tga", powerType: 2),
        Power("Pool.Speed.Hasten", "Hasten", "SuperSpeed_AcceleratedCombat.tga"),
        Power("Pool.Fighting.Boxing", "Boxing", "Fighting_Boxing.tga"),
        Power("Epic.Brute_Mu_Mastery.Electrifying_Fences", "Electrifying Fences", "Arachnos_Patron_RangedAoEImmobilize.tga"),
        Power("Inherent.Inherent.Brawl", "Brawl", "Inherent_Brawl.tga", true, true),
        Power("Inherent.Inherent.prestige_DVD_Glidep", "Prestige Power Slide", "Inherent_Sprint.tga", true, true, 2),
        Power("Inherent.Fitness.Stamina", "Stamina", "Fitness_Stamina.tga", true, true, 1),
        Power("Redirects.Inherents.Fury_Proc", "Fury", "BattleAxe_Taunt.tga", isFree: true, powerType: 5)
    ];

    private static readonly SyntheticPowersetRecord[] Powersets =
    [
        Powerset("Brute_Melee.Fiery_Melee", "Fiery Melee"),
        Powerset("Tanker_Melee.Fiery_Melee", "Fiery Melee"),
        Powerset("Brute_Defense.Fiery_Aura", "Fiery Aura"),
        Powerset("Pool.Leaping", "Leaping"),
        Powerset("Pool.Speed", "Speed"),
        Powerset("Pool.Fighting", "Fighting"),
        Powerset("Epic.Brute_Mu_Mastery", "Mu Mastery"),
        Powerset("Inherent.Inherent", "Inherent"),
        Powerset("Inherent.Fitness", "Inherent Fitness"),
        Powerset("Redirects.Inherents", "Inherents")
    ];

    private readonly string _root;

    private HomecomingPowerReferenceFixture(string root)
    {
        _root = root;
    }

    internal static HomecomingPowerReferenceFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "coh-power-reference-" + Guid.NewGuid().ToString("N"));
        var liveRoot = Path.Combine(root, "assets", "live");
        Directory.CreateDirectory(liveRoot);

        var messages = Powers
            .Select(power => (power.DisplayNameMessageKey, DisplayNameFor(power.SourceId)))
            .Concat(Powersets.Select(set => (set.DisplayNameMessageKey, DisplayNameFor(set.SourceId))))
            .Distinct()
            .ToArray();
        File.WriteAllBytes(
            Path.Combine(liveRoot, "bin.pigg"),
            HomecomingBinaryFixtureBuilder.CreatePigg(
                [
                    (HomecomingBinaryFixtureBuilder.MessageMemberName, HomecomingBinaryFixtureBuilder.CreateMessageStore(messages)),
                    (HomecomingBinaryFixtureBuilder.PowersetsMemberName, HomecomingBinaryFixtureBuilder.CreatePowersets(Powersets))
                ]));
        File.WriteAllBytes(
            Path.Combine(liveRoot, "bin_powers.pigg"),
            HomecomingBinaryFixtureBuilder.CreatePigg(
                [(HomecomingBinaryFixtureBuilder.PowersMemberName, HomecomingBinaryFixtureBuilder.CreatePowerPresentations(Powers))]));
        return new HomecomingPowerReferenceFixture(root);
    }

    internal HomecomingPowerReferenceCatalog CreateCatalog()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(
                installationService,
                new HomecomingInstallation
                {
                    InstallRoot = _root,
                    LauncherPath = Path.Combine(_root, "Homecoming Launcher.exe")
                });
        return new HomecomingPowerReferenceCatalog(installationService);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static SyntheticPowerPresentationRecord Power(
        string sourceId,
        string displayName,
        string iconIdentity,
        bool isAutoIssued = false,
        bool isFree = false,
        uint powerType = 0)
    {
        PowerDisplayNames.Add(sourceId, displayName);
        return new(sourceId, MessageKey(sourceId), iconIdentity, isAutoIssued, isFree, powerType);
    }

    private static SyntheticPowersetRecord Powerset(string sourceId, string displayName)
    {
        PowersetDisplayNames.Add(sourceId, displayName);
        return new(sourceId, MessageKey(sourceId));
    }

    private static string MessageKey(string sourceId) => "P_" + sourceId.Replace('.', '_');

    private static string DisplayNameFor(string sourceId) =>
        PowerDisplayNames.TryGetValue(sourceId, out var powerDisplayName)
            ? powerDisplayName
            : PowersetDisplayNames[sourceId];
}
