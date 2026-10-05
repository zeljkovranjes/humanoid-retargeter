#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using HumanoidRetargeterDmx;
using HumanoidRetargeterVrf;
using HumanoidRetargeterVrf.IO;
using HumanoidRetargeterVrf.ResourceTypes;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Editor;

/// <summary>One bone of a Smart Port target: its parent-relative bind transform in engine units (inches, Z up).</summary>
public sealed class SmartPortBone
{
	public string Name { get; set; } = "";

	/// <summary>The parent bone's name; null or empty for a root.</summary>
	public string? Parent { get; set; }

	/// <summary>x, y, z.</summary>
	public float[] Position { get; set; } = new float[3];

	/// <summary>x, y, z, w.</summary>
	public float[] Rotation { get; set; } = { 0, 0, 0, 1 };
}

/// <summary>What <see cref="SmartPortHeadless.Port(SmartPortHeadlessRequest, CancellationToken)"/> works on.</summary>
public sealed class SmartPortHeadlessRequest
{
	/// <summary>Asset path of the model whose animation setup is ported (the source), e.g. the stock Citizen.</summary>
	public string SourceModel { get; set; } = "models/citizen/citizen.vmdl";

	/// <summary>Asset path of the source's graph; empty = the graph the compiled source model references.</summary>
	public string? SourceGraph { get; set; }

	/// <summary>
	/// Folders that hold assets by asset path, searched in order: compiled files (<c>&lt;path&gt;_c</c>) for the source
	/// model and what it depends on, editable sources for its graph and subgraphs (e.g. the s&amp;box install's
	/// <c>addons/citizen/Assets</c>).
	/// </summary>
	public string[] ContentRoots { get; set; } = Array.Empty<string>();

	/// <summary>The target's editable ModelDoc source (its meshes, materials, collision...), in engine units.</summary>
	public string TargetVmdl { get; set; } = "";

	/// <summary>The target's skeleton as it compiles (bind pose).</summary>
	public SmartPortBone[] TargetBones { get; set; } = Array.Empty<SmartPortBone>();

	/// <summary>Target bones for humanoid roles the mapper cannot find, by role name (<see cref="HumanoidRetargeter.Mapping.BoneRole"/>).</summary>
	public Dictionary<string, string> TargetRoles { get; set; } = new();

	/// <summary>Asset path the ported model is written to (the graph copy names it as its preview model).</summary>
	public string ModelPath { get; set; } = "";

	/// <summary>Asset folder for the generated data: retargeted clips, the graph and subgraph copies.</summary>
	public string OutputFolder { get; set; } = "";

	/// <summary>The physical folder standing for <see cref="OutputFolder"/>; generated files are written there.</summary>
	public string StagingDirectory { get; set; } = "";

	/// <summary>
	/// ModelDoc list classes whose target entries are kept next to the source's (Smart Port otherwise takes them
	/// from the source), e.g. <c>GameDataList</c>, <c>BoneMarkupList</c>, or <c>AnimationList</c> to keep the
	/// target's own sequences as well. Entries whose name the source already uses are skipped with a warning.
	/// </summary>
	public string[] KeepTargetLists { get; set; } = Array.Empty<string>();

	/// <summary>When set, DMX element ids derive from this seed: the same request writes byte-identical files.</summary>
	public string? Seed { get; set; }
}

/// <summary>A finished headless Smart Port.</summary>
public sealed class SmartPortHeadlessResult
{
	/// <summary>The ported model's ModelDoc source: target geometry, source animation setup, the graph copy assigned.</summary>
	public string Vmdl { get; set; } = "";

	/// <summary>Asset path of the graph copy the model uses.</summary>
	public string GraphPath { get; set; } = "";

	/// <summary>Asset paths of the generated files the model and its graphs use (all under the output folder).</summary>
	public string[] Files { get; set; } = Array.Empty<string>();

	/// <summary>The ported model's sequences.</summary>
	public string[] Sequences { get; set; } = Array.Empty<string>();

	/// <summary>Source bone name to the target bone that took its place (unmatched source helpers keep their name).</summary>
	public Dictionary<string, string> BoneMap { get; set; } = new();

	/// <summary>Target to source leg length: how translations and root motion were scaled.</summary>
	public float MotionScale { get; set; } = 1;

	/// <summary>False when the two armatures matched and the clips were copied as they are.</summary>
	public bool Retargeted { get; set; }

