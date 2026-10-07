using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Cleanup;

public class EffectorIkPassTests
{
    private const float Rad2Deg = 180f / MathF.PI;

    // Synthetic 4-bone leg: hip → knee → ankle → toe, with a slight rest bend so the
    // chain's natural bend plane is well defined.
    private static SkeletonModel BuildChain() => SkeletonModel.Create(new[]
    {
        new BoneDefinition("hip", null, new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
        new BoneDefinition("knee", "hip", new XForm(new Vector3(0f, -38f, 10f), Quaternion.Identity)),
        new BoneDefinition("ankle", "knee", new XForm(new Vector3(0f, -37f, -10f), Quaternion.Identity)),
        new BoneDefinition("toe", "ankle", new XForm(new Vector3(0f, -1f, 10f), Quaternion.Identity)),
    });

    private static LimbChain ChainOf(SkeletonModel s) => new()
    {
        Upper = s.IndexOf("hip"),
        Lower = s.IndexOf("knee"),
        End = s.IndexOf("ankle"),
    };

    /// <summary>Frames with mildly varied hip pose so every frame solves a different chain.</summary>
    private static List<XForm[]> MakeFrames(SkeletonModel skeleton, int count)
    {
        var frames = new List<XForm[]>();
        for (int i = 0; i < count; i++)
        {
            var locals = Pose.Rest(skeleton).Locals;
            locals[0] = new XForm(
                new Vector3(0.5f * i, 80f - 0.3f * i, 0.2f * i),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.05f * i));
            frames.Add(locals);
        }
        return frames;
    }

    [Fact]
    public void ReachableGoals_AnkleHitsGoal_AllFrames()
    {
        var skeleton = BuildChain();
        var chain = ChainOf(skeleton);
        var frames = MakeFrames(skeleton, 6);

        var goals = new List<Vector3>();
        for (int i = 0; i < frames.Count; i++)
            goals.Add(new Vector3(5f + i, 12f + 2f * i, 5f - i)); // well within reach (~79 cm)

        var toeLocalsBefore = new XForm[frames.Count];
        var ankleWorldRotBefore = new Quaternion[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            toeLocalsBefore[i] = frames[i][chain.End + 1];
            ankleWorldRotBefore[i] = new Pose(frames[i]).ToWorld(skeleton)[chain.End].Rot;
        }

        EffectorIk.ApplyGoals(frames, skeleton, chain, goals, Vector3.UnitX);

        for (int i = 0; i < frames.Count; i++)
        {
            var world = new Pose(frames[i]).ToWorld(skeleton);

            // Effector lands on the goal.
            float miss = Vector3.Distance(world[chain.End].Pos, goals[i]);
            Assert.True(miss <= 0.05f, $"frame {i}: ankle missed goal by {miss} cm");

            // Ankle WORLD rotation untouched (effector orientation preserved).
            float rotDeltaDeg = MathQ.AngleBetween(world[chain.End].Rot, ankleWorldRotBefore[i]) * Rad2Deg;
            Assert.True(rotDeltaDeg <= 0.1f, $"frame {i}: ankle world rotation disturbed by {rotDeltaDeg}°");

            // Toe local untouched, but its world follows the moved ankle rigidly.
            Assert.Equal(toeLocalsBefore[i], frames[i][chain.End + 1]);
            float toeDist = Vector3.Distance(world[chain.End + 1].Pos, world[chain.End].Pos);
            Assert.InRange(toeDist, 10.0498f - 0.01f, 10.0498f + 0.01f); // |(0,-1,10)|

            // Segment lengths preserved (rotations only — rest-local translations intact).
            float l1 = Vector3.Distance(world[chain.Lower].Pos, world[chain.Upper].Pos);
            float l2 = Vector3.Distance(world[chain.End].Pos, world[chain.Lower].Pos);
            Assert.InRange(l1, 39.293f - 0.01f, 39.293f + 0.01f);  // |(0,-38,10)|
            Assert.InRange(l2, 38.328f - 0.01f, 38.328f + 0.01f);  // |(0,-37,-10)|
        }
    }

    [Fact]
    public void GoalAtRestPosition_IsNearNoOp()
    {
        var skeleton = BuildChain();
        var chain = ChainOf(skeleton);
        var frames = new List<XForm[]> { Pose.Rest(skeleton).Locals };

        var restAnkle = new Pose(frames[0]).ToWorld(skeleton)[chain.End].Pos;
        var before = (XForm[])frames[0].Clone();

        EffectorIk.ApplyGoals(frames, skeleton, chain, new[] { restAnkle }, Vector3.UnitX);

        for (int b = 0; b < skeleton.Count; b++)
        {
            float deg = MathQ.AngleBetween(before[b].Rot, frames[0][b].Rot) * Rad2Deg;
            Assert.True(deg <= 0.01f, $"bone {skeleton[b].Name} rotated {deg}° for an at-rest goal");
            Assert.Equal(before[b].Pos, frames[0][b].Pos);
        }
    }

    [Fact]
    public void ParentlessMidBone_NoCrash_FramesUntouched()
    {
        // The mid (Lower) bone has no parent: the chain assumption is broken and the
        // correction must be skipped entirely instead of indexing world[-1].
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("hip", null, new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
            new BoneDefinition("knee", null, new XForm(new Vector3(0f, 42f, 10f), Quaternion.Identity)),
            new BoneDefinition("ankle", "knee", new XForm(new Vector3(0f, -37f, -10f), Quaternion.Identity)),
        });
        var chain = new LimbChain
        {
            Upper = skeleton.IndexOf("hip"),
            Lower = skeleton.IndexOf("knee"),
            End = skeleton.IndexOf("ankle"),
        };
        var frames = new List<XForm[]> { Pose.Rest(skeleton).Locals };
        var before = (XForm[])frames[0].Clone();

        EffectorIk.ApplyGoals(frames, skeleton, chain, new[] { new Vector3(5f, 10f, 5f) }, Vector3.UnitX);

        for (int b = 0; b < skeleton.Count; b++)
            Assert.Equal(before[b], frames[0][b]);
    }

    [Fact]
    public void ParentlessEndBone_NoCrash_FramesUntouched()
    {
        var skeleton = SkeletonModel.Create(new[]
        {
            new BoneDefinition("hip", null, new XForm(new Vector3(0f, 80f, 0f), Quaternion.Identity)),
            new BoneDefinition("knee", "hip", new XForm(new Vector3(0f, -38f, 10f), Quaternion.Identity)),
            new BoneDefinition("ankle", null, new XForm(new Vector3(0f, 5f, 0f), Quaternion.Identity)),
        });
        var chain = new LimbChain
        {
            Upper = skeleton.IndexOf("hip"),
            Lower = skeleton.IndexOf("knee"),
            End = skeleton.IndexOf("ankle"),
        };
        var frames = new List<XForm[]> { Pose.Rest(skeleton).Locals };
        var before = (XForm[])frames[0].Clone();

        EffectorIk.ApplyGoals(frames, skeleton, chain, new[] { new Vector3(5f, 10f, 5f) }, Vector3.UnitX);

        for (int b = 0; b < skeleton.Count; b++)
            Assert.Equal(before[b], frames[0][b]);
    }

    [Fact]
    public void GoalCountMismatch_Throws()
    {
        var skeleton = BuildChain();
        var frames = MakeFrames(skeleton, 3);
        Assert.Throws<ArgumentException>(() => EffectorIk.ApplyGoals(
            frames, skeleton, ChainOf(skeleton), new[] { Vector3.Zero }, Vector3.UnitX));
    }
}
