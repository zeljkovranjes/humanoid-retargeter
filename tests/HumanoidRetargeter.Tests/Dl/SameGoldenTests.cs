using HumanoidRetargeter.Core.Dl;
using Xunit;

namespace HumanoidRetargeter.Tests.Dl;

/// <summary>
/// Parity of the pure-managed SAME port against golden tensors produced by the verified
/// Python pipeline (dev/m10/scripts/export_golden.py: SAME ckpt0 + the explicit-math GAT
/// wrappers proven bit-equal to the original PyG layers). Gate: ≤ 1e-3 everywhere.
/// </summary>
public class SameGoldenTests
{
    private const float ModelTolerance = 1e-3f;

    // ---------------------------------------------------------------- weights blob

    [Fact]
    public void WeightsBlobParsesWithExpectedArchitecture()
    {
        var weights = DlFixtures.Weights.Value;
        Assert.Equal(48, weights.Tensors.Count); // 8 GAT layers × 4 tensors + 16 ms stats

        var first = weights.Get("encoder.0.convs.0.lin_src.weight");
        Assert.Equal(new[] { 256, 32 }, first.Shape);
        var lastDec = weights.Get("decoder.0.deconvs.3.lin_src.weight");
        Assert.Equal(new[] { 11, 262 }, lastDec.Shape);
        Assert.Equal(6, weights.Stat("q_m").Length);
        Assert.Equal(4, weights.Stat("r_s").Length);
    }

    // ---------------------------------------------------------------- model forward parity

    [Fact]
    public void EncoderMatchesGoldenOnGoldenInputs()
    {
        var golden = DlFixtures.Golden.Value;
        var model = DlFixtures.Model.Value;

        var x = golden.Get("src.x").Data;
        var edges = golden.Get("src.edge_index");
        var e = edges.Shape[1];
        var edgeSrc = DlFixtures.ToInts(edges.Data.Take(e).ToArray());
        var edgeDst = DlFixtures.ToInts(edges.Data.Skip(e).ToArray());
        var batch = DlFixtures.ToInts(golden.Get("src.batch").Data);
        var frames = batch.Max() + 1;

        var nodeZ = model.EncodeNodes(x, edgeSrc, edgeDst);
        AssertClose(golden.Get("enc.node_z").Data, nodeZ, ModelTolerance, "node_z");

        var z = SameModel.MaxPool(nodeZ, SameModel.ZDim, batch, frames);
        AssertClose(golden.Get("enc.z").Data, z, ModelTolerance, "z");
    }

    [Fact]
    public void DecoderMatchesGoldenOnGoldenInputs()
    {
        var golden = DlFixtures.Golden.Value;
        var model = DlFixtures.Model.Value;
        var (_, _, frames) = DlFixtures.GoldenNames();

        var z = golden.Get("enc.z").Data;
        var tgtX = golden.Get("tgt.x");
        var j = tgtX.Shape[0];
        var edges = golden.Get("tgt.edge_index");
        var e = edges.Shape[1];
        var src1 = DlFixtures.ToInts(edges.Data.Take(e).ToArray());
        var dst1 = DlFixtures.ToInts(edges.Data.Skip(e).ToArray());

        // Tile the single-frame golden graph across the batch.
        var x = new float[frames * tgtX.Data.Length];
        var batch = new int[frames * j];
        var edgeSrc = new int[frames * e];
        var edgeDst = new int[frames * e];
        for (var f = 0; f < frames; f++)
        {
            Array.Copy(tgtX.Data, 0, x, f * tgtX.Data.Length, tgtX.Data.Length);
            for (var i = 0; i < j; i++)
                batch[f * j + i] = f;
            for (var i = 0; i < e; i++)
            {
                edgeSrc[f * e + i] = src1[i] + f * j;
                edgeDst[f * e + i] = dst1[i] + f * j;
            }
        }

        var hatD = model.Decode(z, x, edgeSrc, edgeDst, batch);
        AssertClose(golden.Get("dec.hatD").Data, hatD, ModelTolerance, "hatD");
    }

    // ---------------------------------------------------------------- feature pipeline parity

    [Fact]
    public void SourceFeaturesMatchGolden()
    {
        var golden = DlFixtures.Golden.Value;
        var (srcNames, _, frames) = DlFixtures.GoldenNames();

        var scene = DlFixtures.FixtureBvhTruncated(frames + 2); // tpose + wov frames
        var graph = SameFeatures.BuildSourceGraph(
            scene, 0, map: null, DlFixtures.Stats.Value,
            new SameFeatures.SourceOptions
            {
                NativeFrameDrop = true,
                Align = false,
                GroundShift = false,
            });

        Assert.Equal(frames, graph.FrameCount);
        Assert.Equal(srcNames.Length, graph.JointCount);

        var map = DlFixtures.MapNames(srcNames, graph.JointNames);
        var goldenX = golden.Get("src.x").Data;
        var width = SameModel.InputDim;

        // The importer resamples the 0.0333333s-frame-time file onto an exact 30 fps grid
        // (≈1e-5 of slerp per frame) and works in float32 against numpy's float64;
        // measured worst deviation 2.6e-5.
        const float tolerance = 1e-3f;
        var worst = 0f;
        var worstWhere = "";
        for (var f = 0; f < frames; f++)
        {
            for (var g = 0; g < srcNames.Length; g++)
            {
                var ours = (f * graph.JointCount + map[g]) * width;
                var theirs = (f * srcNames.Length + g) * width;
                for (var c = 0; c < width; c++)
                {
                    var d = MathF.Abs(goldenX[theirs + c] - graph.X[ours + c]);
                    if (d > worst)
                    {
                        worst = d;
                        worstWhere = $"frame {f}, joint {srcNames[g]}, column {c}";
                    }
                }
            }
        }
        Assert.True(worst <= tolerance,
            $"Source features deviate from golden by {worst} (> {tolerance}) at {worstWhere}.");
    }