	/// <summary>Everything that was not ported exactly.</summary>
	public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Smart Port without the editor UI or asset system: the same steps as the Smart Port dialog's "use the source's
/// setup" mode (<see cref="SmartPortModels"/>) for a target given as ModelDoc source plus its skeleton. The source is
/// recovered from its compiled files and its clips are retargeted on the way (<see cref="SmartPortRig"/>), the
/// target keeps its geometry and gets the source's animations, graph, attachments, IK and constraints. Nothing is
/// compiled; the caller compiles the written files. Engine-independent, so build tools and tests can run it.
/// </summary>
public static class SmartPortHeadless
{
	/// <summary>Changes when the request or result shape changes incompatibly.</summary>
	public const int ApiVersion = 1;

	static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	/// <see cref="Port(SmartPortHeadlessRequest, CancellationToken)"/> with JSON in and out, for callers that bind
	/// this library at runtime instead of referencing it. A failure is returned as <c>{ "Error": "..." }</c>.
	/// </summary>
	public static string PortJson( string requestJson )
	{
		try
		{
			var request = JsonSerializer.Deserialize<SmartPortHeadlessRequest>( requestJson, Json )
				?? throw new ArgumentException( "Empty Smart Port request." );
			return JsonSerializer.Serialize( Port( request ) );
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			return JsonSerializer.Serialize( new Dictionary<string, string> { ["Error"] = e.Message, ["Kind"] = e.GetType().Name } );
		}
	}

