using System;
using Editor;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using Sandbox;

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
	}

	/// <summary>Switches the clip being previewed (restarts playback).</summary>
	public void SetClip( HumanoidRetargeter.ClipResult clip )
	{
		_clip = clip;
		_time = 0;
		CurrentFrame = 0;
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

		if ( !_sceneModel.IsValid() || _clip?.SolvedFrames is not { Count: > 0 } frames )
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

		_sceneModel.Update( RealTime.Delta );
		ApplyCurrentFrame();
	}

	/// <summary>Applies the current frame's solved pose to the scene model (no-op without a
	/// model or clip). Public so the UI smoke gate can drive a frame headlessly.</summary>
	public void ApplyCurrentFrame()
	{
		if ( !_sceneModel.IsValid() || _clip?.SolvedFrames is not { Count: > 0 } frames )
			return;
		ApplyPose( frames[Math.Clamp( CurrentFrame, 0, frames.Count - 1 )] );
	}

	/// <summary>
	/// Applies an arbitrary pose (local transforms in target-skeleton bone order) to the
	/// scene model. Public so the UI smoke gate can drive the rig's rest pose headlessly and
	/// assert engine-space bone positions.
	/// </summary>
	public void ApplyPose( XForm[] locals )
	{
		if ( !_sceneModel.IsValid() || locals is null )
			return;

		var skeleton = _rig.Skeleton;
		var count = Math.Min( locals.Length, skeleton.Count );
		for ( var i = 0; i < count; i++ )
		{
			var parent = skeleton[i].ParentIndex;
			_worldScratch[i] = parent < 0 ? locals[i] : XForm.Compose( _worldScratch[parent], locals[i] );
		}

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

	void UpdateCamera()
	{
		if ( !Camera.IsValid() )
			return;

		// Frame the POSED character, not the model asset: Model.Bounds is the bind-pose box
		// anchored at the scene origin, so clips that start offset or travel (BVH mocap
		// especially) would walk out of a frame built from it. SceneObject.Bounds follows
		// the current pose; the bind-pose SIZE is kept for the zoom so it doesn't pulse
		// with the animation (arms out ≠ zoom out).
		var center = _sceneModel.IsValid()
			? _sceneModel.Bounds.Center
			: Vector3.Up * 32f;
		var sizeBounds = _sceneModel.IsValid()
			? _sceneModel.Model.Bounds
			: BBox.FromPositionAndSize( Vector3.Up * 32f, 64f );
		var radius = MathF.Max( sizeBounds.Size.Length * 0.5f, 8f );
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
