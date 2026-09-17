#nullable enable
using System;
using System.Linq;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using HumanoidRetargeterVrf.IO;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.ResourceTypes;
using HumanoidRetargeterVrf.ResourceTypes.ModelAnimation;
using VrfSkeleton = HumanoidRetargeterVrf.ResourceTypes.ModelAnimation.Skeleton;

namespace HumanoidRetargeter.Editor;

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

    internal static SmartPortClip[] ReadClips(string compiledPath, SmartPortRig rig)
    {
        using var resource = new Resource();
        resource.Read(compiledPath);
        var model = (Model)resource.DataBlock!;
        var indices = rig.Source.Bones.Select(b => Array.FindIndex(model.Skeleton.Bones, s => s.Name == b.Name)).ToArray();
        return model.GetEmbeddedAnimations().Select(a => new SmartPortClip(a.Name, a.IsLooping,
            IsAdditive(model, a, rig, indices), a.Hidden || a.Worldspace)).ToArray();
    }

    static bool IsAdditive(Model model, Animation animation, SmartPortRig rig, int[] indices)
    {
        var probe = new Frame(model.Skeleton, model.FlexControllers) { FrameIndex = 0 };
        animation.DecodeFrame(probe);
        return animation.Delta || rig.IsDeltaPose(indices.Select(i => new XForm(probe.Bones[i].Position, probe.Bones[i].Angle)).ToArray());
    }
}
