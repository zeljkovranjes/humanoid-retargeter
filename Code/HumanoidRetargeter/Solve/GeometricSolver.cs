using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Solve;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// The geometric retarget solver (design §5): world-rotation deltas conjugated through
/// canonical anatomical frames, with arc-length spine interpolation, hip-height-scaled pelvis
/// translation, and curl/splay finger redistribution (<see cref="FingerSolver"/>).
/// </summary>
/// <remarks>
/// <para><b>Formulation: absolute canonical-orientation matching.</b> Both rests are first
/// T-pose-normalized (<see cref="RestNormalizer"/>) and canonical frames are built on the
/// <i>normalized</i> rests. Per frame and mapped role, the source bone's <i>current</i>
/// canonical frame orientation is <c>ΔR(f) · C_src</c> with
/// <c>ΔR(f) = R_srcWorld(f) · R_srcNormRest⁻¹</c> (the canonical frame rides the bone). The
/// target bone is rotated so its current canonical frame has the <b>same orientation in
/// character-frame coordinates</b> (<c>Q</c> = the rig's character basis, forward/up derived
/// from rest geometry):
/// <c>R_tgtWorld(f) = Q_tgt · Q_src⁻¹ · ΔR(f) · C_src · C_tgt⁻¹ · R_tgtNormRest</c>.</para>
/// <para>This matches worldspace anatomical <i>directions</i> exactly (the canonical X axis is
/// the chain-child direction), rather than preserving each rig's idiosyncratic rest offsets:
/// a naive rest-relative delta (<c>C_tgt·ΔC·C_tgt⁻¹·R_rest</c>) carries the full rest-pose
/// direction mismatch between rigs into every frame — measured at 52° on the s&amp;box rig's
/// curled finger rest vs Mixamo's straight fingers. Note ΔR is measured from the
/// <i>normalized</i> rest, and the source's rest anatomy enters only through <c>C_src</c>,
/// built on that same normalized rest.</para>
/// <para><b>Per-role transfer modes.</b> The argument inverts for roles whose rest direction
/// is <i>anatomy</i>, not pose. Shoulder girdle / neck carriage (rest clavicle directions
/// diverge 6–28° from the s&amp;box rig's): absolute matching drags the target's shoulders to
/// the source's rest line and hunches the neck, so <see cref="RoleTransferMode.DeltaFromRest"/>
/// roles instead replay the source's canonical-space delta from its own normalized rest onto
/// the target's normalized rest: <c>R_tgtWorld(f) = C_tgt · ΔC(f) · C_tgt⁻¹ · R_tgtNormRest</c>
/// with <c>ΔC(f) = C_src⁻¹·ΔR(f)·C_src</c>. Feet (rest foot→toe directions diverge 11–44°
/// from the s&amp;box rig's steep ankle): absolute matching pitched/yawed planted feet by
/// that divergence ("feet bent upward/inward"), while canonical-frame remapping would tilt
/// the rotation <i>axes</i> by it (measured up to 47° planted-pitch error on a CMU-style
/// rig) — so feet default to <see cref="RoleTransferMode.CharacterDeltaFromRest"/>, which
/// replays the delta with its world axes intact:
/// <c>R_tgtWorld(f) = M · ΔR(f) · M⁻¹ · R_tgtNormRest</c> with <c>M = Q_tgt·Q_src⁻¹</c>.
/// All three modes differ only in the constant pre/postmultipliers around <c>ΔR(f)</c>
/// (see <see cref="SolveOptions.DefaultTransferModes"/> for the default role set); with
/// source == target every mode collapses to <c>ΔR(f)·R_normRest</c>, so the round-trip
/// identity below holds regardless. Under the DEFAULT modes
/// (<see cref="SolveOptions.TransferModes"/> = null) feet additionally fall back to
/// canonical delta when the source foot direction is a <i>virtual</i> character-forward
/// extension (no mapped toe) but the target's is real anatomy
/// (<see cref="CanonicalFrames.HasVirtualPrimary"/>). An explicit (non-null) mode map
/// disables that heuristic along with the defaults: the caller's entries are exact, roles
/// absent from the map are absolute. The residual planted-stance offset a source with a
/// non-stance rest pose leaves behind (the delta modes reference the REST) is removed by
/// the <see cref="Cleanup.FootGroundAlign"/> cleanup pass, not the solver.</para>
/// <para>Identity proof (citizen round-trip): with source == target, <c>Q_tgt = Q_src</c>,
/// <c>C_tgt = C_src</c> and the normalized rests coincide, so
/// <c>R_tgt(f) = ΔR(f)·R_normRest = R_src(f)</c> exactly.</para>
/// <para><b>Spine.</b> When source and target map the same spine role set the chain transfers
/// 1:1 like any body bone. Otherwise both chains are parametrized by normalized arc length at
/// rest (hips→chest, extended to the neck/head anchor when mapped) and each target spine bone
/// Slerps the <i>absolute</i> character-space canonical orientations of its two bracketing
/// source spine bones (UE "Interpolated").</para>
/// <para><b>Positions.</b> Target bones keep their rest local translations (bone lengths are
/// never modified) except the hips: the pelvis travel from the normalized source rest is
/// re-expressed in the source character frame, scaled (horizontal/vertical, default = hip
/// height ratio), and re-expressed in the target world.</para>
/// <para>Bones with <see cref="BoneClass.ConstraintDriven"/> or <see cref="BoneClass.IkBaked"/>
/// and unmapped animated bones keep their rest locals every frame (IK baking is a separate,
/// later pass). The output asserts finiteness; solving is deterministic.</para>
/// </remarks>
public sealed class GeometricSolver : IRetargetSolver
{
    /// <inheritdoc />
    public Clip Solve(SourceScene source, MappingResult sourceMap, TargetRig target, SolveOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentNullException.ThrowIfNull(target);
        options ??= new SolveOptions();

        if (options.ClipIndex < 0 || options.ClipIndex >= source.Clips.Count)
            throw new ArgumentOutOfRangeException(nameof(options), options.ClipIndex,
                $"ClipIndex out of range; the source has {source.Clips.Count} clip(s).");
        var clip = source.Clips[options.ClipIndex];

        var plan = new Plan(source.Skeleton, sourceMap, target, options);
        var output = new Clip(options.ClipName ?? clip.Name, clip.Fps, clip.Looping);
        foreach (var frame in clip.Frames)
            output.Frames.Add(plan.SolveFrame(frame));
        return output;
    }

