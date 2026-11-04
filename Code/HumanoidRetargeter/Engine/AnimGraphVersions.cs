#if SANDBOX
using System.Linq;
using Sandbox;

namespace HumanoidRetargeter.Engine;

/// <summary>Tells which <see cref="AnimGraphVersion"/> drives a model or renderer, and how to name it to a person.</summary>
public static class AnimGraphVersions
{
	/// <summary>Whether this engine has AnimGraph2 at all. Looked up once: an engine doesn't gain or lose it while running.</summary>
	public static bool IsAnimGraph2Available => _hasAnimGraph2 ??= TypeLibrary.GetType( typeof( SkinnedModelRenderer ) )?.GetProperty( "Animation" ) is not null;

	// Looked up on first use: the type library is off while static constructors run
	private static bool? _hasAnimGraph2;

	/// <summary>
	/// What drives <paramref name="model"/> by default: AnimGraph2 when it names an AnimGraph2 graph, otherwise AnimGraph1
	/// (whether or not it has a graph at all). Legacy for null.
	/// </summary>
	public static AnimGraphVersion Of( Model model )
	{
		if ( model is null || !IsAnimGraph2Available )
			return AnimGraphVersion.Legacy;

		return TypeLibrary.GetPropertyValue( model, "AnimationGraph" ) is not null ? AnimGraphVersion.AnimGraph2 : AnimGraphVersion.Legacy;
	}

	/// <summary>The resource path of the AnimGraph2 graph <paramref name="model"/> names, or null when it names none.</summary>
	public static string GraphPathOf( Model model )
	{
		if ( model is null || !IsAnimGraph2Available || TypeLibrary.GetPropertyValue( model, "AnimationGraph" ) is not { } graph )
			return null;

		return TypeLibrary.GetPropertyValue( graph, "ResourcePath" ) as string;
	}

	/// <summary>What drives <paramref name="renderer"/> now: its own AnimGraph2 graph, or else its model's. Legacy for null.</summary>
	public static AnimGraphVersion Of( SkinnedModelRenderer renderer )
	{
		return GraphOf( renderer ) is not null ? AnimGraphVersion.AnimGraph2 : AnimGraphVersion.Legacy;
	}

	/// <summary>The indicator text for <paramref name="version"/>: "AnimGraph1 (legacy)" or "Using AnimGraph2".</summary>
	public static string Describe( AnimGraphVersion version )
	{
		return version == AnimGraphVersion.AnimGraph2 ? "Using AnimGraph2" : "AnimGraph1 (legacy)";
	}

	/// <summary>
	/// Drives <paramref name="renderer"/> with the AnimGraph2 graph at <paramref name="path"/> (a <c>.ag</c>), or with its
	/// model's own graphs again for null. False when this engine has no AnimGraph2 or the graph doesn't load.
	/// </summary>
	public static bool UseAnimGraph2( SkinnedModelRenderer renderer, string path )
	{
		if ( !renderer.IsValid() || !IsAnimGraph2Available || TypeLibrary.GetPropertyValue( renderer, "Animation" ) is not { } animation )
			return false;

		object graph = null;
		if ( !string.IsNullOrEmpty( path ) && (graph = Load( path )) is null )
			return false;

		renderer.UseAnimGraph = true;

		// The type library won't set a property to null, so a graph is taken off through the renderer's saved state
		if ( graph is null )
			renderer.DeserializeImmediately( new System.Text.Json.Nodes.JsonObject { [ "Animation" ] = new System.Text.Json.Nodes.JsonObject { [ "Graph" ] = null } } );
		else
			TypeLibrary.SetProperty( animation, "Graph", graph );

		AnimGraphParameters.Forget( renderer );
		return true;
	}

	/// <summary>Whether the AnimGraph2 graph at <paramref name="path"/> (a <c>.ag</c>) loads without error. False when this engine has no AnimGraph2.</summary>
	public static bool CanLoadAnimGraph2( string path )
	{
		return IsAnimGraph2Available && !string.IsNullOrEmpty( path ) && Load( path ) is not null;
	}

	// The AnimGraph2 graph at path (a Sandbox.Animation.Graph), or null when it doesn't load
	private static object Load( string path )
	{
		// GetMethod finds instance methods only; Load is static
		var load = TypeLibrary.GetType( "Sandbox.Animation.Graph" )?.Methods.FirstOrDefault( x => x.Name == "Load" && x.IsStatic && x.Parameters.Length == 1 );
		var graph = load?.InvokeWithReturn<object>( null, new object[] { path } );
		return graph is null || TypeLibrary.GetPropertyValue( graph, "IsError" ) is true ? null : graph;
	}

	/// <summary>The AnimGraph2 graph driving <paramref name="renderer"/> (a <c>Sandbox.Animation.Graph</c>), or null.</summary>
	internal static object GraphOf( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !IsAnimGraph2Available )
			return null;

		var animation = TypeLibrary.GetPropertyValue( renderer, "Animation" );
		return animation is null ? null : TypeLibrary.GetPropertyValue( animation, "Graph" );
	}
}
#endif
