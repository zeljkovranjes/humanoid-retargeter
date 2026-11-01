using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Solve;

/// <summary>
/// <see cref="PlanarSolver"/> through the facade, on the synthetic walk: the left thigh
/// swings 30 degrees backward at frame 7 (toes point +Z, the swing goes to -Z), the hips sway
/// ±1.5 cm along the lateral axis, and the BVH rest has no placement (hips at 0, clip at 95).
/// </summary>
public class PlanarSolverTests
{
    private const int PeakFrame = 7;
    private static readonly float Sin30 = MathF.Sin(MathF.PI / 6f);
    private static readonly float Cos30 = MathF.Cos(MathF.PI / 6f);

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "humanoid-retargeter.sbproj")))
                return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repo root (humanoid-retargeter.sbproj) not found.");
    }

    private static readonly Lazy<RetargetTargetSpec> SboxTarget = new(()
        => RetargetTargetSpec.SboxDefault(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox.json"))));

    private static RetargetRequest Walk(PlanarView view, bool foreshorten = true,
        RootMotionMode rootMotion = RootMotionMode.Off, bool mirrored = false) => new()
    {
        SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
        SourceFileName = "synth_walk.bvh",
        Solver = SolverKind.Planar,
        Solve = new SolveOptions { View = view, Foreshorten = foreshorten },
        RootMotion = rootMotion,
        CreateMirroredVariant = mirrored,
    };

    private static ClipResult Convert(RetargetRequest request, RetargetTargetSpec target)
    {
        var clip = Retargeter.Convert(request, target).Clips[0];
        Assert.True(clip.Success, clip.Error);
        return clip;
    }

    private static XForm[] World(ClipResult clip, TargetRig rig, int frame)
        => new Pose(clip.SolvedFrames![frame]).ToWorld(rig.Skeleton);

    /// <summary>The rig's character frame around true vertical, which is what the view uses.</summary>
    private static CharacterFrame FrameOf(TargetRig rig)
    {
        var up = CharacterFrame.Compute(rig.Skeleton, rig.ToMappingResult(), rig.Skeleton.RestWorld).Up;
        var size = Vector3.Abs(up);
        var vertical = size.Y >= size.Z ? new Vector3(0f, MathF.Sign(up.Y), 0f) : new Vector3(0f, 0f, MathF.Sign(up.Z));
        return CharacterFrame.Compute(rig.Skeleton, rig.ToMappingResult(), rig.Skeleton.RestWorld, vertical);
    }

    private static Vector3 Between(TargetRig rig, XForm[] world, BoneRole from, BoneRole to)
        => world[rig.BoneForRole(to)!.Value].Pos - world[rig.BoneForRole(from)!.Value].Pos;

    /// <summary>A T-pose 3D rig: the source's own skeleton as a custom target.</summary>
    private static RetargetTargetSpec TPoseTarget()
    {
        var scene = Retargeter.ResolveSource(HumanoidRetargeter.Core.Formats.Bvh.BvhImporter.Import(
            Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh())));
        return new RetargetTargetSpec
        {
            Rig = TargetRig.FromSkeleton(scene.Scene.Skeleton, scene.Mapping),
            VmdlScale = 1f,
        };
    }

    /// <summary>Joint positions of a humanoid drawn in the XY plane.</summary>
    private static RetargetTargetSpec FlatTarget(bool profile, float facing = 1f, float lean = 0f, float legs = 1f)
    {
        var hip = profile ? 0f : 8f;
        var shoulder = profile ? 0f : 16f;
        var toe = profile ? facing * 12f : 0f;
        var bones = new (BoneRole Role, BoneRole? Parent, Vector3 Pos)[]
        {
            (BoneRole.Hips, null, new(0, 100, 0)),
            (BoneRole.Spine1, BoneRole.Hips, new(0, 125, 0)),
            (BoneRole.Spine2, BoneRole.Spine1, new(0, 150, lean)),
            (BoneRole.Neck, BoneRole.Spine2, new(0, 165, lean)),
            (BoneRole.Head, BoneRole.Neck, new(0, 175, lean)),
            (BoneRole.UpperArmL, BoneRole.Spine2, new(shoulder, 160, lean)),
            (BoneRole.LowerArmL, BoneRole.UpperArmL, new(shoulder, 130, lean)),
            (BoneRole.HandL, BoneRole.LowerArmL, new(shoulder, 105, lean)),
            (BoneRole.UpperArmR, BoneRole.Spine2, new(-shoulder, 160, lean)),
            (BoneRole.LowerArmR, BoneRole.UpperArmR, new(-shoulder, 130, lean)),
            (BoneRole.HandR, BoneRole.LowerArmR, new(-shoulder, 105, lean)),
            (BoneRole.UpperLegL, BoneRole.Hips, new(hip, 95, 0)),
            (BoneRole.LowerLegL, BoneRole.UpperLegL, new(hip, 95 - 45 * legs, 0)),
            (BoneRole.FootL, BoneRole.LowerLegL, new(hip, 95 - 89 * legs, 0)),
            (BoneRole.ToeL, BoneRole.FootL, new(hip + toe, 95 - 95 * legs, 0)),
            (BoneRole.UpperLegR, BoneRole.Hips, new(-hip, 95, 0)),
            (BoneRole.LowerLegR, BoneRole.UpperLegR, new(-hip, 95 - 45 * legs, 0)),
            (BoneRole.FootR, BoneRole.LowerLegR, new(-hip, 95 - 89 * legs, 0)),
            (BoneRole.ToeR, BoneRole.FootR, new(-hip + toe, 95 - 95 * legs, 0)),
        };
        var positions = bones.ToDictionary(b => b.Role, b => b.Pos);
        var skeleton = SkeletonModel.Create(bones.Select(b => new BoneDefinition(
            b.Role.ToString(), b.Parent?.ToString(),
            new XForm(b.Pos - (b.Parent is { } parent ? positions[parent] : Vector3.Zero), Quaternion.Identity))).ToList());
        var map = new MappingResult("flat", MappingSource.Preset);
        foreach (var (role, _, _) in bones)
            map.RoleToBone[role] = skeleton.IndexOf(role.ToString());
        return new RetargetTargetSpec { Rig = TargetRig.FromSkeleton(skeleton, map), VmdlScale = 1f };
    }

    private static readonly (BoneRole From, BoneRole To)[] Segments =
    {
        (BoneRole.UpperLegL, BoneRole.LowerLegL), (BoneRole.LowerLegL, BoneRole.FootL),
        (BoneRole.UpperLegR, BoneRole.LowerLegR), (BoneRole.LowerLegR, BoneRole.FootR),
        (BoneRole.UpperArmL, BoneRole.LowerArmL), (BoneRole.LowerArmL, BoneRole.HandL),
        (BoneRole.UpperArmR, BoneRole.LowerArmR), (BoneRole.LowerArmR, BoneRole.HandR),
        (BoneRole.Hips, BoneRole.Head),
    };

    public static TheoryData<string> ThreeDimensionalRigs => new() { "sbox", "citizen", "tpose" };

    private static RetargetTargetSpec Rig3d(string name) => name switch
    {
        "sbox" => SboxTarget.Value,
        "citizen" => RetargetTargetSpec.SboxCitizen(
            File.ReadAllText(RepoFile("Assets", "data", "humanoid_retargeter", "target_rig_sbox_citizen.json"))),
        _ => TPoseTarget(),
    };

    [Theory]
    [MemberData(nameof(ThreeDimensionalRigs))]
    public void Side_KeepsEveryChainParallelToThePlane(string name)
    {
        var target = Rig3d(name);
        var rig = target.Rig;
        var clip = Convert(Walk(PlanarView.Side), target);
        var lateral = FrameOf(rig).Lateral;

        for (var f = 0; f < clip.SolvedFrames!.Count; f++)
        {
            var world = World(clip, rig, f);
            foreach (var (from, to) in Segments)
                Assert.True(MathF.Abs(Vector3.Dot(Between(rig, world, from, to), lateral)) < 1e-2f,
                    $"{from}->{to} leaves the plane on frame {f}");
        }
    }

    [Theory]
    [MemberData(nameof(ThreeDimensionalRigs))]
    public void Side_TheThighTakesTheSourceSwing(string name)
    {
        var target = Rig3d(name);
        var rig = target.Rig;
        var frame = FrameOf(rig);
        var clip = Convert(Walk(PlanarView.Side), target);

        var thigh = Vector3.Normalize(Between(rig, World(clip, rig, PeakFrame), BoneRole.UpperLegL, BoneRole.LowerLegL));

        Assert.Equal(-Sin30, Vector3.Dot(thigh, frame.Forward), 2);
        Assert.Equal(-Cos30, Vector3.Dot(thigh, frame.Up), 2);
    }

    [Fact]
    public void Side_FollowsTheWayTheClipFacesNotTheRest()
    {
        var rig = SboxTarget.Value.Rig;
        var frame = FrameOf(rig);
        var scene = HumanoidRetargeter.Core.Formats.Bvh.BvhImporter.Import(
            Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()));
        var map = Retargeter.ResolveSource(scene).Mapping;

        // The performer stands a quarter turn from its T-pose, as mocap usually does.
        var hips = map.RoleToBone[BoneRole.Hips];
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        foreach (var locals in scene.Clips[0].Frames)
            locals[hips] = new XForm(locals[hips].Pos, quarterTurn * locals[hips].Rot);

        var solved = new PlanarSolver().Solve(scene, map, rig, new SolveOptions { View = PlanarView.Side });

        var world = new Pose(solved.Frames[PeakFrame]).ToWorld(rig.Skeleton);
        var thigh = Vector3.Normalize(Between(rig, world, BoneRole.UpperLegL, BoneRole.LowerLegL));
        Assert.Equal(-Sin30, Vector3.Dot(thigh, frame.Forward), 2);
    }

    [Fact]
    public void AFlipThroughTheViewAxisTurnsNoFasterThanTheLimit()
    {
        var rig = SboxTarget.Value.Rig;
        var scene = HumanoidRetargeter.Core.Formats.Bvh.BvhImporter.Import(
            Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()));
        var map = Retargeter.ResolveSource(scene).Mapping;

        // The thigh points at the camera, just to one side and then just to the other, from
        // one frame to the next: well above the hold ratio, its projection flips 180 degrees.
        var thigh = map.RoleToBone[BoneRole.UpperLegL];
        var frames = scene.Clips[0].Frames;
        for (var f = 0; f < frames.Count; f++)
        {
            var aim = new Vector3(f < frames.Count / 2 ? 0.5f : -0.5f, 0f, 1f);
            frames[f][thigh] = new XForm(frames[f][thigh].Pos, MathQ.FromTo(new Vector3(0f, -1f, 0f), aim));
        }

        var solved = new PlanarSolver().Solve(scene, map, rig, new SolveOptions { View = PlanarView.Front });

        var bone = rig.BoneForRole(BoneRole.UpperLegL)!.Value;
        var limit = 10f * MathF.PI / solved.Fps + 1e-3f;
        for (var f = 1; f < solved.Frames.Count; f++)
        {
            var a = new Pose(solved.Frames[f - 1]).ToWorld(rig.Skeleton)[bone].Rot;
            var b = new Pose(solved.Frames[f]).ToWorld(rig.Skeleton)[bone].Rot;
            Assert.True(MathQ.AngleBetween(a, b) <= limit, $"frame {f} turned past the limit");
        }
    }

    [Fact]
    public void Front_ForeshortensALegSwingingAlongTheViewAxis()
    {
        var rig = SboxTarget.Value.Rig;
        var restThigh = Between(rig, rig.Skeleton.RestWorld.ToArray(), BoneRole.UpperLegL, BoneRole.LowerLegL).Length();

        var shortened = Convert(Walk(PlanarView.Front), SboxTarget.Value);
        var full = Convert(Walk(PlanarView.Front, foreshorten: false), SboxTarget.Value);

        Assert.Equal(restThigh * Cos30,
            Between(rig, World(shortened, rig, PeakFrame), BoneRole.UpperLegL, BoneRole.LowerLegL).Length(), 1);
        Assert.Equal(restThigh,
            Between(rig, World(full, rig, PeakFrame), BoneRole.UpperLegL, BoneRole.LowerLegL).Length(), 1);
    }

    [Fact]
    public void ForeshorteningReachesTheDmxAsAJointTranslation()
    {
        var rig = SboxTarget.Value.Rig;
        var knee = rig.BoneForRole(BoneRole.LowerLegL)!.Value;
        var shortened = Convert(Walk(PlanarView.Front), SboxTarget.Value);
        var full = Convert(Walk(PlanarView.Front, foreshorten: false), SboxTarget.Value);

        var local = shortened.SolvedFrames![PeakFrame][knee].Pos;
        Assert.NotEqual(full.SolvedFrames![PeakFrame][knee].Pos.Length(), local.Length(), 1);
        Assert.NotEqual(full.DmxContent, shortened.DmxContent);
    }

    [Fact]
    public void Front_AFootPointingAtTheCameraKeepsItsLength()
    {
        var rig = SboxTarget.Value.Rig;
        var rest = Between(rig, rig.Skeleton.RestWorld.ToArray(), BoneRole.FootL, BoneRole.ToeL).Length();
        var clip = Convert(Walk(PlanarView.Front), SboxTarget.Value);

        Assert.Equal(rest, Between(rig, World(clip, rig, 20), BoneRole.FootL, BoneRole.ToeL).Length(), 1);
    }

    [Fact]
    public void AClipWithoutRestPlacementStartsStandingOnTheRig()
    {
        var rig = SboxTarget.Value.Rig;
        var hips = rig.BoneForRole(BoneRole.Hips)!.Value;
        var clip = Convert(Walk(PlanarView.Side), SboxTarget.Value);

        // Rest hips 0 and clip hips 95 in the BVH: naively that floats the rig 95 cm.
        var lift = Vector3.Dot(World(clip, rig, 20)[hips].Pos - rig.Skeleton.RestWorld[hips].Pos, FrameOf(rig).Up);
        Assert.InRange(lift, -8f, 8f);
    }

    [Fact]
    public void InPlace_HoldsTheHipsOnTheGroundPlane()
    {
        var rig = SboxTarget.Value.Rig;
        var hips = rig.BoneForRole(BoneRole.Hips)!.Value;
        var right = FrameOf(rig).Lateral;
        var travel = Convert(Walk(PlanarView.Front), SboxTarget.Value);
        var inPlace = Convert(Walk(PlanarView.Front, rootMotion: RootMotionMode.InPlace), SboxTarget.Value);

        float Sway(ClipResult clip)
        {
            var values = Enumerable.Range(0, clip.SolvedFrames!.Count)
                .Select(f => Vector3.Dot(World(clip, rig, f)[hips].Pos, right)).ToList();
            return values.Max() - values.Min();
        }

        Assert.True(Sway(travel) > 1f);
        Assert.True(Sway(inPlace) < 1e-3f);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void FlatProfile_StaysFlatAndFacesTheWayItWasDrawn(float facing)
    {
        var target = FlatTarget(profile: true, facing);
        var rig = target.Rig;
        var clip = Convert(Walk(PlanarView.Side), target);

        for (var f = 0; f < clip.SolvedFrames!.Count; f++)
        {
            var world = World(clip, rig, f);
            foreach (var (from, to) in Segments)
                Assert.True(MathF.Abs(Between(rig, world, from, to).Z) < 1e-2f, $"{from}->{to} leaves the plane on frame {f}");
        }

        var thigh = Vector3.Normalize(Between(rig, World(clip, rig, PeakFrame), BoneRole.UpperLegL, BoneRole.LowerLegL));
        Assert.Equal(-Sin30 * facing, thigh.X, 2);
        Assert.Equal(-Cos30, thigh.Y, 2);
    }

    [Fact]
    public void FlatProfile_LayersTheNearLimbsInFrontOfTheFarOnes()
    {
        var target = FlatTarget(profile: true);
        var rig = target.Rig;
        var world = World(Convert(Walk(PlanarView.Side), target), rig, 0);

        float Depth(BoneRole role) => world[rig.BoneForRole(role)!.Value].Pos.Z;

        // Side view shows the character's right, and toes along +X make that +Z here.
        Assert.True(Depth(BoneRole.UpperLegR) > Depth(BoneRole.Hips));
        Assert.True(Depth(BoneRole.UpperLegL) < Depth(BoneRole.Hips));
        Assert.True(Depth(BoneRole.UpperArmR) > Depth(BoneRole.UpperArmL));
    }

    [Fact]
    public void FlatFront_KeepsItsLeftOnScreenRight()
    {
        var target = FlatTarget(profile: false);
        var rig = target.Rig;
        var world = World(Convert(Walk(PlanarView.Front), target), rig, 20);

        Assert.True(Between(rig, world, BoneRole.UpperLegR, BoneRole.UpperLegL).X > 10f);
    }

    [Fact]
    public void Facade_WritesDmxAndSkipsThe3dCleanups()
    {
        var result = Retargeter.Convert(Walk(PlanarView.Side, mirrored: true), SboxTarget.Value);

        Assert.Equal(2, result.Clips.Count);
        Assert.All(result.Clips, clip =>
        {
            Assert.True(clip.Success, clip.Error);
            Assert.False(string.IsNullOrEmpty(clip.DmxContent));
            Assert.All(clip.SolvedFrames!, frame => Assert.All(frame, bone =>
                Assert.True(float.IsFinite(bone.Pos.X + bone.Pos.Y + bone.Pos.Z + bone.Rot.W))));
        });
        Assert.DoesNotContain(result.Clips[0].Mapping!.Notes, note => note.Contains("Foot-plant cleanup skipped"));
    }

    [Fact]
    public void AClipRemembersItWasSolvedIn2dThroughATrim()
    {
        var solved = Convert(Walk(PlanarView.Front), SboxTarget.Value);

        Assert.Equal(PlanarView.Front, solved.View2d);
        Assert.Equal(PlanarView.Front, solved.WithFrames(solved.SolvedFrames!).View2d);
    }

    [Fact]
    public void A3dClipHasNo2dView()
    {
        var request = Walk(PlanarView.Side);
        var clip = Convert(new RetargetRequest
        {
            SourceData = request.SourceData,
            SourceFileName = request.SourceFileName,
        }, SboxTarget.Value);

        Assert.Null(clip.View2d);
    }

    [Theory]
    [MemberData(nameof(ThreeDimensionalRigs))]
    public void CameraAxes_LookSquareOnAtA3dRig(string name)
    {
        var target = Rig3d(name);
        var rig = target.Rig;
        var frame = FrameOf(rig);
        var clip = Convert(Walk(PlanarView.Side), target);
        var (up, toward) = PlanarSolver.CameraAxes(rig, PlanarView.Side);

        // Profile, facing screen-right: screen right is up x toward.
        Assert.Equal(1f, Vector3.Dot(Vector3.Cross(up, toward), frame.Forward), 2);
        var world = World(clip, rig, PeakFrame);
        foreach (var (from, to) in Segments)
            Assert.True(MathF.Abs(Vector3.Dot(Between(rig, world, from, to), toward)) < 1e-2f, $"{from}->{to}");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void CameraAxes_LookSquareOnAtAFlatRigFacingScreenRight(float facing)
    {
        var (up, toward) = PlanarSolver.CameraAxes(FlatTarget(profile: true, facing).Rig, PlanarView.Side);

        Assert.Equal(1f, MathF.Abs(toward.Z), 3);
        Assert.Equal(1f, up.Y, 3);
        Assert.Equal(facing, Vector3.Cross(up, toward).X, 3);
    }

    [Fact]
    public void CameraAxes_StayVerticalOnALeaningRig()
    {
        // A Mixamo rig's shoulders sit behind its hips: hips to shoulders is 7 degrees off vertical.
        var rig = FlatTarget(profile: false, lean: 12f).Rig;

        var (up, toward) = PlanarSolver.CameraAxes(rig, PlanarView.Side);

        Assert.Equal(1f, up.Y, 3);
        Assert.Equal(0f, toward.Y, 3);
    }

    [Fact]
    public void ACrouchKeepsTheFeetOnTheGroundOnShorterLegs()
    {
        var target = FlatTarget(profile: true, legs: 0.6f);
        var rig = target.Rig;
        var scene = HumanoidRetargeter.Core.Formats.Bvh.BvhImporter.Import(
            Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()));
        var map = Retargeter.ResolveSource(scene).Mapping;

        // Hips drop 20 cm over a knee bend that leaves the ankles (40 + 40 cm legs) where they were.
        const float Drop = 20f;
        var bend = MathF.Acos((80f - Drop) / 80f);
        var hips = map.RoleToBone[BoneRole.Hips];
        foreach (var locals in scene.Clips[0].Frames.Skip(1))
        {
            locals[hips] = new XForm(locals[hips].Pos - new Vector3(0f, Drop, 0f), locals[hips].Rot);
            foreach (var (role, angle) in new[]
            {
                (BoneRole.UpperLegL, bend), (BoneRole.LowerLegL, -2f * bend),
                (BoneRole.UpperLegR, bend), (BoneRole.LowerLegR, -2f * bend),
            })
            {
                var bone = map.RoleToBone[role];
                locals[bone] = new XForm(locals[bone].Pos, Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle));
            }
        }

        var solved = new PlanarSolver().Solve(scene, map, rig, new SolveOptions { View = PlanarView.Side });

        float Ankle(int frame) => new Pose(solved.Frames[frame]).ToWorld(rig.Skeleton)[rig.BoneForRole(BoneRole.FootL)!.Value].Pos.Y;
        Assert.Equal(Ankle(0), Ankle(20), 0);
    }

    [Fact]
    public void ARigWithoutATrunkFailsItsClipWithAClearError()
    {
        var skeleton = SkeletonModel.Create(new[] { new BoneDefinition("lonely", null, XForm.Identity) });
        var map = new MappingResult("empty", MappingSource.Preset);
        var target = new RetargetTargetSpec { Rig = TargetRig.FromSkeleton(skeleton, map), VmdlScale = 1f };

        var clip = Retargeter.Convert(Walk(PlanarView.Side), target).Clips[0];

        Assert.False(clip.Success);
        Assert.Contains("hips", clip.Error);
    }
}
