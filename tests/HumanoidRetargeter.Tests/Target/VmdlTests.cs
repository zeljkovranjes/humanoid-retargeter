using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Tests.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

public class VmdlTests
{
    private static string ReadKv3Fixture(string name)
        => File.ReadAllText(SkeletonTests.FixturePath(Path.Combine("kv3", name)));

    // ================================================================ Kv3 parser/writer

    [Theory]
    [InlineData("citizen_animationlist.vmdl_prefab")]
    [InlineData("citizen.vmdl")]
    [InlineData("citizen_human_male.vmdl")]
    public void Kv3_ShippedVmdl_RoundTripsSemantically(string fixture)
    {
        var text = ReadKv3Fixture(fixture);
        var first = Kv3.Parse(text);
        var serialized = Kv3.Serialize(first);
        var second = Kv3.Parse(serialized);

        Assert.Equal(first.Header, second.Header);
        Assert.True(KvValue.DeepEquals(first.Root, second.Root),
            $"Round-tripped tree of {fixture} is not semantically equal to the original.");

        // And a third generation must be byte-identical (the writer is a fixed point).
        Assert.Equal(serialized, Kv3.Serialize(second));
    }

    [Fact]
    public void Kv3_PreservesHeaderVerbatim()
    {
        var text = ReadKv3Fixture("citizen.vmdl");
        var doc = Kv3.Parse(text);
        Assert.Equal(
            "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->",
            doc.Header);
        Assert.StartsWith(doc.Header + "\r\n", Kv3.Serialize(doc));
    }

    [Fact]
    public void Kv3_ParsesScalarKinds_AndDistinguishesIntFromDouble()
    {
        var doc = Kv3.Parse(
            "<!-- kv3 test -->\n{\n\ta = 1\n\tb = 1.0\n\tc = -0.25\n\td = true\n\te = false\n\tf = null\n\tg = \"hi\"\n}");
        var root = Assert.IsType<KvObject>(doc.Root);
        Assert.Equal(1L, Assert.IsType<KvLong>(root["a"]).Value);
        Assert.Equal(1.0, Assert.IsType<KvDouble>(root["b"]).Value);
        Assert.Equal(-0.25, Assert.IsType<KvDouble>(root["c"]).Value);
        Assert.True(Assert.IsType<KvBool>(root["d"]).Value);
        Assert.False(Assert.IsType<KvBool>(root["e"]).Value);
        Assert.IsType<KvNull>(root["f"]);
        Assert.Equal("hi", Assert.IsType<KvString>(root["g"]).Value);

        // KvLong(1) and KvDouble(1.0) are distinct kinds.
        Assert.False(KvValue.DeepEquals(root["a"], root["b"]));
    }

    [Fact]
    public void Kv3_ParsesEscapes_AndWriterReEscapes()
    {
        var doc = Kv3.Parse("<!-- kv3 test -->\n{\n\tnote = \"don\\'t\\nstop\"\n}");
        var root = Assert.IsType<KvObject>(doc.Root);
        Assert.Equal("don't\nstop", Assert.IsType<KvString>(root["note"]).Value);

        var serialized = Kv3.Serialize(doc);
        Assert.Contains("don\\'t\\nstop", serialized);
        Assert.True(KvValue.DeepEquals(doc.Root, Kv3.Parse(serialized).Root));
    }

    [Fact]
    public void Kv3_ParsesMultiLineStrings_BareIdentifiers_TrailingCommas_Comments()
    {
        var doc = Kv3.Parse(
            "<!-- kv3 test -->\n{\n" +
            "\tml = \"\"\"x\ny\"\"\"\n" +
            "\tbare = Single // trailing comment\n" +
            "\tarr = [ 1, 2, ]\n" +
            "}");
        var root = Assert.IsType<KvObject>(doc.Root);
        Assert.Equal("x\ny", Assert.IsType<KvString>(root["ml"]).Value);
        Assert.Equal("Single", Assert.IsType<KvString>(root["bare"]).Value);
        Assert.Equal(2, Assert.IsType<KvArray>(root["arr"]).Items.Count);

        // Semantic round-trip survives re-serialization of these constructs.
        Assert.True(KvValue.DeepEquals(doc.Root, Kv3.Parse(Kv3.Serialize(doc)).Root));
    }

