using HumanoidRetargeter.Editor;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using Xunit;

namespace SmartPort.Parser.Tests;

public class SmartPortHandSocketMetadataTests
{
    [CompiledFixtureFact]
    public void AttachmentFittingUsesCompiledParentBones()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c");
        var names = SmartPortAttachments.BoneNames(path);
        using var resource = new Resource();
        resource.Read(path);
        var model = Assert.IsType<Model>(resource.DataBlock);
        Assert.Contains("hold_R", names);
        Assert.Contains("hold_L", names);
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(names.Where(n => !string.IsNullOrEmpty(n)), n => Assert.Contains(model.Skeleton.Bones, b => b.Name == n));
    }
}
