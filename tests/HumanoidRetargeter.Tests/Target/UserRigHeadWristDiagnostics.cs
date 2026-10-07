using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repro (2026-07-04, local-only): "on the die animation the wrist problem is
/// still there" - the embedded take is authored ON this rig, so its role-cascade round-trip
/// must reproduce the original take. Measures per-bone world-rotation error of the solved
/// output vs the raw take, per frame; big errors name the defective bones exactly.</summary>
public class UserDieEmbeddedTakeRoundTrip
{
    private readonly ITestOutputHelper _out;
    public UserDieEmbeddedTakeRoundTrip(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EmbeddedTake_OnOwnRig_RoundTripsExactly()
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "die", "source", "DieExported.fbx");
        if (!File.Exists(fbx)) return; // local-only

        var bytes = File.ReadAllBytes(fbx);
        var scene = FbxImporter.Import(bytes);
        Assert.True(scene.Clips.Count > 0, "no embedded take");
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };

        // Exactly what EditorPipeline.BuildEmbeddedTakeRequests sends.
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = bytes, SourceFileName = "DieExported.fbx",
                RootMotion = HumanoidRetargeter.Core.Cleanup.RootMotionMode.Off,
                FootPlantCleanup = false, ArmEffectorIk = false,
                PreserveSourceTranslations = true } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Assert.True(clip.Success, clip.Error);

        var take = scene.Clips[0];
        var frames = Math.Min(clip.SolvedFrames!.Count, take.Frames.Count);
        _out.WriteLine($"take '{take.Name}' frames: src={take.Frames.Count} solved={clip.SolvedFrames.Count}");

        var mapped = new HashSet<int>(map.RoleToBone.Values);
        var maxRotErr = new float[scene.Skeleton.Count];
        var maxPosErr = new float[scene.Skeleton.Count];
        var hips = rig.BoneForRole(BoneRole.Hips)!.Value;
        for (var f = 0; f < frames; f++)
        {
            var srcWorld = new HumanoidRetargeter.Core.Skeleton.Pose(take.Frames[f]).ToWorld(scene.Skeleton);
            var outWorld = new HumanoidRetargeter.Core.Skeleton.Pose(clip.SolvedFrames[f]).ToWorld(rig.Skeleton);
            for (var i = 0; i < scene.Skeleton.Count; i++)
            {
                var rotErr = 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(
                    Quaternion.Normalize(srcWorld[i].Rot), Quaternion.Normalize(outWorld[i].Rot))))) * 180f / MathF.PI;
                var posErr = ((srcWorld[i].Pos - srcWorld[hips].Pos) - (outWorld[i].Pos - outWorld[hips].Pos)).Length();
                maxRotErr[i] = MathF.Max(maxRotErr[i], rotErr);
                maxPosErr[i] = MathF.Max(maxPosErr[i], posErr);
            }
        }

        _out.WriteLine("worst bones by rotation error (deg / hips-relative cm):");
        foreach (var i in Enumerable.Range(0, scene.Skeleton.Count)
                     .OrderByDescending(i => maxRotErr[i]).Take(18))
            _out.WriteLine($"  {scene.Skeleton[i].Name,-28} rot {maxRotErr[i],7:0.##}  pos {maxPosErr[i],7:0.##}  "
                + $"mapped={mapped.Contains(i)}");

        // The wrist chain specifically.
        foreach (var role in new[] { BoneRole.LowerArmR, BoneRole.HandR, BoneRole.LowerArmL, BoneRole.HandL })
            if (rig.BoneForRole(role) is { } b)
                _out.WriteLine($"{role}: '{rig.Skeleton[b].Name}' rot {maxRotErr[b]:0.##} deg  pos {maxPosErr[b]:0.##}");

        // Where do hips-relative POSITION errors come from? Compare LOCAL translations at
        // a mid frame for the worst offenders (a Biped take animates local translations).
        var mid = frames / 2;
        foreach (var i in Enumerable.Range(0, scene.Skeleton.Count)
                     .OrderByDescending(i => maxPosErr[i]).Take(5))
        {
            _out.WriteLine($"LOCAL '{scene.Skeleton[i].Name}': take={take.Frames[mid][i].Pos} "
                + $"solved={clip.SolvedFrames[mid][i].Pos} rest={rig.Skeleton[i].RestLocal.Pos}");
        }

        // ORIENTATION must round-trip exactly - while this holds, a wrist complaint on the
        // embedded take cannot be a rotation defect in the retarget.
        for (var i = 0; i < scene.Skeleton.Count; i++)
            Assert.True(maxRotErr[i] < 1f,
                $"{scene.Skeleton[i].Name} world rotation error {maxRotErr[i]:0.##} deg");

        // POSITIONS too: the take animates Biped local translations (spine sway to ~19cm,
        // thigh shifts on the death fall) - authored data must transfer exactly, not pin
        // to the rest translations. The hips' ANCESTORS are exempt: the solver owns the
        // trajectory and compensates through the pelvis local (pelvis world is exact), so
        // the unskinned Bip01 COM helper intentionally parks at rest.
        var ancestors = new HashSet<int>();
        for (var b = rig.Skeleton[hips].ParentIndex; b >= 0; b = rig.Skeleton[b].ParentIndex)
            ancestors.Add(b);
        for (var i = 0; i < scene.Skeleton.Count; i++)
            Assert.True(ancestors.Contains(i) || maxPosErr[i] < 1f,
                $"{scene.Skeleton[i].Name} hips-relative position error {maxPosErr[i]:0.##} cm");
    }
}
