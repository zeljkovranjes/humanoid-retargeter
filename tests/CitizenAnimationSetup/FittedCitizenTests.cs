using System.Numerics;
using HumanoidRetargeter.Core.Formats.Dmx;
using HumanoidRetargeter.Core.Formats.Fbx;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Target;

public class FittedCitizenTests
{
    private static SkeletonModel Rig(float length, Quaternion rootRotation = default)
        => SkeletonModel.Create(new[]
        {
            new BoneDefinition("pelvis", null, new XForm(new Vector3(0, 0, 10), rootRotation == default ? Quaternion.Identity : rootRotation)),
            new BoneDefinition("hand_L", "pelvis", new XForm(new Vector3(length, 0, 0), Quaternion.Identity))
        });

    [Fact]
    public void FittedHierarchyIsEligibleButDirectCopyStillFails()
    {
        Assert.Null(CitizenAnimationSetup.HierarchyError(Rig(2), Rig(1)));
        Assert.Contains("retargeting", CitizenAnimationSetup.CompatibilityError(Rig(2), Rig(1)));
    }

    [Fact]
    public void IncompleteRigStillFails()
    {
        var incomplete = SkeletonModel.Create(new[] { new BoneDefinition("pelvis", null, XForm.Identity) });
        Assert.Contains("hand_L", CitizenAnimationSetup.HierarchyError(incomplete, Rig(1)));
        Assert.Throws<ArgumentException>(() => new FittedCitizenPose(Rig(1), incomplete));
    }

    [Fact]
    public void InvalidBindCannotEnableRetargeting()
        => Assert.NotNull(CitizenAnimationSetup.HierarchyError(Rig(float.NaN), Rig(1)));

