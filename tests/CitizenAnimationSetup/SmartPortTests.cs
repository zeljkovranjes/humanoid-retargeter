using System.Numerics;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class SmartPortTests
{
    static string Model(string nodes, string fields = "") => VmdlWriter.Kv3Header
        + "{ rootNode = { _class = \"RootNode\" children = [" + nodes + "] " + fields + " } }";
    const string Mesh = "{ _class = \"RenderMeshList\" children = [{ filename = \"target.dmx\" }] }";

    [Fact]
    public void TransfersWholeSourceSetupButKeepsTargetGeometry()
    {
        var source = Model("""
            { _class = "RenderMeshList" children = [{ filename = "source.dmx" }] },
            { _class = "MaterialGroupList" children = [{ material = "source.vmat" }] },
            { _class = "AnimationList" children = [{ _class = "Folder" children = [
              { _class = "AnimFile" name = "walk" source_filename = "walk.dmx" children = [
                { _class = "AnimSubtract" anim_name = "idle" },
                { _class = "AnimEvent" event_class = "AE_FOOTSTEP" event_frame = 7 event_keys = { Foot = "1" } }
              ] }
            ] }] },
            { _class = "AnimConstraintList" children = [{ name = "CopyPinky" }] },
            { _class = "FutureRigExtension" value = 42 }
            """, "anim_graph_name = \"source.vanmgrph\" model_archetype = \"character\"");
        var target = Model(Mesh + """
            , { _class = "MaterialGroupList" children = [{ material = "target.vmat" }] },
            { _class = "PhysicsShapeList" children = [{ radius = 12 }] },
            { _class = "AnimationList" children = [{ name = "old" }] }
            """);
        var result = SmartPortSetup.Apply(target, source, "graphs/ported.vanmgrph");
        Assert.Contains("target.dmx", result);
        Assert.Contains("target.vmat", result);
        Assert.DoesNotContain("source.dmx", result);
        Assert.DoesNotContain("source.vmat", result);
        Assert.DoesNotContain("\"old\"", result);
        Assert.Equal("graphs/ported.vanmgrph", StockAnimationGraph.GraphName(result));
        foreach (var category in new[] { "AnimationList", "AnimConstraintList", "FutureRigExtension" })
            Assert.True(KvValue.DeepEquals(Node(source, category), Node(result, category)));
        Assert.True(KvValue.DeepEquals(Node(target, "PhysicsShapeList"), Node(result, "PhysicsShapeList")));
    }

    [Fact]
    public void DifferentSourceUnitsAreRejectedEvenForMatchingCompiledBones()
    {
        var scaled = Model(Mesh + ", { _class = \"ModelModifierList\" children = [{ scale = 0.3937 }] }");
        Assert.Throws<InvalidOperationException>(() => SmartPortSetup.Apply(scaled, Model(Mesh), "x.vanmgrph"));
    }

    [Fact]
    public void BaseModelsAndMissingMeshOrGraphAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => SmartPortSetup.Apply(Model(Mesh, "base_model_name = \"base.vmdl\""), Model(Mesh), "x.vanmgrph"));
        Assert.Throws<InvalidOperationException>(() => SmartPortSetup.Apply(Model(""), Model(Mesh), "x.vanmgrph"));
        Assert.Throws<InvalidOperationException>(() => SmartPortSetup.Apply(Model(Mesh), Model(Mesh), ""));
    }

    [Fact]
    public void RebaseChangesOnlyRecoveredPathsIncludingArrayReferences()
    {
        var input = Model(Mesh + ", { _class = \"Other\" refs = [\"target.dmx\", \"installed.vmat\"] }");
        var output = SmartPortSetup.Rebase(input, new Dictionary<string, string> { ["target.dmx"] = "port/target.dmx" });
        Assert.Equal(2, output.Split("port/target.dmx").Length - 1);
        Assert.Contains("installed.vmat", output);
    }

    static SkeletonModel Rig(float length = 1, string? parent = "root", bool extra = false)
    {
        var bones = new List<BoneDefinition> { new("root", null, XForm.Identity), new("arm", parent, new XForm(new Vector3(length, 0, 0), Quaternion.Identity)) };
        if (extra) bones.Add(new("eye", "root", XForm.Identity));
        return SkeletonModel.Create(bones);
    }

    [Fact]
    public void CompatibilityChecksWholeHierarchyAndBindNotJustNames()
    {
        Assert.Null(SmartPortSetup.CompatibilityError(Rig(extra: true), Rig()));
        Assert.Contains("missing", SmartPortSetup.CompatibilityError(Rig(), Rig(extra: true)));
        Assert.Contains("parent", SmartPortSetup.CompatibilityError(Rig(parent: null), Rig()));
        Assert.Contains("bind pose", SmartPortSetup.CompatibilityError(Rig(2), Rig()));
        Assert.Null(SmartPortSetup.CompatibilityError(Rig(1.001f), Rig()));
    }

    static KvObject Node(string model, string category)
        => ((KvArray)((KvObject)((KvObject)Kv3.Parse(model).Root)["rootNode"])["children"]).Items.OfType<KvObject>().Single(n => n.GetString("_class") == category);
}
