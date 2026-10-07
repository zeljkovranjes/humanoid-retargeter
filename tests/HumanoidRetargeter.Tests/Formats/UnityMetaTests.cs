using HumanoidRetargeter.Core.Formats;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

/// <summary>
/// Unity <c>.fbx.meta</c> sidecar support: the <see cref="UnityMeta.ParseClipAnimations"/>
/// line parser over the clipAnimations YAML subset, and the <see cref="UnityMeta.Slice"/>
/// native-frame → resampled-grid range mapping. The fixture
/// (fixtures/unity/synthetic_stance.fbx.meta) is SYNTHETIC, replicating the structure Unity
/// 2020+ writes (ModelImporter serializedVersion 20300, clipAnimations serializedVersion 16).
/// </summary>
public class UnityMetaTests
{
    private static string FixturePath(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "fixtures" }.Concat(parts).ToArray());

    private static string FixtureText()
        => File.ReadAllText(FixturePath("unity", "synthetic_stance.fbx.meta"));

    // ---------------------------------------------------------------- parser

    [Fact]
    public void Parse_SyntheticUnityMeta_YieldsBothDefinitions()
    {
        var defs = UnityMeta.ParseClipAnimations(FixtureText());

        Assert.Equal(2, defs.Count);

        Assert.Equal("Idle", defs[0].Name);
        Assert.Equal("root|Animation", defs[0].TakeName);
        Assert.Equal(0f, defs[0].FirstFrame);
        Assert.Equal(20f, defs[0].LastFrame);
        Assert.True(defs[0].Loop); // loopTime: 1 (NOT the unrelated "loop: 0" field)

        // Quoted name unwrapped; fractional frames parsed.
        Assert.Equal("Idle Short", defs[1].Name);
        Assert.Equal("root|Animation", defs[1].TakeName);
        Assert.Equal(5.5f, defs[1].FirstFrame);
        Assert.Equal(6.5f, defs[1].LastFrame);
        Assert.False(defs[1].Loop); // loopTime: 0
    }

    [Fact]
    public void Parse_NestedEventListEntries_DoNotBleedIntoDefinitions()
    {
        // The first item carries an events list whose entries are themselves "- key: value"
        // lines at deeper indentation — they must neither start new definitions nor
        // overwrite the item's own fields.
        var defs = UnityMeta.ParseClipAnimations(FixtureText());
        Assert.Equal(2, defs.Count);
        Assert.Equal("Idle", defs[0].Name);
    }

    [Fact]
    public void Parse_InlineEmptyList_YieldsNoDefinitions()
        => Assert.Empty(UnityMeta.ParseClipAnimations(
            "ModelImporter:\n  animations:\n    clipAnimations: []\n    isReadable: 0\n"));

    [Fact]
    public void Parse_NoClipAnimationsKey_YieldsNoDefinitions()
        => Assert.Empty(UnityMeta.ParseClipAnimations(
            "fileFormatVersion: 2\nguid: abc\nTextureImporter:\n  mipmaps: 1\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("complete garbage \0 {{{{ ---")]
    [InlineData("clipAnimations:")]
    [InlineData("clipAnimations:\n- name: x")] // dash at indent 0 < key indent is malformed → tolerated
    public void Parse_DegenerateInput_NeverThrows(string? text)
    {
        var defs = UnityMeta.ParseClipAnimations(text);
        Assert.NotNull(defs);
    }

    [Fact]
    public void Parse_MissingFields_KeepDefaults()
    {
        var defs = UnityMeta.ParseClipAnimations(
            "  clipAnimations:\n  - name: OnlyName\n");

        var def = Assert.Single(defs);
        Assert.Equal("OnlyName", def.Name);
        Assert.Equal("", def.TakeName);
        Assert.Equal(0f, def.FirstFrame);
        Assert.True(float.IsPositiveInfinity(def.LastFrame)); // "to the end of the take"
        Assert.False(def.Loop);
    }

    [Fact]
    public void Parse_ListEndsAtSiblingKey()
    {
        var defs = UnityMeta.ParseClipAnimations(
            "  clipAnimations:\n  - name: A\n    lastFrame: 3\n  isReadable: 0\n  - name: NotAClip\n");
        var def = Assert.Single(defs);
        Assert.Equal("A", def.Name);
    }

    [Fact]
    public void Parse_NoiseOnlyItems_AreDropped()
    {
        var defs = UnityMeta.ParseClipAnimations(
            "  clipAnimations:\n  - serializedVersion: 16\n    wrapMode: 0\n  - name: Real\n    lastFrame: 2\n");
        var def = Assert.Single(defs);
        Assert.Equal("Real", def.Name);
    }

    // ---------------------------------------------------------------- slicing math

    /// <summary>Clip whose frame f carries Pos.X = f on its single bone — slice contents
    /// are then directly checkable.</summary>
    private static Clip IndexClip(int frameCount, float fps, float nativeFps)
    {
        var frames = new List<XForm[]>(frameCount);
        for (var f = 0; f < frameCount; f++)
            frames.Add(new[] { new XForm(new System.Numerics.Vector3(f, 0, 0), System.Numerics.Quaternion.Identity) });
        return new Clip("take", fps, looping: false, frames, nativeFps);
    }

    private static ExternalClipDef Def(float first, float last, bool loop = false, string name = "part")
        => new() { Name = name, FirstFrame = first, LastFrame = last, Loop = loop };

    [Fact]
    public void Slice_SameNativeAndSampleRate_UsesFrameIndicesDirectly()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 30f);

        var full = UnityMeta.Slice(source, Def(0, 43));
        Assert.Equal(44, full.FrameCount);

        var pose = UnityMeta.Slice(source, Def(0, 1));
        Assert.Equal(2, pose.FrameCount);
        Assert.Equal(0f, pose.Frames[0][0].Pos.X);
        Assert.Equal(1f, pose.Frames[1][0].Pos.X);
    }

    [Fact]
    public void Slice_RescalesNativeFramesOntoSampleGrid()
    {
        // 24 fps native take of 54 native intervals resampled at 30 fps:
        // round(54/24*30)+1 = 69 samples. Native frame n maps to sample round(n*30/24).
        var source = IndexClip(69, fps: 30f, nativeFps: 24f);

        var sliced = UnityMeta.Slice(source, Def(8, 43));
        Assert.Equal(10f, sliced.Frames[0][0].Pos.X);  // round(8 * 1.25) = 10
        Assert.Equal(54f, sliced.Frames[^1][0].Pos.X); // round(43 * 1.25) = 54 (53.75 rounds up)
        Assert.Equal(45, sliced.FrameCount);
    }

    [Fact]
    public void Slice_ClampsRangeIntoClip()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 30f);

        var past = UnityMeta.Slice(source, Def(100, 999));
        Assert.Equal(1, past.FrameCount);
        Assert.Equal(43f, past.Frames[0][0].Pos.X);

        var overrun = UnityMeta.Slice(source, Def(40, 999));
        Assert.Equal(4, overrun.FrameCount);
        Assert.Equal(43f, overrun.Frames[^1][0].Pos.X);
    }

    [Fact]
    public void Slice_SingleFrameRange_YieldsOneFrame()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 30f);
        var sliced = UnityMeta.Slice(source, Def(5, 5));
        Assert.Equal(1, sliced.FrameCount);
        Assert.Equal(5f, sliced.Frames[0][0].Pos.X);
    }

    [Fact]
    public void Slice_InvertedRange_CollapsesToFirstFrame()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 30f);
        var sliced = UnityMeta.Slice(source, Def(10, 2));
        Assert.Equal(1, sliced.FrameCount);
        Assert.Equal(10f, sliced.Frames[0][0].Pos.X);
    }

    [Fact]
    public void Slice_MissingLastFrame_RunsToTheEnd()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 30f);
        var sliced = UnityMeta.Slice(source, new ExternalClipDef { Name = "tail", FirstFrame = 30 });
        Assert.Equal(14, sliced.FrameCount);
        Assert.Equal(43f, sliced.Frames[^1][0].Pos.X);
    }

    [Fact]
    public void Slice_CarriesNameLoopAndRates()
    {
        var source = IndexClip(44, fps: 30f, nativeFps: 24f);
        var sliced = UnityMeta.Slice(source, Def(0, 5, loop: true, name: "walk cycle"));

        Assert.Equal("walk cycle", sliced.Name); // sanitization happens pipeline-side
        Assert.True(sliced.Looping);
        Assert.Equal(30f, sliced.Fps);
        Assert.Equal(24f, sliced.NativeFps);

        var named = UnityMeta.Slice(source, Def(0, 5), "explicit");
        Assert.Equal("explicit", named.Name);
    }
}
