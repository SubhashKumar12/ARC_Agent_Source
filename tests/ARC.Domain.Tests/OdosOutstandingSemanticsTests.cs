using ARC.Domain.Odos;

namespace ARC.Domain.Tests;

public sealed class OdosOutstandingSemanticsTests
{
    [Fact]
    public void Over90_is_sum_of_buckets_1_through_4()
    {
        var over90 = OdosOutstandingSemantics.ComputeOver90(10m, 20m, 30m, 40m);
        Assert.Equal(100m, over90);
    }

    [Fact]
    public void Bucket_breakdown_preserves_documented_labels()
    {
        var buckets = OdosOutstandingSemantics.BucketBreakdown(1m, 2m, 3m, 4m, 5m);
        Assert.Equal(5, buckets.Count);
        Assert.Equal(OdosOutstandingSemantics.Bucket0Label, buckets[0].Label);
        Assert.Equal(OdosOutstandingSemantics.Bucket4Label, buckets[4].Label);
        Assert.Equal(15m, buckets.Sum(b => b.Amount));
    }
}
