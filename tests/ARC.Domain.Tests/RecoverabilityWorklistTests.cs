using ARC.Domain.Enums;
using ARC.Domain.Metrics;

namespace ARC.Domain.Tests;

public sealed class RecoverabilityWorklistTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(20, 2)]
    [InlineData(100, 10)]
    public void Top_decile_size_is_ceiling_of_ten_percent(int eligible, int expected)
        => Assert.Equal(expected, RecoverabilityWorklist.TopDecileSize(eligible));

    [Fact]
    public void Unscored_blocked_and_failed_are_not_rankable()
    {
        Assert.False(RecoverabilityWorklist.IsRankable(null, nameof(RecoveryTier.Notice), nameof(WorkflowStatus.Running)));
        Assert.False(RecoverabilityWorklist.IsRankable(5_001m, null, nameof(WorkflowStatus.Running)));
        Assert.False(RecoverabilityWorklist.IsRankable(5_001m, nameof(RecoveryTier.Notice), nameof(WorkflowStatus.Blocked)));
        Assert.False(RecoverabilityWorklist.IsRankable(5_001m, nameof(RecoveryTier.Notice), nameof(WorkflowStatus.Failed)));
        Assert.True(RecoverabilityWorklist.IsRankable(5_001m, nameof(RecoveryTier.Notice), nameof(WorkflowStatus.Running)));
        Assert.True(RecoverabilityWorklist.IsRankable(5_001m, nameof(RecoveryTier.Notice), nameof(WorkflowStatus.WaitingForHuman)));
    }
}