	public static SmartPortHeadlessResult Port( SmartPortHeadlessRequest request, CancellationToken token = default )
	{
		if ( string.IsNullOrWhiteSpace( request.TargetVmdl ) || request.TargetBones.Length == 0 )
			throw new ArgumentException( "The target needs its ModelDoc source and skeleton." );
		if ( string.IsNullOrWhiteSpace( request.ModelPath ) || string.IsNullOrWhiteSpace( request.OutputFolder ) || string.IsNullOrWhiteSpace( request.StagingDirectory ) )
			throw new ArgumentException( "Specify the model path, the output folder and its staging directory." );
		var outputFolder = request.OutputFolder.Replace( '\\', '/' ).Trim( '/' );
		var staging = Path.GetFullPath( request.StagingDirectory );
		var files = new ContentFiles( request.ContentRoots );
		var warnings = new List<string>();
		var compiled = files.Compiled( request.SourceModel )
			?? throw new FileNotFoundException( $"The compiled source model {request.SourceModel} is not in the content roots." );

		using var ids = request.Seed is null ? null : ElementIds.Deterministic( request.Seed );

		SkeletonModel source;
		string graphAsset;
		using ( var resource = new Resource { FileName = request.SourceModel } )
		{
			resource.Read( compiled );
			var model = resource.DataBlock as Model ?? throw new InvalidDataException( request.SourceModel + " is not a model." );
			source = SkeletonModel.Create( model.Skeleton.Bones.Select( b => new BoneDefinition( b.Name, b.Parent?.Name, new XForm( b.Position, b.Angle ) ) ).ToArray() );
			graphAsset = !string.IsNullOrWhiteSpace( request.SourceGraph ) ? request.SourceGraph!
				: resource.ExternalReferences?.ResourceRefInfoList.Select( r => r.Name ).FirstOrDefault( n => n.EndsWith( ".vanmgrph", StringComparison.OrdinalIgnoreCase ) )
				?? throw new InvalidOperationException( "Source has no animation graph." );
		}
		graphAsset = graphAsset.Replace( '\\', '/' );
		var graphText = files.Source( graphAsset ) ?? RecoverGraph( files, graphAsset, token );
		var subgraphs = Subgraphs( graphText, files, warnings );

		var target = SkeletonModel.Create( request.TargetBones.Select( b => new BoneDefinition( b.Name, string.IsNullOrEmpty( b.Parent ) ? null : b.Parent,
			new XForm( new Vector3( b.Position[0], b.Position[1], b.Position[2] ),
				Quaternion.Normalize( new Quaternion( b.Rotation[0], b.Rotation[1], b.Rotation[2], b.Rotation[3] ) ) ) ) ).ToArray() );
		SmartPortRig? rig = null;
		if ( SmartPortSetup.CompatibilityError( target, source ) is not null )
		{
			// IK goals live in the graph and, for the stock Citizen, mostly in its subgraphs.
			var goals = new Dictionary<string, string>( StringComparer.Ordinal );
			foreach ( var text in subgraphs.Values.Prepend( graphText ) )
				foreach ( var (goal, end) in SmartPortIkTargets.Read( compiled, text ) )
				{
					if ( goals.TryGetValue( goal, out var previous ) && previous != end )
						throw new InvalidOperationException( "Conflicting IK effectors for goal: " + goal );
					goals[goal] = end;
				}
			var roles = request.TargetRoles.ToDictionary(
				p => Enum.TryParse<HumanoidRetargeter.Mapping.BoneRole>( p.Key, true, out var role ) ? role : throw new ArgumentException( "Unknown humanoid role: " + p.Key ),
				p => p.Value );
			rig = new SmartPortRig( source, target, goals, SmartPortAttachments.BoneNames( compiled ), roles );
		}
		token.ThrowIfCancellationRequested();

		var sourceRoot = Path.Combine( staging, "source" );
		Directory.CreateDirectory( sourceRoot );
		var sourceText = CompiledAssetRecovery.Recover( compiled, request.SourceModel, sourceRoot, outputFolder + "/source", files, token, rig );

		var name = Path.GetFileNameWithoutExtension( request.ModelPath );
		var graphPath = outputFolder + "/" + name + ".vanmgrph";
		var targetText = WithSkeleton( request.TargetVmdl, target );
		var vmdl = rig is null ? SmartPortSetup.Apply( targetText, sourceText, graphPath )
			: SmartPortSetup.ApplyRetargeted( targetText, sourceText, graphPath, rig );
		if ( rig is not null ) vmdl = SmartPortAttachments.AlignInfluencesAndFixedAxes( SmartPortAttachments.Align( vmdl, sourceText, rig ), sourceText, rig );
		vmdl = KeepTargetEntries( vmdl, request.TargetVmdl, request.KeepTargetLists, warnings );

		var outputs = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( rig is not null )
		{
			var copy = SmartPortGraphs.Rebase( graphText, rig.BoneNames, files.Source, outputFolder + "/subgraphs" );
			graphText = copy.Graph;
			foreach ( var (path, text) in copy.Subgraphs ) outputs[path] = text;
			foreach ( var path in copy.Unread )
				warnings.Add( $"Subgraph {path} has no editable source in the content roots; it still names the source's bones." );
		}
		outputs[graphPath] = StockAnimationGraph.CopyForModel( graphText, request.ModelPath );
		foreach ( var (path, text) in outputs )
		{
			var file = StagedFile( staging, outputFolder, path );
			Directory.CreateDirectory( Path.GetDirectoryName( file )! );
			File.WriteAllText( file, text );
		}

		// Recovery also writes the source's meshes and collision, which the target replaces: report only what is used.
		var used = new SortedSet<string>( StringComparer.Ordinal );
		foreach ( var text in outputs.Values.Prepend( vmdl ) )
			foreach ( var value in Strings( Kv3.Parse( text ).Root ) )
			{
				var path = value.Replace( '\\', '/' );
				if ( path.StartsWith( outputFolder + "/", StringComparison.OrdinalIgnoreCase ) && File.Exists( StagedFile( staging, outputFolder, path ) ) )
					used.Add( path );
			}
		foreach ( var path in outputs.Keys ) used.Add( path );

		return new SmartPortHeadlessResult
		{
			Vmdl = vmdl,
			GraphPath = graphPath,
			Files = used.ToArray(),
			Sequences = Sequences( vmdl ),
			BoneMap = rig?.BoneNames.ToDictionary( p => p.Key, p => p.Value ) ?? source.Bones.ToDictionary( b => b.Name, b => b.Name ),
			MotionScale = rig?.MotionScale ?? 1,
			Retargeted = rig is not null,
			Warnings = warnings.ToArray(),
		};
	}

	/// <summary>The physical file of an asset path under the output folder.</summary>
	static string StagedFile( string staging, string outputFolder, string path )
	{
		var relative = path.Replace( '\\', '/' )[(outputFolder.Length + 1)..];
		return CompiledAssetRecovery.ConfinedPath( staging, relative );
	}

	static string RecoverGraph( ContentFiles files, string graphAsset, CancellationToken token )
	{
		var compiled = files.Compiled( graphAsset ) ?? throw new FileNotFoundException( "The source's animation graph is not in the content roots: " + graphAsset );
		return CompiledAssetRecovery.Recover( compiled, graphAsset, Path.GetTempPath(), "", files, token );
	}

