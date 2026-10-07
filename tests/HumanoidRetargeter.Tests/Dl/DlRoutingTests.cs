using HumanoidRetargeter.Core.Mapping;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Dl;

/// <summary>
/// Facade routing of the DL solver (design §10): <see cref="RetargetRequest.Solver"/> =
/// <see cref="SolverKind.DeepLearning"/> runs the SAME solver when the spec carries
/// weights, and fails per-clip with a clear error when it does not.
/// </summary>
public class DlRoutingTests
{
    private static byte[] FixtureBytes() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "fixtures", "bvh", "bvhpython_test_freebvh.bvh"));

    private static string RigJson() => File.ReadAllText(
        DlFixtures.RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"));

    [Fact]
    public void DeepLearningRequestSolvesThroughTheFacade()
    {
        var weights = File.ReadAllBytes(
            DlFixtures.RepoFile("Assets", "data", "humanoid_retargeter", "dl", "same_v1.weights"));
        var target = RetargetTargetSpec.SboxDefault(RigJson(), weights);

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = FixtureBytes(),
            SourceFileName = "bvhpython_test_freebvh.bvh",
            Solver = SolverKind.DeepLearning,
        }, target);

        var clip = Assert.Single(result.Clips);
        Assert.True(clip.Success, clip.Error);
        Assert.NotNull(clip.SolvedFrames);
        Assert.True(clip.SolvedFrames!.Count > 0);
        Assert.Contains(clip.Mapping!.Notes, n => n.Contains("Deep-learning solver"));
    }

    [Fact]
    public void DeepLearningWithoutWeightsFailsPerClipWithClearError()
    {
        var target = RetargetTargetSpec.SboxDefault(RigJson()); // no weights

        var result = Retargeter.Convert(new RetargetRequest
        {
            SourceData = FixtureBytes(),
            SourceFileName = "bvhpython_test_freebvh.bvh",
            Solver = SolverKind.DeepLearning,
        }, target);

        var clip = Assert.Single(result.Clips);
        Assert.False(clip.Success);
        Assert.Contains("DlWeights", clip.Error);
    }
}