    [Fact]
    public void TargetGraphMatchesGolden()
    {
        var golden = DlFixtures.Golden.Value;
        var (_, tgtNames, _) = DlFixtures.GoldenNames();

        var graph = SameTarget.Build(DlFixtures.SboxRig.Value, DlFixtures.Stats.Value, align: false);
        Assert.Equal(tgtNames.Length, graph.NodeCount);

        var map = DlFixtures.MapNames(tgtNames, graph.NodeNames);
        var goldenLo = golden.Get("tgt.lo").Data;
        var goldenGo = golden.Get("tgt.go").Data;
        var goldenX = golden.Get("tgt.x").Data;

        // cm-scale geometry; the golden side round-tripped through a 6-decimal BVH.
        const float geomTolerance = 1e-2f;
        for (var g = 0; g < tgtNames.Length; g++)
        {
            var n = map[g];
            for (var c = 0; c < 3; c++)
            {
                Assert.True(MathF.Abs(goldenLo[g * 3 + c] - graph.LoRaw[n * 3 + c]) <= geomTolerance,
                    $"lo[{tgtNames[g]}][{c}]: golden {goldenLo[g * 3 + c]} vs {graph.LoRaw[n * 3 + c]}");
                Assert.True(MathF.Abs(goldenGo[g * 3 + c] - graph.GoRaw[n * 3 + c]) <= geomTolerance,
                    $"go[{tgtNames[g]}][{c}]: golden {goldenGo[g * 3 + c]} vs {graph.GoRaw[n * 3 + c]}");
            }
            for (var c = 0; c < SameModel.SkelDim; c++)
            {
                Assert.True(MathF.Abs(goldenX[g * 6 + c] - graph.X[n * 6 + c]) <= ModelTolerance,
                    $"tgt_x[{tgtNames[g]}][{c}]");
            }
        }
    }

    [Fact]
    public void DecoderMatchesGoldenOnPortBuiltTargetGraph()
    {
        var golden = DlFixtures.Golden.Value;
        var model = DlFixtures.Model.Value;
        var (_, tgtNames, frames) = DlFixtures.GoldenNames();

        var graph = SameTarget.Build(DlFixtures.SboxRig.Value, DlFixtures.Stats.Value, align: false);
        var (x, edgeSrc, edgeDst, batch) = SameTarget.Tile(graph, frames);
        var hatD = model.Decode(golden.Get("enc.z").Data, x, edgeSrc, edgeDst, batch);

        // The GAT is permutation-equivariant: rows must match per joint NAME even though
        // this port orders nodes differently from the Python BVH traversal.
        var map = DlFixtures.MapNames(tgtNames, graph.NodeNames);
        var goldenD = golden.Get("dec.hatD").Data;
        var worst = 0f;
        for (var f = 0; f < frames; f++)
        {
            for (var g = 0; g < tgtNames.Length; g++)
            {
                var ours = (f * graph.NodeCount + map[g]) * SameModel.OutDim;
                var theirs = (f * tgtNames.Length + g) * SameModel.OutDim;
                for (var c = 0; c < SameModel.OutDim; c++)
                    worst = MathF.Max(worst, MathF.Abs(goldenD[theirs + c] - hatD[ours + c]));
            }
        }
        Assert.True(worst <= 1e-3f, $"hatD deviates from golden by {worst} on the port-built graph.");
    }

    // ---------------------------------------------------------------- end-to-end vs golden

    [Fact]
    public void FullPipelineMatchesGoldenEmbeddings()
    {
        var golden = DlFixtures.Golden.Value;
        var model = DlFixtures.Model.Value;
        var (_, _, frames) = DlFixtures.GoldenNames();

        var scene = DlFixtures.FixtureBvhTruncated(frames + 2);
        var graph = SameFeatures.BuildSourceGraph(
            scene, 0, map: null, DlFixtures.Stats.Value,
            new SameFeatures.SourceOptions
            {
                NativeFrameDrop = true,
                Align = false,
                GroundShift = false,
            });
        var z = model.Encode(graph.X, graph.EdgeSrc, graph.EdgeDst, graph.Batch, graph.FrameCount);

        var goldenZ = golden.Get("enc.z").Data;
        var worst = 0f;
        for (var i = 0; i < goldenZ.Length; i++)
            worst = MathF.Max(worst, MathF.Abs(goldenZ[i] - z[i]));
        Assert.True(worst <= 1e-3f,
            $"End-to-end embedding deviates from golden by {worst} (importer + feature + model stack).");
    }

    private static void AssertClose(float[] expected, float[] actual, float tolerance, string what)
    {
        Assert.Equal(expected.Length, actual.Length);
        var worst = 0f;
        var at = -1;
        for (var i = 0; i < expected.Length; i++)
        {
            var d = MathF.Abs(expected[i] - actual[i]);
            if (d > worst)
            {
                worst = d;
                at = i;
            }
        }
        Assert.True(worst <= tolerance, $"{what} deviates by {worst} (> {tolerance}) at flat index {at}.");
    }
}