    // ================================================================ solve plan

    /// <summary>Everything built once per solve; <see cref="SolveFrame"/> is then pure
    /// per-frame math over preallocated scratch buffers.</summary>
    private sealed class Plan
    {
        private static readonly BoneRole[] SpineRoles =
        {
            BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3, BoneRole.Spine4,
        };

        private readonly SkeletonModel _src;
        private readonly SkeletonModel _tgt;
        private readonly XForm[] _srcNormRest;
        private readonly XForm[] _tgtNormRest;
        private readonly CanonicalFrames _srcCanon;
        private readonly CanonicalFrames _tgtCanon;
        private readonly Quaternion _chrSrcInv;
        private readonly Quaternion _chrTgt;

        /// <summary>Premultiplier <c>Q_tgt · Q_src⁻¹</c> (character-frame change of basis) —
        /// a solve-level constant shared by every direct entry.</summary>
        private readonly Quaternion _basisChange;
        private readonly float _scaleH;
        private readonly float _scaleV;
        private readonly int _srcHips = -1;
        private readonly int _tgtHips = -1;

        // Source world-delta slots: which source bones need ΔR computed each frame.
        private readonly List<(int SrcBone, Quaternion NormRestRotInv)> _slots = new();
        private readonly Dictionary<int, int> _slotByBone = new();

