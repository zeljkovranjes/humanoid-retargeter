using HumanoidRetargeter.Editor;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using Xunit;

namespace SmartPort.Parser.Tests;

public class SmartPortClipMetadataTests
{
    [CompiledFixtureFact]
    public void GraphExtensionUsesDecodedAdditiveChannelsAndAuthoredLooping()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c");
        using var resource = new Resource();
        resource.Read(path);
        var model = Assert.IsType<Model>(resource.DataBlock);
        var skeleton = Skeleton.Create(model.Skeleton.Bones.Select(b =>
            new BoneDefinition(b.Name, b.Parent?.Name, new XForm(b.Position, b.Angle))).ToArray());
        var clips = SmartPortAnimationExport.ReadClips(path, new SmartPortRig(skeleton, skeleton));
        Assert.Equal(391, clips.Length);
        var delta = Assert.Single(clips.Where(c => c.Name == "bindPose_delta"));
        Assert.True(delta.Additive);
        Assert.False(delta.Looping);
        var walk = Assert.Single(clips.Where(c => c.Name == "Walk_N"));
        Assert.False(walk.Additive);
        Assert.True(walk.Looping);
        Assert.False(walk.Hidden);
    }
}
