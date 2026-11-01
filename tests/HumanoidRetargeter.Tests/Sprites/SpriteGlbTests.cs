using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Formats.Gltf;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Solve;
using HumanoidRetargeter.Core.Sprites;
using HumanoidRetargeter.Core.Target;
using Xunit;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Tests.Sprites;

/// <summary>A drawn stick figure through the whole sprite path: silhouette, rig, GLB, mapping,
/// 2D solve. The alpha is a figure 200 px tall on a transparent 256 x 256 image.</summary>
public class SpriteGlbTests
{
    private const int Size = 256;
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

    private static void Line(byte[] alpha, (float X, float Y) from, (float X, float Y) to, float radius)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var ax = to.X - from.X;
                var ay = to.Y - from.Y;
                var t = Math.Clamp(((x - from.X) * ax + (y - from.Y) * ay) / MathF.Max(1f, ax * ax + ay * ay), 0f, 1f);
                var dx = x - (from.X + ax * t);
                var dy = y - (from.Y + ay * t);
                if (dx * dx + dy * dy <= radius * radius)
                    alpha[y * Size + x] = 255;
            }
        }
    }

    /// <summary>Head, trunk, arms down, legs apart.</summary>
    private static byte[] FrontFigure()
    {
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 60), (128, 130), 18);
        foreach (var side in new[] { 1f, -1f })
        {
            Line(alpha, (128 + 20 * side, 66), (128 + 34 * side, 140), 6);
            Line(alpha, (128 + 12 * side, 130), (128 + 24 * side, 222), 9);
        }
        return alpha;
    }

    /// <summary>Head, trunk, one arm, one leg and a foot pointing at screen right.</summary>
    private static byte[] ProfileFigure(float facing = 1f)
    {
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 60), (128, 130), 12);
        Line(alpha, (128, 66), (132, 136), 5);
        Line(alpha, (128, 130), (128, 216), 9);
        Line(alpha, (128, 218), (128 + 24 * facing, 222), 5);
        return alpha;
    }

    private static TargetRig Imported(byte[] glb)
    {
        var scene = GltfImporter.Import(glb, new GltfImportOptions { UseSkinBindPose = true });
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);
        return TargetRig.FromSkeleton(scene.Skeleton, map);
    }

    private static byte[] Glb(byte[] alpha)
        => SpriteGlb.Write(SpriteRig.Fit(alpha, Size, Size), alpha, Png, "sprite");

    private static ClipResult Walk(TargetRig rig, PlanarView view)
    {
        var clip = Retargeter.Convert(new RetargetRequest
        {
            SourceData = Encoding.UTF8.GetBytes(WalkFixture.SyntheticWalkBvh()),
            SourceFileName = "synth_walk.bvh",
            Solver = SolverKind.Planar,
            Solve = new SolveOptions { View = view },
        }, new RetargetTargetSpec { Rig = rig, VmdlScale = 1f }).Clips[0];
        Assert.True(clip.Success, clip.Error);
        return clip;
    }

    [Fact]
    public void Fit_ReadsAFrontFigure()
    {
        var alpha = FrontFigure();
        var rig = SpriteRig.Fit(alpha, Size, Size);

        Assert.False(rig.IsProfile);
        Assert.True(rig.Joints[BoneRole.UpperLegL].X > rig.Joints[BoneRole.UpperLegR].X);
        Assert.InRange(rig.Joints[BoneRole.FootL].X, 140f, 160f);
        Assert.True(rig.Joints[BoneRole.Head].Y < rig.Joints[BoneRole.Hips].Y);
        Assert.True(rig.Joints[BoneRole.Hips].Y < rig.Joints[BoneRole.FootL].Y);
    }

    [Theory]
    [InlineData(1f, false)]
    [InlineData(-1f, true)]
    public void Fit_ReadsAProfileAndTheWayItFaces(float facing, bool facesLeft)
    {
        var rig = SpriteRig.Fit(ProfileFigure(facing), Size, Size);

        Assert.True(rig.IsProfile);
        Assert.Equal(facesLeft, rig.FacesLeft);
        Assert.Equal(rig.Joints[BoneRole.FootL], rig.Joints[BoneRole.FootR]);
        Assert.True((rig.Joints[BoneRole.ToeL].X - rig.Joints[BoneRole.FootL].X) * facing > 0f);
    }

    [Fact]
    public void Fit_FindsArmsHeldOut()
    {
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 60), (128, 130), 18);
        Line(alpha, (128, 70), (230, 70), 6);
        Line(alpha, (128, 70), (26, 70), 6);
        foreach (var side in new[] { 1f, -1f })
            Line(alpha, (128 + 12 * side, 130), (128 + 16 * side, 222), 9);

        var rig = SpriteRig.Fit(alpha, Size, Size);

        Assert.True(rig.Joints[BoneRole.HandL].X > 215f);
        Assert.True(rig.Joints[BoneRole.HandR].X < 41f);
    }

    [Fact]
    public void Fit_KeepsAChibisShouldersBelowItsHead()
    {
        // A head as tall as the body, wider than the shoulders under a thin neck.
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 66), (128, 80), 46);
        Line(alpha, (128, 130), (128, 134), 7);
        Line(alpha, (128, 150), (128, 180), 22);
        foreach (var side in new[] { 1f, -1f })
            Line(alpha, (128 + 10 * side, 190), (128 + 12 * side, 226), 6);

        var rig = SpriteRig.Fit(alpha, Size, Size);

        Assert.True(rig.Joints[BoneRole.UpperArmL].Y > 126f, $"shoulder at {rig.Joints[BoneRole.UpperArmL].Y} is inside the head");
        Assert.True(rig.Joints[BoneRole.Head].Y < 130f);
    }

    [Fact]
    public void Fit_DoesNotTakeTheTopOfARoundHeadForTheNeck()
    {
        var alpha = FrontFigure();

        var rig = SpriteRig.Fit(alpha, Size, Size);

        // The figure's neck is near y 56, its hips near 130. A neck read at the crown of the
        // head (y 40) pulled every joint under it up the body.
        Assert.InRange(rig.Joints[BoneRole.Neck].Y, 48f, 66f);
        Assert.InRange(rig.Joints[BoneRole.Hips].Y, 118f, 150f);
    }

    [Fact]
    public void Fit_ReadsTheCrotchFromTheGapBetweenTheLegs()
    {
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 60), (128, 150), 18);
        foreach (var side in new[] { 1f, -1f })
            Line(alpha, (128 + 12 * side, 150), (128 + 18 * side, 222), 8);

        var rig = SpriteRig.Fit(alpha, Size, Size);

        Assert.InRange(rig.Joints[BoneRole.UpperLegL].Y, 150f, 180f);
    }

    [Fact]
    public void Fit_EndsAStraightHangingArmAtItsHand()
    {
        // Wide shoulders over arms hanging straight down, reaching past the template's hands.
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 62), (128, 130), 16);
        foreach (var side in new[] { 1f, -1f })
        {
            Line(alpha, (128, 66), (128 + 80 * side, 66), 6);
            Line(alpha, (128 + 80 * side, 66), (128 + 80 * side, 140), 6);
            Line(alpha, (128 + 12 * side, 130), (128 + 16 * side, 222), 9);
        }

        var rig = SpriteRig.Fit(alpha, Size, Size);

        Assert.True(rig.Joints[BoneRole.HandL].Y > 130f, $"hand at {rig.Joints[BoneRole.HandL].Y}");
    }

    [Fact]
    public void Fit_DoesNotTakeTheGapBetweenTwoShoesForTheCrotch()
    {
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 42), (128, 44), 14);
        Line(alpha, (128, 60), (128, 205), 20);
        foreach (var side in new[] { 1f, -1f })
            Line(alpha, (128 + 14 * side, 210), (128 + 16 * side, 222), 8);

        var rig = SpriteRig.Fit(alpha, Size, Size, isProfile: false);

        Assert.True(rig.Joints[BoneRole.Hips].Y < 180f, $"hips at {rig.Joints[BoneRole.Hips].Y}");
    }

    [Fact]
    public void Fit_KeepsTemplateProportionsWithoutANeck()
    {
        // A hooded figure: no row narrower than the head and shoulders.
        var alpha = new byte[Size * Size];
        Line(alpha, (128, 50), (128, 210), 26);

        var rig = SpriteRig.Fit(alpha, Size, Size, isProfile: false);

        Assert.InRange(rig.Joints[BoneRole.Neck].Y, 55f, 80f);
    }

    [Fact]
    public void Fit_TakesANarrowStandingFigureForAProfile()
    {
        var rig = SpriteRig.Fit(ProfileFigure(), Size, Size);

        Assert.True(rig.IsProfile);
    }

    [Fact]
    public void Fit_RejectsASpriteWithNoBackground()
    {
        var alpha = new byte[Size * Size];
        Array.Fill(alpha, (byte)255);

        var error = Assert.Throws<ArgumentException>(() => SpriteRig.Fit(alpha, Size, Size));
        Assert.Contains("transparent", error.Message);
    }

    [Fact]
    public void Move_CarriesTheFarLimbOnAProfile()
    {
        var rig = SpriteRig.Fit(ProfileFigure(), Size, Size);

        rig.Move(BoneRole.LowerLegL, new Vector2(150f, 170f));

        Assert.Equal(new Vector2(150f, 170f), rig.Joints[BoneRole.LowerLegR]);
        Assert.DoesNotContain(BoneRole.LowerLegR, rig.EditableJoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Glb_ImportsAsAFlatFullyMappedHumanoid(bool profile)
    {
        var glb = Glb(profile ? ProfileFigure() : FrontFigure());
        var scene = GltfImporter.Import(glb, new GltfImportOptions { UseSkinBindPose = true });
        var (map, _) = Retargeter.ResolveMapping(scene.Skeleton);

        // Without fingers this is the best-effort path, so every bone is checked by name.
        Assert.Equal(20, map.RoleToBone.Count);
        foreach (var (role, name) in new[]
        {
            (BoneRole.Hips, "Hips"), (BoneRole.Head, "Head"),
            (BoneRole.UpperLegL, "LeftUpLeg"), (BoneRole.UpperLegR, "RightUpLeg"),
            (BoneRole.FootL, "LeftFoot"), (BoneRole.FootR, "RightFoot"),
            (BoneRole.UpperArmL, "LeftArm"), (BoneRole.HandR, "RightHand"),
        })
            Assert.Equal("mixamorig_" + name, scene.Skeleton[map.RoleToBone[role]].Name);
        var rig = TargetRig.FromSkeleton(scene.Skeleton, map);
        Assert.Equal(profile ? PlanarView.Side : PlanarView.Front, PlanarSolver.DrawnView(rig));
    }

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

    [Theory]
    [InlineData("profile", 90f)]
    [InlineData("humanoid", 0f)]
    [InlineData("chibi", 0f)]
    public void BuiltInCharacters_AreFlatMappedHumanoids(string name, float yaw)
    {
        var glb = File.ReadAllBytes(RepoFile("Assets", "data", "humanoid_retargeter", "sprites", $"bonebase_{name}.glb"));

        var rig = Imported(glb);

        Assert.Equal(new PlanarView(yaw), PlanarSolver.DrawnView(rig));
        Assert.True(Walk(rig, new PlanarView(yaw)).SolvedFrames!.Count > 1);
    }

    [Fact]
    public void BuiltInChibi_KeepsItsFeetOnTheGround()
    {
        // Chibi legs are far shorter than the walk's: the clip used to sink them through the floor.
        var rig = Imported(File.ReadAllBytes(RepoFile("Assets", "data", "humanoid_retargeter", "sprites", "bonebase_chibi.glb")));
        var feet = new[] { BoneRole.FootL, BoneRole.FootR, BoneRole.ToeL, BoneRole.ToeR }.Select(r => rig.BoneForRole(r)!.Value).ToArray();
        var rest = feet.Min(f => rig.Skeleton.RestWorld[f].Pos.Y);

        var clip = Walk(rig, PlanarView.Side);

        var lowest = clip.SolvedFrames!.Min(frame =>
        {
            var world = new Pose(frame).ToWorld(rig.Skeleton);
            return feet.Min(f => world[f].Pos.Y);
        });
        Assert.Equal(rest, lowest, 1);
    }

    [Fact]
    public void Glb_StandsTheFigureOnTheGroundAtHumanHeight()
    {
        var rig = Imported(Glb(FrontFigure()));
        var rest = rig.Skeleton.RestWorld;

        var head = rest[rig.BoneForRole(BoneRole.Head)!.Value].Pos.Y;
        var toe = rest[rig.BoneForRole(BoneRole.ToeL)!.Value].Pos.Y;
        Assert.InRange(toe, 0f, 12f);
        Assert.InRange(head, 145f, 180f);
    }

    [Fact]
    public void Glb_WritesAModelDmxWithItsMesh()
    {
        var glb = Glb(FrontFigure());
        var scene = GltfImporter.Import(glb, new GltfImportOptions { UseSkinBindPose = true });

        var dmx = GltfModelDmxWriter.Write(glb, scene.Skeleton, "sprite", null);

        Assert.Contains("DmeMesh", dmx);
        Assert.Contains("mixamorig_Hips", dmx);
    }

    [Fact]
    public void Glb_FlipsItsLayersWithTheWayAProfileFaces()
    {
        var right = Imported(Glb(ProfileFigure(1f)));
        var left = Imported(Glb(ProfileFigure(-1f)));

        Assert.Equal(1f, Vector3.Cross(Up(right), PlanarSolver.CameraAxes(right, PlanarView.Side).Toward).X, 3);
        Assert.Equal(-1f, Vector3.Cross(Up(left), PlanarSolver.CameraAxes(left, PlanarView.Side).Toward).X, 3);

        static Vector3 Up(TargetRig rig) => PlanarSolver.CameraAxes(rig, PlanarView.Side).Up;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Glb_TakesAWalk(bool profile)
    {
        var rig = Imported(Glb(profile ? ProfileFigure() : FrontFigure()));
        var view = PlanarSolver.DrawnView(rig)!.Value;

        var clip = Walk(rig, view);

        var (_, toward) = PlanarSolver.CameraAxes(rig, view);
        foreach (var frame in clip.SolvedFrames!)
        {
            var world = new Pose(frame).ToWorld(rig.Skeleton);
            var thigh = world[rig.BoneForRole(BoneRole.LowerLegL)!.Value].Pos - world[rig.BoneForRole(BoneRole.UpperLegL)!.Value].Pos;
            Assert.True(MathF.Abs(Vector3.Dot(thigh, toward)) < 1e-2f);
        }
    }
}
