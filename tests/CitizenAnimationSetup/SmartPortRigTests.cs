using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortRigTests
{
    static SkeletonModel Rig(float scale = 1, bool source = false)
    {
        var bones = new List<BoneDefinition>();
        void Bone(string name, string? parent, Vector3 position) => bones.Add(new(name, parent, new XForm(position * scale, Quaternion.Identity)));
        Bone("pelvis", null, new(0, 0, 40));
        Bone("spine_0", "pelvis", new(0, 0, 6));
        Bone("spine_1", "spine_0", new(0, 0, 6));
        Bone("spine_2", "spine_1", new(0, 0, 6));
        if (source) Bone("spine_3", "spine_2", new(0, 0, 2));
        Bone("neck_0", source ? "spine_3" : "spine_2", new(0, 0, 3));
        Bone(source ? "head_0" : "head", "neck_0", new(0, 0, 5));
        foreach (var side in new[] { "L", "R" })
        {
            var s = source ? side.ToLowerInvariant() : side;
            var sign = side == "L" ? 1 : -1;
            Bone("clavicle_" + s, source ? "spine_3" : "spine_2", new(0, sign * 2, 1));
            Bone("arm_upper_" + s, "clavicle_" + s, new(0, sign * 4, 0));
            Bone("arm_lower_" + s, "arm_upper_" + s, new(0, sign * 10, 0));
            Bone("hand_" + s, "arm_lower_" + s, new(0, sign * 9, 0));
            Bone("leg_upper_" + s, "pelvis", new(0, sign * 4, 0));
            Bone("leg_lower_" + s, "leg_upper_" + s, new(0, 0, -18));
            Bone("ankle_" + s, "leg_lower_" + s, new(0, 0, -18));
            Bone("ball_" + s, "ankle_" + s, new(5, 0, -3));
        }
        Bone(source ? "weapon_jnt" : "custom_ears", source ? "hand_r" : "head", new(1, 0, 0));
        return SkeletonModel.Create(bones);
    }

    [Fact]
    public void DifferentNamesSpineAndBindAreRetargetedWithoutReplacingSkinBones()
    {
        var source = Rig(source: true);
        var target = Rig(.7f);
        Assert.NotNull(SmartPortSetup.CompatibilityError(target, source));
        var plan = new SmartPortRig(source, target);
        Assert.Equal("head", plan.BoneNames["head_0"]);
        Assert.Equal("hand_R", plan.BoneNames["hand_r"]);
        Assert.True(plan.Target.IndexOf("weapon_jnt") >= 0);
        foreach (var bone in target.Bones)
        {
            var preserved = plan.Target[plan.Target.IndexOf(bone.Name)];
            Assert.Equal(bone.RestLocal, preserved.RestLocal);
            Assert.Equal(bone.ParentIndex < 0 ? null : target[bone.ParentIndex].Name,
                preserved.ParentIndex < 0 ? null : plan.Target[preserved.ParentIndex].Name);
        }
        Assert.Equal(.7f, plan.MotionScale, 5);
    }

    [Fact]
    public void ZeroAdditivePoseStaysZeroOnDifferentProportions()
    {
        var plan = new SmartPortRig(Rig(source: true), Rig(.7f));
        var result = plan.Transfer(Enumerable.Repeat(XForm.Identity, plan.Source.Count).ToArray(), delta: true);
        foreach (var pose in result)
        {
            Assert.True(pose.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(pose.Rot, Quaternion.Identity) < .001f);
        }
    }

    [Fact]
    public void UnflaggedAdditivesAreDetectedFromChannelsNotFilenames()
    {
        var plan = new SmartPortRig(Rig(source: true), Rig(.7f));
        var rest = plan.Source.Bones.Select(b => b.RestLocal).ToArray();
        Assert.False(plan.IsDeltaPose(rest));
        Assert.True(plan.IsDeltaPose(Enumerable.Repeat(XForm.Identity, plan.Source.Count).ToArray()));
        rest[plan.Source.IndexOf("pelvis")].Pos = Vector3.Zero;
        Assert.False(plan.IsDeltaPose(rest)); // In-place locomotion is not an additive.
    }

    [Fact]
    public void AnimatedLimbsKeepTargetLengthsAndUnmappedBonesKeepTheirRest()
    {
        var plan = new SmartPortRig(Rig(source: true), Rig(.7f));
        var frame = plan.Source.Bones.Select(b => b.RestLocal).ToArray();
        frame[plan.Source.IndexOf("arm_lower_l")].Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .8f);
        var output = plan.Transfer(frame);
        foreach (var bone in plan.Target.Bones.Where(b => b.ParentIndex >= 0))
            Assert.Equal(bone.RestLocal.Pos.Length(), output[bone.Index].Pos.Length(), 3);
        Assert.Equal(plan.Target[plan.Target.IndexOf("custom_ears")].RestLocal, output[plan.Target.IndexOf("custom_ears")]);
        Assert.True(MathQ.AngleBetween(output[plan.Target.IndexOf("arm_lower_L")].Rot, Quaternion.Identity) > .3f);
    }

    [Fact]
    public void NonHumanoidStillFailsAndWrongFrameSizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SmartPortRig(Rig(), SkeletonModel.Create(new[] { new BoneDefinition("root", null, XForm.Identity) })));
        Assert.Throws<ArgumentException>(() => new SmartPortRig(Rig(), Rig()).Transfer(Array.Empty<XForm>()));
    }

    [Fact]
    public void RetargetedSetupKeepsTargetHelpersAndSourceGraphBonesAndEvents()
    {
        var source = Rig(source: true);
        var target = Rig(.7f);
        string Document(SkeletonModel skeleton, string extra)
        {
            KvObject BoneNode(int index)
            {
                var b = skeleton[index];
                var children = new KvArray();
                foreach (var child in skeleton.Bones.Where(x => x.ParentIndex == index)) children.Items.Add(BoneNode(child.Index));
                return new KvObject { ["_class"] = new KvString("Bone"), ["name"] = new KvString(b.Name), ["children"] = children };
            }
            var roots = new KvArray();
            foreach (var b in skeleton.Bones.Where(x => x.ParentIndex < 0)) roots.Items.Add(BoneNode(b.Index));
            var doc = Kv3.Parse(VmdlWriter.Kv3Header + "{ rootNode = { children = [ { _class = \"RenderMeshList\" }, { _class = \"Skeleton\" } " + extra + " ] } }");
            var nodes = (KvArray)((KvObject)((KvObject)doc.Root)["rootNode"])["children"];
            ((KvObject)nodes.Items[1])["children"] = roots;
            return Kv3.Serialize(doc);
        }
        var result = SmartPortSetup.ApplyRetargeted(Document(target, """
            , { _class = "AnimConstraintList" children = [{ name = "fitted" constrained_bone = "hand_R" }] }
            , { _class = "AttachmentList" children = [{ name = "hold" parent_bone = "hand_R" }] }
            """), Document(source, """
            , { _class = "AnimConstraintList" children = [
                { name = "wrong_bind" constrained_bone = "hand_r" },
                { name = "weapon_driver" constrained_bone = "weapon_jnt" }] }
            , { _class = "AnimationList" children = [{ name = "walk" children = [{ event_class = "AE_FOOTSTEP" }] }] }
            """), "ported.vanmgrph", new SmartPortRig(source, target));
        Assert.Contains("weapon_jnt", result);
        Assert.Contains("weapon_driver", result);
        Assert.Contains("fitted", result);
        Assert.Contains("AE_FOOTSTEP", result);
        Assert.Contains("custom_ears", result);
        Assert.DoesNotContain("wrong_bind", result);
        Assert.DoesNotContain("\"hand_r\"", result);
        Assert.Equal("ported.vanmgrph", StockAnimationGraph.GraphName(result));
    }
}
