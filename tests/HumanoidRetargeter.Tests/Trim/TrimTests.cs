using System.Numerics;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Trim;
using Xunit;
using Skel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Trim;

public class TrimTests
{
    [Fact]
    public void CutsUndoRedoAndRestore()
    {
        var edit = new TrimEdit(10);
        Assert.True(edit.Delete(3, 5));
        Assert.Equal(new[] { 0, 1, 2, 6, 7, 8, 9 }, edit.Kept);
        Assert.True(edit.IsCutAfter(2));
        Assert.False(edit.IsCutAfter(3));

        Assert.True(edit.Keep(1, 4));
        Assert.Equal(new[] { 1, 2, 6, 7 }, edit.Kept);
        Assert.False(edit.Delete(0, 2)); // would leave one frame

        Assert.True(edit.Undo());
        Assert.Equal(new[] { 0, 1, 2, 6, 7, 8, 9 }, edit.Kept);
        Assert.True(edit.Redo());
        Assert.Equal(new[] { 1, 2, 6, 7 }, edit.Kept);

        Assert.True(edit.Restore());
        Assert.False(edit.IsTrimmed);
        Assert.True(edit.Undo());
        Assert.Equal(new[] { 1, 2, 6, 7 }, edit.Kept);
    }

    [Fact]
    public void CuttingTheMiddleOfAWalkKeepsTheTravelContinuous()
    {
        // Pelvis walks 2 cm a frame along X, with a child bone that must not be moved.
        var skeleton = Skel.Create(new[]
        {
            new BoneDefinition("pelvis", null, XForm.Identity),
            new BoneDefinition("spine", "pelvis", new XForm(new Vector3(0, 10, 0), Quaternion.Identity)),
        });
        var frames = Enumerable.Range(0, 10).Select(f => new[]
        {
            new XForm(new Vector3(2 * f, 90 + f, 0), Quaternion.Identity),
            new XForm(new Vector3(0, 10, 0), Quaternion.Identity),
        }).ToList();

        var trimmed = ClipTrim.Apply(frames, new[] { 0, 1, 2, 7, 8, 9 }, skeleton, Vector3.UnitY);

        Assert.Equal(6, trimmed.Count);
        for (var i = 0; i < trimmed.Count; i++)
            Assert.Equal(2f * i, trimmed[i][0].Pos.X, 3);
        Assert.Equal(97f, trimmed[3][0].Pos.Y, 3); // height is the frame's own
        Assert.Equal(new Vector3(0, 10, 0), trimmed[3][1].Pos);
        Assert.Equal(14f, frames[7][0].Pos.X); // source frames untouched
    }

    [Fact]
    public void ConvertingWithKeptFramesOutputsOnlyThoseFrames()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "humanoid-retargeter.sbproj")))
            root = root.Parent!;
        var target = RetargetTargetSpec.SboxDefault(File.ReadAllText(Path.Combine(root.FullName, "Assets", "data", "humanoid_retargeter", "target_rig_sbox.json")));
        RetargetRequest Request(IReadOnlyList<int>? kept) => new()
        {
            SourceData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fbx", "synthetic_two_takes.fbx")),
            SourceFileName = "synthetic_two_takes.fbx",
            TakeIndex = 0,
            KeptFrames = kept,
        };
        var kept = Enumerable.Range(0, 10).Concat(Enumerable.Range(20, 11)).ToArray();

        var full = Retargeter.Convert(Request(null), target).Clips.Single();
        var cut = Retargeter.Convert(Request(kept), target).Clips.Single();

        Assert.True(cut.Success, cut.Error);
        Assert.Equal(31, full.SolvedFrames!.Count);
        Assert.Equal(21, cut.SolvedFrames!.Count);
        var spine = target.Rig.Skeleton.IndexByName["spine_0"];
        for (var i = 0; i < kept.Length; i++)
            Assert.Equal(full.SolvedFrames[kept[i]][spine].Rot, cut.SolvedFrames[i][spine].Rot);
        Assert.NotEqual(full.DmxContent, cut.DmxContent);
    }
}
