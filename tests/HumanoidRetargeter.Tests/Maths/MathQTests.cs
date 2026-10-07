using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Maths;

public class MathQTests
{
    private const float Eps = 1e-5f;

    // ---------------------------------------------------------------- Normalize

    [Fact]
    public void Normalize_NearZeroQuaternion_ReturnsIdentity()
    {
        Assert.Equal(Quaternion.Identity, MathQ.Normalize(new Quaternion(0f, 0f, 0f, 0f)));
        Assert.Equal(Quaternion.Identity, MathQ.Normalize(new Quaternion(1e-9f, -1e-9f, 1e-9f, 1e-9f)));
    }

    [Fact]
    public void Normalize_ScaledQuaternion_ReturnsUnitQuaternionWithSameRotation()
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var scaled = new Quaternion(q.X * 5f, q.Y * 5f, q.Z * 5f, q.W * 5f);
        var n = MathQ.Normalize(scaled);
        Assert.True(MathF.Abs(n.Length() - 1f) < 1e-6f);
        TestUtil.AssertQuaternionEqual(q, n, Eps);
    }

    // ---------------------------------------------------------------- FromTo

    [Fact]
    public void FromTo_RotatesAOntoB_OnSeededRandomPairs()
    {
        var rng = new Random(20260611);
        for (var i = 0; i < 100; i++)
        {
            var a = TestUtil.RandomVector(rng);
            var b = TestUtil.RandomVector(rng);
            if (a.Length() < 0.1f || b.Length() < 0.1f)
                continue;

            var q = MathQ.FromTo(a, b);
            Assert.True(MathF.Abs(q.Length() - 1f) < 1e-5f, $"FromTo result not unit at iteration {i}");

            var rotated = Vector3.Normalize(Vector3.Transform(a, q));
            TestUtil.AssertVectorEqual(Vector3.Normalize(b), rotated, Eps);
        }
    }

    [Fact]
    public void FromTo_ParallelVectors_ReturnsIdentity()
    {
        var q = MathQ.FromTo(new Vector3(0f, 3f, 0f), new Vector3(0f, 7f, 0f));
        TestUtil.AssertQuaternionEqual(Quaternion.Identity, q, Eps);
    }

    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0.6f, -0.8f, 0f)]
    [InlineData(2f, 3f, -4f)]
    public void FromTo_AntiparallelVectors_Produces180DegreeRotation(float x, float y, float z)
    {
        var a = new Vector3(x, y, z);
        var q = MathQ.FromTo(a, -a);

        var rotated = Vector3.Transform(Vector3.Normalize(a), q);
        TestUtil.AssertVectorEqual(-Vector3.Normalize(a), rotated, Eps);

        // 180 degrees: w component must be ~0.
        Assert.True(MathF.Abs(q.W) < 1e-4f, $"Expected half-turn (w~0), got {q}");
        Assert.True(MathF.Abs(q.Length() - 1f) < 1e-5f);
    }

    // ---------------------------------------------------------------- SwingTwist

    [Fact]
    public void SwingTwist_SwingTimesTwist_ReconstructsInput()
    {
        var rng = new Random(424242);
        for (var i = 0; i < 100; i++)
        {
            var q = TestUtil.RandomQuaternion(rng);
            var axis = TestUtil.RandomUnitVector(rng);

            MathQ.SwingTwist(q, axis, out var swing, out var twist);

            TestUtil.AssertQuaternionEqual(q, swing * twist, Eps);
            Assert.True(MathF.Abs(swing.Length() - 1f) < 1e-5f);
            Assert.True(MathF.Abs(twist.Length() - 1f) < 1e-5f);
        }
    }

    [Fact]
    public void SwingTwist_TwistAxis_IsParallelToRequestedAxis()
    {
        var rng = new Random(777);
        for (var i = 0; i < 100; i++)
        {
            var q = TestUtil.RandomQuaternion(rng);
            var axis = TestUtil.RandomUnitVector(rng);

            MathQ.SwingTwist(q, axis, out _, out var twist);

            var twistVec = new Vector3(twist.X, twist.Y, twist.Z);
            // Either no twist at all, or the rotation axis is parallel to the requested axis.
            if (twistVec.Length() > 1e-5f)
            {
                var cross = Vector3.Cross(Vector3.Normalize(twistVec), axis);
                Assert.True(cross.Length() < 1e-4f,
                    $"Twist axis {twistVec} not parallel to {axis} at iteration {i}");
            }
        }
    }

    [Fact]
    public void SwingTwist_PureTwistRotation_YieldsIdentitySwing()
    {
        var axis = Vector3.Normalize(new Vector3(1f, 2f, 3f));
        var q = Quaternion.CreateFromAxisAngle(axis, 1.1f);

        MathQ.SwingTwist(q, axis, out var swing, out var twist);

        TestUtil.AssertQuaternionEqual(Quaternion.Identity, swing, Eps);
        TestUtil.AssertQuaternionEqual(q, twist, Eps);
    }

    [Fact]
    public void SwingTwist_PureSwingRotation_YieldsIdentityTwist()
    {
        // 180-degree rotation about an axis perpendicular to the twist axis: q.W == 0 and
        // the projection onto the twist axis is zero — the degenerate branch.
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

        MathQ.SwingTwist(q, Vector3.UnitY, out var swing, out var twist);

        TestUtil.AssertQuaternionEqual(Quaternion.Identity, twist, Eps);
        TestUtil.AssertQuaternionEqual(q, swing, Eps);
    }

    [Fact]
    public void SwingTwist_ZeroAxis_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MathQ.SwingTwist(Quaternion.Identity, Vector3.Zero, out _, out _));
    }

    // ---------------------------------------------------------------- AngleBetween

    [Fact]
    public void AngleBetween_QuaternionAndItsNegation_IsZero()
    {
        var rng = new Random(31337);
        for (var i = 0; i < 50; i++)
        {
            var q = TestUtil.RandomQuaternion(rng);
            Assert.True(MathQ.AngleBetween(q, Quaternion.Negate(q)) < 1e-4f);
            Assert.True(MathQ.AngleBetween(q, q) < 1e-4f);
        }
    }

    [Fact]
    public void AngleBetween_KnownRotations_ReturnsGeodesicAngle()
    {
        var a = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 30f * MathF.PI / 180f);
        var b = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 90f * MathF.PI / 180f);
        Assert.True(MathF.Abs(MathQ.AngleBetween(a, b) - 60f * MathF.PI / 180f) < 1e-4f);

        var c = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        Assert.True(MathF.Abs(MathQ.AngleBetween(Quaternion.Identity, c) - MathF.PI) < 1e-4f);
    }

    // ---------------------------------------------------------------- BasisFromForwardUp

    [Fact]
    public void BasisFromForwardUp_MapsUnitYToForwardAndUnitZToOrthonormalizedUp()
    {
        var rng = new Random(9001);
        for (var i = 0; i < 100; i++)
        {
            var forward = TestUtil.RandomVector(rng);
            var up = TestUtil.RandomVector(rng);
            if (forward.Length() < 0.1f || up.Length() < 0.1f)
                continue;
            var fn = Vector3.Normalize(forward);
            var upOrtho = up - fn * Vector3.Dot(up, fn);
            if (upOrtho.Length() < 0.1f)
                continue; // near-parallel handled in a dedicated test

            var r = MathQ.BasisFromForwardUp(forward, up);

            Assert.True(MathF.Abs(r.Length() - 1f) < 1e-5f);
            TestUtil.AssertVectorEqual(fn, Vector3.Transform(Vector3.UnitY, r), Eps);
            TestUtil.AssertVectorEqual(Vector3.Normalize(upOrtho), Vector3.Transform(Vector3.UnitZ, r), Eps);
            // Right-handedness: X = Y x Z.
            TestUtil.AssertVectorEqual(
                Vector3.Cross(fn, Vector3.Normalize(upOrtho)),
                Vector3.Transform(Vector3.UnitX, r), Eps);
        }
    }

    [Fact]
    public void BasisFromForwardUp_ParallelUp_UsesStableFallback()
    {
        var forward = new Vector3(0f, 0f, 2f);
        var r = MathQ.BasisFromForwardUp(forward, forward * 3f);

        Assert.True(MathF.Abs(r.Length() - 1f) < 1e-5f);
        TestUtil.AssertVectorEqual(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, r), Eps);
        // The produced Z axis must be a unit vector orthogonal to forward.
        var z = Vector3.Transform(Vector3.UnitZ, r);
        Assert.True(MathF.Abs(z.Length() - 1f) < Eps);
        Assert.True(MathF.Abs(Vector3.Dot(z, Vector3.UnitZ)) < Eps);
    }

    [Fact]
    public void BasisFromForwardUp_IsOrthonormal()
    {
        var rng = new Random(5150);
        for (var i = 0; i < 50; i++)
        {
            var r = MathQ.BasisFromForwardUp(TestUtil.RandomUnitVector(rng), TestUtil.RandomUnitVector(rng));
            var x = Vector3.Transform(Vector3.UnitX, r);
            var y = Vector3.Transform(Vector3.UnitY, r);
            var z = Vector3.Transform(Vector3.UnitZ, r);
            Assert.True(MathF.Abs(x.Length() - 1f) < Eps);
            Assert.True(MathF.Abs(y.Length() - 1f) < Eps);
            Assert.True(MathF.Abs(z.Length() - 1f) < Eps);
            Assert.True(MathF.Abs(Vector3.Dot(x, y)) < Eps);
            Assert.True(MathF.Abs(Vector3.Dot(y, z)) < Eps);
            Assert.True(MathF.Abs(Vector3.Dot(z, x)) < Eps);
        }
    }
}
