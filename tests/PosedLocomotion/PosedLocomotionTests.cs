using System.Numerics;
using System.Text;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Formats.Bvh;
using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Solve;

public class PosedLocomotionTests
{
    [Theory]
    [InlineData(1, 500)]
    [InlineData(1, -500)]
    [InlineData(2, 500)]
    [InlineData(2, -500)]
    public void InPlaceCentersClipsWhoseStaticReferenceIsFarFromTheTake(int upAxis, float restTravel)
    {
        var target = Target(upAxis);
        var frames = Convert(Fixture(upAxis, restTravel: restTravel), target, RootMotionMode.InPlace);
        var hips = target.Rig.BoneForRole(HumanoidRetargeter.Mapping.BoneRole.Hips)!.Value;
        var center = frames.Aggregate(Vector3.Zero, (sum, frame) => sum + new Pose(frame).ToWorld(target.Rig.Skeleton)[hips].Pos) / frames.Count;
        var delta = center - target.Rig.Skeleton.RestWorld[hips].Pos;
        if (upAxis == 1) delta.Y = 0; else delta.Z = 0;
        Assert.True(delta.Length() < .01f, $"In-place clip is displaced from the target bind by {delta}.");
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(1, true, -90)]
    [InlineData(2, false, 90)]
    [InlineData(2, true, 90)]
    public void SerializedRootYawMatchesTargetImportConvention(int upAxis, bool embedsMesh, float degrees)
    {
        var original = Target(upAxis);
        var target = new RetargetTargetSpec
        {
            Rig = original.Rig, UpAxis = original.UpAxis, VmdlScale = 1,
            MeshFilePath = embedsMesh ? "test.fbx" : "",
        };
        var frame = target.Rig.Skeleton.Bones.Select(b => b.RestLocal).ToArray();
        var before = frame.ToArray();
        var serialized = Retargeter.TestHook_CompensateEmbeddedMeshRootYaw(new[] { frame }, target).Single();
        var yaw = Quaternion.CreateFromAxisAngle(upAxis == 1 ? Vector3.UnitY : Vector3.UnitZ, degrees * MathF.PI / 180);
        for (var i = 0; i < frame.Length; i++)
        {
            var root = target.Rig.Skeleton[i].ParentIndex < 0;
            var expectedPosition = root ? Vector3.Transform(before[i].Pos, yaw) : before[i].Pos;
            var expectedRotation = root ? Quaternion.Normalize(yaw * before[i].Rot) : before[i].Rot;
            Assert.True(Vector3.Distance(expectedPosition, serialized[i].Pos) < .0001f);
            Assert.True(MathF.Abs(Quaternion.Dot(expectedRotation, serialized[i].Rot)) > .99999f);
            Assert.Equal(before[i], frame[i]); // Serialization must not mutate solved poses.
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LeaningSourceRestDoesNotTurnForwardTravelIntoVerticalMotion(int upAxis)
    {
        var target = Target(upAxis);
        var result = Convert(Fixture(upAxis, torsoLean: 70), target, RootMotionMode.InPlace);
        var hip = target.Rig.BoneForRole(HumanoidRetargeter.Mapping.BoneRole.Hips)!.Value;
        var heights = result.Select(frame => Vertical(new Pose(frame).ToWorld(target.Rig.Skeleton)[hip].Pos, upAxis)).ToArray();
        Assert.True(heights.Max() - heights.Min() < .05f,
            $"Horizontal travel changed pelvis height by {heights.Max() - heights.Min()}.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovingRootTravelPreservesGroundedHeightsOnLeaningTarget(int upAxis)
    {
        var target = Target(upAxis, torsoLean: 20);
        var bytes = Fixture(upAxis);
        var moving = Convert(bytes, target, RootMotionMode.Off);
        var inPlace = Convert(bytes, target, RootMotionMode.InPlace);
        for (var f = 0; f < moving.Count; f++)
        {
            var before = new Pose(moving[f]).ToWorld(target.Rig.Skeleton);
            var after = new Pose(inPlace[f]).ToWorld(target.Rig.Skeleton);
            for (var b = 0; b < before.Length; b++)
                Assert.True(MathF.Abs(Vertical(before[b].Pos - after[b].Pos, upAxis)) < .005f,
                    $"Frame {f}, bone {b}: root-motion removal changed ground clearance.");
        }
    }

    [Fact]
    public void LiftedFootInExportedRestDoesNotPushPlantedFootBelowGround()
    {
        var target = Target(1);
        var result = Convert(Fixture(1, liftedRest: 35, travel: 0), target, RootMotionMode.Off);
        var toe = target.Rig.BoneForRole(HumanoidRetargeter.Mapping.BoneRole.ToeR)!.Value;
        var floor = target.Rig.Skeleton.RestWorld[toe].Pos.Y;
        foreach (var frame in result)
            Assert.True(new Pose(frame).ToWorld(target.Rig.Skeleton)[toe].Pos.Y >= floor - .1f,
                "A mid-step rest was incorrectly used as the planted-foot height reference.");
    }

    static float Vertical(Vector3 value, int axis) => axis == 1 ? value.Y : value.Z;

    static RetargetTargetSpec Target(int upAxis, float torsoLean = 0)
    {
        var skeleton = FbxImporter.Import(Fixture(upAxis, torsoLean)).Skeleton;
        var (map, _) = Retargeter.ResolveMapping(skeleton);
        return new RetargetTargetSpec
        {
            Rig = TargetRig.FromSkeleton(skeleton, map), VmdlScale = 1,
            UpAxis = upAxis == 1 ? TargetUpAxis.YUpCm : TargetUpAxis.ZUpEngine,
        };
    }

    static List<HumanoidRetargeter.Maths.XForm[]> Convert(byte[] bytes, RetargetTargetSpec target, RootMotionMode mode)
    {
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes, SourceFileName = "posed-locomotion.fbx",
            RootMotion = mode, FootPlantCleanup = true,
        }, target);
        Assert.True(result.Success, string.Join("; ", result.Clips.Select(c => c.Error)));
        return result.Clips[0].SolvedFrames!;
    }

    // A tiny animation-only FBX: no proprietary pack files or optional corpus needed.
    // The static rig can be bent over or caught mid-step; the take moves horizontally.
    static byte[] Fixture(int upAxis, float torsoLean = 0, float liftedRest = 0, float travel = 100, float restTravel = 0)
    {
        var skeleton = BvhImporter.Import(Encoding.UTF8.GetBytes(PosedSkeletonFixture.SyntheticWalkBvh())).Skeleton;
        var objects = new StringBuilder();
        var connections = new StringBuilder();
        foreach (var bone in skeleton.Bones)
        {
            var p = bone.RestLocal.Pos;
            if (bone.ParentIndex < 0) { p.Y = 93; p.Z = restTravel; }
            if (upAxis == 2) p = new Vector3(p.X, -p.Z, p.Y);
            var angle = bone.Name == "mixamorig:Spine" ? torsoLean
                : bone.Name == "mixamorig:RightUpLeg" ? liftedRest : 0;
            objects.AppendLine(FormattableString.Invariant($$"""
                Model: {{bone.Index + 1}}, "Model::{{bone.Name}}", "LimbNode" {
                    Properties70: {
                        P: "Lcl Translation", "Lcl Translation", "", "A",{{p.X}},{{p.Y}},{{p.Z}}
                        P: "Lcl Rotation", "Lcl Rotation", "", "A",{{angle}},0,0
                    }
                }
                """));
            connections.AppendLine($"C: \"OO\",{bone.Index + 1},{bone.ParentIndex + 1}");
        }
        void Curve(int id, int bone, string property, string axis, float end)
        {
            objects.AppendLine(FormattableString.Invariant($$"""
                AnimationCurveNode: {{id}}, "AnimCurveNode::curve", "" { }
                AnimationCurve: {{id + 1}}, "AnimCurve::value", "" {
                    KeyTime: *2 { a: 0,46186158000 }
                    KeyValueFloat: *2 { a: 0,{{end}} }
                }
                """));
            connections.AppendLine($"C: \"OO\",{id},10001\nC: \"OP\",{id},{bone},\"{property}\"\nC: \"OP\",{id + 1},{id},\"d|{axis}\"");
        }
        Curve(10002, 1, "Lcl Translation", upAxis == 1 ? "Z" : "Y", upAxis == 1 ? travel : -travel);
        Curve(10004, skeleton.IndexOf("mixamorig:RightUpLeg") + 1, "Lcl Rotation", "X", 0);
        return Encoding.UTF8.GetBytes(FormattableString.Invariant($$"""
            GlobalSettings: { Properties70: {
                P: "UpAxis", "int", "Integer", "",{{upAxis}}
                P: "UpAxisSign", "int", "Integer", "",1
                P: "UnitScaleFactor", "double", "Number", "",1
            } }
            Objects: {
                {{objects}}
                AnimationStack: 10000, "AnimStack::walk", "" { }
                AnimationLayer: 10001, "AnimLayer::base", "" { }
            }
            Connections: {
                {{connections}}
                C: "OO",10001,10000
            }
            """));
    }
}