    [Fact]
    public void Kv3_FailsLoudly_OnGarbageAndDuplicateKeys()
    {
        Assert.Throws<FormatException>(() => Kv3.Parse("not a kv3 file"));
        Assert.Throws<FormatException>(() => Kv3.Parse("<!-- h -->\n{ a = @weird }"));
        Assert.Throws<FormatException>(() => Kv3.Parse("<!-- h -->\n{ a = 1 a = 2 }"));
        Assert.Throws<FormatException>(() => Kv3.Parse("<!-- h -->\n{ a = 1 } trailing"));
    }

    // ================================================================ VmdlWriter

    private static List<AnimEntry> GoldenAnims()
        => new()
        {
            new AnimEntry
            {
                Name = "walk",
                SourceFilename = "animations/walk.dmx",
                Looping = true,
                ExtractMotion = false,
            },
            new AnimEntry
            {
                Name = "jump",
                SourceFilename = "animations/jump.dmx",
                Looping = false,
                ExtractMotion = true,
            },
        };

    private const string AnimFileTail =
        "\t\t\t\t\t\tactivity_name = \"\"\n" +
        "\t\t\t\t\t\tactivity_weight = 1\n" +
        "\t\t\t\t\t\tweight_list_name = \"\"\n" +
        "\t\t\t\t\t\tfade_in_time = 0.2\n" +
        "\t\t\t\t\t\tfade_out_time = 0.2\n";

