using System;
using System.Numerics;
using HumanoidRetargeter.Maths;

namespace HumanoidRetargeter.Cleanup;

/// <summary>
/// Analytic two-bone IK (Holden's formulation) with soft reach clamping.
/// Operates purely on world-space positions and returns world-space rotation deltas,
/// keeping it independent of any particular skeleton convention.
/// </summary>
public static class TwoBoneIk
{
    /// <summary>
    /// World-space rotation deltas produced by <see cref="Solve"/>.
    /// Application convention (used by tests and callers):
    ///   b' = a + UpperWorldDelta * (b - a)
    ///   c' = b' + LowerWorldDelta * (c - b)
    /// i.e. <see cref="UpperWorldDelta"/> premultiplies the upper joint's world rotation and
    /// <see cref="LowerWorldDelta"/> premultiplies the lower joint's world rotation.
    /// </summary>
    public readonly struct Result
    {
        public Quaternion UpperWorldDelta { get; init; }
        public Quaternion LowerWorldDelta { get; init; }
    }

    /// <summary>
    /// Solves the chain a→b→c to bring the end effector c onto <paramref name="target"/>.
    /// </summary>
    /// <param name="a">Upper joint world position (hip/shoulder).</param>
    /// <param name="b">Mid joint world position (knee/elbow).</param>
    /// <param name="c">End effector world position (ankle/wrist).</param>
    /// <param name="target">Desired world position for c.</param>
    /// <param name="soften">
    /// Fraction of full extension where soft clamping begins (0 disables). Within the soft
    /// zone the reach approaches full extension asymptotically, removing the "snap" at lockout.
    /// </param>
    /// <param name="stableBendAxis">
    /// Bend axis to use when the chain is near-collinear (its natural bend plane is undefined).
    /// </param>
    public static Result Solve(
        Vector3 a, Vector3 b, Vector3 c, Vector3 target,
        float soften = 0.02f, Vector3? stableBendAxis = null)
    {
        const float Eps = 1e-5f;

        float lab = Vector3.Distance(a, b);
        float lcb = Vector3.Distance(b, c);
        float maxExt = lab + lcb;

        var at = target - a;
        float dist = at.Length();
        if (dist < Eps)
            return new Result { UpperWorldDelta = Quaternion.Identity, LowerWorldDelta = Quaternion.Identity };

        float lat = SoftClamp(dist, maxExt, soften);
        lat = Math.Clamp(lat, Eps, maxExt - Eps * maxExt);

        var ab = b - a;
        var ac = c - a;
        var bc = c - b;

        // Bend axis: the chain's natural bend plane normal, or the caller-provided stable axis
        // when the chain is straight enough that the cross product degenerates.
        var axisRaw = Vector3.Cross(ac, ab);
        Vector3 axis0;
        if (axisRaw.Length() < Eps * lab * lcb || axisRaw.Length() < 1e-8f)
        {
            var fallback = stableBendAxis ?? PickPerpendicular(ac);
            // Project to be orthogonal to ac so the hinge is well-defined.
            var proj = fallback - Vector3.Dot(fallback, Vector3.Normalize(ac)) * Vector3.Normalize(ac);
            axis0 = proj.Length() > 1e-8f ? Vector3.Normalize(proj) : PickPerpendicular(ac);
        }
        else
        {
            axis0 = Vector3.Normalize(axisRaw);
        }

        float lac = ac.Length();
        if (lac < Eps)
            lac = Eps;

        // Current interior angles.
        float acAb0 = AngleBetween(ac, ab);
        float baBc0 = AngleBetween(-ab, bc);

        // Desired interior angles from the law of cosines for chain length lat.
        float acAb1 = MathF.Acos(Math.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = MathF.Acos(Math.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        var r0 = Quaternion.CreateFromAxisAngle(axis0, acAb1 - acAb0);
        var r1 = Quaternion.CreateFromAxisAngle(axis0, baBc1 - baBc0);

        // Swing the (now correctly extended) chain so the effector direction hits the target.
        var r2 = MathQ.FromTo(ac, at);

        var upper = MathQ.Normalize(r2 * r0);
        var lower = MathQ.Normalize(r2 * r0 * r1);
        return new Result { UpperWorldDelta = upper, LowerWorldDelta = lower };
    }

    /// <summary>
    /// Monotonic soft clamp: identity below the soft boundary, then asymptotic approach to
    /// <paramref name="max"/> (never reaching it) — ozz-style reach softening.
    /// </summary>
    private static float SoftClamp(float d, float max, float soften)
    {
        soften = Math.Clamp(soften, 0f, 0.5f);
        float softStart = max * (1f - soften);
        if (soften <= 0f)
            return Math.Min(d, max);
        if (d <= softStart)
            return d;
        float range = max - softStart;
        return softStart + range * (1f - MathF.Exp(-(d - softStart) / range));
    }

    private static float AngleBetween(Vector3 u, Vector3 v)
    {
        float denom = u.Length() * v.Length();
        if (denom < 1e-12f)
            return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(u, v) / denom, -1f, 1f));
    }

    private static Vector3 PickPerpendicular(Vector3 v)
    {
        var n = Vector3.Normalize(v);
        var candidate = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(n, candidate));
    }
}