    [Fact]
    public void RestMapsToFittedRestWithoutChangingEitherSkeleton()
    {
        var source = Rig(1);
        var target = Rig(3, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .7f));
        var frame = source.Bones.Select(b => b.RestLocal).ToArray();
        var output = new FittedCitizenPose(source, target).Transfer(frame);
        for (var i = 0; i < target.Count; i++)
        {
            Assert.True(Vector3.Distance(target[i].RestLocal.Pos, output[i].Pos) < .00001f);
            Assert.True(MathQ.AngleBetween(target[i].RestLocal.Rot, output[i].Rot) < .0001f);
            Assert.Equal(source[i].RestLocal, frame[i]);
        }
    }

    [Fact]
    public void RotationDeltasAreReexpressedInFittedParentBasis()
    {
        var source = Rig(1);
        var target = Rig(2, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        var frame = source.Bones.Select(b => b.RestLocal).ToArray();
        frame[1].Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .5f);
        var result = new FittedCitizenPose(source, target).Transfer(frame);
        Assert.True(MathQ.AngleBetween(result[1].Rot, Quaternion.CreateFromAxisAngle(-Vector3.UnitY, .5f)) < .0001f);
        Assert.Equal(target[1].RestLocal.Pos, result[1].Pos);
    }

    [Fact]
    public void AuthoredTranslationDeltasScaleWithFittedLength()
    {
        var source = Rig(1);
        var frame = source.Bones.Select(b => b.RestLocal).ToArray();
        frame[1].Pos.X += .1f;
        var output = new FittedCitizenPose(source, Rig(2)).Transfer(frame);
        Assert.Equal(2.2f, output[1].Pos.X, 5);
    }

    [Fact]
    public void TPoseBoundArmFollowsTheStockArmDirection()
    {
        // Blender characters are usually bound in a T-pose while the Citizen binds in an A-pose.
        // The fitted arm must point where the stock arm points, not keep the T-pose offset.
        static SkeletonModel Arm(float downDegrees) => SkeletonModel.Create(new[]
        {
            new BoneDefinition("pelvis", null, new XForm(new Vector3(0, 0, 10), Quaternion.Identity)),
            new BoneDefinition("arm_upper_L", "pelvis", new XForm(new Vector3(0, 2, 5),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, downDegrees * MathF.PI / 180f))),
            new BoneDefinition("arm_lower_L", "arm_upper_L", new XForm(new Vector3(3, 0, 0), Quaternion.Identity)),
        });
        var source = Arm(50);
        var target = Arm(0);
        foreach (var bend in new[] { 0f, .6f })
        {
            var frame = source.Bones.Select(b => b.RestLocal).ToArray();
            frame[1].Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bend) * frame[1].Rot);
            var output = new FittedCitizenPose(source, target).Transfer(frame);
            Vector3 Direction(SkeletonModel rig, XForm[] pose)
            {
                var upper = XForm.Compose(pose[0], pose[1]);
                return Vector3.Normalize(XForm.Compose(upper, pose[2]).Pos - upper.Pos);
            }
            Assert.True(Vector3.Dot(Direction(source, frame), Direction(target, output)) > .9999f,
                $"bend {bend}: fitted arm points {Direction(target, output)}, stock arm {Direction(source, frame)}");
        }
    }

    [Fact]
    public void PinkyFollowsTheRingFingerWhenStockClipsHaveNoPinkyMotion()
    {
        // Stock Citizen clips curl the ring finger only; the stock model copies it onto the pinky.
        var hand = SkeletonModel.Create(new[]
        {
            new BoneDefinition("hand_L", null, XForm.Identity),
            new BoneDefinition("finger_ring_0_L", "hand_L", new XForm(new Vector3(1, 0, 0), Quaternion.Identity)),
            new BoneDefinition("finger_pinky_0_L", "hand_L", new XForm(new Vector3(1, -1, 0), Quaternion.Identity)),
        });
        var frame = hand.Bones.Select(b => b.RestLocal).ToArray();
        frame[1].Rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f);
        var copied = new FittedCitizenPose(hand, hand, copyRingToPinky: true).Transfer(frame);
        Assert.True(MathQ.AngleBetween(copied[2].Rot, frame[1].Rot) < .0001f);
        var independent = new FittedCitizenPose(hand, hand).Transfer(frame);
        Assert.True(MathQ.AngleBetween(independent[2].Rot, Quaternion.Identity) < .0001f);
    }

    private static string Document(string children, string extra = "")
        => VmdlWriter.Kv3Header + "{ rootNode = { children = [ " + children + " ] " + extra + " } }";

    private static string Animations => Document("""
        { _class = "AnimationList" children = [
          { _class = "Prefab" target_file = "stock.vmdl_prefab" }
        ] }
        """);

    private static string Prefab => Document("""
        { _class = "AnimationList" children = [
          { _class = "AnimFile" name = "walk" source_filename = "walk.fbx" take = 1
            framerate = -1.0 start_frame = 3 end_frame = 14 reverse = true looping = true
            anim_hold_list = [ { frame = 5 frame_count = 2 } ]
            children = [ { _class = "AnimSubtract" anim_name = "idle" frame = 0 },
                         { _class = "Prefab" target_file = "events.vmdl_prefab" } ] },
          { _class = "AnimFile" name = "fast" source_filename = "walk.fbx" take = 1 framerate = 60.0 },
          { _class = "AnimFile" name = "other_take" source_filename = "walk.fbx" take = 2 },
          { _class = "AnimFile" name = "disabled" source_filename = "missing.fbx" disabled = true }
        ] }
        """);

    private static CitizenAnimationSources Plan() => new(Animations, path => path == "stock.vmdl_prefab"
        ? Prefab : Document("{ _class = \"AnimEvent\" event_class = \"AE_FOOTSTEP\" event_frame = 5 }"));

    [Fact]
    public void SourcesDeduplicateByFileAndTakeNotSequenceAndSkipDisabledNodes()
    {
        var plan = Plan();
        Assert.Equal(2, plan.Sources.Count);
        Assert.Equal(2, plan.Sources[0].Users.Count);
        Assert.Equal(1, plan.Sources[0].Take);
        Assert.Equal(2, plan.Sources[1].Take);
    }

    [Fact]
    public void RetargetingPreservesAllSequenceProcessingTimingAndEvents()
    {
        var plan = Plan();
        var first = plan.Sources[0].Users[0];
        var before = first.Keys.ToDictionary(k => k, k => first[k]);
        plan.Sources[0].SetConvertedFile("project/walk.dmx", 24);
        foreach (var key in before.Keys.Except(new[] { "source_filename", "take", "framerate" }))
            Assert.True(KvValue.DeepEquals(before[key], first[key]), key);
        Assert.Equal(0, ((KvLong)first["take"]).Value);
        Assert.Equal(24, ((KvDouble)first["framerate"]).Value);
        Assert.Equal(60, ((KvDouble)plan.Sources[0].Users[1]["framerate"]).Value);
        Assert.Contains("events.vmdl_prefab", plan.ModelText);
        Assert.Contains("AnimSubtract", plan.ModelText);
    }

    [Fact]
    public void RawSamplingDoesNotBakeAdditiveEventsGraphOrConstraintsTwice()
    {
        var source = Document("""
            { _class = "RenderMeshList" children = [ { _class = "Prefab" target_file = "mesh.vmdl_prefab" } ] },
            { _class = "AnimationList" children = [] },
            { _class = "AnimConstraintList" children = [] }
            """, "anim_graph_name = \"stock.vanmgrph\"");
        var raw = Plan().SamplingModel(source);
        Assert.DoesNotContain("AnimSubtract", raw);
        Assert.DoesNotContain("AnimConstraintList", raw);
        Assert.DoesNotContain("stock.vanmgrph", raw);
        Assert.Contains("mesh.vmdl_prefab", raw);
        Assert.Contains("source_0000_frames", raw);
        Assert.Contains("framerate = 1.0", raw);
        Assert.Contains("take = 2", raw);
    }

    [Fact]
    public void CyclicPrefabsFailInsteadOfRecursingForever()
        => Assert.Throws<FormatException>(() => new CitizenAnimationSources(Animations, _ => Animations));

    [Fact]
    public void PrefabRootSiblingFoldersAndFolderOnlyPrefabsAreNotDropped()
    {
        var prefab = Document("""
            { _class = "AnimationList" children = [] },
            { _class = "Folder" name = "extras" children = [
              { _class = "Prefab" target_file = "folder_only.vmdl_prefab" }
            ] }
            """);
        var folderOnly = Document("""
            { _class = "Folder" name = "debug" children = [
              { _class = "AnimFile" name = "scaling" source_filename = "scaling.fbx" enable_scale = true }
            ] }
            """);
        var plan = new CitizenAnimationSources(Animations, path => path == "stock.vmdl_prefab" ? prefab : folderOnly);
        Assert.Single(plan.Sources);
        Assert.Contains("extras", plan.ModelText);
        Assert.Contains("debug", plan.ModelText);
        Assert.Contains("enable_scale = true", plan.ModelText);
    }

    [Fact]
    public void FittedSettingsArePreservedAndCopyPinkyProtected()
    {
        var custom = Document("""
            { _class = "ModelModifierList" children = [ { _class = "ModelModifier_ScaleAndMirror" scale = 0.3937 } ] },
            { _class = "AttachmentList" children = [ { _class = "Attachment" name = "fitted" relative_origin = [ 1, 2, 3 ] } ] },
            { _class = "AnimConstraintList" children = [ { _class = "Folder" name = "CopyPinky" children = [] } ] }
            """);
        var stock = Document("""
            { _class = "AnimationList" children = [ { _class = "Prefab" target_file = "animations.vmdl_prefab" } ] },
            { _class = "AttachmentList" children = [ { _class = "Attachment" name = "stock" } ] },
            { _class = "AnimConstraintList" children = [] }
            """, "anim_graph_name = \"stock.vanmgrph\"");
        Assert.Throws<InvalidOperationException>(() => CitizenAnimationSetup.Apply(custom, stock));
        var result = CitizenAnimationSetup.Apply(custom, stock, preserveFittedSettings: true);
        Assert.Contains("fitted", result);
        Assert.DoesNotContain("name = \"stock\"", result);
        Assert.Contains("HumanoidRetargeter_CitizenConstraints", result);
        Assert.Contains("CopyPinky", result);
    }

    [Fact]
    public void ScaleBearingFbxKeepsAllChannelsAndRejectsMismatchedFrames()
    {
        var skeleton = Rig(1);
        var clip = new Clip("scale", 30, false, new() { skeleton.Bones.Select(b => b.RestLocal).ToArray() });
        Assert.DoesNotContain("pelvis_s", DmxWriter.Write(skeleton, clip, new()));
        var fbx = FbxAnimationWriter.Write(skeleton, clip, new[] { new[] { .5f, 1f } });
        var scene = FbxScene.Build(FbxTokenizer.Parse(fbx));
        Assert.Single(scene.Stacks);
        Assert.Equal(clip.Name, FbxImporter.Import(fbx).Clips.Single().Name);
        Assert.Equal(skeleton.Count, scene.Models.Count);
        var curves = FbxTokenizer.Parse(fbx).Child("Objects")!.ChildrenNamed("AnimationCurve").ToArray();
        Assert.Equal(skeleton.Count * 9, curves.Length);
        Assert.Equal(.5f, curves[6].Child("KeyValueFloat")!.AsFloatArray(0)[0]);
        Assert.Throws<ArgumentException>(() => FbxAnimationWriter.Write(skeleton, clip, Array.Empty<float[]>()));
    }

    [Fact]
    public void SampledFbxRoundTripsMotionAndNativeFrameGrid()
    {
        var skeleton = Rig(1);
        var first = skeleton.Bones.Select(b => b.RestLocal).ToArray();
        var second = (XForm[])first.Clone();
        second[0].Pos += new Vector3(2, 3, 4);
        second[1].Rot = Quaternion.CreateFromYawPitchRoll(.2f, .3f, -.7f);
        var clip = new Clip("motion", 30, false, new() { first, second });
        var fbx = FbxAnimationWriter.Write(skeleton, clip, new[] { new[] { 1f, 1f }, new[] { 1f, 1f } });
        var imported = FbxImporter.Import(fbx).Clips.Single();
        Assert.Equal(2, imported.FrameCount);
        for (var f = 0; f < 2; f++)
        for (var b = 0; b < 2; b++)
        {
            Assert.True(Vector3.Distance(clip.Frames[f][b].Pos, imported.Frames[f][b].Pos) < .0001f);
            Assert.True(MathQ.AngleBetween(clip.Frames[f][b].Rot, imported.Frames[f][b].Rot) < .001f);
        }
    }
}
