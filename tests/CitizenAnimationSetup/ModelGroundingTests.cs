using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class ModelGroundingTests
{
    private static string Model => VmdlWriter.Kv3Header + """
        { rootNode = { children = [
          { _class = "ModelModifierList" children = [ { _class = "ModelModifier_ScaleAndMirror" scale = 0.3937 } ] },
          { _class = "RenderMeshList" children = [ { filename = "custom.dmx" } ] },
          { _class = "AnimationList" children = [ { name = "idle" source_filename = "idle.dmx" } ] }
        ] } }
        """;

    [Theory]
    [InlineData(-37.484535f)]
    [InlineData(-10f)]
    public void GroundingTranslatesWholeModelAfterScaleWithoutChangingSources(float minimumZ)
    {
        var result = ModelGrounding.Apply(Model, minimumZ);
        Assert.Equal(-minimumZ, ModelGrounding.Offset(result));
        CitizenAnimationSetup.ValidateSourceScale(result);
        var before = (KvArray)((KvObject)((KvObject)Kv3.Parse(Model).Root)["rootNode"])["children"];
        var after = (KvArray)((KvObject)((KvObject)Kv3.Parse(result).Root)["rootNode"])["children"];
        Assert.True(KvValue.DeepEquals(before.Items[1], after.Items[1]));
        Assert.True(KvValue.DeepEquals(before.Items[2], after.Items[2]));
        var modifiers = (KvArray)((KvObject)after.Items[0])["children"];
        Assert.Equal("ModelModifier_ScaleAndMirror", ((KvObject)modifiers.Items[0]).GetString("_class"));
        Assert.True(ModelGrounding.IsGrounding((KvObject)modifiers.Items[1]));
        Assert.Equal(0, ModelGrounding.Offset(Model));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.01f)]
    [InlineData(5f)]
    public void GroundedAndElevatedModelsStayUnchanged(float minimumZ)
        => Assert.Equal(Model, ModelGrounding.Apply(Model, minimumZ));

    [Fact]
    public void RegroundingDoesNotStackDuplicateModifiers()
    {
        var first = ModelGrounding.Apply(Model, -10);
        Assert.Equal(first, ModelGrounding.Apply(first, 0));
        var updated = ModelGrounding.Apply(first, -2);
        Assert.Equal(12, ModelGrounding.Offset(updated));
        Assert.Equal(1, updated.Split(ModelGrounding.ModifierName).Length - 1);
    }

    [Fact]
    public void SourceRecoveryUnshiftsAllRootsButNeverChildOffsetsOrRotations()
    {
        var local = new XForm(new Vector3(1, 2, 30), Quaternion.CreateFromYawPitchRoll(.2f, .3f, .4f));
        var root = ModelGrounding.SourceLocal(local, true, 37);
        Assert.Equal(local.Pos, root.Pos + Vector3.UnitZ * 37);
        Assert.Equal(local.Rot, root.Rot);
        Assert.Equal(local, ModelGrounding.SourceLocal(local, false, 37));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidBoundsAreRejected(float minimumZ)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ModelGrounding.Apply(Model, minimumZ));
}