    // Hand-written against the field set of m0_test.vmdl (compile-proven in M0) and the
    // shipped citizen vmdl layout conventions.
    private const string StandaloneGolden =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
        "{\n" +
        "\trootNode = \n" +
        "\t{\n" +
        "\t\t_class = \"RootNode\"\n" +
        "\t\tchildren = \n" +
        "\t\t[\n" +
        "\t\t\t{\n" +
        "\t\t\t\t_class = \"ModelModifierList\"\n" +
        "\t\t\t\tchildren = \n" +
        "\t\t\t\t[\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t_class = \"ModelModifier_ScaleAndMirror\"\n" +
        "\t\t\t\t\t\tscale = 0.3937\n" +
        "\t\t\t\t\t\tmirror_x = false\n" +
        "\t\t\t\t\t\tmirror_y = false\n" +
        "\t\t\t\t\t\tmirror_z = false\n" +
        "\t\t\t\t\t\tflip_bone_forward = false\n" +
        "\t\t\t\t\t\tswap_left_and_right_bones = false\n" +
        "\t\t\t\t\t},\n" +
        "\t\t\t\t]\n" +
        "\t\t\t},\n" +
        "\t\t\t{\n" +
        "\t\t\t\t_class = \"AnimationList\"\n" +
        "\t\t\t\tchildren = \n" +
        "\t\t\t\t[\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t_class = \"AnimFile\"\n" +
        "\t\t\t\t\t\tname = \"walk\"\n" +
        AnimFileTail +
        "\t\t\t\t\t\tlooping = true\n" +
        "\t\t\t\t\t\tdelta = false\n" +
        "\t\t\t\t\t\tworldSpace = false\n" +
        "\t\t\t\t\t\thidden = false\n" +
        "\t\t\t\t\t\tanim_markup_ordered = false\n" +
        "\t\t\t\t\t\tdisable_compression = false\n" +
        "\t\t\t\t\t\tdisable_interpolation = false\n" +
        "\t\t\t\t\t\tenable_scale = false\n" +
        "\t\t\t\t\t\tsource_filename = \"animations/walk.dmx\"\n" +
        "\t\t\t\t\t\tstart_frame = -1\n" +
        "\t\t\t\t\t\tend_frame = -1\n" +
        "\t\t\t\t\t\tframerate = -1.0\n" +
        "\t\t\t\t\t\ttake = 0\n" +
        "\t\t\t\t\t\treverse = false\n" +
        "\t\t\t\t\t},\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t_class = \"AnimFile\"\n" +
        "\t\t\t\t\t\tname = \"jump\"\n" +
        "\t\t\t\t\t\tchildren = \n" +
        "\t\t\t\t\t\t[\n" +
        "\t\t\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t\t\t_class = \"ExtractMotion\"\n" +
        "\t\t\t\t\t\t\t\textract_tx = true\n" +
        "\t\t\t\t\t\t\t\textract_ty = true\n" +
        "\t\t\t\t\t\t\t\textract_tz = false\n" +
        "\t\t\t\t\t\t\t\textract_rz = false\n" +
        "\t\t\t\t\t\t\t\tlinear = true\n" +
        "\t\t\t\t\t\t\t\tquadratic = false\n" +
        "\t\t\t\t\t\t\t\troot_bone_name = \"pelvis\"\n" +
        "\t\t\t\t\t\t\t\tmotion_type = \"Single\"\n" +
        "\t\t\t\t\t\t\t},\n" +
        "\t\t\t\t\t\t]\n" +
        AnimFileTail +
        "\t\t\t\t\t\tlooping = false\n" +
        "\t\t\t\t\t\tdelta = false\n" +
        "\t\t\t\t\t\tworldSpace = false\n" +
        "\t\t\t\t\t\thidden = false\n" +
        "\t\t\t\t\t\tanim_markup_ordered = false\n" +
        "\t\t\t\t\t\tdisable_compression = false\n" +
        "\t\t\t\t\t\tdisable_interpolation = false\n" +
        "\t\t\t\t\t\tenable_scale = false\n" +
        "\t\t\t\t\t\tsource_filename = \"animations/jump.dmx\"\n" +
        "\t\t\t\t\t\tstart_frame = -1\n" +
        "\t\t\t\t\t\tend_frame = -1\n" +
        "\t\t\t\t\t\tframerate = -1.0\n" +
        "\t\t\t\t\t\ttake = 0\n" +
        "\t\t\t\t\t\treverse = false\n" +
        "\t\t\t\t\t},\n" +
        "\t\t\t\t]\n" +
        "\t\t\t\tdefault_root_bone_name = \"pelvis\"\n" +
        "\t\t\t},\n" +
        "\t\t]\n" +
        "\t\tmodel_archetype = \"\"\n" +
        "\t\tprimary_associated_entity = \"\"\n" +
        "\t\tanim_graph_name = \"\"\n" +
        "\t\tbase_model_name = \"models/citizen/citizen.vmdl\"\n" +
        "\t}\n" +
        "}\n";

    [Fact]
    public void GenerateStandalone_MatchesGolden()
    {
        var actual = VmdlWriter.GenerateStandalone(
            "models/citizen/citizen.vmdl", GoldenAnims(), 0.3937f, "pelvis");
        Assert.Equal(StandaloneGolden.Replace("\n", "\r\n"), actual);
    }

    [Fact]
    public void GenerateStandalone_ScaleOne_OmitsModelModifierList()
    {
        var text = VmdlWriter.GenerateStandalone("", GoldenAnims(), 1.0f, "pelvis");
        Assert.DoesNotContain("ModelModifierList", text);
        Assert.DoesNotContain("ModelModifier_ScaleAndMirror", text);

        var doc = Kv3.Parse(text);
        var rootNode = (KvObject)((KvObject)doc.Root)["rootNode"];
        var children = (KvArray)rootNode["children"];
        var animList = Assert.Single(children.Items.OfType<KvObject>());
        Assert.Equal("AnimationList", ((KvString)animList["_class"]).Value);
        Assert.Equal("", ((KvString)rootNode["base_model_name"]).Value);
    }

