using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>User repro round 2 (2026-07-04): catgirl "leg is up, fingers stretched like
/// crazy". Per-finger-chain tip distances and leg pose vs the source, per frame.</summary>
public class UserCatgirlHandLegAudit
{
    private readonly ITestOutputHelper _out;
    public UserCatgirlHandLegAudit(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData("todo/Neutral_throw_ball_001__A057.bvh")]
    [InlineData("mixamo/Surprised.fbx")]
    [InlineData("actorcore/catwalk-loop-378982.fbx")]
    public void FingerAndLegAudit(string clipRelative)
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        var clipPath = TestUtil.RepoFile(("dev/corpus/" + clipRelative).Split('/'));
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
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

        var skeleton = rig.Skeleton;
        var rest = skeleton.RestWorld;
        _out.WriteLine($"== {clipRelative}: frames={clip.SolvedFrames!.Count}");

        // Finger tips: distance from the hand at rest vs worst frame (ratio > ~1.1 = the
        // chain is being straightened/swept; >1.5 = "stretched like crazy").
        foreach (var side in new[] { "l", "r" })
        {
            var hand = Enumerable.Range(0, skeleton.Count).Single(i => skeleton[i].Name == $"hand.{side}");
            foreach (var finger in new[] { "index", "middle", "ring", "pinky", "thumb" })
            {
                var tipName = finger == "thumb" ? $"thumb3.{side}" : $"{finger}3.{side}";
                var tip = Enumerable.Range(0, skeleton.Count)
                    .SingleOrDefault(i => skeleton[i].Name == tipName, -1);
                if (tip < 0) continue;
                var restD = (rest[tip].Pos - rest[hand].Pos).Length();
                float maxD = 0, minD = float.MaxValue;
                var maxF = 0;
                for (var f = 0; f < clip.SolvedFrames.Count; f++)
                {
                    var world = new HumanoidRetargeter.Core.Skeleton.Pose(clip.SolvedFrames[f]).ToWorld(skeleton);
                    var d = (world[tip].Pos - world[hand].Pos).Length();
                    if (d > maxD) { maxD = d; maxF = f; }
                    minD = MathF.Min(minD, d);
                }
                _out.WriteLine($"  {tipName}: rest {restD:0.#}  frame [{minD:0.#}..{maxD:0.#}] "
                    + $"ratio {maxD / restD:0.##} (worst f{maxF})");
            }
        }

        // Legs: thigh direction vs character up at a mid frame, target vs source.
        var srcScene = HumanoidRetargeter.Core.Retargeter.ImportSource(File.ReadAllBytes(clipPath), Path.GetFileName(clipPath));
        var (srcMap, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(srcScene.Skeleton);
        foreach (var role in new[] { BoneRole.UpperLegL, BoneRole.UpperLegR })
        {
            var t = rig.BoneForRole(role)!.Value;
            var tChild = rig.BoneForRole(role == BoneRole.UpperLegL ? BoneRole.LowerLegL : BoneRole.LowerLegR)!.Value;
            var s = srcMap.RoleToBone[role];
            var sChild = srcMap.RoleToBone[role == BoneRole.UpperLegL ? BoneRole.LowerLegL : BoneRole.LowerLegR];
            for (var f = 0; f < clip.SolvedFrames.Count; f += Math.Max(1, clip.SolvedFrames.Count / 6))
            {
                var sf = Math.Min(f, srcScene.Clips[0].Frames.Count - 1);
                var tw = new HumanoidRetargeter.Core.Skeleton.Pose(clip.SolvedFrames[f]).ToWorld(skeleton);
                var sw = new HumanoidRetargeter.Core.Skeleton.Pose(srcScene.Clips[0].Frames[sf]).ToWorld(srcScene.Skeleton);
                var tDir = Vector3.Normalize(tw[tChild].Pos - tw[t].Pos);
                var sDir = Vector3.Normalize(sw[sChild].Pos - sw[s].Pos);
                // Y is up on both rigs (Y-up imports).
                _out.WriteLine($"  {role} f{f}: tgt upComponent {tDir.Y:0.##}  src upComponent {sDir.Y:0.##}");
            }
        }
    }
}