	/// <summary>Every subgraph a graph reaches whose editable source the content roots hold, by asset path.</summary>
	static Dictionary<string, string> Subgraphs( string graph, ContentFiles files, List<string> warnings )
	{
		var found = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var missing = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var queue = new Queue<string>( SubgraphReferences( graph ) );
		while ( queue.Count > 0 )
		{
			var path = queue.Dequeue();
			if ( found.ContainsKey( path ) || missing.Contains( path ) ) continue;
			if ( files.Source( path ) is not { } text )
			{
				warnings.Add( $"Subgraph {path} has no editable source in the content roots; its IK goals are not fitted." );
				missing.Add( path );
				continue;
			}
			found[path] = text;
			foreach ( var child in SubgraphReferences( text ) ) queue.Enqueue( child );
		}
		return found;
	}

	static IEnumerable<string> SubgraphReferences( string graph )
	{
		var references = new List<string>();
		Visit( Kv3.Parse( graph ).Root );
		return references;

		void Visit( KvValue value )
		{
			if ( value is KvObject node )
			{
				if ( node.GetString( "m_subGraphFilename" ) is { Length: > 0 } path ) references.Add( path.Replace( '\\', '/' ) );
				foreach ( var key in node.Keys ) Visit( node[key] );
			}
			else if ( value is KvArray array )
				foreach ( var item in array.Items ) Visit( item );
		}
	}

	/// <summary>
	/// Smart Port merges the source's helper bones into the target's ModelDoc <c>Skeleton</c> node, which recovered
	/// models have. An authored target may define its skeleton only through its meshes: describe it explicitly
	/// (the same bones and bind, so the compiled skeleton does not change).
	/// </summary>
	static string WithSkeleton( string vmdl, SkeletonModel skeleton )
	{
		var doc = Kv3.Parse( vmdl );
		var root = (KvObject)((KvObject)doc.Root)["rootNode"];
		if ( root.GetOrNull( "children" ) is not KvArray children ) throw new InvalidOperationException( "ModelDoc has no node list." );
		if ( children.Items.OfType<KvObject>().Any( n => n.GetString( "_class" ) == "Skeleton" ) ) return vmdl;
		var nodes = new KvArray[skeleton.Count];
		var top = new KvArray();
		foreach ( var bone in skeleton.Bones )
		{
			var angles = ModelExtract.ToEulerAngles( bone.RestLocal.Rot );
			nodes[bone.Index] = new KvArray();
			var node = new KvObject
			{
				["_class"] = new KvString( "Bone" ),
				["name"] = new KvString( bone.Name ),
				["origin"] = Numbers( bone.RestLocal.Pos.X, bone.RestLocal.Pos.Y, bone.RestLocal.Pos.Z ),
				["angles"] = Numbers( angles.X, angles.Y, angles.Z ),
				["do_not_discard"] = new KvBool( true ),
				["children"] = nodes[bone.Index],
			};
			(bone.ParentIndex < 0 ? top : nodes[bone.ParentIndex]).Items.Add( node );
		}
		children.Items.Add( new KvObject { ["_class"] = new KvString( "Skeleton" ), ["children"] = top } );
		return Kv3.Serialize( doc );

		static KvArray Numbers( params float[] values )
		{
			var array = new KvArray();
			foreach ( var value in values ) array.Items.Add( new KvDouble( Math.Round( value, 6 ) ) );
			return array;
		}
	}

