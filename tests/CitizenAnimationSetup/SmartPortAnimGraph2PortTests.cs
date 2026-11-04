using System.Text.Json.Nodes;
using HumanoidRetargeter.Core.Target;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortAnimGraph2PortTests
{
    const string Graph = """
        { "Version": 1, "Model": "models/human.vmdl", "Skeleton": "models/human.skel",
          "parameters": [ { "Identifier": "p", "Type": "Target", "Name": "ik.foot", "PreviewStartBone": "ankle_L" } ],
          "nodes": [
            { "_class": "PoseResultNode", "Pose": { "Identifier": "ik", "Output": "Result" }, "Identifier": "result" },
            { "_class": "AnimationClipNode", "Loop": true, "DefaultVariationData": { "AnimationName": "clips/walk.anim" },
              "Overrides": [ { "VariationID": "Female", "VariationData": { "AnimationName": "clips/walk_female.anim" } } ], "Identifier": "walk" },
            { "_class": "LookAtNode", "ReferenceBone": "head", "Bones": [ { "Bone": "head", "Weight": 0.6 }, { "Bone": "neck_0", "Weight": 0.4 } ], "Identifier": "look" },
            { "_class": "BoneMaskNode", "BoneName": "upper_body", "Identifier": "mask" },
            { "_class": "ConstBoneTargetNode", "Bone": "foot_L_IK_target", "Identifier": "goal" },
            { "_class": "TargetOffsetNode", "Target": { "Identifier": "goal", "Output": "Target" }, "Identifier": "offset" },
            { "_class": "TwoBoneIKNode", "Input": { "Identifier": "walk", "Output": "Result" }, "EffectorTarget": { "Identifier": "offset", "Output": "Result" },
              "DefaultVariationData": { "EffectorBone": "ankle_L" }, "Overrides": [], "Identifier": "ik" },
            { "_class": "TwoBoneIKNode", "EffectorTarget": { "Identifier": "param", "Output": "Value" },
              "DefaultVariationData": { "EffectorBone": "hand_R" }, "Identifier": "ik2" },
            { "_class": "StateMachineNode", "States": [ { "Name": "head", "Graph": { "nodes": [
              { "_class": "AnimationPoseNode", "DefaultVariationData": { "AnimationName": "clips/idle.anim" }, "Identifier": "nested" } ] } } ], "Identifier": "machine" }
          ],
          "variations": [ { "ID": "Female", "Model": "models/female.vmdl" } ] }
        """;

    static readonly Dictionary<string, string> Bones = new()
    {
        ["head"] = "mixamorig_Head", ["neck_0"] = "mixamorig_Neck", ["ankle_L"] = "mixamorig_LeftFoot", ["foot_L_IK_target"] = "foot_L_IK_target",
    };

    static readonly Dictionary<string, string> Animations = new()
    {
        ["clips/walk.anim"] = "port/clips/walk.anim", ["clips/idle.anim"] = "port/clips/idle.anim",
    };

    static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var (_, value) in obj)
                foreach (var child in Objects(value)) yield return child;
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                foreach (var child in Objects(item)) yield return child;
    }

    static JsonObject Node(JsonNode root, string id) => Objects(root).Single(o => o["_class"] is not null && o["Identifier"]?.GetValue<string>() == id);

    [Fact]
    public void RebasePointsTheGraphAtTheTargetsModelSkeletonClipsAndBones()
    {
        var root = JsonNode.Parse(SmartPortAnimGraph2Port.Rebase(Graph, "port/out.vmdl", "port/out.skel", Animations, Bones))!;

        Assert.Equal("port/out.vmdl", root["Model"]!.GetValue<string>());
        Assert.Equal("port/out.skel", root["Skeleton"]!.GetValue<string>());
        Assert.Null(root["variations"]); // the copy drives one model, so the Female variation and its overrides go
        Assert.Empty(Node(root, "walk")["Overrides"]!.AsArray());
        Assert.Equal("port/clips/walk.anim", Node(root, "walk")["DefaultVariationData"]!["AnimationName"]!.GetValue<string>());
        Assert.Equal("port/clips/idle.anim", Node(root, "nested")["DefaultVariationData"]!["AnimationName"]!.GetValue<string>());
        var look = Node(root, "look");
        Assert.Equal("mixamorig_Head", look["ReferenceBone"]!.GetValue<string>());
        Assert.Equal(new[] { "mixamorig_Head", "mixamorig_Neck" }, look["Bones"]!.AsArray().Select(b => b!["Bone"]!.GetValue<string>()));
        Assert.Equal("mixamorig_LeftFoot", Node(root, "ik")["DefaultVariationData"]!["EffectorBone"]!.GetValue<string>());
        Assert.Equal("mixamorig_LeftFoot", root["parameters"]![0]!["PreviewStartBone"]!.GetValue<string>());
        // Only bone properties are renamed: a state that happens to share a bone's name, and mask names, stay
        Assert.Equal("head", Node(root, "machine")["States"]![0]!["Name"]!.GetValue<string>());
        Assert.Equal("upper_body", Node(root, "mask")["BoneName"]!.GetValue<string>());
    }

    [Fact]
    public void RebaseFailsOnAnAnimationWithoutAPort()
    {
        var partial = new Dictionary<string, string> { ["clips/walk.anim"] = "port/clips/walk.anim" };
        var error = Assert.Throws<InvalidOperationException>(() => SmartPortAnimGraph2Port.Rebase(Graph, "out.vmdl", "out.skel", partial, Bones));
        Assert.Contains("clips/idle.anim", error.Message);
    }

    [Fact]
    public void IkTargetsFollowConstantBoneTargetsThroughOffsets()
    {
        var goals = SmartPortAnimGraph2Port.IkTargets(Graph);

        Assert.Equal(new Dictionary<string, string> { ["foot_L_IK_target"] = "ankle_L" }, goals); // a parameter target names no bone
    }

    const string CompiledSkeleton = AnimGraph2Sources.GenericHeader + """

        {
        	m_boneIDs = [ "pelvis", "head" ]
        	m_maskDefinitions =
        	[
        		{
        			m_ID = "upper_body"
        			m_primaryWeightList = { m_skeletonName = "models/human.skel" m_defaultFloatChannelWeight = 0.5 m_boneIDs = [ "pelvis", "head", "jawOpen" ] m_weights = [ 0.0, 1.0, 1.0 ] }
        			m_secondaryWeightLists = [ ]
        		},
        		{
        			m_ID = "eyes"
        			m_primaryWeightList = { m_defaultFloatChannelWeight = 1.0 m_boneIDs = [ "eye_L" ] m_weights = [ 1.0 ] }
        		},
        	]
        	m_flGlobalScale = 0.3937
        }
        """;

    [Fact]
    public void SkeletonKeepsEachMaskOnTheTargetsBones()
    {
        var text = SmartPortAnimGraph2Port.Skeleton("port/skeleton.dmx", CompiledSkeleton,
            new Dictionary<string, string> { ["pelvis"] = "mixamorig_Hips", ["head"] = "mixamorig_Head" }, new[] { "mixamorig_Hips", "mixamorig_Head" });
        var root = (KvObject)Kv3.Parse(text).Root;

        Assert.Equal("port/skeleton.dmx", root.GetString("m_sourceFilename"));
        Assert.Equal(1.0, ((KvDouble)root["m_flGlobalScale"]).Value); // the DMX is in engine units already
        var masks = ((KvArray)root["m_boneMaskSetDefinitions"]).Items.Cast<KvObject>().ToArray();
        Assert.Equal(new[] { "upper_body", "eyes" }, masks.Select(m => m.GetString("m_ID")));
        var upper = (KvObject)masks[0]["m_primaryWeightList"];
        Assert.Equal(new[] { "mixamorig_Hips", "mixamorig_Head" }, ((KvArray)upper["m_boneIDs"]).Items.Cast<KvString>().Select(s => s.Value)); // no morph channel
        Assert.Equal(new[] { 0.0, 1.0 }, ((KvArray)upper["m_weights"]).Items.Cast<KvDouble>().Select(d => d.Value));
        Assert.Equal(0.5, ((KvDouble)upper["m_defaultFloatChannelWeight"]).Value);
        // A mask the target has no bone for keeps its name, weighting nothing, so graph nodes naming it still compile
        var eyes = (KvObject)masks[1]["m_primaryWeightList"];
        Assert.Equal(new[] { "mixamorig_Hips" }, ((KvArray)eyes["m_boneIDs"]).Items.Cast<KvString>().Select(s => s.Value));
        Assert.Equal(new[] { 0.0 }, ((KvArray)eyes["m_weights"]).Items.Cast<KvDouble>().Select(d => d.Value));
    }

    [Fact]
    public void AdditiveClipsStayAdditiveAgainstAnIdentityBase()
    {
        var plain = (KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("port/walk.dmx", "port/out.skel", null)).Root;
        var additive = (KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("port/lean.dmx", "port/out.skel", "port/base.dmx")).Root;

        Assert.Equal("port/walk.dmx", plain.GetString("m_sourceFilename"));
        Assert.Equal("port/out.skel", plain.GetString("m_animationSkeletonName"));
        Assert.Null(plain.GetOrNull("m_additiveType"));
        Assert.Equal(SmartPortAnimGraph2Port.AdditiveType, additive.GetString("m_additiveType"));
        Assert.Equal("port/base.dmx", additive.GetString("m_additiveBaseFilename"));
    }

    const string CompiledClip = AnimGraph2Sources.GenericHeader + """

        {
        	m_skeleton = resource:"models/human.skel"
        	m_nNumFrames = 27
        	m_events =
        	[
        		{ m_startTime = 0.1925 m_duration = 0.0 m_phase = 0 _class = "CFootEvent" },
        		{ m_name = "AE_FOOTSTEP" m_startTime = 0.230769 m_duration = 0.0 m_eventData = { attachment = "foot_L" Foot = 0 volume = 0.75 } _class = "CGenericEvent" },
        		{ m_startTime = 0.73 m_duration = 0.0 m_phase = 2 _class = "CFootEvent" },
        	]
        	m_syncTrack = { m_nStartEventOffset = 0 m_syncEvents = [ { m_ID = 1377454944 m_startTime = 0.1925 m_duration = 0.5375 }, { m_ID = 676906460 m_startTime = 0.73 m_duration = 0.4625 } ] }
        }
        """;

    [Fact]
    public void ClipKeepsTheCompiledClipsEventsAsFrameTracks()
    {
        var root = (KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("port/walk.dmx", "port/out.skel", null, CompiledClip)).Root;
        var tracks = ((KvArray)root["m_eventTracks"]).Items.Cast<KvObject>().ToArray();

        Assert.Equal(new[] { "CFootEvent", "CGenericEvent" }, tracks.Select(t => t.GetString("m_eventClassName")));
        var feet = ((KvArray)tracks[0]["m_events"]).Items.Cast<KvObject>().ToArray();
        Assert.Equal(new[] { 5.005, 18.98 }, feet.Select(e => Math.Round(((KvDouble)e["m_startTime"]).Value, 3))); // 0..1 becomes frames 0..26
        var step = ((KvArray)tracks[1]["m_events"]).Items.Cast<KvObject>().Single();
        Assert.Equal("AE_FOOTSTEP", step.GetString("m_name"));
        Assert.Equal("foot_L", ((KvObject)step["m_eventData"]).GetString("attachment"));
        Assert.Equal(6.0, Math.Round(((KvDouble)step["m_startTime"]).Value, 3));
        Assert.Null(((KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("a.dmx", "b.skel", null)).Root).GetOrNull("m_eventTracks"));
        // The foot events are where the sync events are: that track is the sync track, with the sync IDs
        Assert.True(((KvBool)tracks[0]["m_bIsSyncTrack"]).Value);
        Assert.Equal(new long[] { 1377454944, 676906460 }, feet.Select(e => ((KvLong)e["m_ID"]).Value));
        Assert.False(((KvBool)tracks[1]["m_bIsSyncTrack"]).Value);
    }

    [Fact]
    public void ClipExtractsRootMotionAlongTheSourceAxes()
    {
        var root = (KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("port/roll.dmx", "port/out.skel", null, rootMotion: (true, true, false, false))).Root;

        Assert.True(((KvBool)root["m_bExtractMotion"]).Value);
        Assert.Equal("", root.GetString("m_motionExtractionBone")); // the root bone, which the DMX's root motion moves
        Assert.Equal(new[] { true, true, false, false },
            new[] { "TX", "TY", "TZ", "RZ" }.Select(axis => ((KvBool)root["m_bExtractMotion" + axis]).Value));
        Assert.Null(((KvObject)Kv3.Parse(SmartPortAnimGraph2Port.Clip("a.dmx", "b.skel", null)).Root).GetOrNull("m_bExtractMotion"));
    }
}
