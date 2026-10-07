using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Cleanup;

public class TwoBoneIkTests
{
    /// <summary>
    /// Applies the solver's world-space deltas the way the solver defines them:
    /// b' = a + upperDelta*(b-a); c' = b' + lowerDelta*(c-b).
    /// </summary>
    private static (Vector3 b, Vector3 c) Apply(
        Vector3 a, Vector3 b, Vector3 c, in TwoBoneIk.Result r)
    {
        var b2 = a + Vector3.Transform(b - a, r.UpperWorldDelta);
        var c2 = b2 + Vector3.Transform(c - b, r.LowerWorldDelta);
        return (b2, c2);
    }

    [Fact]
    public void ReachableTarget_HitExactly()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(0, 10, 2);
        var c = new Vector3(0, 20, 0);
        var t = new Vector3(5, 14, 3); // |t-a| ~ 15.6 < l1+l2 ~ 20.3, bent

        var r = TwoBoneIk.Solve(a, b, c, t);
        var (_, c2) = Apply(a, b, c, r);

        Assert.True(Vector3.Distance(c2, t) < 0.01f,
            $"effector {c2} should reach target {t}");
    }

    [Fact]
    public void SegmentLengths_ArePreserved()
    {
        var a = new Vector3(1, 2, 3);
        var b = new Vector3(2, 9, 4);
        var c = new Vector3(1, 17, 2);
        var t = new Vector3(6, 10, 5);

        var r = TwoBoneIk.Solve(a, b, c, t);
        var (b2, c2) = Apply(a, b, c, r);

        Assert.Equal(Vector3.Distance(a, b), Vector3.Distance(a, b2), 3);
        Assert.Equal(Vector3.Distance(b, c), Vector3.Distance(b2, c2), 3);
    }

    [Fact]
    public void BeyondReach_SoftClamped_AndMonotonic()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0, 10, 1);
        var c = new Vector3(0, 20, 0);
        float maxExt = Vector3.Distance(a, b) + Vector3.Distance(b, c);

        float prevReach = 0;
        // targets from comfortably-inside to far beyond reach: reach must increase
        // monotonically and never exceed full extension.
        for (int i = 0; i < 30; i++)
        {
            float d = maxExt * (0.5f + i * 0.05f);
            var t = new Vector3(0, 0, d); // straight out along +Z
            var r = TwoBoneIk.Solve(a, b, c, t);
            var (_, c2) = Apply(a, b, c, r);
            float reach = Vector3.Distance(c2, a);

            Assert.True(reach <= maxExt + 1e-3f, $"reach {reach} exceeded max extension {maxExt}");
            Assert.True(reach >= prevReach - 1e-3f, $"reach not monotonic at step {i}: {reach} < {prevReach}");
            prevReach = reach;
        }
    }

    [Fact]
    public void PolePlane_IsPreserved_ForReachableTargets()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0, 10, 3);  // knee forward (+Z)
        var c = new Vector3(0, 20, 0);
        // target: same height-ish, reachable, shifted sideways
        var t = new Vector3(4, 17, 1);

        var r = TwoBoneIk.Solve(a, b, c, t);
        var (b2, c2) = Apply(a, b, c, r);

        // original bend axis, transported by the upper delta, must match the new bend axis
        var n0 = Vector3.Normalize(Vector3.Cross(c - a, b - a));
        var n0Transported = Vector3.Transform(n0, r.UpperWorldDelta);
        var n1 = Vector3.Normalize(Vector3.Cross(c2 - a, b2 - a));

        Assert.True(Vector3.Dot(n0Transported, n1) > 0.99f,
            $"bend plane changed: transported {n0Transported} vs actual {n1}");
    }

    [Fact]
    public void NearCollinearChain_WithStableAxis_NoNaN_AndReaches()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0, 10, 0);
        var c = new Vector3(0, 20, 0); // perfectly straight
        var t = new Vector3(3, 15, 0); // requires bending

        var r = TwoBoneIk.Solve(a, b, c, t, stableBendAxis: new Vector3(0, 0, 1));
        var (b2, c2) = Apply(a, b, c, r);

        Assert.True(float.IsFinite(c2.X) && float.IsFinite(c2.Y) && float.IsFinite(c2.Z));
        Assert.True(Vector3.Distance(c2, t) < 0.01f, $"effector {c2} should reach {t}");
        Assert.True(float.IsFinite(b2.X));
    }

    [Fact]
    public void ZeroLengthChain_ReturnsIdentity_NoThrow()
    {
        // All three joints coincident: nothing can rotate to reach anywhere. Must report
        // an identity result instead of throwing (the reach clamp used to get min > max).
        var p = new Vector3(1, 2, 3);
        var r = TwoBoneIk.Solve(p, p, p, new Vector3(4, 5, 6));

        Assert.Equal(Quaternion.Identity, r.UpperWorldDelta);
        Assert.Equal(Quaternion.Identity, r.LowerWorldDelta);
    }

    [Fact]
    public void NoOp_WhenTargetIsCurrentEffector()
    {
        var a = Vector3.Zero;
        var b = new Vector3(0, 10, 2);
        var c = new Vector3(0, 20, 0);

        var r = TwoBoneIk.Solve(a, b, c, c);
        Assert.True(MathQ.AngleBetween(r.UpperWorldDelta, Quaternion.Identity) < 1e-3f);
        Assert.True(MathQ.AngleBetween(r.LowerWorldDelta, Quaternion.Identity) < 1e-3f);
    }
}
