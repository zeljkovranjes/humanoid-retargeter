using System.Globalization;
using System.Text;

namespace HumanoidRetargeter.Tests;

/// <summary>
/// Synthetic mixamo-named BVH walk with EXACTLY known foot-plant phases, shared by the
/// footstep-event and mirrored-variant facade tests.
/// </summary>
/// <remarks>
/// 60 frames at 30 fps, hips fixed at height 95 cm (plus a ±1.5 cm lateral sway — slow
/// enough to stay under the plant-detection speed threshold, but enough to make "pelvis
/// lateral offset negates" assertions non-trivial). The legs step IN PLACE by rotating the
/// hip joints about the lateral axis (a 30° sin-arc swing lifts the ankle ~10.7 cm, far
/// beyond the 6 cm hysteresis exit; between swings the leg holds its rest pose, so the foot
/// is world-stationary = planted):
/// <list type="bullet">
/// <item>LEFT leg: swings frames 0–14, planted 15–59 → exactly one touchdown at frame 15.</item>
/// <item>RIGHT leg: planted 0–29 (already down at clip start — no touchdown), swings 30–44,
/// planted 45–59 → exactly one touchdown at frame 45.</item>
/// </list>
/// </remarks>
internal static class PosedSkeletonFixture
{
    /// <summary>Total frames (30 fps).</summary>
    public const int Frames = 60;

    /// <summary>The left foot's only touchdown frame.</summary>
    public const int LeftTouchdownFrame = 15;

    /// <summary>The right foot's only touchdown frame (its frame-0 plant has no touchdown).</summary>
    public const int RightTouchdownFrame = 45;

    /// <summary>Builds the BVH text (mixamorig names → the shipped mixamo preset detects it).</summary>
    public static string SyntheticWalkBvh()
    {
        var sb = new StringBuilder();
        sb.AppendLine("HIERARCHY");
        sb.AppendLine("ROOT mixamorig:Hips");
        sb.AppendLine("{");
        sb.AppendLine("  OFFSET 0 0 0");
        sb.AppendLine("  CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation");

        void Joint(string name, float x, float y, float z, Action? children = null,
            (float X, float Y, float Z)? end = null)
        {
            sb.AppendLine($"  JOINT mixamorig:{name}");
            sb.AppendLine("  {");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    OFFSET {x} {y} {z}"));
            sb.AppendLine("    CHANNELS 3 Zrotation Xrotation Yrotation");
            children?.Invoke();
            if (end is { } e)
            {
                sb.AppendLine("    End Site");
                sb.AppendLine("    {");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"      OFFSET {e.X} {e.Y} {e.Z}"));
                sb.AppendLine("    }");
            }
            sb.AppendLine("  }");
        }

        // Joint order (matters for the MOTION channel layout below):
        //  1 Spine  2 Spine1  3 Spine2  4 Neck  5 Head
        //  6 LeftShoulder  7 LeftArm  8 LeftForeArm  9 LeftHand
        // 10 RightShoulder 11 RightArm 12 RightForeArm 13 RightHand
        // 14 LeftUpLeg 15 LeftLeg 16 LeftFoot 17 LeftToeBase
        // 18 RightUpLeg 19 RightLeg 20 RightFoot 21 RightToeBase
        Joint("Spine", 0, 10, 0, () =>
        Joint("Spine1", 0, 12, 0, () =>
        Joint("Spine2", 0, 12, 0, () =>
        {
            Joint("Neck", 0, 14, 0, () =>
                Joint("Head", 0, 6, 0, end: (0, 12, 0)));
            Joint("LeftShoulder", 6, 8, 0, () =>
            Joint("LeftArm", 12, 0, 0, () =>
            Joint("LeftForeArm", 26, 0, 0, () =>
            Joint("LeftHand", 25, 0, 0, end: (10, 0, 0)))));
            Joint("RightShoulder", -6, 8, 0, () =>
            Joint("RightArm", -12, 0, 0, () =>
            Joint("RightForeArm", -26, 0, 0, () =>
            Joint("RightHand", -25, 0, 0, end: (-10, 0, 0)))));
        })));
        Joint("LeftUpLeg", 9, -5, 0, () =>
        Joint("LeftLeg", 0, -40, 0, () =>
        Joint("LeftFoot", 0, -40, 0, () =>
        Joint("LeftToeBase", 0, -8, 12, end: (0, 0, 8)))));
        Joint("RightUpLeg", -9, -5, 0, () =>
        Joint("RightLeg", 0, -40, 0, () =>
        Joint("RightFoot", 0, -40, 0, () =>
        Joint("RightToeBase", 0, -8, 12, end: (0, 0, 8)))));

        sb.AppendLine("}");
        sb.AppendLine("MOTION");
        sb.AppendLine($"Frames: {Frames}");
        sb.AppendLine("Frame Time: 0.0333333");

        // 30° sin-arc swing inside [start, end]; 0 (rest = planted) outside.
        static float Swing(int frame, int start, int end)
            => frame >= start && frame <= end
                ? 30f * MathF.Sin(MathF.PI * (frame - start) / (end - start))
                : 0f;

        for (var f = 0; f < Frames; f++)
        {
            var sway = 1.5f * MathF.Sin(2f * MathF.PI * f / Frames); // < 5 cm/s: never unplants
            var left = Swing(f, 0, LeftTouchdownFrame - 1);          // swings 0–14, plants at 15
            var right = Swing(f, 30, RightTouchdownFrame - 1);       // planted 0–29, swings 30–44

            var values = new float[69]; // 6 root + 21 joints x 3 (Zrot Xrot Yrot)
            values[0] = sway;            // hips Xposition
            values[1] = 95f;             // hips Yposition
            values[6 + 13 * 3 + 1] = left;  // joint 14 (LeftUpLeg) Xrotation
            values[6 + 17 * 3 + 1] = right; // joint 18 (RightUpLeg) Xrotation

            sb.AppendLine(string.Join(' ',
                values.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture))));
        }
        return sb.ToString();
    }
}
