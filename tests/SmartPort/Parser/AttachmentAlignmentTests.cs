using System.Numerics;
using HumanoidRetargeter.Editor;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace SmartPort.Parser.Tests;

public class AttachmentAlignmentTests
{
    static string Document(string bone, bool ignore = false) => VmdlWriter.Kv3Header + """
        { rootNode = { children = [{ _class = "AttachmentList" children = [{
          _class = "Attachment" name = "tool_mount"
        """ + $"parent_bone = \"{bone}\" ignore_rotation = {ignore.ToString().ToLowerInvariant()} " + """
          relative_origin = [1, 2, 3] relative_angles = [0, 0, 0] weight = 1
        }] }] } }
        """;

    static KvObject Attachment(string text) => (KvObject)((KvArray)((KvObject)((KvArray)((KvObject)((KvObject)
        Kv3.Parse(text).Root)["rootNode"])["children"]).Items[0])["children"]).Items[0];

    [CompiledFixtureFact]
    public void SharedSocketAdoptsSourceAxesWithoutMovingItsFittedPositionOrSkin()
    {
        using var resource = new Resource();
        resource.Read(Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c"));
        var model = Assert.IsType<Model>(resource.DataBlock);
        var source = SkeletonModel.Create(model.Skeleton.Bones.Select(b => new BoneDefinition(b.Name, b.Parent?.Name, new(b.Position, b.Angle))).ToArray());
        var target = SkeletonModel.Create(source.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name,
            b.Name == "hold_R" ? new XForm(b.RestLocal.Pos, Quaternion.CreateFromAxisAngle(Vector3.UnitY, .3f) * b.RestLocal.Rot) : b.RestLocal)).ToArray());
        var rig = new SmartPortRig(source, target);
        var sourceText = Document("hold_r"); // compiled attachment names can differ in case
        var targetText = Document("hold_R");
        var aligned = Attachment(SmartPortAttachments.Align(targetText, sourceText, rig));
        Assert.True(KvValue.DeepEquals(Attachment(targetText)["relative_origin"], aligned["relative_origin"]));
        var values = ((KvArray)aligned["relative_angles"]).Items.Select(v => v is KvDouble d ? (float)d.Value : ((KvLong)v).Value).ToArray();
        var local = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, values[1] * MathF.PI / 180)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, values[0] * MathF.PI / 180)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, values[2] * MathF.PI / 180);
        var pose = rig.Transfer(source.Bones.Select(b => b.RestLocal).ToArray());
        var world = new XForm[rig.Target.Count];
        foreach (var bone in rig.Target.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        Assert.True(MathQ.AngleBetween(world[rig.Target.IndexOf("hold_R")].Rot * local, source.RestWorld[source.IndexOf("hold_R")].Rot) < .001f);
        foreach (var bone in target.Bones) Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        var ignored = Document("hold_R", true);
        Assert.True(KvValue.DeepEquals(Attachment(ignored), Attachment(SmartPortAttachments.Align(ignored, sourceText, rig))));
    }

    [CompiledFixtureFact]
    public void RecoveredGraphRetainsEveryNodePositionWhenCopied()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/mplayer/mplayer_animgraph.vanmgrph_c");
        var recovered = CompiledAssetRecovery.Recover(path, "models/player/mplayer/mplayer_animgraph.vanmgrph",
            Path.GetTempPath(), "unused", new Dictionary<string, string>(), CancellationToken.None);
        using var resource = new Resource(); resource.Read(path);
        var original = (KvObject)Kv3.Parse(resource.DataBlock!.ToString()!).Root;
        var copied = (KvObject)Kv3.Parse(StockAnimationGraph.CopyForModel(recovered, "custom.vmdl")).Root;
        Assert.True(KvValue.DeepEquals(original["m_nodeManager"], copied["m_nodeManager"]));
        Assert.Contains("m_vecPosition", recovered);
    }
}
