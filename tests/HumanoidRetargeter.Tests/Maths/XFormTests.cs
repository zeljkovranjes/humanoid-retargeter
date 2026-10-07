using System.Numerics;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Maths;

public class XFormTests
{
    private const float PosEps = 1e-3f;
    private const float RotEps = 1e-5f;

    [Fact]
    public void Identity_ComposesAsNeutralElement()
    {
        var rng = new Random(1234);
        var x = TestUtil.RandomXForm(rng);

        var left = XForm.Compose(XForm.Identity, x);
        var right = XForm.Compose(x, XForm.Identity);

        TestUtil.AssertVectorEqual(x.Pos, left.Pos, PosEps);
        TestUtil.AssertQuaternionEqual(x.Rot, left.Rot, RotEps);
        TestUtil.AssertVectorEqual(x.Pos, right.Pos, PosEps);
        TestUtil.AssertQuaternionEqual(x.Rot, right.Rot, RotEps);
    }

    [Fact]
    public void ComposeThenToLocal_RoundTripsOnSeededRandomTransforms()
    {
        var rng = new Random(20260611);
        for (var i = 0; i < 100; i++)
        {
            var parent = TestUtil.RandomXForm(rng);
            var local = TestUtil.RandomXForm(rng);

            var world = XForm.Compose(parent, local);
            var back = XForm.ToLocal(parent, world);

            TestUtil.AssertVectorEqual(local.Pos, back.Pos, PosEps);
            TestUtil.AssertQuaternionEqual(local.Rot, back.Rot, RotEps);

            // And the other direction: ToLocal then Compose reproduces the world transform.
            var worldAgain = XForm.Compose(parent, back);
            TestUtil.AssertVectorEqual(world.Pos, worldAgain.Pos, PosEps);
            TestUtil.AssertQuaternionEqual(world.Rot, worldAgain.Rot, RotEps);
        }
    }

    [Fact]
    public void Inverse_ComposedWithSelf_YieldsIdentity()
    {
        var rng = new Random(55);
        for (var i = 0; i < 100; i++)
        {
            var x = TestUtil.RandomXForm(rng);
            var inv = x.Inverse();

            var a = XForm.Compose(x, inv);
            var b = XForm.Compose(inv, x);

            TestUtil.AssertVectorEqual(Vector3.Zero, a.Pos, PosEps);
            TestUtil.AssertQuaternionEqual(Quaternion.Identity, a.Rot, RotEps);
            TestUtil.AssertVectorEqual(Vector3.Zero, b.Pos, PosEps);
            TestUtil.AssertQuaternionEqual(Quaternion.Identity, b.Rot, RotEps);
        }
    }

    [Fact]
    public void Compose_FollowsColumnVectorConvention()
    {
        // Parent: +90 degrees about Z, translated to (10, 0, 0).
        // Local point offset (0, 5, 0) must land at parent.Pos + rotate(parent.Rot, local.Pos) = (5, 0, 0) + (10,0,0).
        var parent = new XForm(new Vector3(10f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f));
        var local = new XForm(new Vector3(0f, 5f, 0f), Quaternion.Identity);

        var world = XForm.Compose(parent, local);

        TestUtil.AssertVectorEqual(new Vector3(5f, 0f, 0f), world.Pos, PosEps);
        TestUtil.AssertQuaternionEqual(parent.Rot, world.Rot, RotEps);
    }

    [Fact]
    public void TransformPoint_MatchesComposeOfPointAsTranslation()
    {
        var rng = new Random(99);
        var x = TestUtil.RandomXForm(rng);
        var p = TestUtil.RandomVector(rng, 50f);

        var viaCompose = XForm.Compose(x, new XForm(p, Quaternion.Identity)).Pos;
        TestUtil.AssertVectorEqual(viaCompose, x.TransformPoint(p), PosEps);
    }
}
