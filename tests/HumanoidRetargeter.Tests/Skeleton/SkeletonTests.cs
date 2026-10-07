using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Skeleton;

public class SkeletonTests
{
    private const float PosEpsCm = 1e-3f;
    private const float RotEps = 1e-4f;

    public static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    // ---------------------------------------------------------------- RigJson

    [Fact]
    public void LoadHumanMale_Has94Bones_AndPelvisAndRootIkAreRoots()
    {
        var (skeleton, _) = RigJson.Load(ReadFixture("rig_human_male.json"));

        Assert.Equal(94, skeleton.Count);

        var pelvis = skeleton[skeleton.IndexByName["pelvis"]];
        Assert.Equal(-1, pelvis.ParentIndex);

        var rootIk = skeleton[skeleton.IndexByName["root_IK"]];
        Assert.Equal(-1, rootIk.ParentIndex);
    }

    [Fact]
    public void LoadCitizen_Has95Bones()
    {
        var (skeleton, _) = RigJson.Load(ReadFixture("rig_citizen.json"));
        Assert.Equal(95, skeleton.Count);
        Assert.True(skeleton.IndexByName.ContainsKey("pelvis"));
    }

    [Fact]
    public void LoadHumanMale_PositionsAreCentimeters()
    {
        // Pelvis sits ~93 cm above the ground in the source rig (Y-up).
        var (skeleton, _) = RigJson.Load(ReadFixture("rig_human_male.json"));
        var pelvisWorld = skeleton.RestWorld[skeleton.IndexByName["pelvis"]];
        Assert.InRange(pelvisWorld.Pos.Y, 80f, 110f);
    }

    [Theory]
    [InlineData("rig_human_male.json")]
    [InlineData("rig_citizen.json")]
    public void FkOfRestLocals_ReproducesStoredWorldTransforms(string fixture)
    {
        var json = ReadFixture(fixture);
        var (skeleton, _) = RigJson.Load(json);
        var expected = ParseExpectedWorld(json);

        Assert.Equal(expected.Count, skeleton.Count);

        for (var i = 0; i < skeleton.Count; i++)
        {
            var name = skeleton[i].Name;
            var (worldPos, worldRot) = expected[name];
            var actual = skeleton.RestWorld[i];

            TestUtil.AssertVectorEqual(worldPos, actual.Pos, PosEpsCm);
            TestUtil.AssertQuaternionEqual(worldRot, actual.Rot, RotEps);
        }
    }

    [Fact]
    public void LoadHumanMale_GeometryHeadMatchesWorldPosition()
    {
        var json = ReadFixture("rig_human_male.json");
        var (skeleton, geometry) = RigJson.Load(json);

        Assert.Equal(skeleton.Count, geometry.Count);

        foreach (var bone in skeleton.Bones)
        {
            var (head, tail) = geometry[bone.Name];
            TestUtil.AssertVectorEqual(skeleton.RestWorld[bone.Index].Pos, head, PosEpsCm);
            Assert.True((tail - head).Length() > 0.01f, $"{bone.Name}: tail must differ from head");
        }
    }

    /// <summary>Reads world_pos/world_rot_xyzw straight from the research JSON, applying the same
    /// object_scale x 100 position conversion the loader applies.</summary>
    private static Dictionary<string, (Vector3 Pos, Quaternion Rot)> ParseExpectedWorld(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var armature = doc.RootElement.GetProperty("armatures")[0];
        var scale = (float)armature.GetProperty("object_scale")[0].GetDouble() * 100f;

        var result = new Dictionary<string, (Vector3, Quaternion)>();
        foreach (var bone in armature.GetProperty("bones").EnumerateArray())
        {
            var p = bone.GetProperty("world_pos");
            var r = bone.GetProperty("world_rot_xyzw");
            result[bone.GetProperty("name").GetString()!] = (
                new Vector3((float)p[0].GetDouble(), (float)p[1].GetDouble(), (float)p[2].GetDouble()) * scale,
                new Quaternion((float)r[0].GetDouble(), (float)r[1].GetDouble(), (float)r[2].GetDouble(), (float)r[3].GetDouble()));
        }
        return result;
    }

