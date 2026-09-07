using CoHAnalytics.Models;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingProcessInstanceTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_process_id_and_start_time_are_equal()
    {
        var left = FakeGameRuntimeService.CreateClient(3_524, StartA);
        var right = FakeGameRuntimeService.CreateClient(3_524, StartA, executablePath: @"D:\other\cityofheroes.exe");

        Assert.True(left.Equals(right));
        Assert.True(HomecomingProcessInstance.SequenceEqualByIdentity([left], [right]));
    }

    [Fact]
    public void Same_process_id_with_different_start_time_are_not_equal()
    {
        var left = FakeGameRuntimeService.CreateClient(3_524, StartA);
        var right = FakeGameRuntimeService.CreateClient(3_524, StartB);

        Assert.False(left.Equals(right));
        Assert.False(HomecomingProcessInstance.SequenceEqualByIdentity([left], [right]));
    }

    [Fact]
    public void Different_process_ids_are_not_equal()
    {
        var left = FakeGameRuntimeService.CreateClient(3_524, StartA);
        var right = FakeGameRuntimeService.CreateClient(6_356, StartA);

        Assert.False(left.Equals(right));
    }
}
