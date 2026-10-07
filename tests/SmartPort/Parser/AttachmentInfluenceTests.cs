using System.Numerics;
using HumanoidRetargeter.EditorTools;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace SmartPort.Parser.Tests;

/// <summary>
/// Attachments that ignore their bones' rotation or blend several bones (the Citizen's forward_reference_modelspace,
/// the frame its body aim turns the spine toward) ported onto a skeleton whose bones have other axes.
/// </summary>
public class AttachmentInfluenceTests
{
    // The Citizen's bones and a Source 1 biped's point their axes differently (Citizen pelvis (-90, -90, 0),
    // ValveBiped pelvis (0, 90, 90)); joint positions are the same here.
    static readonly Quaternion CitizenAxes = Euler(-90, -90, 0);
    static readonly Quaternion BipedAxes = Euler(0, 90, 90);

    static Quaternion Euler(float pitch, float yaw, float roll) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw * MathF.PI / 180)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitY, pitch * MathF.PI / 180)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll * MathF.PI / 180);

    static SkeletonModel Humanoid(Func<string, string> name, Quaternion axes)
    {
        var world = new Dictionary<string, Vector3>();
        var bones = new List<BoneDefinition>();
        void Bone(string bone, string? parent, Vector3 offset)
        {
            var at = (parent is null ? Vector3.Zero : world[parent]) + offset;
            world[bone] = at;
            // every bone has the same world axes, so only the root carries a rotation
            bones.Add(new(name(bone), parent is null ? null : name(parent),
                new XForm(parent is null ? at : Vector3.Transform(offset, Quaternion.Conjugate(axes)), parent is null ? axes : Quaternion.Identity)));
        }
        Bone("pelvis", null, new(0, 0, 40));
        Bone("spine_0", "pelvis", new(0, 0, 6));
        Bone("spine_1", "spine_0", new(0, 0, 6));
        Bone("spine_2", "spine_1", new(0, 0, 6));
        Bone("neck_0", "spine_2", new(0, 0, 3));
        Bone("head", "neck_0", new(0, 0, 5));
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? 1 : -1;
            Bone("clavicle_" + side, "spine_2", new(0, sign * 2, 1));
            Bone("arm_upper_" + side, "clavicle_" + side, new(0, sign * 4, 0));
            Bone("arm_lower_" + side, "arm_upper_" + side, new(0, sign * 10, 0));
            Bone("hand_" + side, "arm_lower_" + side, new(0, sign * 9, 0));
            Bone("leg_upper_" + side, "pelvis", new(0, sign * 4, 0));
            Bone("leg_lower_" + side, "leg_upper_" + side, new(0, 0, -18));
            Bone("ankle_" + side, "leg_lower_" + side, new(0, 0, -18));
            Bone("ball_" + side, "ankle_" + side, new(5, 0, -3));
        }
        return SkeletonModel.Create(bones);
    }

    static string Biped(string citizen) => "ValveBiped_Bip01_" + citizen switch
    {
        "pelvis" => "Pelvis", "spine_0" => "Spine", "spine_1" => "Spine1", "spine_2" => "Spine2", "neck_0" => "Neck1", "head" => "Head1",
        var n when n.StartsWith("clavicle_") => n[^1] + "_Clavicle",
        var n when n.StartsWith("arm_upper_") => n[^1] + "_UpperArm",
        var n when n.StartsWith("arm_lower_") => n[^1] + "_Forearm",
        var n when n.StartsWith("hand_") => n[^1] + "_Hand",
        var n when n.StartsWith("leg_upper_") => n[^1] + "_Thigh",
        var n when n.StartsWith("leg_lower_") => n[^1] + "_Calf",
        var n when n.StartsWith("ankle_") => n[^1] + "_Foot",
        var n when n.StartsWith("ball_") => n[^1] + "_Toe0",
        var n => n,
    };

    static string Document(Func<string, string> name) => VmdlWriter.Kv3Header + $$"""
        { rootNode = { children = [{ _class = "AttachmentList" children = [
          { _class = "Attachment" name = "forward_reference_modelspace" parent_bone = "{{name("pelvis")}}" ignore_rotation = true
            relative_origin = [0, -1, 0] relative_angles = [0, 90, 90] weight = 0.5
            children = [{ _class = "AttachmentInfluence" parent_bone = "{{name("head")}}" relative_origin = [0, -1, 0] relative_angles = [0, 90, 90] weight = 0.5 }] },
          { _class = "Attachment" name = "middle_of_both_hands" parent_bone = "{{name("hand_R")}}" ignore_rotation = false
            relative_origin = [1, 2, 3] relative_angles = [10, 20, 30] weight = 0.5
            children = [{ _class = "AttachmentInfluence" parent_bone = "{{name("hand_L")}}" relative_origin = [1, 2, 3] relative_angles = [0, 0, 45] weight = 0.5 }] },
          { _class = "Attachment" name = "eyes" parent_bone = "{{name("head")}}" ignore_rotation = false
            relative_origin = [1, 2, 3] relative_angles = [0, 90, 90] weight = 1 }
        ] }] } }
        """;

    static KvObject Node(string text, string attachment, int influence = -1)
    {
        var list = (KvObject)((KvArray)((KvObject)((KvObject)Kv3.Parse(text).Root)["rootNode"])["children"]).Items[0];
        var node = ((KvArray)list["children"]).Items.OfType<KvObject>().Single(n => n.GetString("name") == attachment);
        return influence < 0 ? node : (KvObject)((KvArray)node["children"]).Items[influence];
    }

    static Quaternion Angles(KvObject node)
    {
        var values = ((KvArray)node["relative_angles"]).Items.Select(v => v is KvDouble d ? (float)d.Value : ((KvLong)v).Value).ToArray();
        return Euler(values[0], values[1], values[2]);
    }

    [Fact]
    public void FixedAndBlendedAttachmentsKeepTheSourceOrientationOnOtherBoneAxes()
    {
        var source = Humanoid(n => n, CitizenAxes);
        var target = Humanoid(Biped, BipedAxes);
        var rig = new SmartPortRig(source, target);
        var sourceText = Document(n => n);
        var targetText = Document(Biped); // as Smart Port copies them: the source's angles under the mapped bones
        var aligned = SmartPortAttachments.AlignInfluencesAndFixedAxes(SmartPortAttachments.Align(targetText, sourceText, rig), sourceText, rig);

        // an ignore_rotation attachment: each influence's bind frame times its angles is the source's
        foreach (var (influence, bone) in new[] { (-1, "pelvis"), (0, "head") })
        {
            var expected = source.RestWorld[source.IndexOf(bone)].Rot * Euler(0, 90, 90);
            var actual = rig.Target.RestWorld[rig.Target.IndexOf(Biped(bone))].Rot * Angles(Node(aligned, "forward_reference_modelspace", influence));
            Assert.True(MathQ.AngleBetween(expected, actual) < .001f, $"{bone}: {MathQ.AngleBetween(expected, actual)}");
            Assert.True(KvValue.DeepEquals(Node(targetText, "forward_reference_modelspace", influence)["relative_origin"],
                Node(aligned, "forward_reference_modelspace", influence)["relative_origin"]));
        }

        // a blended attachment that follows its bones: matches the source in the transferred rest pose, like Align's
        var pose = rig.Transfer(source.Bones.Select(b => b.RestLocal).ToArray());
        var world = new XForm[rig.Target.Count];
        foreach (var bone in rig.Target.Bones)
            world[bone.Index] = bone.ParentIndex < 0 ? pose[bone.Index] : XForm.Compose(world[bone.ParentIndex], pose[bone.Index]);
        foreach (var (influence, bone, angles) in new[] { (-1, "hand_R", Euler(10, 20, 30)), (0, "hand_L", Euler(0, 0, 45)) })
        {
            var expected = source.RestWorld[source.IndexOf(bone)].Rot * angles;
            var actual = world[rig.Target.IndexOf(Biped(bone))].Rot * Angles(Node(aligned, "middle_of_both_hands", influence));
            Assert.True(MathQ.AngleBetween(expected, actual) < .001f, $"{bone}: {MathQ.AngleBetween(expected, actual)}");
        }

        // single-bone attachments stay Align's
        Assert.True(KvValue.DeepEquals(Node(SmartPortAttachments.Align(targetText, sourceText, rig), "eyes"), Node(aligned, "eyes")));
    }

    [Fact]
    public void AttachmentsOnUnmappedBonesAreLeftAsTheyAre()
    {
        var source = Humanoid(n => n, CitizenAxes);
        var target = Humanoid(Biped, BipedAxes);
        var rig = new SmartPortRig(source, target);
        var sourceText = Document(n => n);
        var targetText = Document(n => n == "head" ? "some_other_bone" : Biped(n));
        var aligned = SmartPortAttachments.AlignInfluencesAndFixedAxes(targetText, sourceText, rig);
        Assert.True(KvValue.DeepEquals(Node(targetText, "forward_reference_modelspace", 0), Node(aligned, "forward_reference_modelspace", 0)));
        Assert.False(KvValue.DeepEquals(Node(targetText, "forward_reference_modelspace"), Node(aligned, "forward_reference_modelspace")));
    }
}
