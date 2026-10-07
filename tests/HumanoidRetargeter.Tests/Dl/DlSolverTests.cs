using System.Numerics;
using HumanoidRetargeter.Core.Dl;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using Xunit;

namespace HumanoidRetargeter.Tests.Dl;

/// <summary>
/// Quality gate for the DL solver (Milestone 10): on the spike's fixture clip, the SAME
/// port's role trajectories must agree with the verified geometric solver's — the spike
/// measured a mean role cosine of 0.937 against the geometric dump; the gate sits below it
/// (mean ≥ 0.85 across body roles, feet/head ≥ 0.9 individually, hands are the known-weak
/// spot of the pretrained checkpoint).
/// </summary>
public class DlSolverTests
{
    private static readonly Lazy<byte[]> WeightBytes = new(() => File.ReadAllBytes(
        DlFixtures.RepoFile("Assets", "humanoid_retargeter", "dl", "same_v1.weights")));

    private static readonly string[] BodyRoleBones =
    {
        "head", "hand_L", "hand_R", "ankle_L", "ankle_R",
        "ball_L", "ball_R", "arm_lower_L", "arm_lower_R",
        "leg_lower_L", "leg_lower_R",
    };

    private static readonly string[] StrictBones = { "head", "ankle_L", "ankle_R", "ball_L", "ball_R" };

    private static (Clip Dl, Clip Geo, SourceScene Scene) Solve()
    {
        var scene = DlFixtures.FixtureBvh();
        var map = ProfileDetector.Detect(scene.Skeleton) is { } detected
            ? detected.Result
            : throw new InvalidOperationException("fixture should detect as mixamo");
        var rig = DlFixtures.SboxRig.Value;

        var dl = new DlSolver(WeightBytes.Value).Solve(scene, map, rig, new SolveOptions());
        var geo = new GeometricSolver().Solve(scene, map, rig, new SolveOptions());
        return (dl, geo, scene);
    }

    /// <summary>One shared solve pair for this class and the deriver tests.</summary>
    internal static readonly Lazy<(Clip Dl, Clip Geo, SourceScene Scene)> Solved = new(Solve);

    [Fact]
    public void SolveIsFiniteAndFrameAligned()
    {
        var (dl, _, scene) = Solved.Value;
        var clip = scene.Clips[0];

        Assert.Equal(clip.FrameCount, dl.FrameCount);
        Assert.Equal(clip.Fps, dl.Fps);
        foreach (var frame in dl.Frames)
        {
            Assert.Equal(DlFixtures.SboxRig.Value.Skeleton.Count, frame.Length);
            foreach (var xf in frame)
            {
                Assert.True(float.IsFinite(xf.Pos.X) && float.IsFinite(xf.Pos.Y) && float.IsFinite(xf.Pos.Z));
                Assert.True(float.IsFinite(xf.Rot.X) && float.IsFinite(xf.Rot.Y)
                    && float.IsFinite(xf.Rot.Z) && float.IsFinite(xf.Rot.W));
            }
        }
    }

    [Fact]
    public void SolveIsDeterministic()
    {
        var (dl, _, scene) = Solved.Value;
        var map = ProfileDetector.Detect(scene.Skeleton)!.Value.Result;
        var again = new DlSolver(WeightBytes.Value).Solve(
            scene, map, DlFixtures.SboxRig.Value, new SolveOptions());

        for (var f = 0; f < dl.FrameCount; f++)
        {
            for (var b = 0; b < dl.Frames[f].Length; b++)
                Assert.Equal(dl.Frames[f][b], again.Frames[f][b]);
        }
    }

    [Fact]
    public void QualityGate_RoleTrajectoriesAgreeWithGeometricSolver()
    {
        var (dl, geo, _) = Solved.Value;
        var rig = DlFixtures.SboxRig.Value;
        var skeleton = rig.Skeleton;
        var frames = Math.Min(dl.FrameCount, geo.FrameCount);

        var pelvis = skeleton.IndexOf("pelvis");
        var head = skeleton.IndexOf("head");
        Assert.True(pelvis >= 0 && head >= 0);

        // World positions per frame for both solves.
        var dlPos = WorldPositions(skeleton, dl, frames);
        var geoPos = WorldPositions(skeleton, geo, frames);

        // Per-side scale proxy (mean pelvis→head distance) for scale-invariant comparison.
        var dlScale = MeanDistance(dlPos, pelvis, head, frames);
        var geoScale = MeanDistance(geoPos, pelvis, head, frames);
        Assert.True(dlScale > 1f && geoScale > 1f);

        var report = new System.Text.StringBuilder();
        var cosines = new Dictionary<string, float>();
        foreach (var bone in BodyRoleBones)
        {
            var index = skeleton.IndexOf(bone);
            Assert.True(index >= 0, $"rig has no bone '{bone}'");
            var sum = 0f;
            for (var f = 0; f < frames; f++)
            {
                var a = (dlPos[f][index] - dlPos[f][pelvis]) / dlScale;
                var b = (geoPos[f][index] - geoPos[f][pelvis]) / geoScale;
                var denom = a.Length() * b.Length() + 1e-8f;
                sum += Vector3.Dot(a, b) / denom;
            }
            var cos = sum / frames;
            cosines[bone] = cos;
            report.AppendLine($"{bone}: {cos:0.000}");
        }

        var mean = cosines.Values.Average();
        Assert.True(mean >= 0.85f,
            $"mean role-trajectory cosine {mean:0.000} < 0.85 (spike measured 0.937)\n{report}");
        foreach (var bone in StrictBones)
        {
            Assert.True(cosines[bone] >= 0.9f,
                $"{bone} trajectory cosine {cosines[bone]:0.000} < 0.90\n{report}");
        }
    }

    private static Vector3[][] WorldPositions(
        HumanoidRetargeter.Core.Skeleton.Skeleton skeleton, Clip clip, int frames)
    {
        var result = new Vector3[frames][];
        for (var f = 0; f < frames; f++)
        {
            var world = new Pose(clip.Frames[f]).ToWorld(skeleton);
            result[f] = new Vector3[skeleton.Count];
            for (var b = 0; b < skeleton.Count; b++)
                result[f][b] = world[b].Pos;
        }
        return result;
    }

    private static float MeanDistance(Vector3[][] positions, int a, int b, int frames)
    {
        var sum = 0f;
        for (var f = 0; f < frames; f++)
            sum += Vector3.Distance(positions[f][a], positions[f][b]);
        return sum / frames;
    }
}
