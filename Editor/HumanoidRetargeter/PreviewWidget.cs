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
/// bone order, target units) are FK-composed to model-space transforms, positions scaled
/// to engine units, and applied via <see cref="SceneModel.SetBoneOverride"/> (which takes
/// transforms local to the SceneModel). Bones are matched to the engine model BY NAME, so
/// helper bones missing from the rig JSON keep their bind pose.
/// </summary>
/// <remarks>
/// This was chosen over building an in-memory <c>Model.Builder</c> model with
/// <c>AddAnimation</c>/<c>AddFrame</c>: a builder model carries bones but no mesh, so a
/// sequence playing on it renders nothing visible - driving the real skinned model shows
/// the actual character. Camera: fixed 3/4 framing from the model bounds with left-drag
/// yaw orbit (same idiom as the editor's other preview widgets).
/// </remarks>
public sealed class PreviewWidget : SceneRenderingWidget
{
	SceneModel _sceneModel;
	TargetRig _rig;
	float _positionScale = 1f;
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
	/// (0.3937 for cm rigs like the s&amp;box source skeleton, 1.0 for engine-unit rigs).
	/// </summary>
	public PreviewWidget( Widget parent, TargetRig rig, string previewModelPath, float positionScale )
		: base( parent )
	{
		_rig = rig;
		_positionScale = positionScale;
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
		ApplyFrame( frames[Math.Clamp( CurrentFrame, 0, frames.Count - 1 )] );
	}

	/// <summary>FK over the target skeleton (locals → model space), then bone overrides
	/// in engine units on the name-matched model bones.</summary>
	void ApplyFrame( XForm[] locals )
	{
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

			var w = _worldScratch[i];
			var transform = new Transform(
				new Vector3( w.Pos.X, w.Pos.Y, w.Pos.Z ) * _positionScale,
				new Rotation( w.Rot.X, w.Rot.Y, w.Rot.Z, w.Rot.W ) );
			_sceneModel.SetBoneOverride( modelBone, transform );
		}
	}

	void UpdateCamera()
	{
		if ( !Camera.IsValid() )
			return;

		var bounds = _sceneModel.IsValid()
			? _sceneModel.Model.Bounds
			: BBox.FromPositionAndSize( Vector3.Up * 32f, 64f );

		var center = bounds.Center;
		var radius = MathF.Max( bounds.Size.Length * 0.5f, 8f );
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
