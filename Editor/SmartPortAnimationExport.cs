#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation;
using VrfSkeleton = HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation.Skeleton;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Retarget during recovery, retaining sequence metadata, root motion and additive channels.</summary>
internal sealed class SmartPortAnimationExport
{
    readonly SmartPortRig rig;
    readonly VrfSkeleton skeleton;

    internal SmartPortAnimationExport(SmartPortRig rig)
    {
        this.rig = rig;
        var bones = rig.Target.Bones.Select(b => new Bone(b.Index, b.Name, b.RestLocal.Pos, b.RestLocal.Rot,
            ModelSkeletonBoneFlags.NoBoneFlags)).ToArray();
        foreach (var bone in rig.Target.Bones)
            if (bone.ParentIndex >= 0) bones[bone.Index].SetParent(bones[bone.ParentIndex]);
        skeleton = VrfSkeleton.FromBones(bones);
    }

    internal byte[] Write(Model model, Animation animation)
    {
        var indices = rig.Source.Bones.Select(b => Array.FindIndex(model.Skeleton.Bones, s => s.Name == b.Name)).ToArray();
        if (indices.Any(i => i < 0)) throw new InvalidOperationException("Compiled source skeleton changed during Smart Port.");
        var delta = IsAdditive(model, animation, rig, indices);
        return ModelExtract.ToDmxAnim(model.Skeleton, model.FlexControllers, animation, skeleton, frame =>
        {
            var input = indices.Select(i => new XForm(frame.Bones[i].Position, frame.Bones[i].Angle)).ToArray();
            var fitted = rig.Transfer(input, delta);
            var output = new Frame(skeleton, model.FlexControllers) { FrameIndex = frame.FrameIndex };
            Array.Copy(frame.Datas, output.Datas, frame.Datas.Length);
            for (var i = 0; i < fitted.Length; i++)
            {
                output.Bones[i].Position = fitted[i].Pos;
                output.Bones[i].Angle = fitted[i].Rot;
            }
            return output;
        }, rig.MotionScale);
    }

    /// <summary>
    /// Retargets frames sampled outside a compiled model (an AnimGraph2 clip): <paramref name="frames"/> holds each
    /// frame's local transforms in <see cref="SmartPortRig.Source"/> bone order, deltas when <paramref name="additive"/>.
    /// <paramref name="movement"/> is the source root motion (position, yaw in degrees) at a time in seconds, or null.
    /// </summary>
    internal byte[] Write(XForm[][] frames, float fps, bool additive, Func<float, (System.Numerics.Vector3 Position, float Angle)>? movement)
    {
        var output = new Frame[frames.Length];
        for (var f = 0; f < frames.Length; f++)
            output[f] = Pose(rig.Transfer(frames[f], additive), f);
        return ModelExtract.ToDmxAnim(skeleton, output, fps,
            movement is null ? null : time => { var m = movement(time); return (m.Position * rig.MotionScale, m.Angle); });
    }

    /// <summary>A one-frame DMX of the target skeleton in its bind pose, or with every bone at the identity when
    /// <paramref name="identity"/> (the base an already-additive clip is made relative to).</summary>
    internal byte[] Write(bool identity)
        => ModelExtract.ToDmxAnim(skeleton, new[] { Pose(rig.Target.Bones.Select(b => identity ? XForm.Identity : b.RestLocal).ToArray(), 0) }, 1f, null,
            isModel: !identity);

    Frame Pose(XForm[] bones, int index)
    {
        var frame = new Frame(skeleton, Array.Empty<HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelFlex.FlexController>()) { FrameIndex = index };
        for (var i = 0; i < bones.Length; i++)
        {
            frame.Bones[i].Position = bones[i].Pos;
            frame.Bones[i].Angle = bones[i].Rot;
        }
        return frame;
    }

    internal static SmartPortClip[] ReadClips(string compiledPath, SmartPortRig rig)
    {
        using var resource = new Resource();
        resource.Read(compiledPath);
        var model = (Model)resource.DataBlock!;
        var indices = rig.Source.Bones.Select(b => Array.FindIndex(model.Skeleton.Bones, s => s.Name == b.Name)).ToArray();
        return model.GetEmbeddedAnimations().Select(a => new SmartPortClip(a.Name, a.IsLooping,
            IsAdditive(model, a, rig, indices), a.Hidden || a.Worldspace)).ToArray();
    }

    /// <summary>
    /// Each sequence's additive flag (as <see cref="ReadClips"/>), frame count and <see cref="SmartPortRig.Source"/>-ordered
    /// local poses at the <see cref="SampleFrames"/>: what tells whether another source of an animation of the same name (an
    /// AnimGraph2 clip, which may take another frame range of the same art) holds the same data.
    /// </summary>
    internal static Dictionary<string, (bool Additive, int FrameCount, XForm[][] Poses)> ReadSamples(string compiledPath, SmartPortRig rig)
    {
        using var resource = new Resource();
        resource.Read(compiledPath);
        var model = (Model)resource.DataBlock!;
        var indices = rig.Source.Bones.Select(b => Array.FindIndex(model.Skeleton.Bones, s => s.Name == b.Name)).ToArray();
        var samples = new Dictionary<string, (bool, int, XForm[][])>(StringComparer.Ordinal);
        foreach (var animation in model.GetEmbeddedAnimations())
            samples.TryAdd(animation.Name, (IsAdditive(model, animation, rig, indices), animation.FrameCount,
                SampleFrames(animation.FrameCount).Select(f =>
                {
                    var frame = new Frame(model.Skeleton, model.FlexControllers) { FrameIndex = f };
                    animation.DecodeFrame(frame);
                    return indices.Select(i => i < 0 ? XForm.Identity : new XForm(frame.Bones[i].Position, frame.Bones[i].Angle)).ToArray();
                }).ToArray()));
        return samples;
    }

    /// <summary>The frames <see cref="ReadSamples"/> compares: the first, middle and last.</summary>
    internal static int[] SampleFrames(int frameCount) => new[] { 0, Math.Max(frameCount - 1, 0) / 2, Math.Max(frameCount - 1, 0) };

    static bool IsAdditive(Model model, Animation animation, SmartPortRig rig, int[] indices)
    {
        var probe = new Frame(model.Skeleton, model.FlexControllers) { FrameIndex = 0 };
        animation.DecodeFrame(probe);
        return animation.Delta || rig.IsDeltaPose(indices.Select(i => new XForm(probe.Bones[i].Position, probe.Bones[i].Angle)).ToArray());
    }
}
