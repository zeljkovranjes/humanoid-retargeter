using System;
using System.Collections.Generic;
using Editor;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using Sandbox;
using SourceClip = HumanoidRetargeter.Skeleton.Clip;
using SourceSkeleton = HumanoidRetargeter.Skeleton.Skeleton;
using VecN = System.Numerics.Vector3;

namespace HumanoidRetargeter.Editor;

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
	XForm[] _worldScratch;

	HumanoidRetargeter.ClipResult _clip;
	float _time;
	float _yaw = 35f;
	Vector2 _lastMouse;

	// ---- target skeleton wireframe (the RETARGETED pose as stick skeleton) --------------
	SceneLineObject _skeletonLines;
	bool _skeletonOnly;
	Vector3 _skeletonCenter = Vector3.Up * 32f;
	float _skeletonRadius = 40f;

	// ---- source ghost (stick-skeleton overlay of the SOURCE clip) -----------------------
	SceneLineObject _ghost;
	SourceSkeleton _ghostSkeleton;
	SourceClip _ghostClip;
	XForm[] _ghostScratch;
	bool _showSourceGhost;

	// source-side alignment (fixed once per SetSourceGhost)
	VecN _srcAnchor, _srcLat, _srcUp, _srcFwd;
	float _srcHipHeight;

	// target-side alignment (lazy, recomputed when the previewed clip changes)
	HumanoidRetargeter.ClipResult _ghostAlignedClip;
	VecN _tgtAnchor, _tgtLat, _tgtUp, _tgtFwd;
	float _ghostScale = 1f;

	static readonly Color GhostBoneColor = new( 1f, 0.75f, 0.25f, 0.5f );   // amber, ~50%
	static readonly Color GhostJointColor = new( 1f, 0.8f, 0.35f, 0.65f );

	static readonly Color SkeletonBoneColor = new( 0.35f, 0.9f, 1f, 0.95f ); // cyan, solid
	static readonly Color SkeletonJointColor = new( 0.6f, 1f, 1f, 1f );

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
			if ( _skeletonLines is not null )
				_skeletonLines.RenderingEnabled = SkeletonOnly;
		}
	}

	/// <summary>Line segments the skeleton view drew on its last update (bones + joint
	/// ticks). Exposed for the UI smoke gate's headless assertion.</summary>
	public int SkeletonLineCount { get; private set; }

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

		// Rest-pose bounds in engine space: camera zoom for the wireframe-skeleton view
		// (kept fixed so the zoom doesn't pulse with the animation; the center follows).
		var restBounds = ComputeRestBounds();
		_skeletonCenter = restBounds.Center;
		_skeletonRadius = MathF.Max( restBounds.Size.Length * 0.5f, 8f );
	}

	BBox ComputeRestBounds()
	{
		var skeleton = _rig.Skeleton;
		if ( skeleton.Count == 0 )
			return BBox.FromPositionAndSize( Vector3.Up * 32f, 64f );
		var first = RigWorldToEngine( skeleton.RestWorld[0] ).Position;
		var bounds = new BBox( first, first );
		for ( var i = 1; i < skeleton.Count; i++ )
			bounds = bounds.AddPoint( RigWorldToEngine( skeleton.RestWorld[i] ).Position );
		return bounds;
	}

	/// <summary>Switches the clip being previewed (restarts playback).</summary>
	public void SetClip( HumanoidRetargeter.ClipResult clip )
	{
		_clip = clip;
		_time = 0;
		CurrentFrame = 0;
		_ghostAlignedClip = null; // ghost anchor depends on this clip's frame 0 - recompute
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
		var missing = 0;
		for ( var i = 0; i < _rig.Skeleton.Count; i++ )
		{
			var bone = model.Bones.GetBone( _rig.Skeleton[i].Name );
			_rigToModelBone[i] = bone?.Index ?? -1;
			if ( _rigToModelBone[i] < 0 )
				missing++;
		}

		if ( missing > 0 )
			Log.Info( $"[humanoid-retargeter] preview: {missing} rig bones have no match on the preview model (kept at bind pose)." );
	}

	protected override void PreFrame()
	{
		Scene.EditorTick( RealTime.Now, RealTime.Delta );
		UpdateCamera();

		if ( _clip?.SolvedFrames is not { Count: > 0 } frames )
			return;

		if ( Playing )
		{
			_time += RealTime.Delta * Math.Max( _clip.Fps, 1f );
			if ( _time >= frames.Count )
				_time -= frames.Count; // preview always loops
			var frame = Math.Clamp( (int)_time, 0, frames.Count - 1 );
			if ( frame != CurrentFrame )
			{
				CurrentFrame = frame;
				FrameChanged?.Invoke( frame );
			}
		}

		if ( _sceneModel.IsValid() )
			_sceneModel.Update( RealTime.Delta );
		ApplyCurrentFrame();
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

		if ( _sceneModel.IsValid() )
		{
			for ( var i = 0; i < count; i++ )
			{
				var modelBone = _rigToModelBone[i];
				if ( modelBone < 0 )
					continue;

				_sceneModel.SetBoneOverride( modelBone, RigWorldToEngine( _worldScratch[i] ) );
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

	/// <summary>Redraws the wireframe-skeleton view from the freshly FK'd pose: parent→child
	/// bone segments plus joint ticks, engine-space, and moves the model-less camera anchor
	/// with the posed bounds (fixed rest-pose zoom so it doesn't pulse).</summary>
	void UpdateSkeletonLines( int count )
	{
		if ( _skeletonLines is null )
		{
			_skeletonLines = new SceneLineObject( Scene.SceneWorld );
			_skeletonLines.Opaque = false;
			_skeletonLines.Lighting = false;
		}
		_skeletonLines.RenderingEnabled = SkeletonOnly;
		if ( !SkeletonOnly )
			return;

		var skeleton = _rig.Skeleton;
		_skeletonLines.Clear();
		SkeletonLineCount = 0;
		var min = new Vector3( float.MaxValue );
		var max = new Vector3( float.MinValue );
		for ( var i = 0; i < count; i++ )
		{
			var pos = RigWorldToEngine( _worldScratch[i] ).Position;
			min = Vector3.Min( min, pos );
			max = Vector3.Max( max, pos );

			var parent = skeleton[i].ParentIndex;
			if ( parent >= 0 && parent < count )
			{
				var parentPos = RigWorldToEngine( _worldScratch[parent] ).Position;
				_skeletonLines.StartLine();
				_skeletonLines.AddLinePoint( parentPos, SkeletonBoneColor, 0.6f );
				_skeletonLines.AddLinePoint( pos, SkeletonBoneColor, 0.6f );
				_skeletonLines.EndLine();
				SkeletonLineCount++;
			}

			_skeletonLines.StartLine();
			_skeletonLines.AddLinePoint( pos - Vector3.Up * 0.4f, SkeletonJointColor, 1.1f );
			_skeletonLines.AddLinePoint( pos + Vector3.Up * 0.4f, SkeletonJointColor, 1.1f );
			_skeletonLines.EndLine();
			SkeletonLineCount++;
		}

		if ( count > 0 )
			_skeletonCenter = (min + max) * 0.5f;
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
			if ( _ghost is not null )
				_ghost.RenderingEnabled = value && HasSourceGhost;
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

		if ( !TryCharacterBasis( SrcBone, skeleton.RestWorld, out _srcLat, out _srcUp, out _srcFwd,
			out _srcHipHeight, out var srcGround ) )
		{
			Log.Info( "[humanoid-retargeter] preview: source ghost unavailable (mapping lacks the hips/legs/shoulder bones the alignment needs)." );
			return;
		}

		_ghostSkeleton = skeleton;
		_ghostClip = clip;
		_ghostScratch = new XForm[skeleton.Count];
		_ghostAlignedClip = null;

		// Anchor = the source hips' ground projection at frame 0 (clips that start offset
		// from the origin - BVH mocap especially - must not push the ghost away).
		var hipsIndex = SrcBone( BoneRole.Hips ) ?? 0;
		var hips0 = FkPosition( skeleton, clip.Frames[0], _ghostScratch, hipsIndex );
		_srcAnchor = hips0 + _srcUp * (srcGround - VecN.Dot( hips0, _srcUp ));

		if ( _ghost is null )
		{
			_ghost = new SceneLineObject( Scene.SceneWorld );
			_ghost.Opaque = false;     // vertex alpha = the ~50% ghost dimming
			_ghost.Lighting = false;
		}
		_ghost.RenderingEnabled = _showSourceGhost;
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
		if ( _ghost is null || _ghostClip is null )
			return;

		if ( !_showSourceGhost || !EnsureGhostAlignment() )
		{
			_ghost.RenderingEnabled = false;
			return;
		}
		_ghost.RenderingEnabled = true;

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

		_ghost.Clear();
		GhostLineCount = 0;
		for ( var i = 0; i < count; i++ )
		{
			var pos = GhostToEngine( _ghostScratch[i].Pos );

			var parent = skeleton[i].ParentIndex;
			if ( parent >= 0 && parent < count )
			{
				var parentPos = GhostToEngine( _ghostScratch[parent].Pos );
				_ghost.StartLine();
				_ghost.AddLinePoint( parentPos, GhostBoneColor, 0.5f );
				_ghost.AddLinePoint( pos, GhostBoneColor, 0.5f );
				_ghost.EndLine();
				GhostLineCount++;
			}

			// Joint marker: a stubby wide segment reads as a small sphere at preview scale.
			_ghost.StartLine();
			_ghost.AddLinePoint( pos - Vector3.Up * 0.4f, GhostJointColor, 1.2f );
			_ghost.AddLinePoint( pos + Vector3.Up * 0.4f, GhostJointColor, 1.2f );
			_ghost.EndLine();
			GhostLineCount++;
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
		var radius = useModelBounds
			? MathF.Max( _sceneModel.Model.Bounds.Size.Length * 0.5f, 8f )
			: _skeletonRadius;
		var distance = MathX.SphereCameraDistance( radius, Camera.FieldOfView ) * 1.05f;

		var yawRad = MathX.DegreeToRadian( _yaw );
		var dir = new Vector3( MathF.Cos( yawRad ), MathF.Sin( yawRad ), 0.35f ).Normal;
		Camera.WorldPosition = center + dir * distance;
		Camera.WorldRotation = Rotation.LookAt( -dir, Vector3.Up );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );
		var delta = e.LocalPosition - _lastMouse;
		_lastMouse = e.LocalPosition;
		if ( (e.ButtonState & MouseButtons.Left) != 0 )
			_yaw -= delta.x * 0.4f;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		Scene?.Destroy();
		Scene = null;
	}
}
