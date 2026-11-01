#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Numerics;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Core.Target;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.Core.Solve;

using Vector3 = System.Numerics.Vector3; // s&box compat: shadow engine's global-namespace Vector3 (see Code/HumanoidRetargeter/Assembly.cs)

/// <summary>
/// Retargets a 3D clip onto a 2D plane of the target armature: an orthographic camera
/// (<see cref="PlanarView"/>) projects the source joints, and each mapped target bone takes
/// the DIRECTION of its projected source segment, so the target keeps its own proportions
/// and only rotates inside the plane.
/// </summary>
/// <remarks>
/// <para>The target's plane comes from its rest geometry. An armature drawn flat (all mapped
/// joints in one coordinate plane) stays in its own plane whatever the camera; any other
/// armature uses the camera turned around its own character frame.</para>
/// <para>A rigid transform has no scale, so foreshortening moves the next joint closer: the
/// length a segment loses to the projection is lost by the bone. Depth order is real depth:
/// shoulders and hips sit at the source's depth from the trunk, scaled to the target, which
/// puts the near limbs in front of the far ones; a flat armature adds this to its own layers.
/// Limbs below them stay parallel to the plane.</para>
/// <para>A segment that rests further than 45 degrees out of the plane has no 2D angle. Arms
/// are laid into the plane (a T-pose seen from the side hangs its arms down first), anything
/// else keeps its real direction (a foot seen from the front). Fingers and unmapped bones ride
/// their parent.</para>
/// </remarks>
public sealed class PlanarSolver : IRetargetSolver
{
    /// <summary>Below this projected / 3D length a segment holds its previous angle.</summary>
    private const float HoldRatio = 0.2f;

    /// <summary>The fastest a bone turns in the plane, in radians per second (1800 degrees,
    /// past real limb speed). A limb crossing the view axis flips its projected angle by up
    /// to 180 degrees; followed at once, that swung a hand 50 cm in one frame of real mocap.</summary>
    private const float MaxTurnSpeed = 10f * MathF.PI;

    /// <summary>Below this planar / full length a segment rests out of the plane (45 degrees):
    /// forced into it, a steep s&amp;box foot seen from the front would point at the floor.</summary>
    private const float RestInPlaneRatio = 0.70710678f;

    /// <summary>The shortest a foreshortened segment gets, as a share of its length.</summary>
    private const float MinForeshorten = 0.1f;

    /// <summary>An armature is flat when its thinnest extent is below this share of its largest.</summary>
    private const float FlatRatio = 0.02f;

    /// <summary>Attachment scale limits, as a factor of the rig's overall scale: a stub over
    /// a tiny source segment must not fling the shoulders off the body.</summary>
    private const float MinAttachScale = 0.5f, MaxAttachScale = 2f;

    /// <summary>Joints closer than this share of the rig's height are the same pivot.</summary>
    private const float SamePivotRatio = 1e-3f;

    private const float Epsilon = 1e-6f;

    private static readonly BoneRole[] Trunk =
    {
        BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2, BoneRole.Spine3,
        BoneRole.Spine4, BoneRole.Neck, BoneRole.Head,
    };

    private static readonly (BoneRole Girdle, BoneRole[] Chain)[] Arms =
    {
        (BoneRole.ClavicleL, new[] { BoneRole.UpperArmL, BoneRole.LowerArmL, BoneRole.HandL }),
        (BoneRole.ClavicleR, new[] { BoneRole.UpperArmR, BoneRole.LowerArmR, BoneRole.HandR }),
    };

    private static readonly BoneRole[][] Legs =
    {
        new[] { BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.ToeL },
        new[] { BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR, BoneRole.ToeR },
    };

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the source or target lacks the bones
    /// the camera needs (hips, a trunk, and on any non-flat rig the character frame).</exception>
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

