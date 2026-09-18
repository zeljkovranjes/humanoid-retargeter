using HumanoidRetargeter.Editor;
using HumanoidRetargeterVrf;
using Xunit;

namespace SmartPort.Parser.Tests;

public class IkTargetMappingTests
{
    [CompiledFixtureFact]
    public void GoalsAreResolvedFromGraphAndChainsNotBoneNamingRules()
    {
        var fixture = Environment.GetEnvironmentVariable("HR_SMART_PORT_FIXTURE")!;
        using var graph = new Resource();
        graph.Read(Path.Combine(fixture, "models/player/mplayer/mplayer_animgraph.vanmgrph_c"));
        var goals = SmartPortIkTargets.Read(Path.Combine(fixture, "models/player/human/frank_mp.vmdl_c"), graph.DataBlock!.ToString()!);
        Assert.Equal("hand_l", goals["hand_L_to_R_ikrule"]);
        Assert.Equal("hand_l", goals["arm_l_iktarget"]);
        Assert.Equal("hand_r", goals["arm_r_iktarget"]);
        Assert.DoesNotContain("hand_L_IK_target", goals.Keys); // Parameter-driven nodes do not animate this placeholder bone.
    }
}