        private readonly struct DirectEntry
        {
            /// <summary>Slot of the source ΔR.</summary>
            public required int Slot { get; init; }

            public required int TgtBone { get; init; }

            /// <summary>Premultiplier: <c>Q_tgt · Q_src⁻¹</c> (character-frame change of
            /// basis, the Plan-level <see cref="_basisChange"/>) for
            /// <see cref="RoleTransferMode.AbsoluteDirection"/> and
            /// <see cref="RoleTransferMode.CharacterDeltaFromRest"/> entries,
            /// <c>C_tgt · C_src⁻¹</c> for <see cref="RoleTransferMode.DeltaFromRest"/>.</summary>
            public required Quaternion Pre { get; init; }

            /// <summary>Postmultiplier: <c>C_src · C_tgt⁻¹ · R_tgtNormRest</c> for the two
            /// canonical-frame modes, <c>Q_src · Q_tgt⁻¹ · R_tgtNormRest</c> for
            /// <see cref="RoleTransferMode.CharacterDeltaFromRest"/>.</summary>
            public required Quaternion B { get; init; }
        }

        private readonly struct SpineEntry
        {
            public required int TgtBone { get; init; }
            public required int LoSlot { get; init; }
            public required int HiSlot { get; init; }
            public required float T { get; init; }
            public required Quaternion CsLo { get; init; }
            public required Quaternion CsHi { get; init; }

            /// <summary>Postmultiplier <c>C_tgt⁻¹ · R_tgtNormRest</c>.</summary>
            public required Quaternion CtInvRest { get; init; }
        }

        private readonly List<DirectEntry> _direct = new();
        private readonly List<SpineEntry> _spine = new();
        private readonly FingerSolver? _fingers;

        // Per-frame scratch.
        private readonly XForm[] _srcWorld;
        private readonly Quaternion[] _deltas;
        private readonly bool[] _solved;
        private readonly Quaternion[] _rot;
        private readonly XForm[] _tgtWorld;

        private readonly IReadOnlyDictionary<BoneRole, RoleTransferMode> _modes;

        /// <summary>True when the caller supplied an explicit <see cref="SolveOptions.TransferModes"/>
        /// map: the map is then exact and every fallback heuristic (the virtual-foot delta
        /// fallback below) is disabled — see <see cref="SolveOptions.TransferModes"/>.</summary>
        private readonly bool _explicitModes;

        public Plan(SkeletonModel src, MappingResult srcMap, TargetRig rig, SolveOptions options)
        {
            _src = src;
            _tgt = rig.Skeleton;
            _explicitModes = options.TransferModes is not null;
            _modes = options.TransferModes ?? SolveOptions.DefaultTransferModes;

            var tgtMap = rig.ToMappingResult();
            var (srcNorm, _) = RestNormalizer.Normalize(src, srcMap);
            var (tgtNorm, _) = RestNormalizer.Normalize(_tgt, tgtMap);
            _srcNormRest = srcNorm.WorldRest;
            _tgtNormRest = tgtNorm.WorldRest;
            _srcCanon = CanonicalFrames.Build(src, srcMap, _srcNormRest);
            _tgtCanon = CanonicalFrames.Build(_tgt, tgtMap, _tgtNormRest);

            _chrSrcInv = Quaternion.Conjugate(
                MathQ.BasisFromForwardUp(_srcCanon.CharacterForward, _srcCanon.CharacterUp));
            _chrTgt = MathQ.BasisFromForwardUp(_tgtCanon.CharacterForward, _tgtCanon.CharacterUp);
            _basisChange = MathQ.Normalize(_chrTgt * _chrSrcInv);

            var ratio = _srcCanon.HipHeight > 1e-3f ? _tgtCanon.HipHeight / _srcCanon.HipHeight : 1f;
            if (!float.IsFinite(ratio) || ratio <= 0f)
                ratio = 1f;
            _scaleH = options.HipScaleHorizontal ?? ratio;
            _scaleV = options.HipScaleVertical ?? ratio;

            if (srcMap.RoleToBone.TryGetValue(BoneRole.Hips, out var srcHips))
                _srcHips = srcHips;
            _tgtHips = rig.BoneForRole(BoneRole.Hips) ?? -1;

            // Body roles (everything but spine + fingers), in target bone order (deterministic).
            for (var i = 0; i < _tgt.Count; i++)
            {
                if (rig.RoleOf(i) is not BoneRole role)
                    continue;
                if (SpineRoles.Contains(role) || FingerSolver.IsFingerRole(role))
                    continue;
                TryAddDirect(role, srcMap, rig);
            }

            BuildSpine(srcMap, rig);

            if (options.TransferFingers)
            {
                _fingers = FingerSolver.Build(
                    srcMap, _srcCanon, _srcNormRest, rig.BoneForRole, _tgtCanon, _tgtNormRest,
                    _chrSrcInv, _chrTgt,
                    RegisterSlot, role => TryAddDirect(role, srcMap, rig));
            }

            _srcWorld = new XForm[_src.Count];
            _deltas = new Quaternion[_slots.Count];
            _solved = new bool[_tgt.Count];
            _rot = new Quaternion[_tgt.Count];
            _tgtWorld = new XForm[_tgt.Count];
        }