    [Fact]
    public void GenerateStandalone_OutputParses_AndContainsAllEntries()
    {
        var text = VmdlWriter.GenerateStandalone(
            "models/citizen/citizen.vmdl", GoldenAnims(), 0.3937f, "pelvis");
        var doc = Kv3.Parse(text);
        var animList = FindAnimationList(doc);
        var names = animList.ChildObjects().Select(o => ((KvString)o["name"]).Value).ToList();
        Assert.Equal(new[] { "walk", "jump" }, names);
    }

    // ================================================================ VmdlAugmenter

    private static List<AnimEntry> AugmentAnims()
        => new()
        {
            new AnimEntry
            {
                Name = "hr_walk",
                SourceFilename = "animations/hr_walk.dmx",
                Looping = true,
                ExtractMotion = false,
            },
            new AnimEntry
            {
                Name = "hr_jump",
                SourceFilename = "animations/hr_jump.dmx",
                Looping = false,
                ExtractMotion = true,
            },
        };

    private static KvObject FindAnimationList(Kv3Document doc)
    {
        var rootNode = (KvObject)((KvObject)doc.Root)["rootNode"];
        var children = (KvArray)rootNode["children"];
        return children.Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimationList");
    }

    [Fact]
    public void Augment_CitizenHumanMale_AddsAnimFiles_LeavesEverythingElseEqual()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        var augmented = VmdlAugmenter.Augment(original, AugmentAnims(), out var backup);

        Assert.Equal(original, backup);

        var beforeDoc = Kv3.Parse(original);
        var afterDoc = Kv3.Parse(augmented);
        Assert.Equal(beforeDoc.Header, afterDoc.Header);

        var beforeRoot = (KvObject)((KvObject)beforeDoc.Root)["rootNode"];
        var afterRoot = (KvObject)((KvObject)afterDoc.Root)["rootNode"];

        // Non-children root attributes untouched.
        foreach (var key in beforeRoot.Keys.Where(k => k != "children"))
            Assert.True(KvValue.DeepEquals(beforeRoot[key], afterRoot[key]), $"rootNode.{key} changed");

        var beforeChildren = ((KvArray)beforeRoot["children"]).Items;
        var afterChildren = ((KvArray)afterRoot["children"]).Items;
        Assert.Equal(beforeChildren.Count, afterChildren.Count);

        for (var i = 0; i < beforeChildren.Count; i++)
        {
            var cls = ((KvObject)beforeChildren[i]).GetString("_class");
            if (cls == "AnimationList")
                continue; // the spliced node — checked below
            Assert.True(KvValue.DeepEquals(beforeChildren[i], afterChildren[i]),
                $"rootNode.children[{i}] ({cls}) changed");
        }

        var beforeList = FindAnimationList(beforeDoc);
        var afterList = FindAnimationList(afterDoc);
        var beforeItems = ((KvArray)beforeList["children"]).Items;
        var afterItems = ((KvArray)afterList["children"]).Items;
        Assert.Equal(beforeItems.Count + 2, afterItems.Count);

