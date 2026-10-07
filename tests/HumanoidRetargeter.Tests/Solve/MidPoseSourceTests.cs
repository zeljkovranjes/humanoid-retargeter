using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// Mid-pose FBX SOURCES: the retargeter adopts the importer's bind-rest alternate skeleton
/// when it stands straighter along the file's up axis. Before the adoption, converting the
/// catgirl's original posed export produced output a MEAN 48° world-rot off the repaired-file
/// ground truth (left foot 124° / 119 cm — "arms are weird, fingers and one of the legs").
/// The Defenses rig (bind rest tilts the character-up estimate ~7°) must keep its exported
/// pose as rest — its planted-feet/head-gaze gates pin that behavior separately.
/// </summary>
public class MidPoseSourceTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Catgirl_PosedSource_SolvesLikeTheRepairedFile()
    {
        var path = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        if (!File.Exists(path))
            return;

        var original = File.ReadAllBytes(path);
        var repaired = FbxBindPoseFixer.TryFix(original, out _);
        Assert.NotNull(repaired);

        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));

        (List<XForm[]> Frames, string Notes) Solve(byte[] bytes)
        {
            var result = Retargeter.Convert(new RetargetRequest
            {
                SourceData = bytes,
                SourceFileName = "Catgirl_2.fbx",
            }, target);
            var clip = result.Clips.First(c => c.Success && c.SolvedFrames is { Count: > 0 });
            return (clip.SolvedFrames!, string.Join(" | ", clip.Mapping?.Notes ?? new List<string>()));
        }

        var (solvedOrig, notes) = Solve(original);
        var (solvedFix, _) = Solve(repaired!);
        Assert.Contains("rest pose restored from the file's own bind data", notes);

        // The posed original must now solve like the repaired ground truth. Small residuals
        // are expected (the repaired file's static-channel overrides still carry posed-space
        // translations; bone lengths barely differ), the 48°/75°-per-limb break must not.
        var skel = target.Rig.Skeleton;
        int n = Math.Min(solvedOrig.Count, solvedFix.Count);
        float worstMean = 0;
        string worstName = "";
        for (int b = 0; b < skel.Count; b++)
        {
            float sum = 0;
            for (int f = 0; f < n; f++)
                sum += MathQ.AngleBetween(
                    WorldRot(solvedOrig[f], skel, b), WorldRot(solvedFix[f], skel, b)) * Rad2Deg;
            var mean = sum / n;
            if (mean > worstMean)
            {
                worstMean = mean;
                worstName = skel[b].Name;
            }
        }
        Assert.True(worstMean < 5f,
            $"posed-source solve diverges from the repaired-file ground truth: "
            + $"worst bone '{worstName}' mean {worstMean:F1} deg (was 124 deg before adoption)");
    }

    [Fact] // skipped (silently green) when the local corpus file is not present
    public void Defenses_KeepsExportedPoseAsRest_UpAxisVeto()
    {
        var path = TestUtil.RepoFile("dev", "corpus", "todo", "Defenses.fbx");
        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var target = RetargetTargetSpec.SboxDefault(
            File.ReadAllText(TestUtil.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));
        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = bytes,
            SourceFileName = "Defenses.fbx",
        }, target);
        var clip = result.Clips.First(c => c.Success);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("kept the exported pose as rest"));
    }

    private static Quaternion WorldRot(XForm[] locals, SkeletonModel skeleton, int bone)
    {
        var rot = locals[bone].Rot;
        for (var b = skeleton[bone].ParentIndex; b >= 0; b = skeleton[b].ParentIndex)
            rot = locals[b].Rot * rot;
        return MathQ.Normalize(rot);
    }
}