        // ------------------------------------------------------------ build helpers

        private int RegisterSlot(int srcBone)
        {
            if (_slotByBone.TryGetValue(srcBone, out var slot))
                return slot;
            slot = _slots.Count;
            _slots.Add((srcBone, Quaternion.Conjugate(_srcNormRest[srcBone].Rot)));
            _slotByBone[srcBone] = slot;
            return slot;
        }

        private void TryAddDirect(BoneRole role, MappingResult srcMap, TargetRig rig)
        {
            if (!srcMap.RoleToBone.TryGetValue(role, out var srcBone))
                return;
            if (rig.BoneForRole(role) is not int tgtBone)
                return;
            if (!_srcCanon.Has(role) || !_tgtCanon.Has(role))
                return;

            var cs = _srcCanon.WorldFrameOf(role);
            var ct = _tgtCanon.WorldFrameOf(role);
            if (!_modes.TryGetValue(role, out var mode))
                mode = RoleTransferMode.AbsoluteDirection;

            // Feet whose SOURCE direction is a virtual character-forward extension (no mapped
            // toe) while the target's is real anatomy fall back to canonical delta transfer:
            // any direction-matching against that arbitrary virtual axis is meaningless
            // (measured: constant ~41° dorsiflex / heel-standing on the toe-less
            // makehuman/daz rig under absolute matching). With real anatomy on both sides
            // feet take the CharacterDeltaFromRest default instead. Same-rig round trips
            // have equal virtual flags on both sides, so this never fires there.
            // HEURISTIC, defaults only: an explicit TransferModes map is a contract — the
            // caller's entries (and absences = absolute) must win, so the fallback never
            // overrides it (see SolveOptions.TransferModes).
            if (!_explicitModes
                && role is BoneRole.FootL or BoneRole.FootR
                && _srcCanon.HasVirtualPrimary(role) && !_tgtCanon.HasVirtualPrimary(role))
            {
                mode = RoleTransferMode.DeltaFromRest;
            }
            _direct.Add(new DirectEntry
            {
                Slot = RegisterSlot(srcBone),
                TgtBone = tgtBone,
                Pre = mode switch
                {
                    RoleTransferMode.DeltaFromRest => MathQ.Normalize(ct * Quaternion.Conjugate(cs)),
                    _ => _basisChange,
                },
                B = mode == RoleTransferMode.CharacterDeltaFromRest
                    ? MathQ.Normalize(Quaternion.Conjugate(_basisChange) * _tgtNormRest[tgtBone].Rot)
                    : MathQ.Normalize(cs * Quaternion.Conjugate(ct) * _tgtNormRest[tgtBone].Rot),
            });
        }

