using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public sealed class CompiledRigSourceSpaceTests
{
    [Fact]
    public void UnrealZUpRootKeepsItsOrientation()
    {
        var engine = new XForm(new Vector3(4, 8, 36), Quaternion.Identity);

        var source = CompiledRigSourceSpace.FromEngineLocal(engine, true, TargetUpAxis.ZUpCm);

        AssertVectorClose(new Vector3(4, 8, 36) / RetargetTargetSpec.SboxSourceScale, source.Pos);
        AssertQuaternionClose(Quaternion.Identity, source.Rot);
    }

    [Fact]
    public void YUpRootReceivesInverseBasisConversion()
    {
        var engine = new XForm(new Vector3(4, -12, 36), Quaternion.Identity);

        var source = CompiledRigSourceSpace.FromEngineLocal(engine, true, TargetUpAxis.YUpCm);

        AssertVectorClose(new Vector3(4, 36, 12) / RetargetTargetSpec.SboxSourceScale, source.Pos);
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI * 0.5f);
        AssertQuaternionClose(expected, source.Rot);
    }

    [Fact]
    public void ChildLocalsNeverReceiveAWorldBasisRotation()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(0.2f, -0.3f, 0.1f);
        var engine = new XForm(new Vector3(1, 2, 3), rotation);

        var source = CompiledRigSourceSpace.FromEngineLocal(engine, false, TargetUpAxis.YUpCm);

        AssertVectorClose(engine.Pos / RetargetTargetSpec.SboxSourceScale, source.Pos);
        AssertQuaternionClose(rotation, source.Rot);
    }

    private static void AssertVectorClose(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Vector3.Distance(expected, actual), 0, 1e-4f);
    }

    private static void AssertQuaternionClose(Quaternion expected, Quaternion actual)
    {
        Assert.InRange(MathF.Abs(Quaternion.Dot(expected, actual)), 0.99999f, 1.00001f);
    }
}
