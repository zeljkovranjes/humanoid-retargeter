#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.Utils;
using System.IO;
using HumanoidRetargeter.EditorTools.Embedded.Datamodel;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO.ContentFormats.DmxModel;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelAnimation;
using HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.ResourceTypes.ModelFlex;

namespace HumanoidRetargeter.EditorTools.Embedded.ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// Gets the list of animations to be extracted with their output file names.
    /// </summary>
    public List<(Animation Anim, string FileName)> AnimationsToExtract { get; } = [];

    private void EnqueueAnimations()
    {
        if (model != null)
        {
            foreach (var anim in model.GetEmbeddedAnimations())
            {
                AnimationsToExtract.Add((anim, GetDmxFileName_ForAnimation(anim.Name)));
            }
        }
    }

    string GetDmxFileName_ForAnimation(string animationName)
    {
        var fileName = ModelName;
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + animationName
            + ".dmx")
            .Replace('\\', '/');
    }

    /// <summary>
    /// Converts an animation to DMX format.
    /// </summary>
    public static byte[] ToDmxAnim(Model model, Animation anim)
        => ToDmxAnim(model.Skeleton, model.FlexControllers, anim);

    /// <summary>
    /// Converts an animation to DMX format using skeleton and flex controllers.
    /// </summary>
    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, Animation anim)
        => ToDmxAnim(skeleton, flexControllers, anim, skeleton, null, 1f);

    internal static byte[] ToDmxAnim(Skeleton source, FlexController[] flexControllers, Animation anim,
        Skeleton skeleton, Func<Frame, Frame>? transfer, float motionScale)
    {
        using var dmx = new HumanoidRetargeter.EditorTools.Embedded.Datamodel.Datamodel("model", 22);

        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms);

        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip
        {
            FrameRate = anim.Fps
        };

        if (anim.FrameCount > 0)
        {
            clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(anim.FrameCount - 1) / MathF.Max(1f, anim.Fps));

            var frames = new Frame[anim.FrameCount];
            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = new Frame(source, flexControllers)
                {
                    FrameIndex = i
                };
                anim.DecodeFrame(frame);
                frames[i] = transfer == null ? frame : transfer(frame);
            }

            if (anim.HasMovementData())
            {
                ProcessRootMotionChannel(dmeSkeleton, clip, anim.FrameCount, anim.Fps, time =>
                {
                    var movement = anim.GetMovementOffsetData(time);
                    return (movement.Position, movement.Angle);
                }, motionScale);
            }
            ProcessBoneChannels(skeleton, anim.FrameCount, anim.Fps, transforms, clip, frames);
            ProcessFlexChannels(flexControllers, anim.FrameCount, anim.Fps, clip, frames);
        }

        return Save(dmx, dmeSkeleton, animationList, clip);
    }

    /// <summary>
    /// Converts frames already decoded on <paramref name="skeleton"/> (Smart Port's AnimGraph2 clips, which have no
    /// <see cref="Animation"/>) to DMX, like <see cref="ToDmxAnim(Model, Animation)"/>. <paramref name="movement"/>
    /// gives the root motion (position, yaw in degrees) at a time in seconds, or is null for a clip without any.
    /// <paramref name="isModel"/> also names the skeleton the file's model, which skeleton importers look for.
    /// </summary>
    internal static byte[] ToDmxAnim(Skeleton skeleton, Frame[] frames, float fps,
        Func<float, (global::System.Numerics.Vector3 Position, float Angle)>? movement, bool isModel = false)
    {
        using var dmx = new HumanoidRetargeter.EditorTools.Embedded.Datamodel.Datamodel("model", 22);
        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms);
        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip
        {
            FrameRate = fps
        };
        if (frames.Length > 0)
        {
            clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(frames.Length - 1) / MathF.Max(1f, fps));
            if (movement != null)
            {
                ProcessRootMotionChannel(dmeSkeleton, clip, frames.Length, fps, movement, 1f);
            }
            ProcessBoneChannels(skeleton, frames.Length, fps, transforms, clip, frames);
        }
        return Save(dmx, dmeSkeleton, animationList, clip, isModel);
    }

    private static byte[] Save(HumanoidRetargeter.EditorTools.Embedded.Datamodel.Datamodel dmx, DmeModel dmeSkeleton,
        DmeAnimationList animationList, DmeChannelsClip clip, bool isModel = false)
    {
        animationList.Animations.Add(clip);

        using var stream = new MemoryStream();

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeSkeleton,
            ["animationList"] = animationList,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["app"] = "sfm", //modeldoc won't import dmx animations without this
                ["source"] = $"Generated with {StringToken.Generator}",
            }
        };
        if (isModel)
        {
            dmx.Root["model"] = dmeSkeleton;
        }

        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms)
    {
        var dmeSkeleton = new DmeModel();
        var children = new ElementArray();

        transforms = new DmeTransform[skeleton.Bones.Length];
        var boneDags = new DmeJoint[skeleton.Bones.Length];

        dmeSkeleton.JointList.Add(dmeSkeleton);

        foreach (var bone in skeleton.Bones)
        {
            var dag = new DmeJoint
            {
                Name = bone.Name
            };

            dag.Transform.Name = bone.Name;
            dag.Transform.Position = bone.Position;
            dag.Transform.Orientation = bone.Angle;

            boneDags[bone.Index] = dag;
            transforms[bone.Index] = dag.Transform;

            dmeSkeleton.JointList.Add(dag);
        }

        foreach (var bone in skeleton.Bones)
        {
            var boneDag = boneDags[bone.Index];
            if (bone.Parent != null)
            {
                var parentDag = boneDags[bone.Parent.Index];
                parentDag.Children.Add(boneDag);
            }
            else
            {
                dmeSkeleton.Children.Add(boneDag);
            }
        }

        return dmeSkeleton;
    }

    private static DmeChannel BuildDmeChannel<T>(string name, Element toElement, string toAttribute, out DmeLog<T> log)
    {
        var channel = new DmeChannel
        {
            Name = name,
            ToElement = toElement,
            ToAttribute = toAttribute,
            Mode = 3
        };

        log = [];
        var logLayer = new DmeLogLayer<T>();

        channel.Log = log;
        log.AddLayer(logLayer);

        return channel;
    }

    private static void ProcessBoneFrameForDmeChannel(Bone bone, Frame frame, TimeSpan time, DmeLogLayer<global::System.Numerics.Vector3> positionLayer, DmeLogLayer<global::System.Numerics.Quaternion> orientationLayer)
    {
        var frameBone = frame.Bones[bone.Index];

        positionLayer.Times.Add(time);
        positionLayer.LayerValues[frame.FrameIndex] = frameBone.Position;

        orientationLayer.Times.Add(time);
        orientationLayer.LayerValues[frame.FrameIndex] = frameBone.Angle;
    }

    private static void ProcessFlexFrameForDmeChannel(int flexId, Frame frame, TimeSpan time, DmeLogLayer<float> flexLayer)
    {
        var flexValue = frame.Datas[flexId];

        flexLayer.Times.Add(time);
        flexLayer.LayerValues[frame.FrameIndex] = flexValue;
    }

    private static void ProcessRootMotionChannel(DmeModel skeleton, DmeChannelsClip clip, int frameCount, float fps,
        Func<float, (global::System.Numerics.Vector3 Position, float Angle)> movementAt, float motionScale)
    {
        var rootPositionChannel = BuildDmeChannel<global::System.Numerics.Vector3>($"_p", skeleton.Transform, "position", out var rootPositionLog);
        var rootPositionLayer = rootPositionLog.GetLayer(0);
        rootPositionLayer.LayerValues = new global::System.Numerics.Vector3[frameCount];

        var rootOrientationChannel = BuildDmeChannel<global::System.Numerics.Quaternion>($"_o", skeleton.Transform, "orientation", out var rootOrientationLog);
        var rootOrientationLayer = rootOrientationLog.GetLayer(0);
        rootOrientationLayer.LayerValues = new global::System.Numerics.Quaternion[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            var time = i / MathF.Max(1f, fps);
            var timespan = TimeSpan.FromSeconds(time);

            var movement = movementAt(time);

            rootPositionLayer.LayerValues[i] = movement.Position * motionScale;
            rootPositionLayer.Times.Add(timespan);

            var degrees = movement.Angle * 0.0174532925f; //Deg to rad
            rootOrientationLayer.LayerValues[i] = global::System.Numerics.Quaternion.CreateFromAxisAngle(global::System.Numerics.Vector3.UnitZ, degrees);
            rootOrientationLayer.Times.Add(timespan);
        }

        ApplyModelDocHack(rootPositionLayer);

        clip.Channels.Add(rootPositionChannel);
        clip.Channels.Add(rootOrientationChannel);
    }

    private static void ProcessFlexChannels(FlexController[] flexControllers, int frameCount, float fps, DmeChannelsClip clip, Frame[] frames)
    {
        for (var flexId = 0; flexId < flexControllers.Length; flexId++)
        {
            var flexController = flexControllers[flexId];

            var flexElement = new Element
            {
                Name = flexController.Name
            };
            flexElement.Add("flexWeight", 0f);

            var flexChannel = BuildDmeChannel<float>($"{flexController.Name}_flex_channel", flexElement, "flexWeight", out var flexLog);
            var flexLogLayer = flexLog.GetLayer(0);
            flexLogLayer.LayerValues = new float[frameCount];

            for (var i = 0; i < frameCount; i++)
            {
                var frame = frames[i];
                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, fps));
                ProcessFlexFrameForDmeChannel(flexId, frame, time, flexLogLayer);
            }
            clip.Channels.Add(flexChannel);
        }
    }

    private static void ProcessBoneChannels(Skeleton skeleton, int frameCount, float fps, DmeTransform[] transforms, DmeChannelsClip clip, Frame[] frames)
    {
        foreach (var bone in skeleton.Bones)
        {
            var transform = transforms[bone.Index];

            var positionChannel = BuildDmeChannel<global::System.Numerics.Vector3>($"{bone.Name}_p", transform, "position", out var positionLog);
            var orientationChannel = BuildDmeChannel<global::System.Numerics.Quaternion>($"{bone.Name}_o", transform, "orientation", out var orientationLog);

            var positionLogLayer = positionLog.GetLayer(0);
            var orientationLogLayer = orientationLog.GetLayer(0);

            positionLogLayer.LayerValues = new global::System.Numerics.Vector3[frameCount];
            orientationLogLayer.LayerValues = new global::System.Numerics.Quaternion[frameCount];

            for (var i = 0; i < frameCount; i++)
            {
                var frame = frames[i];

                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, fps));

                ProcessBoneFrameForDmeChannel(bone, frame, time, positionLogLayer, orientationLogLayer);
            }

            ApplyModelDocHack(positionLogLayer);

            clip.Channels.Add(positionChannel);
            clip.Channels.Add(orientationChannel);
        }
    }

    /// <summary>
    /// Workaround for ModelDoc ignoring animation data on bone when bone doesn't have any motion
    /// </summary>
    private static void ApplyModelDocHack(DmeLogLayer<global::System.Numerics.Vector3> logLayer)
    {
        // I guess this means there is actually no animation data?
        if (logLayer.LayerValues.Length == 0)
        {
            return;
        }

        if (DoesLayerHaveMotion(logLayer))
        {
            return;
        }

        var newLayerValues = new global::System.Numerics.Vector3[logLayer.LayerValues.Length + 2];
        var newTimes = new TimeSpanArray(newLayerValues.Length);

        var baseValue = logLayer.LayerValues[0];

        newLayerValues[0] = baseValue + new global::System.Numerics.Vector3(0, 0, 0.0001f);
        newLayerValues[1] = baseValue;
        newTimes.Add(TimeSpan.FromSeconds(-0.1f));
        newTimes.Add(TimeSpan.FromSeconds(-0.05f));
        for (var i = 0; i < logLayer.LayerValues.Length; i++)
        {
            newLayerValues[i + 2] = logLayer.LayerValues[i];
            newTimes.Add(logLayer.Times[i]);
        }

        logLayer.LayerValues = newLayerValues;
        logLayer.Times = newTimes;
    }

    private static bool DoesLayerHaveMotion(DmeLogLayer<global::System.Numerics.Vector3> logLayer)
    {
        if (logLayer.LayerValues.Length == 1)
        {
            return false;
        }

        var lastVal = logLayer.LayerValues[0];
        for (var i = 1; i < logLayer.LayerValues.Length; i++)
        {
            var currentVal = logLayer.LayerValues[i];

            if ((lastVal - currentVal).Length() >= 0.01f)
            {
                return true;
            }

            lastVal = currentVal;
        }

        return false;
    }
}
