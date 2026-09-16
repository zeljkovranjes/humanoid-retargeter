using System.Numerics;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class AnimationWorkflowTests
{
    const string Graph = VmdlWriter.Kv3Header + "{ _class = \"CAnimationGraph\" m_previewModels = [\"stock.vmdl\"] m_boneMergeModels = [ { m_name = \"hat\" } ] nodes = [ { _class = \"CSequenceAnimNode\" m_sequenceName = \"Walk_N\" speed = 1.25 }, { m_sequenceName = \"Walk_S\" }, { m_sequenceName = \"WalkFast_N\" }, { m_sequenceName = \"Run_N_m\" } ] connections = [ 1, 2 ] }";

    [Fact]
    public void CopyPreservesGraphLogicAndUsesOnlyCustomPreview()
    {
        var original = (KvObject)Kv3.Parse(Graph).Root;
        var copy = (KvObject)Kv3.Parse(StockAnimationGraph.CopyForModel(Graph, "custom/body.vmdl")).Root;
        Assert.True(KvValue.DeepEquals(original["nodes"], copy["nodes"]));
        Assert.True(KvValue.DeepEquals(original["connections"], copy["connections"]));
        Assert.Empty(((KvArray)copy["m_boneMergeModels"]).Items);
        Assert.Equal("custom/body.vmdl", ((KvString)Assert.Single(((KvArray)copy["m_previewModels"]).Items)).Value);
    }

    [Fact]
    public void ReplacingAgainOnlyChangesSelectedSlotsReferences()
    {
        var slot = StockAnimationGraph.Slots.Single(s => s.Id == "walk_n");
        var first = StockAnimationGraph.Replace(Graph, slot, slot.ReplacementPrefix + "one", "custom.vmdl", out var count);
        Assert.Equal(1, count);
        var second = StockAnimationGraph.Replace(first, slot, slot.ReplacementPrefix + "two", "custom.vmdl", out count);
        Assert.Equal(1, count);
        Assert.DoesNotContain(slot.ReplacementPrefix + "one", second);
        Assert.Contains("Walk_S", second);
        Assert.Contains("WalkFast_N", second);
        Assert.Contains("Run_N_m", second);
        Assert.Contains("1.25", second);
        Assert.True(KvValue.DeepEquals(((KvObject)Kv3.Parse(Graph).Root)["connections"], ((KvObject)Kv3.Parse(second).Root)["connections"]));
    }

    [Fact]
    public void HumanMaleForwardRunAliasIsReplaced()
    {
        var slot = StockAnimationGraph.Slots.Single(s => s.Id == "run_n");
        var result = StockAnimationGraph.Replace(Graph, slot, slot.ReplacementPrefix + "test", "custom.vmdl", out var count);
        Assert.Equal(1, count);
        Assert.DoesNotContain("Run_N_m", result);
    }

    [Fact]
    public void MissingSlotFailsWithoutFallingBackToUnrelatedNodes()
        => Assert.Throws<InvalidOperationException>(() => StockAnimationGraph.Replace(Graph, StockAnimationGraph.Slots[0], "idle", "custom.vmdl", out _));

    [Theory]
    [InlineData("", "graphs/body.vanmgrph")]
    [InlineData("models/custom/", "models/custom/graphs/body.vanmgrph")]
    public void GraphLivesBesideItsModel(string folder, string expected)
        => Assert.Equal(expected, StockAnimationGraph.GraphPath(folder, "body"));

    [Fact]
    public void ActualGraphEscapedQuestionMarkRoundTrips()
    {
        var doc = Kv3.Parse(VmdlWriter.Kv3Header + "{ comment = \"why\\?\" }");
        Assert.Equal("why?", ((KvObject)doc.Root).GetString("comment"));
        Assert.True(KvValue.DeepEquals(doc.Root, Kv3.Parse(Kv3.Serialize(doc)).Root));
    }

    [Theory]
    [InlineData("Walk_N", "walk_n")]
    [InlineData("Walk_North-East_01", "walk_ne")]
    [InlineData("RunForwardLeft", "run_nw")]
    [InlineData("Crouch Walk backward right", "crouchwalk_se")]
    [InlineData("jog_SW", "run_sw")]
    [InlineData("walk south west", "walk_sw")]
    [InlineData("walk South", "walk_s")]
    [InlineData("Walk East", "walk_e")]
    [InlineData("Walk left", "walk_w")]
    [InlineData("KB_Cross-L_S", null)]
    [InlineData("Walk_N_S", null)]
    [InlineData("Walk_turn", null)]
    [InlineData("Walk", null)]
    public void NameSuggestionsRespectWordBoundariesAndAmbiguity(string name, string? expected)
        => Assert.Equal(expected, LocomotionSuggestion.Detect(name)?.SlotId);

    private static (SkeletonModel Skeleton, MappingResult Map) Rig()
    {
        var roles = new[] { BoneRole.Hips, BoneRole.UpperLegL, BoneRole.UpperLegR, BoneRole.UpperArmL, BoneRole.UpperArmR, BoneRole.FootL, BoneRole.FootR };
        var positions = new[] { new Vector3(0, 100, 0), new Vector3(10, 90, 0), new Vector3(-10, 90, 0), new Vector3(20, 150, 0), new Vector3(-20, 150, 0), new Vector3(10, 0, 0), new Vector3(-10, 0, 0) };
        var rig = SkeletonModel.Create(roles.Select((r, i) => new BoneDefinition(r.ToString(), null, new XForm(positions[i], Quaternion.Identity))).ToArray());
        var map = new MappingResult("test", MappingSource.Manual);
        for (var i = 0; i < roles.Length; i++) map.RoleToBone[roles[i]] = i;
        return (rig, map);
    }

    [Theory]
    [InlineData(0, 100, "N")]
    [InlineData(-100, 100, "NE")]
    [InlineData(-100, 0, "E")]
    [InlineData(-100, -100, "SE")]
    [InlineData(0, -100, "S")]
    [InlineData(100, -100, "SW")]
    [InlineData(100, 0, "W")]
    [InlineData(100, 100, "NW")]
    [InlineData(0, 0, null)]
    [InlineData(0, 1, null)]
    public void TravelSuggestionsAreCharacterRelative(float x, float z, string? direction)
    {
        var (rig, map) = Rig();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f);
        var frames = Enumerable.Range(0, 31).Select(i => rig.RestWorld.Select(t => new XForm(Vector3.Transform(t.Pos + new Vector3(x, 0, z) * (i / 30f), rotation), rotation)).ToArray()).ToList();
        Assert.Equal(direction, LocomotionSuggestion.Detect("walk", rig, map, new Clip("walk", 30, true, frames))?.Direction);
    }

    [Fact]
    public void TurningMotionIsNotAssignedASingleDirection()
    {
        var (rig, map) = Rig();
        var frames = Enumerable.Range(0, 31).Select(i =>
        {
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i / 30f);
            return rig.RestWorld.Select(t => new XForm(Vector3.Transform(t.Pos, rotation) + new Vector3(0, 0, i * 4), rotation)).ToArray();
        }).ToList();
        Assert.Null(LocomotionSuggestion.Detect("walk", rig, map, new Clip("walk", 30, true, frames)));
    }

    [Theory]
    [InlineData(30, 1, false)]
    [InlineData(60, 1, false)]
    [InlineData(120, 1, false)]
    [InlineData(30, 0.3937f, true)]
    public void InPlaceSlidingFootHasOneEventPerLiftAtAnyRateOrAxis(int fps, float scale, bool zUp)
    {
        var rig = SkeletonModel.Create(new[] { new BoneDefinition("left", null, XForm.Identity), new BoneDefinition("right", null, XForm.Identity) });
        var frames = Enumerable.Range(0, fps * 2).Select(i =>
        {
            float t = i / (float)fps;
            var height = t >= 0.3f && t < 0.8f ? 12f : 0f;
            var position = new Vector3(-80 * t, height, 0) * scale;
            if (zUp) position = new Vector3(position.X, position.Z, position.Y);
            return new[] { new XForm(position, Quaternion.Identity), XForm.Identity };
        }).ToList();
        var events = FootstepEvents.Generate(frames, rig, new FootChain { Hip = 0, Knee = 0, Ankle = 0 }, new FootChain { Hip = 1, Knee = 1, Ankle = 1 }, zUp ? Vector3.UnitZ : Vector3.UnitY, fps,
            new FootPlantOptions { HeightThresholdCm = 4 * scale, SpeedThresholdCmPerSec = 8 * scale, MinPlantFrames = (int)MathF.Ceiling(fps * .1f) });
        var step = Assert.Single(events);
        Assert.Equal("0", step.Foot);
        Assert.InRange(step.Frame / (float)fps, .8f, .85f);
    }

    [Fact]
    public void GroundedSlidingAndHeightJitterDoNotProduceSteps()
    {
        var rig = SkeletonModel.Create(new[] { new BoneDefinition("foot", null, XForm.Identity) });
        var frames = Enumerable.Range(0, 90).Select(i => new[] { new XForm(new Vector3(i, i % 2 * .1f, 0), Quaternion.Identity) }).ToList();
        var foot = new FootChain { Hip = 0, Knee = 0, Ankle = 0 };
        Assert.Empty(FootstepEvents.Generate(frames, rig, foot, foot, Vector3.UnitY, 30));
    }
}