    // ---------------------------------------------------------------- Skeleton.Create

    [Fact]
    public void Create_SortsChildrenAfterParents_EvenWhenInputIsChildFirst()
    {
        var defs = new[]
        {
            new BoneDefinition("hand", "arm", new XForm(new Vector3(0f, 30f, 0f), Quaternion.Identity)),
            new BoneDefinition("arm", "chest", new XForm(new Vector3(0f, 20f, 0f), Quaternion.Identity)),
            new BoneDefinition("chest", null, new XForm(new Vector3(0f, 100f, 0f), Quaternion.Identity)),
        };

        var skeleton = Skel.Create(defs);

        Assert.Equal(3, skeleton.Count);
        for (var i = 0; i < skeleton.Count; i++)
            Assert.True(skeleton[i].ParentIndex < i, $"parent of bone {i} must come first");

        var hand = skeleton[skeleton.IndexByName["hand"]];
        Assert.Equal("arm", skeleton[hand.ParentIndex].Name);
        TestUtil.AssertVectorEqual(new Vector3(0f, 150f, 0f), skeleton.RestWorld[hand.Index].Pos, PosEpsCm);
    }

    [Fact]
    public void Create_PreservesInputOrder_WhenAlreadyTopological()
    {
        var defs = new[]
        {
            new BoneDefinition("root_b", null, XForm.Identity),
            new BoneDefinition("root_a", null, XForm.Identity),
            new BoneDefinition("child_of_b", "root_b", XForm.Identity),
        };

        var skeleton = Skel.Create(defs);

        Assert.Equal(new[] { "root_b", "root_a", "child_of_b" }, skeleton.Bones.Select(b => b.Name).ToArray());
    }

    [Fact]
    public void Create_DuplicateName_Throws()
    {
        var defs = new[]
        {
            new BoneDefinition("a", null, XForm.Identity),
            new BoneDefinition("a", null, XForm.Identity),
        };
        Assert.Throws<ArgumentException>(() => Skel.Create(defs));
    }

    [Fact]
    public void Create_UnknownParent_Throws()
    {
        var defs = new[] { new BoneDefinition("a", "missing", XForm.Identity) };
        Assert.Throws<ArgumentException>(() => Skel.Create(defs));
    }

    [Fact]
    public void Create_ParentCycle_Throws()
    {
        var defs = new[]
        {
            new BoneDefinition("a", "b", XForm.Identity),
            new BoneDefinition("b", "a", XForm.Identity),
        };
        Assert.Throws<ArgumentException>(() => Skel.Create(defs));
    }

    // ---------------------------------------------------------------- Pose

    [Fact]
    public void PoseToWorld_WithRestLocals_MatchesRestWorld()
    {
        var (skeleton, _) = RigJson.Load(ReadFixture("rig_human_male.json"));

        var pose = Pose.Rest(skeleton);
        var world = pose.ToWorld(skeleton);

        Assert.Equal(skeleton.Count, world.Length);
        for (var i = 0; i < skeleton.Count; i++)
        {
            TestUtil.AssertVectorEqual(skeleton.RestWorld[i].Pos, world[i].Pos, PosEpsCm);
            TestUtil.AssertQuaternionEqual(skeleton.RestWorld[i].Rot, world[i].Rot, RotEps);
        }
    }

    [Fact]
    public void PoseToWorld_WrongBoneCount_Throws()
    {
        var (skeleton, _) = RigJson.Load(ReadFixture("rig_human_male.json"));
        var pose = new Pose(new XForm[3]);
        Assert.Throws<ArgumentException>(() => pose.ToWorld(skeleton));
    }

    // ---------------------------------------------------------------- Clip

    [Fact]
    public void Clip_StoresMetadataAndFrames()
    {
        var clip = new Clip("walk", 30f, looping: true);
        clip.Frames.Add(new[] { XForm.Identity });

        Assert.Equal("walk", clip.Name);
        Assert.Equal(30f, clip.Fps);
        Assert.True(clip.Looping);
        Assert.Equal(1, clip.FrameCount);
    }

    [Fact]
    public void Clip_NonPositiveFps_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Clip("bad", 0f, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Clip("bad", -30f, false));
    }
}