        var output = new Clip(options.ClipName ?? clip.Name, clip.Fps, clip.Looping);
        if (clip.Frames.Count == 0)
            return output;
        var plan = new Plan(source, clip, sourceMap, target, options);
        plan.GroundFeet(clip);
        foreach (var frame in clip.Frames)
            output.Frames.Add(plan.SolveFrame(frame));
        return output;
    }

    /// <summary>
    /// The camera that sees a <see cref="SolverKind.Planar"/> clip of <paramref name="target"/>
    /// square-on, in rig space: its up, and the direction from the character toward it.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a non-flat rig has no character frame,
    /// or a flat one no hips and trunk.</exception>
    public static (Vector3 Up, Vector3 Toward) CameraAxes(TargetRig target, PlanarView view)
    {
        ArgumentNullException.ThrowIfNull(target);
        var basis = TargetBasis(target, view);
        return (basis.Up, basis.Toward);
    }

    /// <summary>
    /// The view an armature drawn flat was drawn from: <see cref="PlanarView.Side"/> when its
    /// toes reach further across the plane than its hips, else <see cref="PlanarView.Front"/>.
    /// Null for an armature that is not flat, which any view can look at.
    /// </summary>
    public static PlanarView? DrawnView(TargetRig target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (FlatAxis(target) is not { } axis)
            return null;
        var rest = target.Skeleton.RestWorld;
        float Across(BoneRole from, BoneRole to)
        {
            if (target.BoneForRole(from) is not { } a || target.BoneForRole(to) is not { } b)
                return 0f;
            var span = rest[b].Pos - rest[a].Pos;
            return (span - axis * Vector3.Dot(span, axis)).Length();
        }
        var hipSpan = Across(BoneRole.UpperLegR, BoneRole.UpperLegL);
        var toeSpan = MathF.Max(Across(BoneRole.FootL, BoneRole.ToeL), Across(BoneRole.FootR, BoneRole.ToeR));
        return toeSpan > hipSpan ? PlanarView.Side : PlanarView.Front;
    }

    /// <summary>
    /// The camera a <see cref="SolverKind.Planar"/> solve looks at a source through, in the
    /// source's own space, for showing that source the way it is projected.
    /// <paramref name="firstFrame"/> is the clip's first frame in world space.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the mapping lacks the bones a character
    /// frame needs.</exception>
    public static (Vector3 Right, Vector3 Up, Vector3 Toward) SourceCamera(
        SkeletonModel skeleton, MappingResult map, XForm[] firstFrame, PlanarView view)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(firstFrame);
        var anatomical = CharacterFrame.Compute(skeleton, map, skeleton.RestWorld).Up;
        var basis = ViewBasis.Of(FacingFrame(skeleton, map, firstFrame, NearestAxis(anatomical)), view).Rolled(view.RollDegrees);
        return (basis.Right, basis.Up, basis.Toward);
    }

    /// <summary>A character frame around <paramref name="up"/>, facing the way the clip starts.</summary>
    private static CharacterFrame FacingFrame(SkeletonModel skeleton, MappingResult map, XForm[] firstFrame, Vector3 up)
    {
        try
        {
            return CharacterFrame.Compute(skeleton, map, firstFrame, up);
        }
        catch (ArgumentException)
        {
            // A first frame lying on its side has no hip line to face along.
            return CharacterFrame.Compute(skeleton, map, skeleton.RestWorld, up);
        }
    }

    private static ViewBasis TargetBasis(TargetRig rig, PlanarView view)
        => FlatAxis(rig) is { } axis ? FlatBasis(rig, axis) : ViewBasis.Of(TargetFrame(rig), view);

    /// <summary>The coordinate axis nearest <paramref name="direction"/>, keeping its sign.
    /// Hips to shoulders is not vertical: a Mixamo rig leans 7 degrees, which tilts the whole
    /// view and sinks the feet as the character travels.</summary>
    private static Vector3 NearestAxis(Vector3 direction)
    {
        var size = Vector3.Abs(direction);
        if (size.X >= size.Y && size.X >= size.Z)
            return new Vector3(MathF.Sign(direction.X), 0f, 0f);
        return size.Y >= size.Z
            ? new Vector3(0f, MathF.Sign(direction.Y), 0f)
            : new Vector3(0f, 0f, MathF.Sign(direction.Z));
    }

    private static CharacterFrame TargetFrame(TargetRig rig)
    {
        try
        {
            var map = rig.ToMappingResult();
            var rest = rig.Skeleton.RestWorld;
            return CharacterFrame.Compute(rig.Skeleton, map, rest,
                NearestAxis(CharacterFrame.Compute(rig.Skeleton, map, rest).Up));
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"2D retarget: target character frame not computable ({e.Message}).", e);
        }
    }

    /// <summary>The coordinate axis the mapped joints have no extent along, or null.</summary>
    // shortcut: only sees armatures flat along a coordinate axis, a rotated flat rig reads as 3D; fit a plane if one turns up.
    private static Vector3? FlatAxis(TargetRig rig)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var mapped = false;
        for (var i = 0; i < rig.Skeleton.Count; i++)
        {
            if (rig.RoleOf(i) is null)
                continue;
            mapped = true;
            min = Vector3.Min(min, rig.Skeleton.RestWorld[i].Pos);
            max = Vector3.Max(max, rig.Skeleton.RestWorld[i].Pos);
        }
        if (!mapped)
            return null;
        var size = max - min;
        var limit = MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * FlatRatio;
        if (size.X <= limit)
            return Vector3.UnitX;
        if (size.Y <= limit)
            return Vector3.UnitY;
        return size.Z <= limit ? Vector3.UnitZ : null;
    }

    /// <summary>The basis of a flat armature. Screen right is the way its toes point
    /// when drawn in profile, its left when drawn from the front; the source's facing
    /// then lands on the rig's own, whichever way it was drawn.</summary>
    private static ViewBasis FlatBasis(TargetRig rig, Vector3 axis)
    {
        var rest = rig.Skeleton.RestWorld;
        var trunk = new List<int>(Trunk.Length);
        foreach (var role in Trunk)
        {
            if (rig.BoneForRole(role) is { } bone)
                trunk.Add(bone);
        }
        if (trunk.Count < 2 || rig.BoneForRole(BoneRole.Hips) is null)
            throw new ArgumentException("2D retarget: a flat armature needs its hips and a spine, neck or head mapped.");

        var up = rest[trunk[^1]].Pos - rest[trunk[0]].Pos;
        up = NearestAxis(up - axis * Vector3.Dot(up, axis));
        var right = Vector3.Cross(up, axis);

        float Along(BoneRole from, BoneRole to)
            => rig.BoneForRole(from) is { } a && rig.BoneForRole(to) is { } b
                ? Vector3.Dot(rest[b].Pos - rest[a].Pos, right)
                : 0f;

        var toes = Along(BoneRole.FootL, BoneRole.ToeL) + Along(BoneRole.FootR, BoneRole.ToeR);
        var hips = Along(BoneRole.UpperLegR, BoneRole.UpperLegL);
        return (MathF.Abs(hips) > MathF.Abs(toes) ? hips : toes) < 0f
            ? new ViewBasis(-right, up, -axis)
            : new ViewBasis(right, up, axis);
    }

    /// <summary>Right, up and toward-viewer axes of a view, in rig space.</summary>
    private readonly record struct ViewBasis(Vector3 Right, Vector3 Up, Vector3 Toward)
    {
        public static ViewBasis Of(CharacterFrame frame, PlanarView view)
        {
            var yaw = view.YawDegrees * MathF.PI / 180f;
            var pitch = view.PitchDegrees * MathF.PI / 180f;
            var right = MathF.Cos(yaw) * frame.Lateral + MathF.Sin(yaw) * frame.Forward;
            var toward = MathF.Cos(yaw) * frame.Forward - MathF.Sin(yaw) * frame.Lateral;
            return new ViewBasis(right,
                MathF.Cos(pitch) * frame.Up - MathF.Sin(pitch) * toward,
                MathF.Cos(pitch) * toward + MathF.Sin(pitch) * frame.Up);
        }

        /// <summary>The same camera turned in the picture's own plane.</summary>
        public ViewBasis Rolled(float degrees)
        {
            var roll = degrees * MathF.PI / 180f;
            return new ViewBasis(
                MathF.Cos(roll) * Right + MathF.Sin(roll) * Up,
                MathF.Cos(roll) * Up - MathF.Sin(roll) * Right,
                Toward);
        }

        /// <summary>A rig-space vector as (right, up, toward).</summary>
        public Vector3 ToView(Vector3 world)
            => new(Vector3.Dot(world, Right), Vector3.Dot(world, Up), Vector3.Dot(world, Toward));

        public Vector3 ToWorld(Vector3 view) => view.X * Right + view.Y * Up + view.Z * Toward;
    }

    /// <summary>Places a joint from another one by the source's offset between them, depth
    /// included: what a shoulder or a thigh does hanging off the trunk.</summary>
    private sealed record Attachment(int From, int SrcFrom, int Joint, int SrcJoint, float RestDepth)
    {
        public float Scale { get; set; } = 1f;
    }

    /// <summary>One target bone driven from one source bone.</summary>
    private sealed class Link
    {
        public required int Bone { get; init; }
        public required int SrcBone { get; init; }

        /// <summary>The next joint along the chain; -1 on a leaf, which carries its parent's line on.</summary>
        public int Next { get; init; } = -1;
        public int SrcNext { get; init; } = -1;

        /// <summary>The rest parent line in the source bone's frame (leaves only).</summary>
        public Vector3 SrcLeafAxis { get; init; }

        public float Length { get; init; }
        public float SrcLength { get; init; }
        public required Vector3 RestDirection { get; init; }
        public required Quaternion RestRotation { get; init; }

        /// <summary>The rest rotation laid into the plane, and the angle it then has there.</summary>
        public Quaternion LaidRotation { get; init; }
        public float RestAngle { get; init; }

        /// <summary>Rests along the view axis: keeps its real direction, not a 2D angle.</summary>
        public bool KeepsDepth { get; init; }

        /// <summary>Where this bone goes, when not at the tip of the link before it.</summary>
        public Attachment? Root { get; set; }

        /// <summary>A clavicle: aims at the joint this places instead of taking an angle.</summary>
        public Attachment? Tip { get; set; }

        public float? HeldAngle { get; set; }
    }

    /// <summary>Everything built once per solve. One instance per clip: held angles carry
    /// from frame to frame, so frames must be solved in order.</summary>
    private sealed class Plan
    {
        private readonly SkeletonModel _src;
        private readonly SkeletonModel _tgt;
        private readonly MappingResult _srcMap;
        private readonly TargetRig _rig;
        private readonly ViewBasis _srcView;
        private readonly ViewBasis _tgtView;
        private readonly bool _tgtIsFlat;
        private readonly bool _foreshorten;
        private readonly bool _inPlace;
        private readonly List<Link> _links = new();
        private readonly bool[] _isDriven;
        private readonly int _srcHips;
        private readonly int _tgtHips;
        private readonly Vector3 _srcUp;
        private readonly Vector3 _firstHips;
        private readonly Vector3 _hipsReference;
        private readonly float _scaleH;
        private readonly float _scaleV;
        private readonly float _maxTurn;
        private float _groundLift;
        private readonly Vector3[] _position;
        private readonly Quaternion[] _rotation;
        private readonly XForm[] _world;

        public Plan(SourceScene source, Clip clip, MappingResult srcMap, TargetRig rig, SolveOptions options)
        {
            _src = source.Skeleton;
            _tgt = rig.Skeleton;
            _srcMap = srcMap;
            _rig = rig;
            _foreshorten = options.Foreshorten;
            _inPlace = options.PlanarInPlace;
            _maxTurn = MaxTurnSpeed / clip.Fps;
            _isDriven = new bool[_tgt.Count];
            _position = new Vector3[_tgt.Count];
            _rotation = new Quaternion[_tgt.Count];
            _world = new XForm[_tgt.Count];

            var trunk = Joints(Trunk);
            if (trunk.Count < 2 || trunk[0].Tgt != rig.BoneForRole(BoneRole.Hips))
                throw new ArgumentException(
                    "2D retarget needs the hips and a spine, neck or head mapped on both the source and the target.");
            (_srcHips, _tgtHips) = trunk[0];

            var fileUp = source.UpAxis switch
            {
                0 => new Vector3(source.UpAxisSign, 0f, 0f),
                2 => new Vector3(0f, 0f, source.UpAxisSign),
                _ => new Vector3(0f, source.UpAxisSign, 0f),
            };
            var firstWorld = new Pose(clip.Frames[0]).ToWorld(_src);
            _srcView = ViewBasis.Of(SourceFrame(fileUp, firstWorld), options.View).Rolled(options.View.RollDegrees);
            _srcUp = fileUp;

            _tgtIsFlat = FlatAxis(rig) is not null;
            _tgtView = TargetBasis(rig, options.View);

            AddChain(trunk, laysIntoPlane: false, root: null);
            foreach (var (girdle, chain) in Arms)
                AddLimb(girdle, chain, laysIntoPlane: true);
            foreach (var chain in Legs)
                AddLimb(null, chain, laysIntoPlane: false);

            var scale = RigScale(_ => true);
            foreach (var link in _links)
            {
                ScaleAttachment(link.Root, scale);
                ScaleAttachment(link.Tip, scale);
            }
            // The hips move with the legs that carry them, or a dip lifts or sinks the feet.
            var legScale = RigScale(link => _rig.RoleOf(link.Bone) is BoneRole.UpperLegL or BoneRole.UpperLegR
                or BoneRole.LowerLegL or BoneRole.LowerLegR);
            _scaleH = options.HipScaleHorizontal ?? legScale;
            _scaleV = options.HipScaleVertical ?? legScale;

            _firstHips = firstWorld[_srcHips].Pos;
            _hipsReference = HipsReference(source, clip);
        }

        /// <summary>The joints of <paramref name="roles"/> present on both rigs, in order. A
        /// target joint on the same pivot as the previous one rides it: no direction to take.</summary>
        private List<(int Src, int Tgt)> Joints(BoneRole[] roles)
        {
            var samePivot = RigHeight() * SamePivotRatio;
            var joints = new List<(int Src, int Tgt)>(roles.Length);
            foreach (var role in roles)
            {
                if (_rig.BoneForRole(role) is not { } tgt || !_srcMap.RoleToBone.TryGetValue(role, out var src))
                    continue;
                if (joints.Count > 0 && Vector3.Distance(
                        _tgt.RestWorld[tgt].Pos, _tgt.RestWorld[joints[^1].Tgt].Pos) < samePivot)
                    continue;
                joints.Add((src, tgt));
            }
            return joints;
        }

        private float RigHeight()
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var bone in _tgt.RestWorld)
            {
                min = Vector3.Min(min, bone.Pos);
                max = Vector3.Max(max, bone.Pos);
            }
            var size = max - min;
            return MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        }

        /// <summary>The source's character frame, facing the way the clip starts: mocap
        /// rarely faces the way its T-pose does, and the view is relative to the performer.
        /// Up is the axis nearest the anatomical one, or the file's vertical when the rest leans too far.</summary>
        private CharacterFrame SourceFrame(Vector3 fileUp, XForm[] firstWorld)
        {
            try
            {
                var anatomical = CharacterFrame.Compute(_src, _srcMap, _src.RestWorld).Up;
                var up = Vector3.Dot(anatomical, fileUp) < 0.70710678f ? fileUp : NearestAxis(anatomical);
                return FacingFrame(_src, _srcMap, firstWorld, up);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"2D retarget: source character frame not computable ({e.Message}).", e);
            }
        }

        private void AddLimb(BoneRole? girdle, BoneRole[] roles, bool laysIntoPlane)
        {
            var joints = Joints(roles);
            if (joints.Count < 2)
                return;

            var first = joints[0];
            var clavicle = girdle is { } role ? Joints(new[] { role }) : null;
            if (clavicle is { Count: 1 })
            {
                var (srcBone, bone) = clavicle[0];
                var rest = _tgt.RestWorld;
                var direction = rest[first.Tgt].Pos - rest[bone].Pos;
                var link = new Link
                {
                    Bone = bone, SrcBone = srcBone, Next = first.Tgt, SrcNext = first.Src,
                    RestDirection = direction, RestRotation = rest[bone].Rot,
                    Root = AttachToTrunk(bone, srcBone),
                    Tip = NewAttachment(bone, srcBone, first.Tgt, first.Src),
                };
                _links.Add(link);
                _isDriven[bone] = true;
                AddChain(joints, laysIntoPlane, root: null);
                return;
            }
            AddChain(joints, laysIntoPlane, AttachToTrunk(first.Tgt, first.Src));
        }

        private Attachment AttachToTrunk(int bone, int srcBone)
        {
            var anchor = _links[0];
            for (var p = _tgt[bone].ParentIndex; p >= 0; p = _tgt[p].ParentIndex)
            {
                if (_links.Find(l => l.Bone == p) is { } link)
                {
                    anchor = link;
                    break;
                }
            }
            return NewAttachment(anchor.Bone, anchor.SrcBone, bone, srcBone);
        }

        private Attachment NewAttachment(int from, int srcFrom, int joint, int srcJoint)
        {
            var restOffset = _tgt.RestWorld[joint].Pos - _tgt.RestWorld[from].Pos;
            return new Attachment(from, srcFrom, joint, srcJoint,
                _tgtIsFlat ? Vector3.Dot(restOffset, _tgtView.Toward) : 0f);
        }

        private void AddChain(List<(int Src, int Tgt)> joints, bool laysIntoPlane, Attachment? root)
        {
            var srcRest = _src.RestWorld;
            var rest = _tgt.RestWorld;
            for (var k = 0; k < joints.Count; k++)
            {
                var (srcBone, bone) = joints[k];
                var isLeaf = k == joints.Count - 1;
                var next = isLeaf ? (Src: -1, Tgt: -1) : joints[k + 1];
                var line = isLeaf
                    ? rest[bone].Pos - rest[joints[k - 1].Tgt].Pos
                    : rest[next.Tgt].Pos - rest[bone].Pos;
                var view = _tgtView.ToView(line);
                var planarLength = MathF.Sqrt(view.X * view.X + view.Y * view.Y);

                var isInPlane = planarLength >= RestInPlaneRatio * line.Length();
                var laid = isInPlane ? _tgtView.ToWorld(new Vector3(view.X, view.Y, 0f)) : -_tgtView.Up;
                var keepsDepth = !isInPlane && !laysIntoPlane;
                _links.Add(new Link
                {
                    Bone = bone, SrcBone = srcBone, Next = next.Tgt, SrcNext = next.Src,
                    SrcLeafAxis = isLeaf
                        ? Vector3.Transform(srcRest[srcBone].Pos - srcRest[joints[k - 1].Src].Pos,
                            Quaternion.Conjugate(srcRest[srcBone].Rot))
                        : Vector3.Zero,
                    Length = isLeaf ? 0f : line.Length(),
                    SrcLength = isLeaf ? 0f : Vector3.Distance(srcRest[next.Src].Pos, srcRest[srcBone].Pos),
                    RestDirection = line,
                    RestRotation = rest[bone].Rot,
                    LaidRotation = keepsDepth
                        ? rest[bone].Rot
                        : MathQ.Normalize(MathQ.FromTo(line, laid) * rest[bone].Rot),
                    RestAngle = isInPlane ? MathF.Atan2(view.Y, view.X) : -MathF.PI / 2f,
                    KeepsDepth = keepsDepth,
                    Root = k == 0 ? root : null,
                });
                _isDriven[bone] = true;
            }
        }

        /// <summary>How much bigger the target is than the source, over the chosen segments.</summary>
        private float RigScale(Func<Link, bool> counts)
        {
            float length = 0f, srcLength = 0f;
            foreach (var link in _links)
            {
                if (!counts(link))
                    continue;
                length += link.Length;
                srcLength += link.SrcLength;
            }
            var scale = srcLength > Epsilon ? length / srcLength : 1f;
            return float.IsFinite(scale) && scale > 0f ? scale : 1f;
        }

        private void ScaleAttachment(Attachment? attachment, float rigScale)
        {
            if (attachment is null)
                return;
            var length = Vector3.Distance(_tgt.RestWorld[attachment.Joint].Pos, _tgt.RestWorld[attachment.From].Pos);
            var srcLength = Vector3.Distance(_src.RestWorld[attachment.SrcJoint].Pos, _src.RestWorld[attachment.SrcFrom].Pos);
            attachment.Scale = srcLength > Epsilon
                ? Math.Clamp(length / srcLength, MinAttachScale * rigScale, MaxAttachScale * rigScale)
                : rigScale;
        }

        /// <summary>The source hips position the target's rest hips stand for: over where
        /// the clip starts, and for a rest with no authored placement (BVH), at the rest's
        /// height above the clip's own ground.</summary>
        private Vector3 HipsReference(SourceScene source, Clip clip)
        {
            var rest = _src.RestWorld[_srcHips].Pos;
            var travel = _firstHips - rest;
            var reference = rest + travel - _srcUp * Vector3.Dot(travel, _srcUp);
            if (source.RestPlacementAuthored)
                return reference;

            var motionGround = float.MaxValue;
            foreach (var frame in clip.Frames)
            {
                foreach (var bone in new Pose(frame).ToWorld(_src))
                    motionGround = MathF.Min(motionGround, Vector3.Dot(bone.Pos, _srcUp));
            }
            var restGround = float.MaxValue;
            foreach (var bone in _src.RestWorld)
                restGround = MathF.Min(restGround, Vector3.Dot(bone.Pos, _srcUp));
            return reference + _srcUp * (motionGround - restGround);
        }

        /// <summary>One height correction for the whole clip, so the lowest a foot gets is
        /// where it rests. Legs shorter than the source's cannot reach as far down as the
        /// hips drop; correcting frame by frame would flatten a jump.</summary>
        public void GroundFeet(Clip clip)
        {
            var feet = new List<int>();
            foreach (var role in new[] { BoneRole.FootL, BoneRole.FootR, BoneRole.ToeL, BoneRole.ToeR })
            {
                if (_rig.BoneForRole(role) is { } bone && _isDriven[bone])
                    feet.Add(bone);
            }
            if (feet.Count == 0)
                return;

            var rest = float.MaxValue;
            foreach (var foot in feet)
                rest = MathF.Min(rest, Vector3.Dot(_tgt.RestWorld[foot].Pos, _tgtView.Up));

            var lowest = float.MaxValue;
            foreach (var frame in clip.Frames)
            {
                SolveFrame(frame);
                foreach (var foot in feet)
                    lowest = MathF.Min(lowest, Vector3.Dot(_position[foot], _tgtView.Up));
            }
            foreach (var link in _links)
                link.HeldAngle = null;
            _groundLift = rest - lowest;
        }

        public XForm[] SolveFrame(XForm[] sourceLocals)
        {
            var source = new Pose(sourceLocals).ToWorld(_src);
            _position[_tgtHips] = HipsPosition(source);
            foreach (var link in _links)
                SolveLink(link, source);

            var locals = new XForm[_tgt.Count];
            for (var i = 0; i < locals.Length; i++)
            {
                var parent = _tgt[i].ParentIndex;
                var parentWorld = parent < 0 ? XForm.Identity : _world[parent];
                if (_isDriven[i])
                {
                    _world[i] = new XForm(_position[i], _rotation[i]);
                    locals[i] = XForm.ToLocal(parentWorld, _world[i]);
                }
                else
                {
                    locals[i] = _tgt[i].RestLocal;
                    _world[i] = XForm.Compose(parentWorld, locals[i]);
                }
            }
            return locals;
        }

        private Vector3 HipsPosition(XForm[] source)
        {
            var hips = source[_srcHips].Pos;
            if (_inPlace)
                hips = _firstHips + _srcUp * Vector3.Dot(hips - _firstHips, _srcUp);
            var travel = _srcView.ToView(hips - _hipsReference);
            return _tgt.RestWorld[_tgtHips].Pos + _tgtView.ToWorld(
                new Vector3(travel.X * _scaleH, travel.Y * _scaleV + _groundLift, 0f));
        }

        private void SolveLink(Link link, XForm[] source)
        {
            if (link.Root is { } root)
                _position[link.Bone] = Attach(root, source);
            var position = _position[link.Bone];

            if (link.Tip is { } tip)
            {
                _position[link.Next] = Attach(tip, source);
                var aim = _position[link.Next] - position;
                if (_tgtIsFlat)
                    aim -= _tgtView.Toward * Vector3.Dot(aim, _tgtView.Toward);
                _rotation[link.Bone] = MathQ.Normalize(MathQ.FromTo(link.RestDirection, aim) * link.RestRotation);
                return;
            }

            var segment = _srcView.ToView(SourceLine(link, source));
            if (link.KeepsDepth)
            {
                var direction = _tgtView.ToWorld(segment);
                _rotation[link.Bone] = MathQ.Normalize(MathQ.FromTo(link.RestDirection, direction) * link.RestRotation);
                if (link.Next >= 0 && direction.LengthSquared() > Epsilon)
                    _position[link.Next] = position + Vector3.Normalize(direction) * link.Length;
                return;
            }

            var planarLength = MathF.Sqrt(segment.X * segment.X + segment.Y * segment.Y);
            var length = segment.Length();
            var ratio = length > Epsilon ? MathF.Min(1f, planarLength / length) : 1f;
            var angle = planarLength > Epsilon
                ? MathF.Atan2(segment.Y, segment.X)
                : link.HeldAngle ?? link.RestAngle;
            if (link.HeldAngle is { } held)
            {
                var step = ratio < HoldRatio ? 0f : MathF.IEEERemainder(angle - held, 2f * MathF.PI);
                angle = held + Math.Clamp(step, -_maxTurn, _maxTurn);
            }
            link.HeldAngle = angle;

            var turn = Quaternion.CreateFromAxisAngle(_tgtView.Toward, angle - link.RestAngle);
            _rotation[link.Bone] = MathQ.Normalize(turn * link.LaidRotation);
            if (link.Next >= 0)
            {
                var foreshortened = _foreshorten ? MathF.Max(MinForeshorten, ratio) : 1f;
                var along = _tgtView.ToWorld(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f));
                _position[link.Next] = position + along * link.Length * foreshortened;
            }
        }

        private Vector3 SourceLine(Link link, XForm[] source)
            => link.Next >= 0
                ? source[link.SrcNext].Pos - source[link.SrcBone].Pos
                : Vector3.Transform(link.SrcLeafAxis, source[link.SrcBone].Rot);

        private Vector3 Attach(Attachment attachment, XForm[] source)
        {
            var offset = _srcView.ToView(source[attachment.SrcJoint].Pos - source[attachment.SrcFrom].Pos)
                * attachment.Scale;
            offset.Z += attachment.RestDepth;
            return _position[attachment.From] + _tgtView.ToWorld(offset);
        }
    }
}
