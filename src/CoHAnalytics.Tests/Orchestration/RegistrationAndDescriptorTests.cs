using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class RegistrationAndDescriptorTests
{
    [Fact]
    public async Task Register_rejects_malformed_provider_and_capability_ids()
    {
        var manual = new ManualTimeProvider();
        var descriptor = TestDescriptors.Create(
            "Accounts",
            "Accounts",
            produces: ["install.Homecoming"]);
        var contributor = new FakeContributor(descriptor);

        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual
        });

        var exception = Assert.Throws<ArgumentException>(() => orch.Register(contributor));

        Assert.Contains("ProviderId 'Accounts' is malformed.", exception.Message);
        Assert.Contains("Produces contains malformed capability ID 'install.Homecoming'.", exception.Message);
    }

    [Fact]
    public async Task Register_rejects_duplicate_provider_and_produced_capability()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "installation.homecoming", "Installation", produces: ["install.homecoming"])));

        var duplicateProvider = new FakeContributor(TestDescriptors.Create(
            "installation.homecoming", "Second installation", produces: ["install.second"]));
        var duplicateCapability = new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["install.homecoming"]));

        Assert.Contains("Duplicate provider ID", Assert.Throws<InvalidOperationException>(
            () => orch.Register(duplicateProvider)).Message);
        Assert.Contains("already produced", Assert.Throws<InvalidOperationException>(
            () => orch.Register(duplicateCapability)).Message);
    }

    [Fact]
    public async Task Register_records_schema_mismatch_without_rejecting_contributor()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual);
        var contributor = new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            schemaVersion: ApplicationContributorDescriptor.ExpectedSchemaVersion + 1));

        orch.Register(contributor);

        var diagnostics = orch.GetDiagnostics();
        Assert.Single(diagnostics.RegisteredDescriptors);
        Assert.Single(diagnostics.SchemaVersionMismatches);
        Assert.Contains("accounts", diagnostics.SchemaVersionMismatches[0]);
        Assert.Contains(diagnostics.SchemaVersionMismatches[0], diagnostics.ValidationMessages);
    }

    [Fact]
    public async Task Register_after_startup_is_rejected()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"])));
        await orch.StartAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => orch.Register(
            new FakeContributor(TestDescriptors.Create(
                "runtime.homecoming", "Runtime", produces: ["runtime.status"]))));

        Assert.Contains("cannot be registered after startup", exception.Message);
    }

    private static ApplicationOrchestrator CreateOrchestrator(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
