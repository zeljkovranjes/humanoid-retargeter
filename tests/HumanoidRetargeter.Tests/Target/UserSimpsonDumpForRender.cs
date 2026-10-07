using System.Text.Json;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Target;
using Xunit;
using Xunit.Abstractions;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>Instrumentation (local-only): dumps the Surprised→simpson solve as a
/// render_skinned.py-compatible JSON (dev/out/simpson_surprised_dump.json) so the skinned
/// result can be eyeballed from arbitrary camera angles in headless Blender.</summary>
public class UserSimpsonDumpForRender
{
    private readonly ITestOutputHelper _out;
    public UserSimpsonDumpForRender(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData("mixamo/Surprised.fbx", "simpson_surprised_dump.json")]
    [InlineData("actorcore/catwalk-loop-378982.fbx", "simpson_catwalk_dump.json")]
    public void DumpSolve(string clipRelative, string outName)
    {
        var fbx = TestUtil.RepoFile("dev", "corpus", "user_rigs", "simpson", "source", "C8V7NUC53TF8TTF3BXDMVGQIQ.fbx");
        var clipPath = TestUtil.RepoFile(("dev/corpus/" + clipRelative).Split('/'));
        if (!File.Exists(fbx) || !File.Exists(clipPath)) return; // local-only

        var scene = FbxImporter.Import(File.ReadAllBytes(fbx));
        var (map, _) = HumanoidRetargeter.Core.Retargeter.ResolveMapping(scene.Skeleton);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        var target = new HumanoidRetargeter.Core.RetargetTargetSpec
        { Rig = rig, VmdlScale = 0.3937f, DefaultRootBone = scene.Skeleton[0].Name };
        var batch = HumanoidRetargeter.Core.Retargeter.ConvertBatch(
            new[] { new HumanoidRetargeter.Core.RetargetRequest {
                SourceData = File.ReadAllBytes(clipPath), SourceFileName = Path.GetFileName(clipPath) } },
            target, new HumanoidRetargeter.Core.BatchOptions { DmxFolderRelative = "animations" });
        var clip = batch.Clips.Single();
        Assert.True(clip.Success, clip.Error);

        var outDir = TestUtil.RepoFile("dev", "out");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, outName);

        var skeleton = rig.Skeleton;
        using var stream = File.Create(outPath);
        using var w = new Utf8JsonWriter(stream);
        w.WriteStartObject();
        w.WriteNumber("frameCount", clip.SolvedFrames!.Count);
        w.WriteStartObject("mapping");
        w.WriteStartObject("targetRoles");
        for (var i = 0; i < skeleton.Count; i++)
            if (rig.RoleOf(i) is { } role)
                w.WriteString(role.ToString(), skeleton[i].Name);
        w.WriteEndObject();
        w.WriteEndObject();
        w.WriteStartArray("targetBones");
        for (var i = 0; i < skeleton.Count; i++)
        {
            w.WriteStartObject();
            w.WriteString("name", skeleton[i].Name);
            w.WriteStartObject("rest");
            WriteXf(w, skeleton[i].RestLocal);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("frames");
        foreach (var frame in clip.SolvedFrames)
        {
            w.WriteStartArray();
            for (var i = 0; i < skeleton.Count; i++)
            {
                w.WriteStartObject();
                WriteXf(w, frame[i]);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
        _out.WriteLine($"dump: {outPath} frames={clip.SolvedFrames.Count} bones={skeleton.Count}");
    }

    private static void WriteXf(Utf8JsonWriter w, HumanoidRetargeter.Core.Maths.XForm xf)
    {
        w.WriteStartArray("p");
        w.WriteNumberValue(xf.Pos.X); w.WriteNumberValue(xf.Pos.Y); w.WriteNumberValue(xf.Pos.Z);
        w.WriteEndArray();
        w.WriteStartArray("r");
        w.WriteNumberValue(xf.Rot.X); w.WriteNumberValue(xf.Rot.Y);
        w.WriteNumberValue(xf.Rot.Z); w.WriteNumberValue(xf.Rot.W);
        w.WriteEndArray();
    }
}
