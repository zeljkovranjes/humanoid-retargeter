#if SANDBOX
using Sandbox;
using HumanoidRetargeter.Engine;
using HumanoidRetargeter.Resources;

namespace HumanoidRetargeter.Components;

/// <summary>Plays an exported MMD character with native deformation and material morphs.</summary>
[Title( "MMD Playback" ), Category( "Animation" ), Icon( "animation" )]
public sealed class MmdPlayback : Component, Component.ExecuteInEditor
{
	/// <summary>Animation exported by the retargeter for a PMX/PMD custom target.</summary>
	[Property]
	public MmdAnimation Animation { get; set; }

	/// <summary>Advances playback automatically.</summary>
	[Property]
	public bool Playing { get; set; } = true;

	/// <summary>Playback speed multiplier.</summary>
	[Property, Range( 0, 4 )]
	public float Speed { get; set; } = 1;

	/// <summary>Playback position in seconds; may be set to seek while paused.</summary>
	[Property, Range( 0, 3600 )]
	public float Position { get; set; }

	private MmdPlaybackRenderer _renderer;
	private MmdAnimation _loaded;

	protected override void OnUpdate()
	{
		if ( _loaded != Animation )
		{
			_renderer?.Dispose();
			_renderer = null;
			_loaded = Animation;
			if ( Animation is not null )
			{
				try
				{
					_renderer = new MmdPlaybackRenderer( Scene.SceneWorld, Animation );
				}
				catch ( Exception error )
				{
					Log.Warning( $"MMD playback could not load: {error.Message}" );
				}
			}
		}
		if ( _renderer is null )
			return;
		if ( Playing )
			Position += Sandbox.Time.Delta * Speed;
		if ( _renderer.Looping && _renderer.Duration > 0 )
			Position %= _renderer.Duration;
		else
			Position = Math.Clamp( Position, 0, _renderer.Duration );
		_renderer.Update( Position, WorldTransform );
	}

	protected override void OnDisabled()
	{
		_renderer?.Dispose();
		_renderer = null;
		_loaded = null;
	}
}
#endif
