using System.Numerics;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>3ds Max Biped exports can leave the skeleton posed with a BindPose that agrees
/// with it, while the skin was bound in another pose. Built on that skeleton, the fingers
/// tear apart once animated, so the skin clusters' bind must win.</summary>
public class SkinBindRepairTests
{
    static FbxNode Node(string name, params object[] values)
    {
        var node = new FbxNode(name);
        node.Properties.AddRange(values);
        return node;
    }

    static FbxNode Property(string name, string type, params object[] values)
        => Node("P", new object[] { name, type, "", "A" }.Concat(values).ToArray());

    static double[] Matrix(Vector3 translation, float yawDegrees)
    {
        var m = Matrix4x4.CreateRotationZ(yawDegrees * MathF.PI / 180) * Matrix4x4.CreateTranslation(translation);
        return new double[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
    }

    /// <summary>A hand with one finger. Nodes and BindPose: finger straight out. Skin: finger bound bent 40 degrees.</summary>
    static byte[] Fbx(bool withCluster)
    {
        var root = Node("");
        var header = Node("FBXHeaderExtension");
        header.Children.AddRange(new[] { Node("FBXHeaderVersion", 1003), Node("FBXVersion", 7400) });
        root.Children.Add(header);
        var settings = Node("GlobalSettings");
        var global = Node("Properties70");
        global.Children.Add(Property("UnitScaleFactor", "double", 1.0));
        settings.Children.Add(global);
        root.Children.Add(settings);
        var objects = Node("Objects");
        var connections = Node("Connections");
        FbxNode Model(long id, string name, Vector3 translation, float yaw)
        {
            var model = Node("Model", id, name + "\0\u0001Model", "LimbNode");
            var props = Node("Properties70");
            props.Children.AddRange(new[] {
                Property("Lcl Translation", "Lcl Translation", (double)translation.X, (double)translation.Y, (double)translation.Z),
                Property("Lcl Rotation", "Lcl Rotation", 0.0, 0.0, (double)yaw) });
            model.Children.Add(props);
            return model;
        }
        objects.Children.Add(Model(1, "hand", Vector3.Zero, 0));
        objects.Children.Add(Model(2, "finger", new Vector3(10, 0, 0), 0));
        connections.Children.Add(Node("C", "OO", 1L, 0L));
        connections.Children.Add(Node("C", "OO", 2L, 1L));
        var pose = Node("Pose", 10L, "BindPose\0\u0001Pose", "BindPose");
        foreach (var (id, m) in new[] { (1L, Matrix(Vector3.Zero, 0)), (2L, Matrix(new Vector3(10, 0, 0), 0)) })
        {
            var entry = Node("PoseNode");
            entry.Children.Add(Node("Node", id));
            entry.Children.Add(Node("Matrix", m));
            pose.Children.Add(entry);
        }
        objects.Children.Add(pose);
        if (withCluster)
        {
            // The skin was bound with the finger bent 40 degrees at its knuckle.
            var cluster = Node("Deformer", 20L, "finger\0\u0001SubDeformer", "Cluster");
            cluster.Children.Add(Node("Transform", Matrix(Vector3.Zero, 0)));
            cluster.Children.Add(Node("TransformLink", Matrix(new Vector3(10, 0, 0), 40)));
            objects.Children.Add(cluster);
            var handCluster = Node("Deformer", 21L, "hand\0\u0001SubDeformer", "Cluster");
            handCluster.Children.Add(Node("Transform", Matrix(Vector3.Zero, 0)));
            handCluster.Children.Add(Node("TransformLink", Matrix(Vector3.Zero, 0)));
            objects.Children.Add(handCluster);
            connections.Children.Add(Node("C", "OO", 2L, 20L));
            connections.Children.Add(Node("C", "OO", 1L, 21L));
        }
        root.Children.Add(objects);
        root.Children.Add(connections);
        return FbxBinaryWriter.Write(root);
    }

    [Fact]
    public void SkeletonIsRebuiltInThePoseTheSkinWasBoundTo()
    {
        var original = Fbx(true);
        var scene = FbxScene.Build(FbxTokenizer.Parse(original));
        Assert.Equal(2, scene.SkinBind.Count);
        var repaired = FbxBindPoseFixer.TryFix(original, out var report);
        Assert.NotNull(repaired);
        Assert.Contains("repaired", report);
        var skeleton = FbxImporter.Import(repaired!).Skeleton;
        var finger = skeleton.RestWorld[skeleton.IndexOf("finger")];
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 40 * MathF.PI / 180);
        Assert.True(MathQ.AngleBetween(finger.Rot, expected) < .01f, $"finger bind {finger.Rot}");
        Assert.True(Vector3.Distance(finger.Pos, new Vector3(10, 0, 0)) < .001f);
        // Repairing again finds nothing to do.
        Assert.Null(FbxBindPoseFixer.TryFix(repaired!, out var again));
        Assert.Contains("match", again);
    }

    [Fact]
    public void WithoutSkinTheBindPoseStillDecidesAndAConsistentFileIsLeftAlone()
    {
        Assert.Null(FbxBindPoseFixer.TryFix(Fbx(false), out var report));
        Assert.Contains("match", report);
    }
}
