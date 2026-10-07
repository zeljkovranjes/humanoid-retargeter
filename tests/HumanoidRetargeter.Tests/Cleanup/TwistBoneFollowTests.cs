using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Cleanup;

/// <summary>
/// User repro (2026-07-04): the catgirl's wrists collapse into spike fans on the throw —
/// classic candy-wrapper: the rig distributes limb roll through unmapped twist bones
/// (<c>forearm_twist.l</c> mid-forearm, Auto-Rig Pro; AdvancedSkeleton's ElbowPart1),
/// which froze at rest while the hand pronated. Twist bones must follow their limb's
/// roll by their fractional position along the segment.
/// </summary>
public class TwistBoneFollowTests
{
    private readonly ITestOutputHelper _out;
    public TwistBoneFollowTests(ITestOutputHelper o) => _out = o;

    // Arm chain along +X: upper(0,150) -> forearm(30) -> hand(30); twist leaf under the
    // forearm at 60% toward the hand.
    private static SkeletonModel BuildArm() => SkeletonModel.Create(new[]
    {
        new BoneDefinition("root", null, new XForm(new Vector3(0, 150, 0), Quaternion.Identity)),
        new BoneDefinition("upper_arm", "root", new XForm(new Vector3(10, 0, 0), Quaternion.Identity)),
        new BoneDefinition("forearm", "upper_arm", new XForm(new Vector3(30, 0, 0), Quaternion.Identity)),
        new BoneDefinition("forearm_twist", "forearm", new XForm(new Vector3(18, 0, 0), Quaternion.Identity)),
        new BoneDefinition("hand", "forearm", new XForm(new Vector3(30, 0, 0), Quaternion.Identity)),
    });

    private static TargetRig Rig(SkeletonModel skeleton)
    {
        var map = new MappingResult("test", MappingSource.Manual);
        map.RoleToBone[BoneRole.Hips] = 0;
        map.RoleToBone[BoneRole.UpperArmL] = 1;
        map.RoleToBone[BoneRole.LowerArmL] = 2;
        map.RoleToBone[BoneRole.HandL] = 4;
        return TargetRig.FromSkeleton(skeleton, map);
    }