        private void BuildSpine(MappingResult srcMap, TargetRig rig)
        {
            var srcSpine = SpineRoles
                .Where(r => srcMap.RoleToBone.ContainsKey(r) && _srcCanon.Has(r))
                .ToArray();
            var tgtSpine = SpineRoles
                .Where(r => rig.BoneForRole(r) is not null && _tgtCanon.Has(r))
                .ToArray();
            if (srcSpine.Length == 0 || tgtSpine.Length == 0)
                return;

            if (srcSpine.SequenceEqual(tgtSpine))
            {
                // Same chain shape: degenerate to 1:1 (preserves per-bone detail exactly,
                // and makes the same-rig round-trip identity).
                foreach (var role in srcSpine)
                    TryAddDirect(role, srcMap, rig);
                return;
            }

            var srcU = ArcParams(
                srcSpine.Select(r => _srcNormRest[srcMap.RoleToBone[r]].Pos).ToArray(),
                ChainEndAnchor(r => srcMap.RoleToBone.TryGetValue(r, out var b) ? b : null, _srcNormRest));
            var tgtU = ArcParams(
                tgtSpine.Select(r => _tgtNormRest[rig.BoneForRole(r)!.Value].Pos).ToArray(),
                ChainEndAnchor(rig.BoneForRole, _tgtNormRest));

            for (var k = 0; k < tgtSpine.Length; k++)
            {
                var role = tgtSpine[k];
                var tgtBone = rig.BoneForRole(role)!.Value;
                var (lo, hi, t) = Bracket(srcU, tgtU[k]);

                var ct = _tgtCanon.WorldFrameOf(role);
                _spine.Add(new SpineEntry
                {
                    TgtBone = tgtBone,
                    LoSlot = RegisterSlot(srcMap.RoleToBone[srcSpine[lo]]),
                    HiSlot = RegisterSlot(srcMap.RoleToBone[srcSpine[hi]]),
                    T = t,
                    CsLo = _srcCanon.WorldFrameOf(srcSpine[lo]),
                    CsHi = _srcCanon.WorldFrameOf(srcSpine[hi]),
                    CtInvRest = MathQ.Normalize(Quaternion.Conjugate(ct) * _tgtNormRest[tgtBone].Rot),
                });
            }
        }

        /// <summary>The neck (or head) rest position extends the spine chain so the last spine
        /// bone gets an arc parameter &lt; 1, comparable across rigs.</summary>
        private static Vector3? ChainEndAnchor(Func<BoneRole, int?> boneForRole, XForm[] worldRest)
        {
            foreach (var role in new[] { BoneRole.Neck, BoneRole.Head })
            {
                if (boneForRole(role) is int bone)
                    return worldRest[bone].Pos;
            }
            return null;
        }

        /// <summary>Normalized cumulative arc-length parameters of a chain's bones
        /// (first bone = 0; the optional end anchor counts toward the total length).</summary>
        private static float[] ArcParams(Vector3[] points, Vector3? endAnchor)
        {
            var u = new float[points.Length];
            var cum = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                cum += (points[i] - points[i - 1]).Length();
                u[i] = cum;
            }

            var total = cum + (endAnchor is { } anchor ? (anchor - points[^1]).Length() : 0f);
            if (total <= 1e-6f)
                return u; // degenerate chain: all parameters 0

            for (var i = 0; i < u.Length; i++)
                u[i] /= total;
            return u;
        }

        private static (int Lo, int Hi, float T) Bracket(float[] knots, float u)
        {
            if (knots.Length == 1 || u <= knots[0])
                return (0, 0, 0f);
            if (u >= knots[^1])
                return (knots.Length - 1, knots.Length - 1, 0f);
            for (var j = 0; j + 1 < knots.Length; j++)
            {
                if (u > knots[j + 1])
                    continue;
                var span = knots[j + 1] - knots[j];
                return (j, j + 1, span > 1e-6f ? (u - knots[j]) / span : 0f);
            }
            return (knots.Length - 1, knots.Length - 1, 0f); // unreachable
        }

        // ------------------------------------------------------------ per frame

