using System.Text;
using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class AdditiveReferenceTests
{
    // Small self-contained articulated source: tests require no downloaded rig or engine.
    private static byte[] Source()
    {
        string Joint(string name, string offset, string children = "")
            => $"JOINT mixamorig:{name} {{ OFFSET {offset} CHANNELS 3 Zrotation Xrotation Yrotation {children} }} ";
        var spine = Joint("Spine", "0 20 0", Joint("Neck", "0 30 0", Joint("Head", "0 10 0"))
            + Joint("LeftArm", "20 20 0", Joint("LeftForeArm", "30 0 0", Joint("LeftHand", "20 0 0")))
            + Joint("RightArm", "-20 20 0", Joint("RightForeArm", "-30 0 0", Joint("RightHand", "-20 0 0"))));
        var legs = Joint("LeftUpLeg", "10 -10 0", Joint("LeftLeg", "0 -40 0", Joint("LeftFoot", "0 -40 0")))
            + Joint("RightUpLeg", "-10 -10 0", Joint("RightLeg", "0 -40 0", Joint("RightFoot", "0 -40 0")));
        var frames = string.Join("\n", Enumerable.Range(0, 6).Select(i => $"0 100 {i} " + string.Join(" ", Enumerable.Repeat("0", 48))));
        return Encoding.UTF8.GetBytes($"HIERARCHY ROOT mixamorig:Hips {{ OFFSET 0 100 0 CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation {spine} {legs} }} MOTION\nFrames: 6\nFrame Time: 0.0333333333\n{frames}\n");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void ChosenReferenceFrameIsSerializedWithoutDeltaEvents(int reference)
    {
        var result = Convert(reference);
        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        var root = (KvObject)((KvObject)Kv3.Parse(result.StandaloneVmdl).Root)["rootNode"];
        var list = ((KvArray)root["children"]).Items.OfType<KvObject>().Single(n => n.GetString("_class") == "AnimationList");
        var delta = ((KvArray)list["children"]).Items.OfType<KvObject>().Single(n => n.GetString("name") == clip.AdditiveVariantName);
        var subtract = Assert.IsType<KvObject>(Assert.Single(((KvArray)delta["children"]).Items));
        Assert.Equal("AnimSubtract", subtract.GetString("_class"));
        Assert.Equal(reference, ((KvLong)subtract["frame"]).Value);
        Assert.Equal(clip.ClipName, subtract.GetString("anim_name"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void InvalidReferenceFailsClearlyInsteadOfSilentlyClamping(int reference)
    {
        var clip = Assert.Single(Convert(reference).Clips);
        Assert.False(clip.Success);
        Assert.Contains("Additive reference frame", clip.Error);
    }

    private static RetargetResult Convert(int reference)
    {
        var data = Source();
        var scene = Retargeter.ImportSource(data, "test.bvh");
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        return Retargeter.Convert(new RetargetRequest
        {
            SourceData = data, SourceFileName = "test.bvh", MappingOverride = map,
            CreateAdditiveVariant = true, AdditiveReferenceFrame = reference, GenerateFootstepEvents = true,
        }, new RetargetTargetSpec { Rig = TargetRig.FromSkeleton(scene.Skeleton, map), VmdlScale = 1 });
    }
}
