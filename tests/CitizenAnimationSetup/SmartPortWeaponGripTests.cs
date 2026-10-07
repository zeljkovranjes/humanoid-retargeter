using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>Weapons are mounted on the copied hold_R with an identity offset and are never scaled.</summary>
public class SmartPortWeaponGripTests
{
    static readonly string[] Helpers = { "hold_R", "hold_L", "hand_R_to_L_ikrule", "hand_L_to_R_ikrule" };

    /// <summary>A target with its own bone-axis convention, a bigger body, even bigger hands,
    /// and a rest wrist angle that differs from the source's.</summary>
    static (SkeletonModel Source, SkeletonModel Target) Rigs(Quaternion axes)
    {
        var source = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        var world = source.RestWorld.ToArray();
        bool Under(int bone, string name)
        {
            for (var i = bone; i >= 0; i = source[i].ParentIndex) if (source[i].Name == name) return true;
            return false;
        }
        var result = new XForm[source.Count];
        foreach (var bone in source.Bones)
        {
            var frame = world[bone.Index];
            frame.Pos *= 1.25f;
            foreach (var side in new[] { "L", "R" })
            {
                if (!Under(bone.Index, "hand_" + side)) continue;
                var wrist = world[source.IndexOf("hand_" + side)].Pos * 1.25f;
                var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, side == "R" ? .26f : -.26f);
                frame.Pos = wrist + Vector3.Transform((frame.Pos - wrist) * 1.4f, bend);
                frame.Rot = Quaternion.Normalize(bend * frame.Rot);
            }
            frame.Rot = Quaternion.Normalize(frame.Rot * axes);
            result[bone.Index] = frame;
        }
        var target = SkeletonModel.Create(source.Bones.Where(b => !Helpers.Contains(b.Name)).Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name,
            b.ParentIndex < 0 ? result[b.Index] : XForm.ToLocal(result[b.ParentIndex], result[b.Index]))).ToArray());
        return (source, target);
    }

    /// <summary>Both hands on a weapon in front of the chest; the support goal follows the left hand.</summary>
    static XForm[] WeaponPose(SkeletonModel source)
    {
        var pose = source.Bones.Select(b => b.RestLocal).ToArray();
        var left = source.IndexOf("arm_upper_L"); var right = source.IndexOf("arm_upper_R");
        var center = (source.RestWorld[left].Pos + source.RestWorld[right].Pos) * .5f;
        var up = Vector3.Normalize(source.RestWorld[source.IndexOf("head")].Pos - source.RestWorld[source.IndexOf("pelvis")].Pos);
        var lateral = Vector3.Normalize(source.RestWorld[right].Pos - source.RestWorld[left].Pos);
        var forward = Vector3.Normalize(Vector3.Cross(up, lateral));
        var reach = source[source.IndexOf("arm_lower_R")].RestLocal.Pos.Length() + source[source.IndexOf("hand_R")].RestLocal.Pos.Length();
        foreach (var side in new[] { "L", "R" })
        {
            var chain = new LimbChain { Upper = source.IndexOf("arm_upper_" + side), Lower = source.IndexOf("arm_lower_" + side), End = source.IndexOf("hand_" + side) };
            var goal = center + forward * reach * (side == "L" ? .8f : .55f) + lateral * reach * (side == "L" ? -.05f : .1f) - up * reach * .15f;
            EffectorIk.ApplyGoals(new() { pose }, source, chain, new[] { goal }, lateral, soften: 0);
        }
        var world = new Pose(pose).ToWorld(source);
        pose[source.IndexOf("hand_L_to_R_ikrule")] = XForm.ToLocal(world[source.IndexOf("hand_R")], world[source.IndexOf("hand_L")]);
        return pose;
    }

    /// <summary>Palm axes from the bind knuckles carried by the posed hand bone.</summary>
    static (XForm Frame, float Size) Palm(SkeletonModel rig, XForm[] world, string side)
    {
        var hand = rig.IndexOf("hand_" + side);
        var knuckles = new[] { "index", "middle", "ring" }.Select(f => world[hand].TransformPoint(
            rig.RestWorld[hand].Inverse().TransformPoint(rig.RestWorld[rig.IndexOf($"finger_{f}_0_{side}")].Pos))).ToArray();
        var centre = (knuckles[0] + knuckles[1] + knuckles[2]) / 3;
        var along = Vector3.Normalize(centre - world[hand].Pos);
        var across = knuckles[0] - knuckles[2];
        across = Vector3.Normalize(across - along * Vector3.Dot(across, along));
        var normal = Vector3.Cross(along, across);
        var rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(along.X, along.Y, along.Z, 0,
            across.X, across.Y, across.Z, 0, normal.X, normal.Y, normal.Z, 0, 0, 0, 0, 1));
        return (new XForm(world[hand].Pos, Quaternion.Normalize(rotation)), Vector3.Distance(centre, world[hand].Pos));
    }

    static (float Position, float Rotation, float Aim, float Support) Errors(SmartPortRig rig, XForm[] pose)
    {
        var source = rig.Source;
        var sw = new Pose(pose).ToWorld(source);
        var tw = new Pose(rig.Transfer(pose)).ToWorld(rig.Target);
        var (sp, ss) = Palm(source, sw, "R");
        var (tp, ts) = Palm(rig.Target, tw, "R");
        var sGrip = XForm.ToLocal(sp, sw[source.IndexOf("hold_R")]);
        var tGrip = XForm.ToLocal(tp, tw[rig.Target.IndexOf("hold_R")]);
        // The graph's IK puts the support wrist on its goal; its grip must land where the source's does on the weapon.
        var hand = tw[rig.Target.IndexOf("hand_L")];
        var grip = XForm.Compose(tw[rig.Target.IndexOf("hand_L_to_R_ikrule")], XForm.ToLocal(hand, tw[rig.Target.IndexOf("hold_L")])).Pos;
        var expected = sw[source.IndexOf("hold_R")].Inverse().TransformPoint(sw[source.IndexOf("hold_L")].Pos);
        return (Vector3.Distance(sGrip.Pos / ss, tGrip.Pos / ts) * ts,
            MathQ.AngleBetween(sGrip.Rot, tGrip.Rot) * 180 / MathF.PI,
            MathQ.AngleBetween(sw[source.IndexOf("hold_R")].Rot, tw[rig.Target.IndexOf("hold_R")].Rot) * 180 / MathF.PI,
            Vector3.Distance(expected, tw[rig.Target.IndexOf("hold_R")].Inverse().TransformPoint(grip)));
    }

    public static IEnumerable<object[]> AxisConventions() => new[]
    {
        new object[] { 0f, 0f, 0f },
        new object[] { 90f, 0f, 0f },   // e.g. bones along +Y
        new object[] { 0f, 0f, 90f },
        new object[] { 180f, 90f, 0f },
        new object[] { 35f, -70f, 120f },
    };

    [Theory]
    [MemberData(nameof(AxisConventions))]
    public void CopiedWeaponSitsInTheTargetPalmAndBothHandsStayOnIt(float x, float y, float z)
    {
        var axes = Quaternion.CreateFromYawPitchRoll(y * MathF.PI / 180, x * MathF.PI / 180, z * MathF.PI / 180);
        var (source, target) = Rigs(axes);
        var goals = new Dictionary<string, string> { ["hand_L_to_R_ikrule"] = "hand_L" };
        var rig = new SmartPortRig(source, target, goals, new[] { "hold_R", "hold_L" });
        Assert.True(MathF.Abs(rig.MotionScale - 1.25f) < .01f);
        var pose = WeaponPose(source);
        var (position, rotation, aim, support) = Errors(rig, pose);
        Assert.True(position < .01f, $"Weapon grip is {position} units off the palm");
        Assert.True(rotation < .1f, $"Weapon is turned {rotation} degrees in the palm");
        Assert.True(aim < .1f, $"Weapon aim changed by {aim} degrees");
        Assert.True(support < .01f, $"Support hand is {support} units off the weapon");

        // The same body without fitting floats the weapon; this scenario is not trivially satisfied.
        var before = Errors(new SmartPortRig(source, target, goals), pose);
        Assert.True(before.Position > .5f || before.Rotation > 5 || before.Support > .5f);

        foreach (var bone in target.Bones)
            Assert.Equal(bone.RestLocal, rig.Target[rig.Target.IndexOf(bone.Name)].RestLocal);
        var result = rig.Transfer(pose);
        foreach (var name in new[] { "arm_upper_L", "arm_lower_L", "hand_L", "arm_upper_R", "arm_lower_R", "hand_R" })
        {
            var index = rig.Target.IndexOf(name);
            Assert.True(Vector3.Distance(rig.Target[index].RestLocal.Pos, result[index].Pos) < .0001f, name + " was stretched");
        }
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }

    /// <summary>A Citizen-armature character of another size: it keeps its own sockets and goals.</summary>
    static (SkeletonModel Source, SkeletonModel Target) Scaled(float scale)
    {
        var source = TargetRig.Load(TargetRigGenerator.Generate(File.ReadAllText(SkeletonTests.FixturePath("rig_human_male.json")))).Skeleton;
        return (source, SkeletonModel.Create(source.Bones.Select(b => new BoneDefinition(b.Name,
            b.ParentIndex < 0 ? null : source[b.ParentIndex].Name, new XForm(b.RestLocal.Pos * scale, b.RestLocal.Rot))).ToArray()));
    }

    static float SupportError(SkeletonModel source, XForm[] pose, SkeletonModel target, XForm[] result)
    {
        var sw = new Pose(pose).ToWorld(source);
        var tw = new Pose(result).ToWorld(target);
        var grip = XForm.Compose(tw[target.IndexOf("hand_L_to_R_ikrule")], XForm.ToLocal(tw[target.IndexOf("hand_L")], tw[target.IndexOf("hold_L")])).Pos;
        var expected = sw[source.IndexOf("hold_R")].Inverse().TransformPoint(sw[source.IndexOf("hold_L")].Pos);
        return Vector3.Distance(expected, tw[target.IndexOf("hold_R")].Inverse().TransformPoint(grip));
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(.8f)]
    public void ExistingTargetSocketsKeepTheSupportGripOnTheWeapon(float scale)
    {
        var (source, target) = Scaled(scale);
        var goals = new Dictionary<string, string> { ["hand_L_to_R_ikrule"] = "hand_L" };
        var rig = new SmartPortRig(source, target, goals, new[] { "hold_R", "hold_L" });
        var plain = new SmartPortRig(source, target, goals);
        var pose = WeaponPose(source);
        var result = rig.Transfer(pose);
        Assert.True(SupportError(source, pose, rig.Target, result) < .01f);
        Assert.True(SupportError(source, pose, plain.Target, plain.Transfer(pose)) > .5f);
        // The target's own weapon socket stays authoritative on its hand (the grip as a whole may be
        // drawn into reach of the arms).
        var hold = rig.Target.IndexOf("hold_R");
        var own = plain.Transfer(pose)[hold];
        Assert.True(Vector3.Distance(own.Pos, result[hold].Pos) < .0001f && MathQ.AngleBetween(own.Rot, result[hold].Rot) < .0001f);
        Assert.All(rig.Transfer(Enumerable.Repeat(XForm.Identity, source.Count).ToArray(), true), delta =>
        {
            Assert.True(delta.Pos.Length() < .0001f);
            Assert.True(MathQ.AngleBetween(delta.Rot, Quaternion.Identity) < .001f);
        });
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(.8f)]
    public void FittedCitizenKeepsTheSupportGripOnTheWeapon(float scale)
    {
        var (source, target) = Scaled(scale);
        var pose = WeaponPose(source);
        var result = new FittedCitizenPose(source, target).Transfer(pose);
        Assert.True(SupportError(source, pose, target, result) < .01f, $"Support grip missed by {SupportError(source, pose, target, result)}");
        // Only the support goals move: every other channel is the plain fitted transfer.
        var identity = new FittedCitizenPose(source, source).Transfer(pose);
        for (var i = 0; i < source.Count; i++)
        {
            Assert.True(Vector3.Distance(identity[i].Pos, pose[i].Pos) < .001f, source[i].Name);
            Assert.True(MathQ.AngleBetween(identity[i].Rot, pose[i].Rot) < .001f, source[i].Name);
        }
    }

    [Fact]
    public void ShortArmsKeepTheSourceBendReserveOnTheWeapon()
    {
        // Arms shorter than the body scale: the grip would fit the clip, but with an arm more
        // extended than the source's, which locomotion layers then push out of reach.
        var (source, full) = Rigs(Quaternion.Identity);
        var target = SkeletonModel.Create(full.Bones.Select(b => new BoneDefinition(b.Name, b.ParentIndex < 0 ? null : full[b.ParentIndex].Name,
            b.Name.StartsWith("arm_lower_") || b.Name.StartsWith("hand_") && b.Name.Length == 6
                ? new XForm(b.RestLocal.Pos * .8f, b.RestLocal.Rot) : b.RestLocal)).ToArray());
        var rig = new SmartPortRig(source, target, new Dictionary<string, string> { ["hand_L_to_R_ikrule"] = "hand_L" }, new[] { "hold_R", "hold_L" });
        var pose = WeaponPose(source);
        // How far the support goal (what the graph's IK must reach) is, relative to the arm's length.
        float Extension(SkeletonModel sk, XForm[] world)
        {
            var (u, l, h) = (world[sk.IndexOf("arm_upper_L")].Pos, world[sk.IndexOf("arm_lower_L")].Pos, world[sk.IndexOf("hand_L")].Pos);
            return Vector3.Distance(u, world[sk.IndexOf("hand_L_to_R_ikrule")].Pos) / (Vector3.Distance(u, l) + Vector3.Distance(l, h));
        }
        var sw = new Pose(pose).ToWorld(source);
        var result = rig.Transfer(pose);
        var tw = new Pose(result).ToWorld(rig.Target);
        // The source keeps a bend reserve (a nearly straight source arm would be fitted anyway).
        Assert.True(Extension(source, sw) < .9f);
        Assert.True(Extension(rig.Target, tw) <= Extension(source, sw) + .005f,
            $"support arm {Extension(rig.Target, tw)} vs source {Extension(source, sw)}");
        // Locomotion moves the shoulders at body scale: keep the source's slack at body scale too.
        float Slack(SkeletonModel sk, XForm[] world)
        {
            var (u, l, h) = (world[sk.IndexOf("arm_upper_L")].Pos, world[sk.IndexOf("arm_lower_L")].Pos, world[sk.IndexOf("hand_L")].Pos);
            return Vector3.Distance(u, l) + Vector3.Distance(l, h) - Vector3.Distance(u, world[sk.IndexOf("hand_L_to_R_ikrule")].Pos);
        }
        Assert.True(Slack(rig.Target, tw) >= Slack(source, sw) * rig.MotionScale - .01f,
            $"support arm slack {Slack(rig.Target, tw)} vs source {Slack(source, sw)} x {rig.MotionScale}");
        Assert.True(Vector3.Distance(tw[rig.Target.IndexOf("hand_L")].Pos, tw[rig.Target.IndexOf("hand_L_to_R_ikrule")].Pos) < .01f);
        Assert.True(Errors(rig, pose).Support < .01f);
        foreach (var name in new[] { "arm_upper_L", "arm_lower_L", "hand_L", "arm_upper_R", "arm_lower_R", "hand_R" })
        {
            var index = rig.Target.IndexOf(name);
            Assert.True(Vector3.Distance(rig.Target[index].RestLocal.Pos, result[index].Pos) < .0001f, name + " was stretched");
        }
    }

    [Fact]
    public void GripFollowsTheHandThroughAnimation()
    {
        var (source, target) = Rigs(Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2));
        var rig = new SmartPortRig(source, target, new Dictionary<string, string> { ["hand_L_to_R_ikrule"] = "hand_L" }, new[] { "hold_R", "hold_L" });
        var pose = WeaponPose(source);
        foreach (var angle in new[] { -.5f, -.2f, .3f, .6f })
        {
            var moved = pose.ToArray();
            var wrist = source.IndexOf("hand_R");
            moved[wrist] = new XForm(moved[wrist].Pos, Quaternion.Normalize(moved[wrist].Rot * Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 2, 3)), angle)));
            var spine = source.IndexOf("spine_2");
            moved[spine] = new XForm(moved[spine].Pos, Quaternion.Normalize(moved[spine].Rot * Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle * .3f)));
            var world = new Pose(moved).ToWorld(source);
            moved[source.IndexOf("hand_L_to_R_ikrule")] = XForm.ToLocal(world[wrist], world[source.IndexOf("hand_L")]);
            var (position, rotation, aim, support) = Errors(rig, moved);
            Assert.True(position < .01f && rotation < .1f && aim < .1f && support < .01f,
                $"angle {angle}: grip {position}, turn {rotation}, aim {aim}, support {support}");
        }
    }
}
