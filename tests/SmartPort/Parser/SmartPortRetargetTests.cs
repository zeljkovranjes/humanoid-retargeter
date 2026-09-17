using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using HumanoidRetargeterVrf.ResourceTypes.ModelAnimation;
using HumanoidRetargeterVrf.Serialization.KeyValues;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace SmartPort.Parser.Tests;

public class SmartPortRetargetTests
{
    [CompiledFixtureFact]
    public void CompiledUnflaggedAdditiveDoesNotAddBindLengthDuringRetargeting()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!, "models/player/human/frank_mp.vmdl_c");
        using var resource = new Resource();
        resource.Read(path);
        var model = Assert.IsType<Model>(resource.DataBlock);
        var source = SkeletonModel.Create(model.Skeleton.Bones.Select(b => new BoneDefinition(b.Name, b.Parent?.Name, new XForm(b.Position, b.Angle))).ToArray());
        var target = SkeletonModel.Create(source.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name, new XForm(b.RestLocal.Pos * .7f, b.RestLocal.Rot))).ToArray());
        var plan = new SmartPortRig(source, target);
        var animation = model.GetEmbeddedAnimations().Single(a => a.Name == "bindPose_delta");
        Assert.False(animation.Delta); // Modern ModelDoc stores already-subtracted channels without the legacy flag.
        var frame = new Frame(model.Skeleton, model.FlexControllers) { FrameIndex = 0 };
        animation.DecodeFrame(frame);
        var locals = source.Bones.Select(b =>
        {
            var original = model.Skeleton.Bones.Single(x => x.Name == b.Name).Index;
            return new XForm(frame.Bones[original].Position, frame.Bones[original].Angle);
        }).ToArray();
        Assert.True(plan.IsDeltaPose(locals));
        Assert.All(plan.Transfer(locals, delta: true), p => Assert.True(p.Pos.Length() < .001f));
    }

    [Fact]
    public void RecoveredParentConstraintRetainsItsDestinationBone()
    {
        const string bone = "test_parent_constraint_slave";
        HumanoidRetargeterVrf.Utils.StringToken.Store(new[] { bone });
        var hash = HumanoidRetargeterVrf.Utils.StringToken.InvertedTable.Single(p => p.Value == bone).Key;
        var slave = new KVObject(null);
        slave.AddProperty("m_nBoneHash", hash);
        slave.AddProperty("m_flWeight", 1.0);
        KVObject Array(params double[] values)
        {
            var a = new KVObject(null, isArray: true);
            foreach (var value in values) a.AddItem(value);
            return a;
        }
        slave.AddProperty("m_vBasePosition", Array(0, 0, 0));
        slave.AddProperty("m_qBaseOrientation", Array(0, 0, 0, 1));
        var slaves = new KVObject(null, isArray: true); slaves.AddItem(slave);
        var constraint = new KVObject(null);
        constraint.AddProperty("m_slaves", slaves);
        constraint.AddProperty("m_targets", new KVObject(null, isArray: true));
        var node = new KVObject(null); node.AddProperty("_class", "AnimConstraintParent");
        typeof(HumanoidRetargeterVrf.IO.ModelExtract).GetMethod("ProcessBoneConstraintChildren",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { constraint, node });
        Assert.Equal(bone, node.GetStringProperty("constrained_bone"));
    }
}