	/// <summary>Adds the target's entries of the given list classes back next to the ported model's own.</summary>
	static string KeepTargetEntries( string vmdl, string targetVmdl, IReadOnlyCollection<string> classes, List<string> warnings )
	{
		if ( classes.Count == 0 ) return vmdl;
		var doc = Kv3.Parse( vmdl );
		var children = (KvArray)((KvObject)((KvObject)doc.Root)["rootNode"])["children"];
		var targetChildren = (KvArray)((KvObject)((KvObject)Kv3.Parse( targetVmdl ).Root)["rootNode"])["children"];
		foreach ( var type in classes )
		{
			if ( targetChildren.Items.OfType<KvObject>().FirstOrDefault( n => n.GetString( "_class" ) == type ) is not { } kept ) continue;
			var existing = children.Items.OfType<KvObject>().FirstOrDefault( n => n.GetString( "_class" ) == type );
			if ( existing is null )
			{
				children.Items.Add( kept );
				continue;
			}
			if ( existing.GetOrNull( "children" ) is not KvArray items ) existing["children"] = items = new KvArray();
			// only the source's names can clash: the target's own entries were valid together before
			var names = Objects( items ).Select( Identity ).OfType<string>().Where( n => n.Length > 0 ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			foreach ( var entry in ((kept.GetOrNull( "children" ) as KvArray)?.Items ?? new List<KvValue>()).OfType<KvObject>() )
			{
				var clash = Objects( entry ).Select( Identity ).OfType<string>().FirstOrDefault( names.Contains );
				if ( clash is not null )
				{
					warnings.Add( $"{type}: the target's '{clash}' was not kept; the source defines the same name." );
					continue;
				}
				items.Items.Add( entry );
			}
		}
		return Kv3.Serialize( doc );

		// Named ModelDoc nodes (sequences, game data, weight lists, pose parameters) and bone markup.
		static string? Identity( KvObject node ) => node.GetString( "_class" ) is "AnimEvent" or "AnimSubtract" or "ExtractMotion" ? null
			: node.GetString( "name" ) ?? node.GetString( "target_bone" );
	}

	static string[] Sequences( string vmdl )
	{
		var children = (KvArray)((KvObject)((KvObject)Kv3.Parse( vmdl ).Root)["rootNode"])["children"];
		var list = children.Items.OfType<KvObject>().FirstOrDefault( n => n.GetString( "_class" ) == "AnimationList" );
		if ( list is null ) return Array.Empty<string>();
		return Objects( list ).Where( n => n.GetString( "_class" ) is "AnimFile" or "1DBlend" or "2DBlend" )
			.Select( n => n.GetString( "name" ) ).OfType<string>().Distinct( StringComparer.Ordinal ).ToArray();
	}

	static IEnumerable<KvObject> Objects( KvValue value )
	{
		if ( value is KvObject obj )
		{
			yield return obj;
			foreach ( var key in obj.Keys )
				foreach ( var child in Objects( obj[key] ) ) yield return child;
		}
		else if ( value is KvArray array )
			foreach ( var item in array.Items )
				foreach ( var child in Objects( item ) ) yield return child;
	}

	static IEnumerable<string> Strings( KvValue value )
	{
		if ( value is KvString s ) yield return s.Value;
		else if ( value is KvObject obj )
		{
			foreach ( var key in obj.Keys )
				foreach ( var child in Strings( obj[key] ) ) yield return child;
		}
		else if ( value is KvArray array )
			foreach ( var item in array.Items )
				foreach ( var child in Strings( item ) ) yield return child;
	}

	/// <summary>Assets by path from a list of folders: compiled files for recovery, editable sources for graphs.</summary>
	sealed class ContentFiles : IReadOnlyDictionary<string, string>
	{
		readonly string[] roots;
		readonly Dictionary<string, string?> compiled = new( StringComparer.OrdinalIgnoreCase );

		public ContentFiles( IEnumerable<string> roots ) => this.roots = roots.Where( Directory.Exists ).Select( Path.GetFullPath ).ToArray();

		string? Find( string path )
		{
			path = path.Replace( '\\', '/' ).TrimStart( '/' );
			foreach ( var root in roots )
			{
				var file = Path.Combine( root, path );
				if ( File.Exists( file ) ) return file;
			}
			return null;
		}

		public string? Compiled( string path )
		{
			path = path.Replace( '\\', '/' );
			if ( path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) path = path[..^2];
			if ( !compiled.TryGetValue( path, out var file ) ) compiled[path] = file = Find( path + "_c" );
			return file;
		}

		public string? Source( string path ) => Find( path ) is { } file ? File.ReadAllText( file ) : null;

		// The recovery loader only looks dependencies up by path.
		public bool TryGetValue( string key, out string value )
		{
			value = Compiled( key ) ?? "";
			return value.Length > 0;
		}

		public bool ContainsKey( string key ) => Compiled( key ) is not null;
		public string this[string key] => Compiled( key ) ?? throw new KeyNotFoundException( key );
		public IEnumerable<string> Keys => compiled.Where( p => p.Value is not null ).Select( p => p.Key );
		public IEnumerable<string> Values => compiled.Values.OfType<string>();
		public int Count => Keys.Count();
		public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => compiled.Where( p => p.Value is not null ).Select( p => new KeyValuePair<string, string>( p.Key, p.Value! ) ).GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
