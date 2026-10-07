#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using Sandbox;
using SourceClip = HumanoidRetargeter.Core.Skeleton.Clip;
using SourceSkeleton = HumanoidRetargeter.Core.Skeleton.Skeleton;
using VecN = System.Numerics.Vector3;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// Skinned preview of a retargeted clip BEFORE anything is compiled: the target's real
/// compiled model (e.g. <c>citizen_human_male.vmdl</c>) is loaded into a
/// <see cref="SceneModel"/> and its bones are driven directly from
/// <see cref="ClipResult.SolvedFrames"/> - per frame the solved locals (target skeleton
/// bone order, target units) are FK-composed to model-space transforms, converted to the
/// engine's axis convention and units, and applied via
/// <see cref="SceneModel.SetBoneOverride"/> (which takes transforms local to the
/// SceneModel). Bones are matched to the engine model BY NAME, so helper bones missing
/// from the rig JSON keep their bind pose.
/// </summary>
/// <remarks>
/// <para><b>Axis conversion.</b> <see cref="TargetUpAxis.YUpCm"/> rigs (the s&amp;box source
/// skeleton) are authored Y-up in centimeters while the compiled engine model is Z-up in
/// inches - the same conversion resourcecompiler applies to the Y-up DMX at compile time.
/// FK world transforms are therefore mapped with the +90° rotation about X taking Y-up to
/// Z-up - position (x, y, z) → (x, −z, y), rotation q → q_R ⊗ q - then scaled by
/// <c>positionScale</c> (0.3937 cm→inch). Empirically: the citizen pelvis rests at
/// y ≈ 93 cm → engine (0, 0, ≈36.6 in), which the UI smoke gate asserts.
/// <see cref="TargetUpAxis.ZUpEngine"/> rigs (custom compiled-model targets) are already in
/// engine space - no conversion.</para>
/// <para>This was chosen over building an in-memory <c>Model.Builder</c> model with
/// <c>AddAnimation</c>/<c>AddFrame</c>: a builder model carries bones but no mesh, so a
/// sequence playing on it renders nothing visible - driving the real skinned model shows
/// the actual character. Camera: fixed 3/4 framing from the model bounds with left-drag
/// yaw orbit (same idiom as the editor's other preview widgets).</para>
/// </remarks>
public sealed class PreviewWidget : SceneRenderingWidget
{
	/// <summary>Rotation about +X by 90°, taking Y-up coordinates to Z-up (y→z, z→−y).</summary>
	static readonly System.Numerics.Quaternion YUpToZUp =
		System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitX, MathF.PI * 0.5f );

	SceneModel _sceneModel;
	TargetRig _rig;
	float _positionScale = 1f;
	bool _convertYUpToZUp;
	int[] _rigToModelBone;
	Transform[] _modelBindByRig;
	XForm[] _worldScratch;
	XForm[] _interpolatedScratch;

	HumanoidRetargeter.Core.ClipResult _clip;
	float _time;
	float _yaw = 35f;
	Vector2 _lastMouse;

	// ---- interactive camera (defaults reproduce the fixed 3/4 framing exactly) -----------
	const float DefaultYaw = 35f;
	static readonly float DefaultPitch = MathF.Atan( 0.35f ) * 180f / MathF.PI;
	float _pitch = DefaultPitch;
	float _zoom = 1f;
	Vector3 _pan;

	// ---- ground check (opt-in: the window shows it; headless gate renders stay unchanged) --
	int[] _footBones = Array.Empty<int>();
	int[] _soleBones = Array.Empty<int>();
	float _restFootZ;
	// Per foot: every bone of the foot (ankle, ball, toes, tips) and its lowest height at rest.
	int[][] _sideBones = Array.Empty<int[]>();
	// Per foot: contact points carried by a bone (local offset) - the joints themselves plus a
	// heel under the ankle and a toe tip ahead of the ball, both at floor height in the rest
	// pose. A heel strike lifts every joint but the heel; a toe-off, every joint but the tip.
	(int Bone, Vector3 Local)[][] _sideContacts = Array.Empty<(int, Vector3)[]>();
	float[] _sideRestZ = Array.Empty<float>();
	float[] _sideClearance = Array.Empty<float>();
	float _groundZ;
	float _characterHeight = 72f;
	XForm[] _clearanceScratch;

	// ---- target skeleton wireframe (the RETARGETED pose as stick skeleton) --------------
	// One SceneLineObject per skeleton CHAIN: the object holds a single polyline (every
	// StartLine restarts it - per-bone Start/End cycles left only the last segment on
	// screen, the user's "two bars"), and bridging disjoint segments through one strip
	// leaks connector streaks (the line shader ignores vertex alpha). A chain - spine,
	// each limb, each finger - IS a connected polyline, so this is the supported usage.
	readonly List<SceneLineObject> _skeletonChainObjects = new();
	int[][] _skeletonChains;
	bool _skeletonOnly;
	Vector3 _skeletonCenter = Vector3.Up * 32f;
	float _skeletonRadius = 40f;

	// ---- source ghost (stick-skeleton overlay of the SOURCE clip) -----------------------
	readonly List<SceneLineObject> _ghostChainObjects = new();
	int[][] _ghostChains;
	SourceSkeleton _ghostSkeleton;
	SourceClip _ghostClip;
	XForm[] _ghostScratch;
	bool _showSourceGhost;

	// source-side alignment (fixed once per SetSourceGhost)
	VecN _srcAnchor, _srcLat, _srcUp, _srcFwd;
	float _srcHipHeight;

	// target-side alignment (lazy, recomputed when the previewed clip changes)
	HumanoidRetargeter.Core.ClipResult _ghostAlignedClip;
	VecN _tgtAnchor, _tgtLat, _tgtUp, _tgtFwd;
	float _ghostScale = 1f;

	static readonly Color GhostBoneColor = new( 1f, 0.75f, 0.25f, 0.5f );   // amber, ~50%
	static readonly Color GhostJointColor = new( 1f, 0.8f, 0.35f, 0.65f );

	static readonly Color SkeletonBoneColor = new( 0.35f, 0.9f, 1f, 0.95f ); // cyan, solid
	static readonly Color SkeletonJointColor = new( 0.6f, 1f, 1f, 1f );

	/// <summary>
	/// Builds a line overlay object ready to actually RENDER: without an explicit line
	/// material a SceneLineObject draws with the engine's error material (user report: the
	/// skeleton view and the source ghost showed up as a single purple blob), and without
	/// generous explicit Bounds a SceneCustomObject can be culled entirely (its default
	/// bounds don't follow the added line points).
	/// </summary>
	SceneLineObject CreateLineObject()
	{
		var lines = new SceneLineObject( Scene.SceneWorld );
		lines.Opaque = false;
		lines.Lighting = false;
		var material = Material.Load( "materials/default/default_line.vmat" );
		if ( material is not null )
			lines.Material = material;
		return lines;
	}

	/// <summary>Expands a scene object's bounds around freshly drawn line points so the
	/// renderer never culls the overlay (called after every redraw).</summary>
	static void FitBounds( SceneLineObject lines, Vector3 min, Vector3 max )
	{
		if ( lines is null || min.x > max.x )
			return;
		var bounds = new BBox( min, max );
		lines.Bounds = bounds.Grow( MathF.Max( bounds.Size.Length * 0.25f, 16f ) );
	}

	/// <summary>Line width scaled to the character: the camera frames the whole skeleton,
	/// so a fixed sub-inch width collapses below one pixel on large rigs (measured: a
	/// 190-segment skeleton rendered ~5 pixels at 0.6in width - effectively invisible,
	/// which is what the user reported).</summary>
	float OverlayLineWidth => MathF.Max( 1.2f, _skeletonRadius * 0.03f );

	/// <summary>
	/// Splits a skeleton hierarchy into connected CHAINS (spine, each limb, each finger):
	/// runs of single-child bones, restarted at every branch point (the branch bone leads
	/// each child chain so the polylines connect). Each chain renders as its own
	/// SceneLineObject - the object holds a single polyline, so this is the only layout
	/// that draws a full skeleton (per-segment Start/End left one segment; alpha/width
	/// bridge tricks leak connector streaks).
	/// </summary>
	static int[][] BuildChains( Func<int, int> parentOf, int count )
	{
		var children = new List<int>[count];
		for ( var i = 0; i < count; i++ )
			children[i] = new List<int>();
		var roots = new List<int>();
		for ( var i = 0; i < count; i++ )
		{
			var parent = parentOf( i );
			if ( parent < 0 || parent >= count )
				roots.Add( i );
			else
				children[parent].Add( i );
		}

		var chains = new List<int[]>();
		var stack = new Stack<List<int>>();
		foreach ( var root in roots )
			stack.Push( new List<int> { root } );
		while ( stack.Count > 0 )
		{
			var path = stack.Pop();
			var node = path[^1];
			while ( children[node].Count == 1 )
			{
				node = children[node][0];
				path.Add( node );
			}
			if ( path.Count > 1 )
				chains.Add( path.ToArray() );
			foreach ( var child in children[node] )
				stack.Push( new List<int> { node, child } );
		}
		return chains.ToArray();
	}

	/// <summary>Grows/shrinks a chain-object pool to the chain count.</summary>
	void EnsureChainObjects( List<SceneLineObject> pool, int count )
	{
		while ( pool.Count < count )
			pool.Add( CreateLineObject() );
		while ( pool.Count > count )
		{
			pool[^1].Delete();
			pool.RemoveAt( pool.Count - 1 );
		}
	}

	/// <summary>Redraws one chain object as a single polyline through the given points.</summary>
	static void DrawChain( SceneLineObject lines, IReadOnlyList<Vector3> points, Color color, float width )
	{
		lines.Clear();
		if ( points.Count < 2 )
			return;
		lines.StartLine();
		foreach ( var point in points )
			lines.AddLinePoint( point, color, width );
		lines.EndLine();
	}

	/// <summary>Whether playback advances (play/pause).</summary>
	public bool Playing { get; set; } = true;

	/// <summary>Current frame (clamped to the clip).</summary>
	public int CurrentFrame { get; private set; }

	/// <summary>Frames in the current clip.</summary>
	public int FrameCount => _clip?.SolvedFrames?.Count ?? 0;

	/// <summary>Raised when playback advances to a new frame (drives the scrubber).</summary>
	public Action<int> FrameChanged { get; set; }

	/// <summary>True when a preview model could be loaded for the target.</summary>
	public bool HasModel => _sceneModel.IsValid();

	/// <summary>
	/// Wireframe-skeleton view of the RETARGETED animation: the target skeleton drawn as
	/// stick bones instead of (or, without a model, in place of) the skinned mesh. Targets
	/// with no compiled preview model (custom FBX targets) force this ON — the preview used
	/// to show nothing at all for them. Toggling back OFF restores the skinned view.
	/// </summary>
	public bool SkeletonOnly
	{
		get => _skeletonOnly || !HasModel;
		set
		{
			_skeletonOnly = value;
			if ( _sceneModel.IsValid() )
				_sceneModel.RenderingEnabled = !SkeletonOnly;
			if ( _boneModel.IsValid() )
				_boneModel.RenderingEnabled = SkeletonOnly;
		}
	}

	/// <summary>Line segments the skeleton view drew on its last update (bones + joint
	/// ticks). Exposed for the UI smoke gate's headless assertion.</summary>
	public int SkeletonLineCount { get; private set; }

	/// <summary>Geometry diagnostics of the last skeleton-view redraw (drawn extents,
	/// camera anchor, line width) for the UI smoke gate's notes.</summary>
	public string SkeletonDebug { get; private set; } = "";

	/// <summary>
	/// Creates the preview scene. <paramref name="previewModelPath"/> is the compiled
	/// model whose bone names match <paramref name="rig"/>;
	/// <paramref name="positionScale"/> converts rig positions to engine units
	/// (0.3937 for cm rigs like the s&amp;box source skeleton, 1.0 for engine-unit rigs);
	/// <paramref name="upAxis"/> is the rig's axis convention (<see cref="TargetUpAxis.YUpCm"/>
	/// rigs additionally get the Y-up→Z-up basis conversion, see class remarks).
	/// </summary>
	public PreviewWidget( Widget parent, TargetRig rig, string previewModelPath, float positionScale,
		TargetUpAxis upAxis = TargetUpAxis.YUpCm )
		: base( parent )
	{
		_rig = rig;
		_positionScale = positionScale;
		_convertYUpToZUp = upAxis == TargetUpAxis.YUpCm;
		MinimumSize = new Vector2( 360, 360 );
		MouseTracking = true;

		Scene = Scene.CreateEditorScene();
		using ( Scene.Push() )
		{
			Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			Camera.BackgroundColor = Theme.ControlBackground;
			Camera.ZNear = 1f;
			Camera.ZFar = 4096f;
			Camera.FieldOfView = 45f;
			Camera.Enabled = true;
		}

		var world = Scene.SceneWorld;
		new ScenePointLight( world, new Vector3( 120, 100, 120 ), 600, Color.White * 3.5f ).ShadowsEnabled = false;
		new ScenePointLight( world, new Vector3( -120, -100, 90 ), 600, Color.White * 2.0f ).ShadowsEnabled = false;

		if ( previewModelPath is not null )
		{
			var model = Model.Load( previewModelPath );
			if ( model is not null && !model.IsError )
			{
				_sceneModel = new SceneModel( world, model, Transform.Zero );
				_sceneModel.UseAnimGraph = false;
				BuildBoneMap( model );
			}
		}

		_worldScratch = new XForm[rig.Skeleton.Count];
		_interpolatedScratch = new XForm[rig.Skeleton.Count];

		// Camera anchor bones: the MAPPED role bones only. Character FBX files routinely
		// carry stray far-away nodes (Biped dummies, exporter helpers) - bounds taken over
		// every node put the camera fifty character-heights away, shrinking the wireframe
		// skeleton and the source ghost to a bar-sized smudge (user report: "it shows
		// like two bars"). The mapped roles ARE the character.
		var mapped = new List<int>();
		foreach ( var role in Enum.GetValues<HumanoidRetargeter.Core.Mapping.BoneRole>() )
		{
			if ( rig.BoneForRole( role ) is { } index )
				mapped.Add( index );
		}
		_cameraBones = mapped.Count >= 4
			? mapped.ToArray()
			: Enumerable.Range( 0, rig.Skeleton.Count ).ToArray();

		// Rest-pose bounds in engine space: camera zoom for the wireframe-skeleton view
		// (kept fixed so the zoom doesn't pulse with the animation; the center follows).
		var restBounds = ComputeRestBounds();
		_skeletonCenter = restBounds.Center;
		_skeletonRadius = MathF.Max( restBounds.Size.Length * 0.5f, 8f );
		_restCenterZ = restBounds.Center.z;
		SetUpGround( restBounds );
	}

	/// <summary>Draws a floor grid at the character's ground and a feet read-out, so hovering
	/// or sinking is visible at a glance. Off by default (the UI smoke gate renders without it).</summary>
	public bool ShowGround { get; set; }

	/// <summary>Lowest foot joint above (+) or below (−) where it stands in the rest pose, in
	/// engine units, for the pose on screen. Null when the rig has no mapped feet.</summary>
	public float? FootClearance { get; private set; }

	/// <summary>Contact tolerance for the whole-clip verdict: about an inch on a Citizen-sized
	/// character. A clip whose feet never get this close to the floor floats.</summary>
	public float ContactTolerance => MathF.Max( 0.4f, _characterHeight * 0.015f );

	/// <summary>Tolerance for a single frame: about 3 in on a human. The heel and toe tip are
	/// estimated, so a planted foot rolling through a step can read a couple of inches; real
	/// flight (runs, jumps) clears this easily. Hovering clips are caught by the whole-clip verdict.</summary>
	public float FrameContactTolerance => MathF.Max( 1f, _characterHeight * 0.04f );

	/// <summary>Whole-clip ground verdict, measured over every solved frame when a clip is set:
	/// the lowest the feet get relative to standing. Above the tolerance the character never
	/// touches the floor (floats); below it the feet sink through it.</summary>
	public float? ClipClearance { get; private set; }

	/// <summary>The feet are the character's contact with the floor: foot and toe bones.
	/// Their rest-pose height is "standing"; the floor is the mesh's lowest point at bind.</summary>
	void SetUpGround( BBox restBounds )
	{
		// A foot touches the floor with whichever of its joints is lowest - the heel lifts the
		// ankle while the ball and toes stay down - so each foot is its whole bone tree.
		var skeleton = _rig.Skeleton;
		var sides = new List<int[]>();
		var soles = new List<int>();
		foreach ( var (foot, toe) in new[] { (BoneRole.FootL, BoneRole.ToeL), (BoneRole.FootR, BoneRole.ToeR) } )
		{
			if ( _rig.BoneForRole( foot ) is not { } root )
				continue;
			var bones = new HashSet<int> { root };
			if ( _rig.BoneForRole( toe ) is { } toeBone )
				bones.Add( toeBone );
			for ( var i = 0; i < skeleton.Count; i++ )
			{
				for ( var p = skeleton[i].ParentIndex; p >= 0; p = skeleton[p].ParentIndex )
				{
					if ( p == root )
					{
						bones.Add( i );
						break;
					}
				}
			}
			sides.Add( bones.ToArray() );
			soles.Add( root );
		}
		_sideBones = sides.ToArray();
		_soleBones = soles.ToArray();
		_footBones = sides.SelectMany( b => b ).ToArray();
		_sideContacts = new (int, Vector3)[_sideBones.Length][];
		for ( var side = 0; side < _sideBones.Length; side++ )
		{
			var bones = _sideBones[side];
			var rest = bones.ToDictionary( i => i, i => RigWorldToEngine( skeleton.RestWorld[i] ) );
			var floor = rest.Values.Min( t => t.Position.z );
			var ankle = rest[soles[side]];
			var contacts = bones.Select( i => (i, Vector3.Zero) ).ToList();
			// Heel: straight under the ankle, on the floor.
			contacts.Add( (soles[side], ankle.PointToLocal( ankle.Position.WithZ( floor ) )) );
			// Toe tip: past the farthest foot joint, half the ankle-to-toe reach again, on the floor.
			var tipBone = bones.OrderByDescending( i => (rest[i].Position - ankle.Position).WithZ( 0 ).Length ).First();
			var reach = (rest[tipBone].Position - ankle.Position).WithZ( 0 );
			if ( tipBone != soles[side] && reach.Length > 0.01f )
			{
				var tip = (rest[tipBone].Position + reach * 0.5f).WithZ( floor );
				contacts.Add( (tipBone, rest[tipBone].PointToLocal( tip )) );
			}
			_sideContacts[side] = contacts.ToArray();
		}
		_sideRestZ = _sideContacts.Select( contacts => contacts.Min( c => RigWorldToEngine( skeleton.RestWorld[c.Bone] ).PointToWorld( c.Local ).z ) ).ToArray();
		_sideClearance = new float[_sideBones.Length];
		_clearanceScratch = new XForm[_rig.Skeleton.Count];
		_characterHeight = MathF.Max( restBounds.Size.z, 8f );
		_restFootZ = _sideRestZ.Length > 0 ? _sideRestZ.Min() : restBounds.Mins.z;
		_groundZ = _restFootZ;
		if ( _sceneModel.IsValid() )
		{
			// The mesh sole sits a little under the foot joints; use it when it is plausible.
			var sole = _sceneModel.Model.Bounds.Mins.z;
			if ( sole <= _restFootZ && _restFootZ - sole < _characterHeight * 0.2f )
				_groundZ = sole;
		}
	}

	/// <summary>How far the lower foot is above (+) or below (−) where it stands at rest, for
	/// one FK'd pose; each foot is measured by its lowest joint. Fills the per-foot values.</summary>
	float? MeasureClearance( XForm[] world, int count )
	{
		if ( _sideBones.Length == 0 )
			return null;
		var lowest = float.MaxValue;
		for ( var side = 0; side < _sideBones.Length; side++ )
		{
			var z = float.MaxValue;
			foreach ( var (bone, local) in _sideContacts[side] )
			{
				if ( bone < count )
					z = MathF.Min( z, RigWorldToEngine( world[bone] ).PointToWorld( local ).z );
			}
			if ( z == float.MaxValue )
				continue;
			_sideClearance[side] = z - _sideRestZ[side];
			lowest = MathF.Min( lowest, _sideClearance[side] );
		}
		return lowest == float.MaxValue ? null : lowest;
	}

	void MeasureClipClearance()
	{
		ClipClearance = null;
		if ( _clip?.SolvedFrames is not { Count: > 0 } frames || _footBones.Length == 0 )
			return;
		var skeleton = _rig.Skeleton;
		var lowest = float.MaxValue;
		foreach ( var frame in frames )
		{
			var count = Math.Min( frame.Length, skeleton.Count );
			for ( var i = 0; i < count; i++ )
			{
				var parent = skeleton[i].ParentIndex;
				_clearanceScratch[i] = parent < 0 ? frame[i] : XForm.Compose( _clearanceScratch[parent], frame[i] );
			}
			if ( MeasureClearance( _clearanceScratch, count ) is { } clearance )
				lowest = MathF.Min( lowest, clearance );
		}
		if ( lowest != float.MaxValue )
			ClipClearance = lowest;
	}

	/// <summary>Resets orbit, pitch, zoom and pan to the default 3/4 framing.</summary>
	public void ResetView()
	{
		_yaw = DefaultYaw;
		_pitch = DefaultPitch;
		_zoom = 1f;
		_pan = Vector3.Zero;
	}

	/// <summary>Named camera angles: yaw around the character and elevation, framing kept.</summary>
	public void SetView( float yaw, float pitch )
	{
		_yaw = yaw;
		_pitch = Math.Clamp( pitch, -10f, 80f );
		_pan = Vector3.Zero;
	}

	int[] _cameraBones;

	BBox ComputeRestBounds()
	{
		var skeleton = _rig.Skeleton;
		if ( skeleton.Count == 0 || _cameraBones.Length == 0 )
			return BBox.FromPositionAndSize( Vector3.Up * 32f, 64f );
		var first = RigWorldToEngine( skeleton.RestWorld[_cameraBones[0]] ).Position;
		var bounds = new BBox( first, first );
		foreach ( var index in _cameraBones )
			bounds = bounds.AddPoint( RigWorldToEngine( skeleton.RestWorld[index] ).Position );
		return bounds;
	}

	/// <summary>Switches the clip being previewed (restarts playback).</summary>
	public void SetClip( HumanoidRetargeter.Core.ClipResult clip )
	{
		_clip = clip;
		_time = 0;
		CurrentFrame = 0;
		_ghostAlignedClip = null; // ghost anchor depends on this clip's frame 0 - recompute
		MeasureClipClearance();
	}

	/// <summary>Jumps to a frame (scrubber); pauses playback.</summary>
	public void Scrub( int frame )
	{
		if ( FrameCount == 0 )
			return;
		Playing = false;
		CurrentFrame = Math.Clamp( frame, 0, FrameCount - 1 );
		_time = CurrentFrame;
	}

	void BuildBoneMap( Model model )
	{
		_rigToModelBone = new int[_rig.Skeleton.Count];
		_modelBindByRig = new Transform[_rig.Skeleton.Count];
		var missing = 0;
		var scaled = 0;
		for ( var i = 0; i < _rig.Skeleton.Count; i++ )
		{
			var bone = model.Bones.GetBone( _rig.Skeleton[i].Name );
			_rigToModelBone[i] = bone?.Index ?? -1;
			if ( _rigToModelBone[i] < 0 )
				missing++;
			// The bind transform carries the bone's SCALE (cartoon/Sketchfab rigs bake
			// non-uniform node scales into the bind). SetBoneOverride replaces the whole
			// transform - overriding with the default scale 1 collapsed/inflated the
			// skin around scaled bones (finger spike fans on a scaled-finger rig).
			_modelBindByRig[i] = bone?.LocalTransform ?? Transform.Zero;
			if ( bone is not null && !_modelBindByRig[i].Scale.AlmostEqual( Vector3.One, 0.001f ) )
				scaled++;
		}

		if ( missing > 0 )
			Log.Info( $"[humanoid-retargeter] preview: {missing} rig bones have no match on the preview model (kept at bind pose)." );
		if ( scaled > 0 )
			Log.Info( $"[humanoid-retargeter] preview: {scaled} model bones carry non-unit bind scale (preserved through pose overrides)." );
	}

	protected override void PreFrame()
	{
		Scene.EditorTick( RealTime.Now, RealTime.Delta );
		UpdateCamera();
		if ( ShowGround )
			DrawGround();

		if ( _clip?.SolvedFrames is not { Count: > 0 } frames )
			return;

		if ( Playing )
		{
			_time += RealTime.Delta * Math.Max( _clip.Fps, 1f );
			var duration = Math.Max( frames.Count - 1, 1 );
			while ( _time >= duration )
				_time -= duration; // preview always loops
			var frame = Math.Clamp( (int)MathF.Floor( _time ), 0, frames.Count - 1 );
			if ( frame != CurrentFrame )
			{
				CurrentFrame = frame;
				FrameChanged?.Invoke( frame );
			}
		}

		if ( _sceneModel.IsValid() )
			_sceneModel.Update( RealTime.Delta );
		if ( Playing && frames.Count > 1 )
			ApplyInterpolatedFrame( frames );
		else
			ApplyCurrentFrame();
	}

	/// <summary>Interpolates solved samples at the editor render rate. Output animation
	/// samples stay untouched; this only removes visible frame stepping in the preview.</summary>
	void ApplyInterpolatedFrame( IReadOnlyList<XForm[]> frames )
	{
		var lo = Math.Clamp( (int)MathF.Floor( _time ), 0, frames.Count - 1 );
		var hi = Math.Min( lo + 1, frames.Count - 1 );
		var blend = _time - lo;
		var count = Math.Min( _interpolatedScratch.Length,
			Math.Min( frames[lo].Length, frames[hi].Length ) );
		for ( var i = 0; i < count; i++ )
		{
			_interpolatedScratch[i] = new XForm(
				System.Numerics.Vector3.Lerp( frames[lo][i].Pos, frames[hi][i].Pos, blend ),
				System.Numerics.Quaternion.Slerp( frames[lo][i].Rot, frames[hi][i].Rot, blend ) );
		}
		ApplyPose( _interpolatedScratch );
	}

	/// <summary>Applies the current frame's solved pose (no-op without a clip), then syncs
	/// the wireframe-skeleton view and the source ghost overlay. Works with or without a
	/// preview model — the skeleton view is all a model-less target (custom FBX) shows.
	/// Public so the UI smoke gate can drive a frame headlessly.</summary>
	public void ApplyCurrentFrame()
	{
		if ( _clip?.SolvedFrames is { Count: > 0 } frames )
			ApplyPose( frames[Math.Clamp( CurrentFrame, 0, frames.Count - 1 )] );
		UpdateGhost();
	}

	/// <summary>
	/// Applies an arbitrary pose (local transforms in target-skeleton bone order) to the
	/// scene model and the wireframe-skeleton view. Public so the UI smoke gate can drive
	/// the rig's rest pose headlessly and assert engine-space bone positions.
	/// </summary>
	public void ApplyPose( XForm[] locals )
	{
		if ( locals is null )
			return;

		var skeleton = _rig.Skeleton;
		var count = Math.Min( locals.Length, skeleton.Count );
		for ( var i = 0; i < count; i++ )
		{
			var parent = skeleton[i].ParentIndex;
			_worldScratch[i] = parent < 0 ? locals[i] : XForm.Compose( _worldScratch[parent], locals[i] );
		}
		FootClearance = MeasureClearance( _worldScratch, count );

		if ( _sceneModel.IsValid() )
		{
			for ( var i = 0; i < count; i++ )
			{
				var modelBone = _rigToModelBone[i];
				if ( modelBone < 0 )
					continue;

				// Keep the bind's SCALE: the rig carries no scale, but scaled-bind rigs
				// (cartoon exports) need it or the skin collapses around those bones.
				var engineTransform = RigWorldToEngine( _worldScratch[i] );
				engineTransform.Scale = _modelBindByRig[i].Scale;
				_sceneModel.SetBoneOverride( modelBone, engineTransform );
			}

			// Flush the overrides into the model's bone state NOW: SetBoneOverride only takes
			// effect on the model's next Update, so without this the rendered pose lags one
			// frame and headless readers (the UI smoke gate's GetModelBoneTransform asserts)
			// would read the previous pose. Verified empirically: before the flush the gate read
			// the bind pose back; with it, the overridden pose.
			_sceneModel.Update( 0f );
		}

		UpdateSkeletonLines( count );
	}

	/// <summary>Redraws the wireframe-skeleton view from the freshly FK'd pose: one
	/// polyline object per skeleton chain, engine-space, and moves the model-less camera
	/// anchor with the posed bounds (fixed rest-pose zoom so it doesn't pulse).</summary>
	void UpdateSkeletonLines( int count )
	{
		if ( _boneModel.IsValid() )
			_boneModel.RenderingEnabled = SkeletonOnly;
		if ( !SkeletonOnly )
			return;

		SkeletonLineCount = 0;
		// Culling bounds cover EVERYTHING drawn; the camera center tracks only the
		// mapped character bones (stray far-away FBX nodes must not drag it off).
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		var cameraMin = new Vector3( float.MaxValue );
		var cameraMax = new Vector3( float.MinValue );
		foreach ( var index in _cameraBones )
		{
			if ( index >= count )
				continue;
			var cameraPos = RigWorldToEngine( _worldScratch[index] ).Position;
			cameraMin = Vector3.Min( cameraMin, cameraPos );
			cameraMax = Vector3.Max( cameraMax, cameraPos );
		}

		var engine = new Transform[count];
		for ( var i = 0; i < count; i++ )
		{
			engine[i] = RigWorldToEngine( _worldScratch[i] );
			min = Vector3.Min( min, engine[i].Position );
			max = Vector3.Max( max, engine[i].Position );
		}
		UpdateBoneMesh( engine, count );
		var boneWidth = 0f;

		if ( count > 0 )
		{
			if ( cameraMin.x <= cameraMax.x )
				_skeletonCenter = (cameraMin + cameraMax) * 0.5f;
			SkeletonDebug = $"drawn=[{min} .. {max}] cam=[{cameraMin} .. {cameraMax}] "
				+ $"center={_skeletonCenter} radius={_skeletonRadius:0.#} width={boneWidth:0.##} "
				+ $"count={count} bones={SkeletonLineCount}";
		}
	}

	// ---- Blender-style octahedral bones (the skeleton view) ---------------------------------
	// A bone runs from its parent joint to its own joint: a narrow double pyramid whose widest
	// square section sits at 10% of the length and is 10% of the length across, drawn solid
	// gray like Blender's default bones. One mesh holds every bone; it is rewritten in place
	// each frame.
	// Head joint, tail joint (-1: an end bone extended past Head, away from Before).
	(int From, int To, int Before)[] _boneSegments;
	Mesh _boneMesh;
	SceneModel _boneModel;
	List<Vertex> _boneVertices;

	static readonly Color BoneGray = new( 0.62f, 0.62f, 0.62f );
	static readonly int[] BoneTriangles = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 };

	/// <summary>The body's bones, like a deform rig in Blender: each mapped joint (hips, spine,
	/// limbs, fingers, head) joins its nearest mapped ancestor, so twist helpers and IK targets
	/// don't add stray wedges; end joints (head, toes, finger tips) get a short tail bone. Rigs
	/// with too few mapped joints draw every bone.</summary>
	(int, int, int)[] BuildBoneSegments()
	{
		var skeleton = _rig.Skeleton;
		var mapped = new HashSet<int>();
		foreach ( var role in Enum.GetValues<BoneRole>() )
		{
			if ( _rig.BoneForRole( role ) is { } index )
				mapped.Add( index );
		}
		if ( mapped.Count < 6 )
		{
			return Enumerable.Range( 0, skeleton.Count ).Where( i => skeleton[i].ParentIndex >= 0 )
				.Select( i => (skeleton[i].ParentIndex, i, -1) ).ToArray();
		}

		int MappedAncestor( int bone )
		{
			for ( var p = skeleton[bone].ParentIndex; p >= 0; p = skeleton[p].ParentIndex )
			{
				if ( mapped.Contains( p ) )
					return p;
			}
			return -1;
		}

		var segments = new List<(int, int, int)>();
		var hasChild = new HashSet<int>();
		foreach ( var bone in mapped )
		{
			var parent = MappedAncestor( bone );
			if ( parent < 0 )
				continue;
			segments.Add( (parent, bone, -1) );
			hasChild.Add( parent );
		}
		foreach ( var bone in mapped )
		{
			if ( !hasChild.Contains( bone ) && MappedAncestor( bone ) is var before and >= 0 )
				segments.Add( (bone, -1, before) );
		}
		return segments.ToArray();
	}

	void UpdateBoneMesh( Transform[] engine, int count )
	{
		var skeleton = _rig.Skeleton;
		_boneSegments ??= BuildBoneSegments();
		var vertexCount = _boneSegments.Length * BoneTriangles.Length;
		_boneVertices ??= new List<Vertex>( vertexCount );
		_boneVertices.Clear();

		// Real bones are never longer than most of the character; longer segments lead to
		// stray exporter nodes far from the body and are left out.
		var longest = _characterHeight * 0.75f;
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		var points = new Vector3[6];
		foreach ( var (from, to, before) in _boneSegments )
		{
			var drawn = from < count && (to < 0 ? before >= 0 && before < count : to < count);
			var head = drawn ? engine[from].Position : Vector3.Zero;
			var tail = !drawn ? Vector3.Zero
				: to >= 0 ? engine[to].Position
				: head + (head - engine[before].Position) * 0.5f;
			var axis = tail - head;
			var length = axis.Length;
			if ( !drawn || length < 0.01f || length > longest )
			{
				// Keep the vertex count fixed: a degenerate bone draws nothing.
				for ( var t = 0; t < BoneTriangles.Length; t++ )
					_boneVertices.Add( new Vertex( head, Vector3.Up, new Vector4( 1, 0, 0, 1 ), Vector2.Zero ) { Color = BoneGray } );
				continue;
			}
			axis /= length;
			var up = engine[from].Rotation.Up;
			if ( MathF.Abs( Vector3.Dot( up, axis ) ) > 0.95f )
				up = MathF.Abs( axis.z ) < 0.9f ? Vector3.Up : Vector3.Forward;
			var side = Vector3.Cross( axis, up ).Normal;
			up = Vector3.Cross( side, axis );
			var ring = head + axis * (length * 0.1f);
			var width = length * 0.1f;
			points[0] = head;
			points[1] = ring + (side + up) * width;
			points[2] = ring + (side - up) * width;
			points[3] = ring + (-side - up) * width;
			points[4] = ring + (-side + up) * width;
			points[5] = tail;
			var center = head + axis * (length * 0.3f);
			for ( var t = 0; t < BoneTriangles.Length; t += 3 )
			{
				var a = points[BoneTriangles[t]];
				var b = points[BoneTriangles[t + 1]];
				var c = points[BoneTriangles[t + 2]];
				var normal = Vector3.Cross( b - a, c - a ).Normal;
				// Wind every face outward so the solid material never culls it.
				if ( Vector3.Dot( normal, (a + b + c) / 3f - center ) < 0 )
				{
					(b, c) = (c, b);
					normal = -normal;
				}
				var shade = BoneGray * (0.7f + 0.3f * MathF.Abs( normal.z ));
				foreach ( var p in new[] { a, b, c } )
					_boneVertices.Add( new Vertex( p, normal, new Vector4( 1, 0, 0, 1 ), Vector2.Zero ) { Color = shade.WithAlpha( 1f ) } );
			}
			min = Vector3.Min( min, Vector3.Min( head, tail ) - width );
			max = Vector3.Max( max, Vector3.Max( head, tail ) + width );
			SkeletonLineCount++;
		}

		if ( _boneMesh is null )
		{
			_boneMesh = new Mesh( Material.Load( "materials/gizmo/solid.vmat" ) );
			_boneMesh.CreateVertexBuffer( _boneVertices.Count, _boneVertices );
			_boneMesh.CreateIndexBuffer( _boneVertices.Count, Enumerable.Range( 0, _boneVertices.Count ).ToArray() );
		}
		else
			_boneMesh.SetVertexBufferData( _boneVertices );
		if ( min.x <= max.x )
			_boneMesh.Bounds = new BBox( min, max );
		if ( !_boneModel.IsValid() )
			_boneModel = new SceneModel( Scene.SceneWorld, Model.Builder.AddMesh( _boneMesh ).Create(), Transform.Zero );
		_boneModel.RenderingEnabled = SkeletonOnly;
	}

	/// <summary>
	/// Rig-space world transform → engine model space: optional Y-up→Z-up basis rotation
	/// (position (x, y, z) → (x, −z, y); rotation q → q_R ⊗ q), then cm→inch position
	/// scaling. Identity + scale for engine-space rigs.
	/// </summary>
	Transform RigWorldToEngine( in XForm w )
	{
		var pos = w.Pos;
		var rot = w.Rot;
		if ( _convertYUpToZUp )
		{
			pos = new System.Numerics.Vector3( pos.X, -pos.Z, pos.Y );
			rot = System.Numerics.Quaternion.Normalize( YUpToZUp * rot );
		}

		return new Transform(
			new Vector3( pos.X, pos.Y, pos.Z ) * _positionScale,
			new Rotation( rot.X, rot.Y, rot.Z, rot.W ) );
	}

	/// <summary>
	/// Renders the preview camera to an offscreen bitmap and counts the pixels matching
	/// <paramref name="predicate"/>. Used by the UI smoke gate to prove the overlays
	/// actually REACH THE SCREEN — line counts alone cannot see a missing line material
	/// (user report: skeleton view and source ghost rendered as a single purple blob).
	/// </summary>
	public int CountRenderedPixels( Func<Color, bool> predicate, int size = 384 )
	{
		if ( !Camera.IsValid() )
			return 0;

		// Fixed small tick: headless probe calls arrive with arbitrarily large real-time
		// deltas, which destabilizes procedural bones (citizen jiggle exploded into
		// stretched geometry in a gate render).
		Scene.EditorTick( RealTime.Now, 0.016f );
		UpdateCamera();

		var bitmap = new Bitmap( size, size );
		Camera.RenderToBitmap( bitmap );

		var count = 0;
		foreach ( var pixel in bitmap.GetPixels() )
		{
			if ( predicate( pixel ) )
				count++;
		}
		return count;
	}

	/// <summary>Renders the preview camera to a PNG (gate diagnostics — lets the harness
	/// LOOK at what the user sees instead of inferring from pixel counts). Null when the
	/// camera is unavailable.</summary>
	public byte[] RenderToPng( int size = 512 )
	{
		if ( !Camera.IsValid() )
			return null;

		Scene.EditorTick( RealTime.Now, 0.016f ); // fixed tick - see CountRenderedPixels
		UpdateCamera();

		var bitmap = new Bitmap( size, size );
		Camera.RenderToBitmap( bitmap );
		return bitmap.ToPng();
	}

	/// <summary>Renders a COMPILED model playing one of its own sequences (the ModelDoc
	/// ground truth) through this widget's camera — gate diagnostics for when the
	/// pose-override preview and the compiled result disagree. The widget's own model is
	/// hidden for the shot. Null when the model/camera is unavailable.</summary>
	public byte[] RenderCompiledSequencePng(
		string modelPath, string sequenceName, float fraction, int size = 512 )
	{
		if ( !Camera.IsValid() || !_sceneModel.IsValid() )
		{
			Log.Info( $"[humanoid-retargeter] compiled render unavailable: camera={Camera.IsValid()} model={_sceneModel.IsValid()}" );
			return null;
		}
		var compiled = Model.Load( modelPath );
		if ( compiled is null || compiled.IsError )
		{
			Log.Info( $"[humanoid-retargeter] compiled render unavailable: load '{modelPath}' null={compiled is null} error={compiled?.IsError}" );
			return null;
		}

		var sequenceModel = new SceneModel( _sceneModel.World, compiled, Transform.Zero );
		try
		{
			sequenceModel.UseAnimGraph = false;
			sequenceModel.CurrentSequence.Name = sequenceName;
			sequenceModel.CurrentSequence.Time =
				sequenceModel.CurrentSequence.Duration * Math.Clamp( fraction, 0f, 1f );
			sequenceModel.Update( 0.016f );
			_sceneModel.RenderingEnabled = false;

			// RenderToPng ticks the scene with WALL-CLOCK time - re-pin the sequence
			// time right before the draw so the render can't sample a different frame
			// than the probes (diagnosed: bone queries read the correct pose while the
			// rendered skin showed a ~90° different one).
			Scene.EditorTick( RealTime.Now, 0.016f );
			sequenceModel.CurrentSequence.Time =
				sequenceModel.CurrentSequence.Duration * Math.Clamp( fraction, 0f, 1f );
			sequenceModel.Update( 0.001f );
			var png = RenderToPng( size );

			// Full bone-state dump of the RENDER-world model (gate forensics: this world
			// evaluates sequences differently from a bare SceneWorld; the dump names
			// exactly which bones diverge). Written to the given folder when armed.
			var dumpDir = Environment.GetEnvironmentVariable( "HR_RENDER_MODEL_DUMP_DIR" );
			if ( !string.IsNullOrEmpty( dumpDir ) && sequenceModel.Model?.Bones?.AllBones is { } dumpBones )
			{
				try
				{
					var lines = dumpBones.Select( b =>
					{
						var t = sequenceModel.GetBoneWorldTransform( b.Name );
						return $"{{\"name\":\"{b.Name}\",\"rot\":[{t.Rotation.x},{t.Rotation.y},{t.Rotation.z},{t.Rotation.w}],"
							+ $"\"pos\":[{t.Position.x},{t.Position.y},{t.Position.z}]}}";
					} );
					System.IO.File.WriteAllText(
						System.IO.Path.Combine( dumpDir, $"render_model_bones_{(int)(fraction * 100)}.json" ),
						"[" + string.Join( ",\n", lines ) + "]" );
				}
				catch ( Exception e )
				{
					Log.Info( $"[humanoid-retargeter] render-model dump failed: {e.Message}" );
				}
			}
			return png;
		}
		finally
		{
			_sceneModel.RenderingEnabled = true;
			sequenceModel.Delete();
		}
	}

	/// <summary>Renders a close-up of one bone (gate diagnostics for hand/finger quality
	/// reports — full-body renders are too small to judge a wrist). The camera frames a
	/// sphere of <paramref name="radius"/> around the CURRENTLY POSED bone. Null when the
	/// model or bone is unavailable.</summary>
	public byte[] RenderBoneCloseUpPng( string boneName, float radius, int size = 512 )
	{
		if ( !Camera.IsValid() )
			return null;
		var bone = GetModelBoneTransform( boneName );
		if ( bone is null )
			return null;

		Scene.EditorTick( RealTime.Now, 0.016f );

		var center = bone.Value.Position;
		var distance = MathX.SphereCameraDistance( radius, Camera.FieldOfView ) * 1.05f;
		var yawRad = MathX.DegreeToRadian( _yaw );
		var dir = new Vector3( MathF.Cos( yawRad ), MathF.Sin( yawRad ), 0.25f ).Normal;
		Camera.WorldPosition = center + dir * distance;
		Camera.WorldRotation = Rotation.LookAt( -dir, Vector3.Up );

		var bitmap = new Bitmap( size, size );
		Camera.RenderToBitmap( bitmap );
		return bitmap.ToPng();
	}

	/// <summary>
	/// Per-bone disagreement between the RIG's rest locals and the compiled model's bind
	/// locals (worst offenders first). The DMX skeleton must match the engine's own import
	/// of the same file — a mismatch (PreRotation/pivot convention differences) plays as a
	/// CONSTANT offset on every frame: twisted wrists, fingers off their sockets.
	/// Non-root local rotations are convention-independent and local positions differ only
	/// by the uniform unit scale, so they compare directly.
	/// </summary>
	public string BindMismatchReport( int top = 12 )
	{
		if ( !_sceneModel.IsValid() )
			return "(no model)";
		var bones = _sceneModel.Model?.Bones;
		if ( bones is null )
			return "(no bones)";

		var skeleton = _rig.Skeleton;
		var entries = new List<(float Score, string Line)>();
		for ( var i = 0; i < skeleton.Count; i++ )
		{
			if ( skeleton[i].ParentIndex < 0 )
				continue; // the root's local carries the axis conversion - not comparable
			var bone = bones.GetBone( skeleton[i].Name );
			if ( bone?.Parent is null )
				continue;

			// Model bind local from the MODEL-space bind transforms.
			var world = bone.LocalTransform;
			var parent = bone.Parent.LocalTransform;
			var modelLocal = new XForm(
				new System.Numerics.Vector3( world.Position.x, world.Position.y, world.Position.z ),
				new System.Numerics.Quaternion( world.Rotation.x, world.Rotation.y, world.Rotation.z, world.Rotation.w ) );
			var parentLocal = new XForm(
				new System.Numerics.Vector3( parent.Position.x, parent.Position.y, parent.Position.z ),
				new System.Numerics.Quaternion( parent.Rotation.x, parent.Rotation.y, parent.Rotation.z, parent.Rotation.w ) );
			var bind = XForm.ToLocal( parentLocal, modelLocal );

			var rig = skeleton[i].RestLocal;
			var posDelta = (rig.Pos * _positionScale - bind.Pos).Length();
			var dot = MathF.Min( MathF.Abs( System.Numerics.Quaternion.Dot(
				System.Numerics.Quaternion.Normalize( rig.Rot ),
				System.Numerics.Quaternion.Normalize( bind.Rot ) ) ), 1f );
			var angle = 2f * MathF.Acos( dot ) * 180f / MathF.PI;
			var score = angle + posDelta * 10f;
			if ( angle > 2f || posDelta > 0.25f )
				entries.Add( (score, $"{skeleton[i].Name}: rot {angle:0.#}deg pos {posDelta:0.##}in") );
		}

		if ( entries.Count == 0 )
			return "(rig matches the compiled bind)";
		return $"{entries.Count} bones differ; worst: "
			+ string.Join( "; ", entries.OrderByDescending( e => e.Score ).Take( top ).Select( e => e.Line ) );
	}

	/// <summary>Names and positions of model bones sitting farther than
	/// <paramref name="radius"/> from the given anchor bone — gate diagnostics for
	/// stretched-mesh reports (skinning webs point at bones left at wild transforms).</summary>
	public string WildBoneReport( string anchorBoneName, float radius )
	{
		if ( !_sceneModel.IsValid() )
			return "(no model)";
		var anchor = GetModelBoneTransform( anchorBoneName )?.Position ?? Vector3.Zero;
		var wild = new List<string>();
		var bones = _sceneModel.Model?.Bones?.AllBones;
		if ( bones is null )
			return "(no bones)";
		foreach ( var bone in bones )
		{
			var position = _sceneModel.GetBoneWorldTransform( bone.Index ).Position;
			if ( position.Distance( anchor ) > radius )
				wild.Add( $"{bone.Name}@{position}" );
		}
		return wild.Count == 0 ? "(none)" : string.Join( "; ", wild );
	}

	/// <summary>World transform of a model bone (by name) as currently posed; null when the
	/// model is missing or has no such bone. Used by the UI smoke gate's pose assertions.</summary>
	public Transform? GetModelBoneTransform( string boneName )
	{
		if ( !_sceneModel.IsValid() )
			return null;
		var bone = _sceneModel.Model?.Bones?.GetBone( boneName );
		if ( bone is null )
			return null;
		return _sceneModel.GetBoneWorldTransform( bone.Index );
	}

	// ============================================================================ source ghost

	/// <summary>True when source-ghost data was installed (drives the dialog's toggle).</summary>
	public bool HasSourceGhost => _ghostClip is not null;

	/// <summary>Show the source clip as a semi-transparent stick-skeleton overlay (off by
	/// default; the preview dialog's "Show source" toggle drives this).</summary>
	public bool ShowSourceGhost
	{
		get => _showSourceGhost;
		set
		{
			_showSourceGhost = value;
			foreach ( var chain in _ghostChainObjects )
				chain.RenderingEnabled = value && HasSourceGhost;
		}
	}

	/// <summary>Line segments the ghost drew on its last update (bones + joint ticks).
	/// Exposed for the UI smoke gate's headless assertion.</summary>
	public int GhostLineCount { get; private set; }

	/// <summary>
	/// Installs the SOURCE clip for the ghost overlay: <paramref name="skeleton"/> /
	/// <paramref name="clip"/> are the imported source scene's (cm, native axes);
	/// <paramref name="mapping"/> locates hips/legs/shoulders for the alignment. The ghost is
	/// root-aligned to the target (the source's hips ground-projection is moved onto the
	/// target's hips ground-projection at frame 0) and scaled by the hip-height ratio so the
	/// two rigs compare at the same size. No-op (ghost unavailable) when the mapping lacks
	/// the bones the character frame needs.
	/// </summary>
	public void SetSourceGhost( SourceSkeleton skeleton, SourceClip clip, MappingResult mapping )
	{
		if ( skeleton is null || clip is null || clip.Frames.Count == 0 || mapping is null )
			return;

		int? SrcBone( BoneRole role )
			=> mapping.RoleToBone.TryGetValue( role, out var index ) ? index : null;

		// The source basis comes from the clip's FRAME 0 pose, not the rest skeleton:
		// stick-bind rigs (NVIDIA SOMA - every rest bone along one axis) have no usable
		// rest geometry, and a near-degenerate basis normalizes into garbage axes that
		// smear the ghost into long streaks (user report). Frame 0 of a real clip is a
		// real pose on every rig.
		var scratch = new XForm[skeleton.Count];
		var hipsIndex = SrcBone( BoneRole.Hips ) ?? 0;
		var hips0 = FkPosition( skeleton, clip.Frames[0], scratch, hipsIndex );
		if ( !TryCharacterBasis( SrcBone, scratch, out _srcLat, out _srcUp, out _srcFwd,
			out _srcHipHeight, out var srcGround ) )
		{
			Log.Info( "[humanoid-retargeter] preview: source ghost unavailable (mapping lacks the hips/legs/shoulder bones the alignment needs)." );
			return;
		}

		_ghostSkeleton = skeleton;
		_ghostClip = clip;
		_ghostScratch = scratch;
		_ghostAlignedClip = null;

		// Anchor = the source hips' ground projection at frame 0 (clips that start offset
		// from the origin - BVH mocap especially - must not push the ghost away).
		_srcAnchor = hips0 + _srcUp * (srcGround - VecN.Dot( hips0, _srcUp ));

		_ghostChains = BuildChains( i => skeleton[i].ParentIndex, skeleton.Count );
		EnsureChainObjects( _ghostChainObjects, _ghostChains.Length );
		foreach ( var chain in _ghostChainObjects )
			chain.RenderingEnabled = _showSourceGhost;
	}

	/// <summary>Target-side alignment: anchor on the TARGET's hips ground-projection at
	/// frame 0 of the previewed clip, hip-height-ratio scale. Lazy - both clips must be
	/// known; recomputed when the previewed clip changes.</summary>
	bool EnsureGhostAlignment()
	{
		if ( _clip?.SolvedFrames is not { Count: > 0 } frames )
			return false;
		if ( ReferenceEquals( _ghostAlignedClip, _clip ) )
			return true;

		int? TgtBone( BoneRole role ) => _rig.BoneForRole( role );

		if ( !TryCharacterBasis( TgtBone, _rig.Skeleton.RestWorld, out _tgtLat, out _tgtUp, out _tgtFwd,
			out var tgtHipHeight, out var tgtGround ) )
			return false;

		var hipsIndex = _rig.BoneForRole( BoneRole.Hips ) ?? 0;
		var hips0 = FkPosition( _rig.Skeleton, frames[0], _worldScratch, hipsIndex );
		_tgtAnchor = hips0 + _tgtUp * (tgtGround - VecN.Dot( hips0, _tgtUp ));
		_ghostScale = _srcHipHeight > 1e-3f ? tgtHipHeight / _srcHipHeight : 1f;
		_ghostAlignedClip = _clip;
		return true;
	}

	/// <summary>Redraws the ghost at the scrub position: the source frame is picked by
	/// NORMALIZED time (source and target clips may differ in frame count), FK'd, mapped
	/// through the character-basis alignment into target rig space and drawn as parent→child
	/// line segments plus small joint ticks.</summary>
	void UpdateGhost()
	{
		if ( _ghostChains is null || _ghostClip is null )
			return;

		var visible = _showSourceGhost && EnsureGhostAlignment();
		foreach ( var chain in _ghostChainObjects )
			chain.RenderingEnabled = visible;
		if ( !visible )
			return;

		// Normalized-time sync (fence-post frames: first→first, last→last).
		var ghostFrames = _ghostClip.Frames;
		var t = FrameCount > 1 ? CurrentFrame / (float)(FrameCount - 1) : 0f;
		var gi = Math.Clamp( (int)MathF.Round( t * (ghostFrames.Count - 1) ), 0, ghostFrames.Count - 1 );

		var skeleton = _ghostSkeleton;
		var locals = ghostFrames[gi];
		var count = Math.Min( locals.Length, skeleton.Count );
		for ( var i = 0; i < count; i++ )
		{
			var parent = skeleton[i].ParentIndex;
			_ghostScratch[i] = parent < 0 ? locals[i] : XForm.Compose( _ghostScratch[parent], locals[i] );
		}

		GhostLineCount = 0;
		var boneWidth = OverlayLineWidth * 0.8f;
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		var engine = new Vector3[count];
		for ( var i = 0; i < count; i++ )
		{
			engine[i] = GhostToEngine( _ghostScratch[i].Pos );
			min = Vector3.Min( min, engine[i] );
			max = Vector3.Max( max, engine[i] );
		}

		var points = new List<Vector3>();
		for ( var c = 0; c < _ghostChains.Length; c++ )
		{
			points.Clear();
			foreach ( var index in _ghostChains[c] )
			{
				if ( index < count )
					points.Add( engine[index] );
			}
			DrawChain( _ghostChainObjects[c], points, GhostBoneColor, boneWidth );
			FitBounds( _ghostChainObjects[c], min, max );
			GhostLineCount += Math.Max( points.Count - 1, 0 );
		}
	}

	/// <summary>Source world position (cm, native axes) → engine space: express the offset
	/// from the source anchor in the source character basis, re-emit it in the target's
	/// character basis at the target anchor (hip-ratio scaled), then run the widget's normal
	/// rig→engine conversion.</summary>
	Vector3 GhostToEngine( VecN p )
	{
		var d = p - _srcAnchor;
		var a = new VecN( VecN.Dot( d, _srcLat ), VecN.Dot( d, _srcUp ), VecN.Dot( d, _srcFwd ) ) * _ghostScale;
		var rigPos = _tgtAnchor + _tgtLat * a.X + _tgtUp * a.Y + _tgtFwd * a.Z;
		return RigWorldToEngine( new XForm( rigPos, System.Numerics.Quaternion.Identity ) ).Position;
	}

	/// <summary>FK of one frame down to every bone, returning <paramref name="boneIndex"/>'s
	/// world position (scratch is filled as a side effect).</summary>
	static VecN FkPosition( SourceSkeleton skeleton, XForm[] locals, XForm[] scratch, int boneIndex )
	{
		var count = Math.Min( locals.Length, skeleton.Count );
		for ( var i = 0; i < count; i++ )
		{
			var parent = skeleton[i].ParentIndex;
			scratch[i] = parent < 0 ? locals[i] : XForm.Compose( scratch[parent], locals[i] );
		}
		return scratch[Math.Clamp( boneIndex, 0, count - 1 )].Pos;
	}

	/// <summary>
	/// Character-level basis of a rig from rest GEOMETRY (the same definition the solver's
	/// CharacterFrame uses, duplicated here because that type is internal to the core
	/// assembly): up = mid-hips→mid-shoulders, lateral = left-positive hip line ⊥ up,
	/// forward = cross(lateral, up); hip height/ground measured along up over the mapped
	/// feet (all bones when no feet are mapped). False when the rig lacks the needed bones.
	/// </summary>
	static bool TryCharacterBasis(
		Func<BoneRole, int?> boneOf, IReadOnlyList<XForm> restWorld,
		out VecN lateral, out VecN up, out VecN forward, out float hipHeight, out float ground )
	{
		lateral = up = forward = default;
		hipHeight = ground = 0f;

		VecN? Pos( BoneRole role ) => boneOf( role ) is { } index && index >= 0 && index < restWorld.Count
			? restWorld[index].Pos : null;
		VecN? Mid( VecN? a, VecN? b ) => a is not null && b is not null ? (a.Value + b.Value) * 0.5f : null;

		var legL = Pos( BoneRole.UpperLegL );
		var legR = Pos( BoneRole.UpperLegR );
		if ( legL is null || legR is null )
			return false;
		var midHips = (legL.Value + legR.Value) * 0.5f;

		var midShoulders = Mid( Pos( BoneRole.UpperArmL ), Pos( BoneRole.UpperArmR ) )
			?? Mid( Pos( BoneRole.ClavicleL ), Pos( BoneRole.ClavicleR ) )
			?? Pos( BoneRole.Neck );
		if ( midShoulders is null )
			return false;

		var upRaw = midShoulders.Value - midHips;
		if ( upRaw.LengthSquared() < 1e-8f )
			return false;
		up = VecN.Normalize( upRaw );

		var acrossHips = legL.Value - legR.Value;
		var latRaw = acrossHips - up * VecN.Dot( acrossHips, up );
		if ( latRaw.LengthSquared() < 1e-8f )
			return false;
		lateral = VecN.Normalize( latRaw );
		forward = VecN.Normalize( VecN.Cross( lateral, up ) );

		ground = float.PositiveInfinity;
		foreach ( var role in new[] { BoneRole.FootL, BoneRole.FootR, BoneRole.ToeL, BoneRole.ToeR } )
		{
			if ( Pos( role ) is { } foot )
				ground = MathF.Min( ground, VecN.Dot( foot, up ) );
		}
		if ( float.IsPositiveInfinity( ground ) )
		{
			foreach ( var world in restWorld )
				ground = MathF.Min( ground, VecN.Dot( world.Pos, up ) );
		}

		hipHeight = VecN.Dot( midHips, up ) - ground;
		return hipHeight > 1e-3f;
	}

	void UpdateCamera()
	{
		if ( !Camera.IsValid() )
			return;

		// Frame the POSED character, not the model asset: Model.Bounds is the bind-pose box
		// anchored at the scene origin, so clips that start offset or travel (BVH mocap
		// especially) would walk out of a frame built from it. SceneObject.Bounds follows
		// the current pose; the bind-pose SIZE is kept for the zoom so it doesn't pulse
		// with the animation (arms out ≠ zoom out). In the wireframe-skeleton view (and
		// for model-less targets) the same policy runs off the FK'd skeleton instead.
		var useModelBounds = _sceneModel.IsValid() && !SkeletonOnly;
		var center = useModelBounds ? _sceneModel.Bounds.Center : _skeletonCenter;
		if ( SmoothCamera )
		{
			// Follow the character's travel, not its bob and sway: ease towards where it is
			// (about half a second behind) at a fixed height.
			var goal = new Vector3( center.x, center.y, _restCenterZ );
			_followCenter = _followCenter is { } current
				? Vector3.Lerp( current, goal, 1f - MathF.Exp( -RealTime.Delta / 0.35f ) )
				: goal;
			center = _followCenter.Value;
		}
		var radius = useModelBounds
			? MathF.Max( _sceneModel.Model.Bounds.Size.Length * 0.5f, 8f )
			: _skeletonRadius;
		var distance = MathX.SphereCameraDistance( radius, Camera.FieldOfView ) * 1.05f * _zoom;

		var yawRad = MathX.DegreeToRadian( _yaw );
		var pitchRad = MathX.DegreeToRadian( _pitch );
		var dir = new Vector3( MathF.Cos( yawRad ) * MathF.Cos( pitchRad ), MathF.Sin( yawRad ) * MathF.Cos( pitchRad ),
			MathF.Sin( pitchRad ) ).Normal;
		_viewRadius = radius;
		Camera.WorldPosition = center + _pan + dir * distance;
		Camera.WorldRotation = Rotation.LookAt( -dir, Vector3.Up );
	}

	float _viewRadius = 40f;
	Vector3? _followCenter;
	float _restCenterZ;

	/// <summary>Eases the camera after the character instead of re-centering on every frame's
	/// pose (which shakes with every step). Off by default: headless renders frame each pose exactly.</summary>
	public bool SmoothCamera { get; set; }

	// Left drag orbits (horizontal) and tilts (vertical); right or middle drag pans; the wheel zooms.
	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var delta = e.LocalPosition - _lastMouse;
		_lastMouse = e.LocalPosition;
		if ( (e.ButtonState & MouseButtons.Left) != 0 )
		{
			_yaw -= delta.x * 0.4f;
			_pitch = Math.Clamp( _pitch + delta.y * 0.3f, -10f, 80f );
		}
		else if ( (e.ButtonState & (MouseButtons.Right | MouseButtons.Middle)) != 0 && Camera.IsValid() )
		{
			var scale = _viewRadius * _zoom * 0.004f;
			_pan += (Camera.WorldRotation.Left * delta.x + Camera.WorldRotation.Up * delta.y) * scale;
		}
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );
		_zoom = Math.Clamp( _zoom * (e.Delta > 0 ? 0.9f : 1.1f), 0.25f, 4f );
		e.Accept();
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		base.OnDoubleClick( e );
		ResetView();
	}

	/// <summary>
	/// The floor at the character's ground: a grid that fades towards its edges, centered under
	/// the character, with a line from each foot down to the floor. Lines turn amber while the
	/// feet are clear of the floor and red when they sink through it.
	/// </summary>
	void DrawGround()
	{
		var step = MathF.Max( _characterHeight / 6f, 2f );
		const int cells = 10;
		var extent = cells * step;
		var center = _sceneModel.IsValid() && !SkeletonOnly ? _sceneModel.Bounds.Center : _skeletonCenter;
		// The floor stays put under a walking character; it only re-centers in large blocks
		// once the character has travelled far across it.
		if ( _followCenter is { } follow )
			center = follow;
		var block = step * 5f;
		var originX = MathF.Round( center.x / block ) * block;
		var originY = MathF.Round( center.y / block ) * block;

		using var scope = Gizmo.Scope( "humanoid-retargeter-ground" );
		Gizmo.Transform = Transform.Zero;
		Gizmo.Draw.IgnoreDepth = false;
		Gizmo.Draw.LineThickness = 1f;
		for ( var i = -cells; i <= cells; i++ )
		{
			var fade = 1f - MathF.Abs( i ) / (cells + 1f);
			Gizmo.Draw.Color = Color.White.WithAlpha( (i == 0 ? .16f : .07f) * fade + .015f );
			var o = i * step;
			Gizmo.Draw.Line( new Vector3( originX + o, originY - extent, _groundZ ), new Vector3( originX + o, originY + extent, _groundZ ) );
			Gizmo.Draw.Line( new Vector3( originX - extent, originY + o, _groundZ ), new Vector3( originX + extent, originY + o, _groundZ ) );
		}

		if ( FootClearance is null )
			return;
		// A ring on the floor under each foot: green while that foot is down, amber while it is
		// off the floor, red when it sinks through it. Walking shows one of each; floating, two amber.
		var tolerance = FrameContactTolerance;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2f;
		for ( var side = 0; side < _soleBones.Length; side++ )
		{
			var index = _soleBones[side];
			if ( index >= _worldScratch.Length )
				continue;
			var clearance = _sideClearance[side];
			var color = clearance > tolerance ? Theme.Yellow : clearance < -tolerance ? Theme.Red : Theme.Green;
			var foot = RigWorldToEngine( _worldScratch[index] ).Position;
			Gizmo.Draw.Color = color.WithAlpha( .85f );
			Gizmo.Draw.LineCircle( new Vector3( foot.x, foot.y, _groundZ ), Vector3.Up, step * 0.18f, 0, 360, 20 );
		}
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		Scene?.Destroy();
		Scene = null;
	}
}
