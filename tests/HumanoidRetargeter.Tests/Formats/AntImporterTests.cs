using HumanoidRetargeter.Core.Formats.Ant;
using Xunit;
using HumanoidRetargeter.Core;

namespace HumanoidRetargeter.Tests.Formats;

/// <summary>
/// EA ANT (<c>.cba</c>) import, verified against a real shipped package.
/// Fixture: <c>fixtures/ant/package_proxy_punch_ali.cba</c> — Fight Night Champion's Ali
/// punch proxy bank (24 clips × 13 channels) with the companion joint table
/// <c>skeleton_boxer.json</c> (293 joints).
/// </summary>
/// <remarks>
/// The decisive assertions are the ones that would have caught the vec3/vec4 stride
/// corruption: positions must be read on a 16-byte stride, and the <c>w</c> component that
/// separates the keys is always zero. A stride-3 reader reproduces the first key correctly
/// and then slides — so key 0 alone proves nothing and the later keys are checked explicitly.
/// </remarks>
public class AntImporterTests
{
    private const string Package = "package_proxy_punch_ali.cba";

    private static string FixturePath(string file)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "ant", file);

    private static byte[] PackageBytes() => File.ReadAllBytes(FixturePath(Package));

    private static byte[] SkeletonBytes() => File.ReadAllBytes(FixturePath("skeleton_boxer.json"));

    // ---------------------------------------------------------------- container

    [Fact]
    public void Import_RealPackage_YieldsEveryClipOverTheCompanionSkeleton()
    {
        var scene = AntImporter.Import(PackageBytes(), SkeletonBytes());

        Assert.Equal(293, scene.Skeleton.Count);
        Assert.Equal(24, scene.Clips.Count);

        // Joint table order and parenting survive the topological sort.
        Assert.Equal(0, scene.Skeleton.IndexOf("Reference"));
        Assert.True(scene.Skeleton[scene.Skeleton.IndexOf("Reference")].ParentIndex < 0);
        var hips = scene.Skeleton.IndexOf("Hips");
        Assert.True(hips >= 0);
        Assert.Equal("AITrajectory", scene.Skeleton[scene.Skeleton[hips].ParentIndex].Name);

        Assert.All(scene.Clips, c => Assert.True(c.FrameCount > 1, $"{c.Name} has {c.FrameCount} frames"));
        Assert.All(scene.Clips, c => Assert.All(c.Frames, f => Assert.Equal(scene.Skeleton.Count, f.Length)));
    }

    [Fact]
    public void Import_ClipsAreNamedFromTheFileStem()
    {
        var scene = AntImporter.Import(
            PackageBytes(), SkeletonBytes(), new AntImportOptions { ClipNameBase = "ali_punch" });

        Assert.Equal("ali_punch_1", scene.Clips[0].Name);
        Assert.Equal("ali_punch_24", scene.Clips[23].Name);
    }

    // ---------------------------------------------------------------- the stride contract

    /// <summary>
    /// Positions are <c>vec4</c> (xyz + zero w pad), 16 bytes per key. A stride-3 reader
    /// slides one float per key, which is exactly how the circulated JSON extraction of these
    /// packages was corrupted. Decoding the raw stream directly here pins the layout so a
    /// future "simplification" to 12 bytes fails loudly.
    /// </summary>
    [Fact]
    public void RawPositionStream_IsVec4WithAZeroWPad()
    {
        var clips = AntStream.ParseClips(PackageBytes());
        Assert.Equal(24, clips.Count);
        Assert.All(clips, c => Assert.Equal(13, c.Channels.Count));

        // First clip / first channel: the Hips track, 4 keys at ticks 0/30/33/66.
        var hips = clips[0].Channels[0];
        Assert.Equal(new[] { 0f, 30f, 33f, 66f }, hips.Times);
        Assert.Equal(4, hips.Keys.Length);

        // Key 0 is where a stride-3 reader still agrees; keys 1..3 are where it diverges.
        AssertPos(hips.Keys[0].Pos, 0.013445f, 2.826397f, 0.070485f);
        AssertPos(hips.Keys[1].Pos, 0.072565f, 2.874669f, 0.106827f);
        AssertPos(hips.Keys[2].Pos, 0.117656f, 2.868636f, 0.122673f);
        AssertPos(hips.Keys[3].Pos, 0.225096f, 2.825475f, 0.074184f);
    }

    /// <summary>Quaternions are stored W FIRST and are unit length in the shipped data.</summary>
    [Fact]
    public void RawRotations_AreUnitQuaternionsStoredWFirst()
    {
        var clips = AntStream.ParseClips(PackageBytes());
        var q = clips[0].Channels[0].Keys[0].Rot;

        Assert.Equal(0.630292f, q.W, 4);
        Assert.Equal(0.696696f, q.X, 4);
        Assert.Equal(0.251306f, q.Y, 4);
        Assert.Equal(0.232791f, q.Z, 4);

        foreach (var clip in clips)
        {
            foreach (var channel in clip.Channels)
            {
                foreach (var key in channel.Keys)
                    Assert.Equal(1f, key.Rot.Length(), 3);
            }
        }
    }

    /// <summary>Channels bind to joints by INDEX; the proxy bank drives exactly the 13
    /// IK-proxy joints and nothing else.</summary>
    [Fact]
    public void Channels_BindToTheExpectedJointIndices()
    {
        var clips = AntStream.ParseClips(PackageBytes());
        var joints = clips[0].Channels.Select(c => c.JointIndex).ToArray();

        Assert.Equal(new[] { 2, 26, 166, 115, 29, 161, 110, 4, 14, 5, 15, 6, 16 }, joints);

        // Every clip in the bank drives the same joint set.
        Assert.All(clips, c => Assert.Equal(joints, c.Channels.Select(x => x.JointIndex).ToArray()));
    }

    // ---------------------------------------------------------------- rest pose policy

    /// <summary>
    /// ANT stores no bind pose, so the first frame is the rest reference and unanimated
    /// joints keep an identity rest local (see AntImporter remarks).
    /// </summary>
    [Fact]
    public void UnanimatedJoints_KeepAnIdentityRestLocal()
    {
        var scene = AntImporter.Import(PackageBytes(), SkeletonBytes());

        // Driven: the Hips carry the decoded first key, not identity.
        var hips = scene.Skeleton[scene.Skeleton.IndexOf("Hips")];
        Assert.NotEqual(0f, hips.RestLocal.Pos.Length(), 3);

        // Not driven by this proxy bank: a muscle/jiggle helper stays at identity.
        var muscle = scene.Skeleton.IndexOf("Muscle_Left_Bicep");
        Assert.True(muscle >= 0);
        Assert.Equal(0f, scene.Skeleton[muscle].RestLocal.Pos.Length(), 5);
    }

    // ---------------------------------------------------------------- errors

    [Fact]
    public void Import_WithoutCompanionSkeleton_ThrowsInstructively()
    {
        var e = Assert.Throws<FormatException>(() => AntImporter.Import(PackageBytes(), null));
        Assert.Contains("joint table", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_NonAntBytes_Throws()
        => Assert.Throws<FormatException>(
            () => AntImporter.Import(new byte[64], SkeletonBytes()));

    [Fact]
    public void Import_SkeletonTooSmallForTheChannels_Throws()
    {
        var tiny = System.Text.Encoding.UTF8.GetBytes(
            """[{"name":"Reference","parent":-1},{"name":"Hips","parent":0}]""");

        var e = Assert.Throws<FormatException>(() => AntImporter.Import(PackageBytes(), tiny));
        Assert.Contains("joint index", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- facade wiring

    [Fact]
    public void ImportSource_DispatchesCbaByExtensionAndByMagic()
    {
        var byExtension = Retargeter.ImportSource(
            PackageBytes(), "package_proxy_punch_ali.cba", skeletonData: SkeletonBytes());
        Assert.Equal(24, byExtension.Clips.Count);

        // Unknown extension: the ANTSTM3b magic sniff must still route it.
        var byMagic = Retargeter.ImportSource(
            PackageBytes(), "punch_bank.unknown", skeletonData: SkeletonBytes());
        Assert.Equal(24, byMagic.Clips.Count);
    }

    private static void AssertPos(System.Numerics.Vector3 v, float x, float y, float z)
    {
        Assert.Equal(x, v.X, 4);
        Assert.Equal(y, v.Y, 4);
        Assert.Equal(z, v.Z, 4);
    }
}