        // Pre-existing AnimationList children are untouched and the new entries are appended.
        for (var i = 0; i < beforeItems.Count; i++)
            Assert.True(KvValue.DeepEquals(beforeItems[i], afterItems[i]));
        Assert.Equal("hr_walk", ((KvObject)afterItems[^2]).GetString("name"));
        Assert.Equal("hr_jump", ((KvObject)afterItems[^1]).GetString("name"));
        Assert.Equal("AnimFile", ((KvObject)afterItems[^1]).GetString("_class"));
    }

    [Fact]
    public void Augment_Twice_IsIdempotent()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        var once = VmdlAugmenter.Augment(original, AugmentAnims(), out _);
        var twice = VmdlAugmenter.Augment(once, AugmentAnims(), out _);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Augment_SameNameAnimFile_IsReplaced()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        var once = VmdlAugmenter.Augment(original, AugmentAnims(), out _);

        var changed = new List<AnimEntry>
        {
            new()
            {
                Name = "hr_walk",
                SourceFilename = "animations/hr_walk_v2.dmx",
                Looping = false,
                ExtractMotion = false,
            },
        };
        var again = VmdlAugmenter.Augment(once, changed, out _);

        var listItems = ((KvArray)FindAnimationList(Kv3.Parse(again))["children"]).Items;
        var walkNodes = listItems.OfType<KvObject>()
            .Where(o => o.GetString("name") == "hr_walk").ToList();
        var walk = Assert.Single(walkNodes);
        Assert.Equal("animations/hr_walk_v2.dmx", walk.GetString("source_filename"));
        Assert.False(((KvBool)walk["looping"]).Value);
    }

    private const string CollisionVmdl =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
        "{\n" +
        "\trootNode = \n" +
        "\t{\n" +
        "\t\t_class = \"RootNode\"\n" +
        "\t\tchildren = \n" +
        "\t\t[\n" +
        "\t\t\t{\n" +
        "\t\t\t\t_class = \"AnimationList\"\n" +
        "\t\t\t\tchildren = \n" +
        "\t\t\t\t[\n" +
        "\t\t\t\t\t{\n" +
        "\t\t\t\t\t\t_class = \"Folder\"\n" +
        "\t\t\t\t\t\tname = \"hr_walk\"\n" +
        "\t\t\t\t\t},\n" +
        "\t\t\t\t]\n" +
        "\t\t\t\tdefault_root_bone_name = \"pelvis\"\n" +
        "\t\t\t},\n" +
        "\t\t]\n" +
        "\t\tbase_model_name = \"\"\n" +
        "\t}\n" +
        "}\n";

    [Fact]
    public void Augment_NameCollisionWithNonAnimFile_Throws()
    {
        var ex = Assert.Throws<VmdlAugmentException>(
            () => VmdlAugmenter.Augment(CollisionVmdl, AugmentAnims(), out _));
        Assert.Contains(ex.Collisions, c => c.Contains("hr_walk"));
    }

    private const string NoAnimationListVmdl =
        "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n" +
        "{\n" +
        "\trootNode = \n" +
        "\t{\n" +
        "\t\t_class = \"RootNode\"\n" +
        "\t\tchildren = \n" +
        "\t\t[\n" +
        "\t\t]\n" +
        "\t\tbase_model_name = \"models/citizen/citizen.vmdl\"\n" +
        "\t}\n" +
        "}\n";

    [Fact]
    public void Augment_MissingAnimationList_CreatesOne()
    {
        var augmented = VmdlAugmenter.Augment(NoAnimationListVmdl, AugmentAnims(), out _);
        var animList = FindAnimationList(Kv3.Parse(augmented));
        Assert.Equal(2, ((KvArray)animList["children"]).Items.Count);
    }

    [Fact]
    public void Augment_MissingAnimationList_UsesDefaultRootBone()
    {
        var augmented = VmdlAugmenter.Augment(NoAnimationListVmdl, AugmentAnims(), out _,
            new AugmentOptions { DefaultRootBone = "pelvis" });
        var animList = FindAnimationList(Kv3.Parse(augmented));

        // The created AnimationList carries the target's root bone (matching the standalone
        // writer), and the ExtractMotion child extracts on the same bone instead of "".
        Assert.Equal("pelvis", animList.GetString("default_root_bone_name"));
        var jump = animList.ChildObjects().Single(o => o.GetString("name") == "hr_jump");
        var extract = jump.ChildObjects().Single(o => o.GetString("_class") == "ExtractMotion");
        Assert.Equal("pelvis", extract.GetString("root_bone_name"));
    }

    [Fact]
    public void Augment_ExistingAnimationListRootBone_WinsOverOption()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl"); // default_root_bone_name = pelvis
        var augmented = VmdlAugmenter.Augment(original, AugmentAnims(), out _,
            new AugmentOptions { DefaultRootBone = "some_other_bone" });
        var animList = FindAnimationList(Kv3.Parse(augmented));
        Assert.Equal("pelvis", animList.GetString("default_root_bone_name"));
        var jump = animList.ChildObjects().Single(o => o.GetString("name") == "hr_jump");
        var extract = jump.ChildObjects().Single(o => o.GetString("_class") == "ExtractMotion");
        Assert.Equal("pelvis", extract.GetString("root_bone_name"));
    }

    // ================================================================ CopyPinky neutralization

    /// <summary>All weight values inside the citizen CopyPinky folder, in document order.</summary>
    private static List<double> CopyPinkyWeights(string vmdlText)
    {
        var rootNode = (KvObject)((KvObject)Kv3.Parse(vmdlText).Root)["rootNode"];
        var constraintList = ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "AnimConstraintList");
        var folder = constraintList.ChildObjects()
            .Single(o => o.GetString("_class") == "Folder" && o.GetString("name") == "CopyPinky");

        var weights = new List<double>();
        CollectWeights(folder, weights);
        return weights;

        static void CollectWeights(KvObject node, List<double> weights)
        {
            if (node.GetOrNull("weight") is KvDouble w)
                weights.Add(w.Value);
            if (node.GetOrNull("children") is KvArray children)
            {
                foreach (var child in children.Items.OfType<KvObject>())
                    CollectWeights(child, weights);
            }
        }
    }

    [Fact]
    public void Augment_NeutralizePinky_ZeroesCopyPinkyWeights_LeavesOtherWeightsAlone()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        Assert.All(CopyPinkyWeights(original), w => Assert.Equal(1.0, w));

        var augmented = VmdlAugmenter.Augment(original, AugmentAnims(), out _,
            new AugmentOptions { NeutralizePinkyConstraints = true });

        var weights = CopyPinkyWeights(augmented);
        Assert.Equal(12, weights.Count); // 3 phalanges x 2 sides x (bone input + slave)
        Assert.All(weights, w => Assert.Equal(0.0, w));

        // Non-constraint weights elsewhere in the document (WeightList entries) are untouched.
        var rootNode = (KvObject)((KvObject)Kv3.Parse(augmented).Root)["rootNode"];
        var weightLists = ((KvArray)rootNode["children"]).Items.OfType<KvObject>()
            .Single(o => o.GetString("_class") == "WeightListList");
        var blink = weightLists.ChildObjects().Single(o => o.GetString("name") == "Human_Blink");
        var blinkWeights = ((KvArray)blink["weights"]).Items.OfType<KvObject>()
            .Select(o => ((KvDouble)o["weight"]).Value);
        Assert.All(blinkWeights, w => Assert.Equal(1.0, w));
    }

    [Fact]
    public void Augment_WithoutNeutralizePinky_KeepsCopyPinkyWeights()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        var augmented = VmdlAugmenter.Augment(original, AugmentAnims(), out _);
        Assert.All(CopyPinkyWeights(augmented), w => Assert.Equal(1.0, w));
    }

    [Fact]
    public void Augment_NeutralizePinky_IsIdempotent()
    {
        var original = ReadKv3Fixture("citizen_human_male.vmdl");
        var options = new AugmentOptions { NeutralizePinkyConstraints = true };
        var once = VmdlAugmenter.Augment(original, AugmentAnims(), out _, options);
        var twice = VmdlAugmenter.Augment(once, AugmentAnims(), out _, options);
        Assert.Equal(once, twice);
    }
}

internal static class KvTestExtensions
{
    public static IEnumerable<KvObject> ChildObjects(this KvObject node)
        => ((KvArray)node["children"]).Items.OfType<KvObject>();
}