    [Fact]
    public void MidTwist_FollowsHandRoll_ByFraction()
    {
        var skeleton = BuildArm();
        var rig = Rig(skeleton);

        // Hand pronates 90 deg about the forearm axis (+X in forearm space).
        var roll = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var frame = Enumerable.Range(0, skeleton.Count).Select(i => skeleton[i].RestLocal).ToArray();
        frame[4] = new XForm(skeleton[4].RestLocal.Pos, roll * skeleton[4].RestLocal.Rot);
        var frames = new List<XForm[]> { frame };

        var driven = TwistBoneFollow.Apply(frames, rig, excluded: null);
        _out.WriteLine($"driven={driven}");
        Assert.Equal(1, driven);

        // twist bone sits at 18/30 = 60% along forearm->hand: 0.6 * 90 = 54 deg roll.
        var twistDelta = frames[0][3].Rot * Quaternion.Conjugate(skeleton[3].RestLocal.Rot);
        var angle = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(twistDelta.W))) * 180f / MathF.PI;
        _out.WriteLine($"twist angle {angle:0.#} deg axis=({twistDelta.X:0.##},{twistDelta.Y:0.##},{twistDelta.Z:0.##})");
        Assert.InRange(angle, 50f, 58f);
        // Roll axis is the limb axis (X): no swing leakage.
        var axis = Vector3.Normalize(new Vector3(twistDelta.X, twistDelta.Y, twistDelta.Z));
        Assert.True(MathF.Abs(axis.X) > 0.99f, $"twist axis off the limb: {axis}");
    }

    [Fact]
    public void HandSwing_WithoutRoll_LeavesTwistAtRest()
    {
        var skeleton = BuildArm();
        var rig = Rig(skeleton);

        // Pure swing (bend about Y) carries no roll - the twist bone must not move.
        var swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3);
        var frame = Enumerable.Range(0, skeleton.Count).Select(i => skeleton[i].RestLocal).ToArray();
        frame[4] = new XForm(skeleton[4].RestLocal.Pos, swing * skeleton[4].RestLocal.Rot);
        var frames = new List<XForm[]> { frame };

        TwistBoneFollow.Apply(frames, rig, excluded: null);

        var delta = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(frames[0][3].Rot),
            Quaternion.Normalize(skeleton[3].RestLocal.Rot))))) * 180f / MathF.PI;
        Assert.True(delta < 1f, $"twist moved {delta:0.#} deg on a pure swing");
    }

    /// <summary>A NEAR-180° pure swing is where the swing-twist decomposition is
    /// ill-conditioned: noise amplifies into huge fake rolls (measured: a kicking foot
    /// injected ±99° into the calf twist and the calf skin flipped upward — the user's
    /// "leg is up"). The pass must keep the twist bone at rest there.</summary>
    [Fact]
    public void Near180Swing_LeavesTwistAtRest()
    {
        var skeleton = BuildArm();
        var rig = Rig(skeleton);

        // 178° swing about Y with a hair of noise off-axis.
        var swing = Quaternion.CreateFromAxisAngle(
            Vector3.Normalize(new Vector3(0.02f, 1f, 0.015f)), 178f * MathF.PI / 180f);
        var frame = Enumerable.Range(0, skeleton.Count).Select(i => skeleton[i].RestLocal).ToArray();
        frame[4] = new XForm(skeleton[4].RestLocal.Pos, swing * skeleton[4].RestLocal.Rot);
        var frames = new List<XForm[]> { frame };

        TwistBoneFollow.Apply(frames, rig, excluded: null);

        var delta = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
            Quaternion.Normalize(frames[0][3].Rot),
            Quaternion.Normalize(skeleton[3].RestLocal.Rot))))) * 180f / MathF.PI;
        Assert.True(delta < 5f, $"twist injected {delta:0.#} deg on a near-180 swing");
    }

    /// <summary>Real asset (local-only): the catgirl's forearm twists must move once the
    /// hand rolls during the throw (they measured 0 deg before this pass).</summary>
    [Fact]
    public void Catgirl_ForearmTwists_FollowTheThrow()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        var clipPath = TestUtil.RepoFile("dev", "corpus", "todo", "Neutral_throw_ball_001__A057.bvh");
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = HumanoidRetargeter.Core.Formats.Fbx.FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath),
                SourceFileName = Path.GetFileName(clipPath) } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Assert.True(clip.Success, clip.Error);

        // Isolation probe: does detection find the twists on this rig at all?
        var copy = clip.SolvedFrames!.Take(1).Select(f => f.ToArray()).ToList();
        var probe = TwistBoneFollow.Apply(copy, rig, excluded: null);
        _out.WriteLine($"direct Apply detects {probe} twist bones");

        // How much roll does the hand's LOCAL delta actually carry about the forearm axis?
        {
            var hand = rig.BoneForRole(BoneRole.HandL)!.Value;
            var axis = System.Numerics.Vector3.Normalize(rig.Skeleton[hand].RestLocal.Pos);
            var maxRoll = 0f;
            foreach (var frame in clip.SolvedFrames!)
            {
                var d = Quaternion.Normalize(frame[hand].Rot
                    * Quaternion.Conjugate(rig.Skeleton[hand].RestLocal.Rot));
                var proj = System.Numerics.Vector3.Dot(new System.Numerics.Vector3(d.X, d.Y, d.Z), axis);
                var roll = 2f * MathF.Atan2(MathF.Abs(proj), MathF.Abs(d.W)) * 180f / MathF.PI;
                maxRoll = MathF.Max(maxRoll, roll);
            }
            _out.WriteLine($"hand.l max local roll about forearm axis: {maxRoll:0.#} deg");
        }

        foreach (var name in new[] { "forearm_twist.l", "forearm_twist.r" })
        {
            var twist = Enumerable.Range(0, scene.Skeleton.Count)
                .Single(i => scene.Skeleton[i].Name == name);
            var rest = rig.Skeleton[twist].RestLocal.Rot;
            var max = 0f;
            foreach (var frame in clip.SolvedFrames!)
                max = MathF.Max(max, 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
                    Quaternion.Normalize(frame[twist].Rot), Quaternion.Normalize(rest))))) * 180f / MathF.PI);
            _out.WriteLine($"{name}: max swing {max:0.#} deg");
            Assert.True(max > 5f, $"{name} never moves ({max:0.#} deg) - wrist candy-wrapper");
        }
    }
}
