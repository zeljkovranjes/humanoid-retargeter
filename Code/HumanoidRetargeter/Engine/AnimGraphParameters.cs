#if SANDBOX
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Sandbox;

namespace HumanoidRetargeter.Engine;

/// <summary>
/// Sets animation graph parameters on a renderer, whichever graph system drives it (see <see cref="AnimGraphVersions"/>).
/// AnimGraph1 takes them through the renderer's own setters, exactly as before. AnimGraph2 takes them through the
/// renderer's serialized animation state: game code may not name AnimGraph2's types (engines without it couldn't
/// compile it) nor reflect on them, and that state is the one route to its parameters left. The engine replaces the
/// whole parameter set on each write, so every value set on a renderer is kept and written together.
/// </summary>
public sealed class AnimGraphParameters : GameObjectSystem<AnimGraphParameters>
{
	// Each AnimGraph2 renderer's values by kind, named as the engine serializes them, and whether they changed since
	// they were written
	private sealed class Values
	{
		public readonly Dictionary<string, float> Floats = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, bool> Bools = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, Vector3> Vectors = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, string> Ids = new( StringComparer.OrdinalIgnoreCase );
		public object Graph;
		public Dictionary<string, string[]> Options;
		public bool Dirty;
	}

	private readonly Dictionary<SkinnedModelRenderer, Values> _values = new();
	private readonly List<(SkinnedModelRenderer Renderer, string Name)> _triggers = new();
	private readonly List<(SkinnedModelRenderer Renderer, string Name)> _firedTriggers = new();
	private readonly List<SkinnedModelRenderer> _gone = new();

	public AnimGraphParameters( Scene scene ) : base( scene )
	{
		// Written as bones are worked out, so everything set this frame reaches the graph together
		Listen( Stage.UpdateBones, -10000, Write, "AnimGraphParameters.Write" );
		Listen( Stage.StartUpdate, 0, EndTriggers, "AnimGraphParameters.EndTriggers" );
	}

	/// <summary>Sets float <paramref name="name"/> on <paramref name="renderer"/>.</summary>
	public static void Set( SkinnedModelRenderer renderer, string name, float value )
	{
		if ( For( renderer ) is { } values )
			Change( values, values.Floats, name, value );
		else
			Legacy( renderer, name, value );
	}

	/// <summary>Sets bool <paramref name="name"/> on <paramref name="renderer"/>; it stays set (see <see cref="Trigger"/>).</summary>
	public static void Set( SkinnedModelRenderer renderer, string name, bool value )
	{
		if ( For( renderer ) is { } values )
			Change( values, values.Bools, name, value );
		else
			Legacy( renderer, name, value );
	}

	/// <summary>
	/// Sets int <paramref name="name"/> on <paramref name="renderer"/>. AnimGraph2 takes an AnimGraph1 enum (a hold type)
	/// as the option at that index of its ID parameter, in the same order; a number otherwise.
	/// </summary>
	public static void Set( SkinnedModelRenderer renderer, string name, int value )
	{
		if ( For( renderer ) is not { } values )
		{
			Legacy( renderer, name, value );
			return;
		}

		if ( OptionsOf( values, name ) is { } options )
		{
			if ( value >= 0 && value < options.Length )
				Change( values, values.Ids, name, options[ value ] );

			return;
		}

		Change( values, values.Floats, name, (float)value );
	}

	/// <summary>Sets option <paramref name="name"/> (an enum or ID parameter) on <paramref name="renderer"/> by its name.</summary>
	public static void Set( SkinnedModelRenderer renderer, string name, string option )
	{
		if ( For( renderer ) is { } values )
			Change( values, values.Ids, name, option ?? "" );
		else
			Legacy( renderer, name, option );
	}

	/// <summary>Sets vector <paramref name="name"/> on <paramref name="renderer"/>.</summary>
	public static void Set( SkinnedModelRenderer renderer, string name, Vector3 value )
	{
		if ( For( renderer ) is { } values )
			Change( values, values.Vectors, name, value );
		else
			Legacy( renderer, name, value );
	}

	/// <summary>
	/// Points look parameter <paramref name="name"/> along <paramref name="directionWorld"/> (made local to the renderer),
	/// and sets <c>{name}_weight</c> to <paramref name="weight"/>, as AnimGraph1's own look direction does.
	/// </summary>
	public static void SetLookDirection( SkinnedModelRenderer renderer, string name, Vector3 directionWorld, float weight )
	{
		if ( For( renderer ) is not { } values )
		{
#pragma warning disable CS0618 // AnimGraph1's own setter: obsolete only on engines that have AnimGraph2
			renderer?.SetLookDirection( name, directionWorld, weight );
#pragma warning restore CS0618
			return;
		}

		Change( values, values.Vectors, name, directionWorld * renderer.WorldRotation.Inverse );
		Change( values, values.Floats, $"{name}_weight", weight );
	}

	/// <summary>
	/// Sets bool <paramref name="name"/> for one update, for an action that fires once (an attack, a reload). AnimGraph1
	/// graphs reset these themselves; AnimGraph2 has it cleared at the start of the next frame.
	/// </summary>
	public static void Trigger( SkinnedModelRenderer renderer, string name )
	{
		if ( For( renderer ) is not { } values )
		{
			Legacy( renderer, name, true );
			return;
		}

		Change( values, values.Bools, name, true );
		renderer.Scene.GetSystem<AnimGraphParameters>()?._triggers.Add( (renderer, name) );
	}