        public XForm[] SolveFrame(XForm[] srcLocals)
        {
            if (srcLocals.Length != _src.Count)
                throw new ArgumentException(
                    $"Frame has {srcLocals.Length} bone transforms but the source skeleton has {_src.Count}.",
                    nameof(srcLocals));

            // Source FK (frame locals live in the original rest hierarchy).
            for (var i = 0; i < _src.Count; i++)
            {
                var parent = _src[i].ParentIndex;
                _srcWorld[i] = parent < 0 ? srcLocals[i] : XForm.Compose(_srcWorld[parent], srcLocals[i]);
            }

            // World rotation deltas from the normalized source rest.
            for (var k = 0; k < _slots.Count; k++)
            {
                var (bone, restInv) = _slots[k];
                _deltas[k] = MathQ.Normalize(_srcWorld[bone].Rot * restInv);
            }

            Array.Clear(_solved, 0, _solved.Length);

            foreach (var d in _direct)
            {
                _rot[d.TgtBone] = MathQ.Normalize(d.Pre * _deltas[d.Slot] * d.B);
                _solved[d.TgtBone] = true;
            }

            foreach (var s in _spine)
            {
                // Absolute character-space canonical orientations of the bracketing source
                // spine bones, Slerped at the target bone's arc parameter.
                var aLo = MathQ.Normalize(_chrSrcInv * _deltas[s.LoSlot] * s.CsLo);
                var dc = s.T <= 0f
                    ? aLo
                    : Quaternion.Slerp(aLo, MathQ.Normalize(_chrSrcInv * _deltas[s.HiSlot] * s.CsHi), s.T);
                _rot[s.TgtBone] = MathQ.Normalize(_chrTgt * dc * s.CtInvRest);
                _solved[s.TgtBone] = true;
            }

            _fingers?.Apply(_deltas, _solved, _rot);

            // Pelvis translation: character-frame re-expression with hip-height scaling.
            Vector3? hipsPos = null;
            if (_srcHips >= 0 && _tgtHips >= 0 && _solved[_tgtHips])
            {
                var v = Vector3.Transform(
                    _srcWorld[_srcHips].Pos - _srcNormRest[_srcHips].Pos, _chrSrcInv);
                v = new Vector3(v.X * _scaleH, v.Y * _scaleH, v.Z * _scaleV); // chr Z = up
                hipsPos = _tgtNormRest[_tgtHips].Pos + Vector3.Transform(v, _chrTgt);
            }

            // Compose output locals top-down over the target hierarchy.
            var outLocals = new XForm[_tgt.Count];
            for (var i = 0; i < _tgt.Count; i++)
            {
                var bone = _tgt[i];
                var parent = bone.ParentIndex;
                if (!_solved[i])
                {
                    _tgtWorld[i] = parent < 0
                        ? bone.RestLocal
                        : XForm.Compose(_tgtWorld[parent], bone.RestLocal);
                    outLocals[i] = bone.RestLocal;
                    continue;
                }

                var pos = i == _tgtHips && hipsPos is { } hp
                    ? hp
                    : parent < 0
                        ? bone.RestLocal.Pos
                        : _tgtWorld[parent].TransformPoint(bone.RestLocal.Pos);
                _tgtWorld[i] = new XForm(pos, _rot[i]);
                outLocals[i] = parent < 0 ? _tgtWorld[i] : XForm.ToLocal(_tgtWorld[parent], _tgtWorld[i]);
            }

            ValidateFinite(outLocals);
            return outLocals;
        }

        private static void ValidateFinite(XForm[] locals)
        {
            foreach (var x in locals)
            {
                var sum = x.Pos.X + x.Pos.Y + x.Pos.Z + x.Rot.X + x.Rot.Y + x.Rot.Z + x.Rot.W;
                if (!float.IsFinite(sum))
                    throw new InvalidOperationException(
                        "Retarget solve produced a non-finite transform — geometry or input data is degenerate.");
            }
        }
    }
}
