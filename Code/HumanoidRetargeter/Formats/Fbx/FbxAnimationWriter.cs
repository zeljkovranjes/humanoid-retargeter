#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Skeleton;
using NVector3 = System.Numerics.Vector3;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Formats.Fbx;

/// <summary>Sampled Y-up centimeter animation export for scale-bearing clips that rigid DMX cannot represent.</summary>
public static class FbxAnimationWriter
{
    public static byte[] Write(SkeletonModel skeleton, Clip clip, IReadOnlyList<float[]> scales)
    {
        if (clip.FrameCount == 0 || scales.Count != clip.FrameCount
            || clip.Frames.Any(f => f.Length != skeleton.Count) || scales.Any(f => f.Length != skeleton.Count))
            throw new ArgumentException("Animation and scale frames must match the skeleton.");
        static FbxNode Node(string name, params object[] values)
        {
            var node = new FbxNode(name);
            node.Properties.AddRange(values);
            return node;
        }
        static FbxNode Property(string name, string type, params object[] values)
            => Node("P", new object[] { name, type, "", "A" }.Concat(values).ToArray());
        static FbxNode Vector(string name, NVector3 v) => Property(name, name, (double)v.X, (double)v.Y, (double)v.Z);
        var root = Node("");
        var header = Node("FBXHeaderExtension");
        header.Children.AddRange(new[] { Node("FBXHeaderVersion", 1003), Node("FBXVersion", 7400) });
        root.Children.Add(header);
        var settings = Node("GlobalSettings");
        settings.Children.Add(Node("Version", 1000));
        var properties = Node("Properties70");
        properties.Children.AddRange(new[]
        {
            Property("UpAxis", "int", 1), Property("UpAxisSign", "int", 1),
            Property("FrontAxis", "int", 2), Property("FrontAxisSign", "int", 1),
            Property("CoordAxis", "int", 0), Property("CoordAxisSign", "int", 1),
            Property("OriginalUpAxis", "int", 1), Property("UnitScaleFactor", "double", 1.0),
            Property("OriginalUnitScaleFactor", "double", 1.0),
            Property("TimeMode", "enum", 14), Property("CustomFrameRate", "double", (double)clip.Fps)
        });
        settings.Children.Add(properties);
        root.Children.Add(settings);
        var objects = Node("Objects");
        var connections = Node("Connections");
        long nextId = 1;
        var boneIds = Enumerable.Range(0, skeleton.Count).Select(_ => nextId++).ToArray();
        var stackId = nextId++;
        var layerId = nextId++;
        var times = Enumerable.Range(0, clip.FrameCount).Select(f => (long)Math.Round(f * (double)FbxAnimCurve.TicksPerSecond / clip.Fps)).ToArray();
        var stack = Node("AnimationStack", stackId, clip.Name + "\0\u0001AnimStack", "");
        var span = Node("Properties70");
        span.Children.AddRange(new[] { Property("LocalStart", "KTime", 0L), Property("LocalStop", "KTime", times[^1]),
            Property("ReferenceStart", "KTime", 0L), Property("ReferenceStop", "KTime", times[^1]) });
        stack.Children.Add(span);
        objects.Children.Add(stack);
        objects.Children.Add(Node("AnimationLayer", layerId, "BaseLayer\0\u0001AnimLayer", ""));
        connections.Children.Add(Node("C", "OO", layerId, stackId));
        for (var b = 0; b < skeleton.Count; b++)
        {
            var bone = skeleton[b];
            var model = Node("Model", boneIds[b], bone.Name + "\0\u0001Model", "LimbNode");
            model.Children.Add(Node("Version", 232));
            var bind = Node("Properties70");
            bind.Children.AddRange(new[] { Vector("Lcl Translation", bone.RestLocal.Pos),
                Vector("Lcl Rotation", FbxBindPoseFixer.QuaternionToEulerDegrees(bone.RestLocal.Rot, 0)),
                Vector("Lcl Scaling", NVector3.One), Property("RotationOrder", "enum", 0), Property("InheritType", "enum", 1) });
            model.Children.Add(bind);
            objects.Children.Add(model);
            var attribute = Node("NodeAttribute", nextId++, bone.Name + "\0\u0001NodeAttribute", "LimbNode");
            attribute.Children.Add(Node("TypeFlags", "Skeleton"));
            objects.Children.Add(attribute);
            connections.Children.Add(Node("C", "OO", attribute.Properties[0], boneIds[b]));
            connections.Children.Add(Node("C", "OO", boneIds[b], bone.ParentIndex < 0 ? 0L : boneIds[bone.ParentIndex]));
            foreach (var kind in new[] { "T", "R", "S" })
            {
                var curveNode = Node("AnimationCurveNode", nextId++, kind + "\0\u0001AnimCurveNode", "");
                objects.Children.Add(curveNode);
                connections.Children.Add(Node("C", "OO", curveNode.Properties[0], layerId));
                connections.Children.Add(Node("C", "OP", curveNode.Properties[0], boneIds[b],
                    kind == "T" ? "Lcl Translation" : kind == "R" ? "Lcl Rotation" : "Lcl Scaling"));
                var values = clip.Frames.Select((frame, f) => kind == "T" ? frame[b].Pos
                    : kind == "R" ? FbxBindPoseFixer.QuaternionToEulerDegrees(frame[b].Rot, 0) : new NVector3(scales[f][b])).ToArray();
                var defaults = Node("Properties70");
                curveNode.Children.Add(defaults);
                for (var axis = 0; axis < 3; axis++)
                {
                    var component = "d|" + "XYZ"[axis];
                    var samples = values.Select(v => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z).ToArray();
                    if (kind == "R")
                        for (var f = 1; f < samples.Length; f++) samples[f] += 360f * MathF.Round((samples[f - 1] - samples[f]) / 360f);
                    defaults.Children.Add(Property(component, "Number", (double)samples[0]));
                    var curve = Node("AnimationCurve", nextId++, "\0\u0001AnimCurve", "");
                    curve.Children.AddRange(new[] { Node("Default", (double)samples[0]), Node("KeyVer", 4008),
                        Node("KeyTime", (object)times), Node("KeyValueFloat", (object)samples),
                        Node("KeyAttrFlags", (object)new[] { 4 }), Node("KeyAttrDataFloat", (object)new float[4]),
                        Node("KeyAttrRefCount", (object)new[] { clip.FrameCount }) });
                    objects.Children.Add(curve);
                    connections.Children.Add(Node("C", "OP", curve.Properties[0], curveNode.Properties[0], component));
                }
            }
        }
        root.Children.Add(objects);
        root.Children.Add(connections);
        return FbxBinaryWriter.Write(root);
    }
}