	/// <summary>Forgets what was set on <paramref name="renderer"/> for AnimGraph2, after its graph is swapped.</summary>
	public static void Forget( SkinnedModelRenderer renderer )
	{
		renderer?.Scene?.GetSystem<AnimGraphParameters>()?._values.Remove( renderer );
	}

	// This renderer's AnimGraph2 values, or null when AnimGraph1 drives it
	private static Values For( SkinnedModelRenderer renderer )
	{
		if ( AnimGraphVersions.GraphOf( renderer ) is not { } graph || renderer.Scene?.GetSystem<AnimGraphParameters>() is not { } system )
			return null;

		if ( !system._values.TryGetValue( renderer, out var values ) || values.Graph != graph )
		{
			values = new Values { Graph = graph };
			system._values[ renderer ] = values;
		}

		return values;
	}

	private static void Change<T>( Values values, Dictionary<string, T> kind, string name, T value )
	{
		if ( string.IsNullOrEmpty( name ) || kind.TryGetValue( name, out var known ) && EqualityComparer<T>.Default.Equals( known, value ) )
			return;

		kind[ name ] = value;
		values.Dirty = true;
	}

	// The options of the graph's ID parameter called name, or null when it isn't one
	private static string[] OptionsOf( Values values, string name )
	{
		values.Options ??= ReadOptions( values.Graph );
		return values.Options.TryGetValue( name, out var options ) ? options : null;
	}

	private static Dictionary<string, string[]> ReadOptions( object graph )
	{
		var options = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
		if ( TypeLibrary.GetPropertyValue( graph, "Parameters" ) is not IEnumerable parameters )
			return options;

		foreach ( var parameter in parameters )
		{
			if ( TypeLibrary.GetPropertyValue( parameter, "Name" ) is string name
				&& TypeLibrary.GetPropertyValue( parameter, "Options" ) is IEnumerable<string> list )
			{
				var array = new List<string>( list ).ToArray();
				if ( array.Length > 0 )
					options[ name ] = array;
			}
		}

		return options;
	}

	private void Write()
	{
		_gone.Clear();
		foreach ( var (renderer, values) in _values )
		{
			if ( !renderer.IsValid() )
			{
				_gone.Add( renderer );
				continue;
			}

			if ( !values.Dirty )
				continue;

			values.Dirty = false;
			renderer.DeserializeImmediately( new JsonObject { [ "Animation" ] = Overlay( renderer, values ) } );
		}

		foreach ( var renderer in _gone )
			_values.Remove( renderer );

		// Written now: they end at the start of the next frame
		_firedTriggers.AddRange( _triggers );
		_triggers.Clear();
	}

	// The renderer's animation state as it is now, with every value set here laid over it. Starting from what the
	// renderer holds keeps values others set (another library's copy of this, game code using AnimGraph2 directly), and
	// keeps its graph as it names it: null when it plays its model's, which naming would restart, losing this frame.
	// shortcut: serializes the renderer and writes it back per changed renderer per frame (well under a millisecond,
	// measured on staging); switch to AnimGraph2's own parameter setters once game code may call them on every engine.
	private static JsonObject Overlay( SkinnedModelRenderer renderer, Values values )
	{
		var animation = renderer.Serialize()?[ "Animation" ]?.DeepClone() as JsonObject ?? new JsonObject();
		if ( animation[ "Parameters" ] is not JsonObject parameters )
			animation[ "Parameters" ] = parameters = new JsonObject();

		var floats = Kind( parameters, "floats" );
		foreach ( var (name, value) in values.Floats )
			floats[ name ] = value;

		var bools = Kind( parameters, "bools" );
		foreach ( var (name, value) in values.Bools )
			bools[ name ] = value;

		var vectors = Kind( parameters, "vectors" );
		foreach ( var (name, value) in values.Vectors )
			vectors[ name ] = string.Create( CultureInfo.InvariantCulture, $"{value.x},{value.y},{value.z}" );

		var ids = Kind( parameters, "ids" );
		foreach ( var (name, value) in values.Ids )
			ids[ name ] = value;

		return animation;
	}

	// One kind of value in the serialized parameters, named as the engine names it
	private static JsonObject Kind( JsonObject parameters, string kind )
	{
		if ( parameters[ kind ] is not JsonObject values )
			parameters[ kind ] = values = new JsonObject();

		return values;
	}

	// Triggers written last frame go back to false now, one update after
	private void EndTriggers()
	{
		foreach ( var (renderer, name) in _firedTriggers )
		{
			if ( For( renderer ) is { } values )
				Change( values, values.Bools, name, false );
		}

		_firedTriggers.Clear();
	}

#pragma warning disable CS0618 // AnimGraph1's own setters: obsolete only on engines that have AnimGraph2
	private static void Legacy( SkinnedModelRenderer renderer, string name, float value ) => renderer?.Set( name, value );
	private static void Legacy( SkinnedModelRenderer renderer, string name, bool value ) => renderer?.Set( name, value );
	private static void Legacy( SkinnedModelRenderer renderer, string name, int value ) => renderer?.Set( name, value );
	private static void Legacy( SkinnedModelRenderer renderer, string name, string value ) => renderer?.Set( name, value );
	private static void Legacy( SkinnedModelRenderer renderer, string name, Vector3 value ) => renderer?.Set( name, value );
#pragma warning restore CS0618
}
#endif
