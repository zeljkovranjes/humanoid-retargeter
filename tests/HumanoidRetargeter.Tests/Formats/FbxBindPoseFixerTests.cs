using System;
using System.IO;
using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Formats;

using Vector3 = System.Numerics.Vector3;

/// <summary>
/// The mid-pose repair chain: euler decomposition (exact inverse of the importer's
/// composition), binary round-trip fidelity of the writer, and the real-file repair —
/// the catgirl rig whose IK'd left foot/hands were exported half a meter from their bind
/// (the "arms weird, fingers, one leg wrong" user report).
/// </summary>
public class FbxBindPoseFixerTests
{
    // ---- euler decomposition --------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EulerDecomposition_RoundTrips_AllOrders(int order)
    {
        var rng = new Random(order * 977 + 13);
        for (int i = 0; i < 500; i++)
        {
            var q = MathQ.Normalize(new Quaternion(
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)));
            var euler = FbxBindPoseFixer.QuaternionToEulerDegrees(q, order);
            var back = FbxTransform.EulerDegreesToQuaternion(euler, order);
            var err = MathQ.AngleBetween(q, back) * 180f / MathF.PI;
            Assert.True(err < 0.01f,
                $"order {order} sample {i}: decompose/compose round-trip off by {err:0.####}°");
        }
    }

    // ---- binary writer round-trip ----------------------------------------------

    [Theory]
    [InlineData("user_rigs", "catgirl", "source", "Catgirl_2.fbx")]
    [InlineData("user_rigs", "die", "source", "DieExported.fbx")]
    [InlineData("actorcore", "catwalk-loop-378982.fbx")]
    public void BinaryWriter_RoundTrips_RealFiles(params string[] parts)
    {
        var path = TestUtil.RepoFile(Combine(parts));
        if (!File.Exists(path))
            return; // corpus subset not present on this machine

        var original = FbxTokenizer.Parse(File.ReadAllBytes(path));
        var rewritten = FbxBinaryWriter.Write(original);
        var reparsed = FbxTokenizer.Parse(rewritten);
        AssertTreesEqual(original, reparsed, "(root)");
    }

    // ---- the real repair ---------------------------------------------------------

    [Fact]
    public void Catgirl_MidPoseExport_IsDetectedAndRepaired()
    {
        var path = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        var fixedBytes = FbxBindPoseFixer.TryFix(File.ReadAllBytes(path), out var report);

        Assert.NotNull(fixedBytes);
        Assert.Contains("repaired", report);

        // The repaired file must re-import with anatomical rests: feet mirrored at ankle
        // height, hands mirrored - the ORIGINAL had foot.l at hip height, half a meter out.
        var scene = FbxImporter.Import(fixedBytes!);
        var skeleton = scene.Skeleton;
        var world = skeleton.RestWorld;

        int Find(string name)
        {
            for (int i = 0; i < skeleton.Count; i++)
            {
                if (skeleton[i].Name == name)
                    return i;
            }
            return -1;
        }

        int footL = Find("foot.l"), footR = Find("foot.r");
        int handL = Find("hand.l"), handR = Find("hand.r");
        Assert.True(footL >= 0 && footR >= 0 && handL >= 0 && handR >= 0, "expected ARP bone names");

        // Mirrored pairs: same height, opposite lateral offset (tolerance 1 cm).
        Assert.True(MathF.Abs(world[footL].Pos.Y - world[footR].Pos.Y) < 1.0f,
            $"feet not level: {world[footL].Pos} vs {world[footR].Pos}");
        Assert.True((world[footL].Pos - world[footR].Pos).Length() > 1.0f, "feet coincide");
        Assert.True(MathF.Abs(world[handL].Pos.Y - world[handR].Pos.Y) < 1.0f,
            $"hands not level: {world[handL].Pos} vs {world[handR].Pos}");

        // Second run on the repaired bytes: nothing left to fix.
        var again = FbxBindPoseFixer.TryFix(fixedBytes!, out var report2);
        Assert.Null(again);
        Assert.Contains("match", report2);
    }

    [Fact]
    public void ConsistentFiles_AreLeftAlone()
    {
        var path = TestUtil.RepoFile("dev", "corpus", "actorcore", "catwalk-loop-378982.fbx");
        var result = FbxBindPoseFixer.TryFix(File.ReadAllBytes(path), out _);
        Assert.Null(result);
    }

    [Fact]
    public void Importer_Notes_MidPoseCondition_ForSources()
    {
        // Sources keep their static rests (clips are authored against them; see the
        // UE-mannequin note in FbxImporter) - but the condition must be SURFACED.
        var path = TestUtil.RepoFile("dev", "corpus", "user_rigs", "catgirl", "source", "Catgirl_2.fbx");
        var scene = FbxImporter.Import(File.ReadAllBytes(path));
        Assert.Contains(scene.Notes, n => n.Contains("mid-pose"));
    }

    // ---- helpers ------------------------------------------------------------------

    private static string[] Combine(string[] parts)
    {
        var all = new string[parts.Length + 2];
        all[0] = "dev";
        all[1] = "corpus";
        Array.Copy(parts, 0, all, 2, parts.Length);
        return all;
    }

    private static void AssertTreesEqual(FbxNode a, FbxNode b, string path)
    {
        Assert.True(a.Name == b.Name, $"{path}: name '{a.Name}' vs '{b.Name}'");
        Assert.True(a.Properties.Count == b.Properties.Count,
            $"{path}/{a.Name}: {a.Properties.Count} vs {b.Properties.Count} properties");
        for (int i = 0; i < a.Properties.Count; i++)
        {
            var (pa, pb) = (a.Properties[i], b.Properties[i]);
            Assert.True(pa.GetType() == pb.GetType(),
                $"{path}/{a.Name}[{i}]: {pa.GetType().Name} vs {pb.GetType().Name}");
            switch (pa)
            {
                case double[] da:
                    Assert.Equal(da, (double[])pb);
                    break;
                case float[] fa:
                    Assert.Equal(fa, (float[])pb);
                    break;
                case long[] la:
                    Assert.Equal(la, (long[])pb);
                    break;
                case int[] ia:
                    Assert.Equal(ia, (int[])pb);
                    break;
                case bool[] oa:
                    Assert.Equal(oa, (bool[])pb);
                    break;
                case byte[] ra:
                    Assert.Equal(ra, (byte[])pb);
                    break;
                default:
                    Assert.Equal(pa, pb);
                    break;
            }
        }
        Assert.True(a.Children.Count == b.Children.Count,
            $"{path}/{a.Name}: {a.Children.Count} vs {b.Children.Count} children");
        for (int i = 0; i < a.Children.Count; i++)
            AssertTreesEqual(a.Children[i], b.Children[i], $"{path}/{a.Name}");
    }
}
