#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Engine;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>Editor asset lookup and compilation for the opt-in Citizen animation setup.</summary>
internal static class CitizenAnimationModels
{
	// The shipped models whose animation setup is copied, in detection order
	static readonly string[] ShippedReferences = { RetargetTargetSpec.SboxHumanMalePath, RetargetTargetSpec.SboxCitizenPath };

	/// <summary>
	/// Finds the shipped Citizen model whose armature <paramref name="target"/> shares, from <paramref name="references"/>
	/// (the shipped Human and Citizen when null; tests pass other copies, such as an AnimGraph2 one).
	/// </summary>
	internal static bool TryDetect( TargetPickers.ResolvedTarget target, out string referencePath, out string reason,
		System.Collections.Generic.IReadOnlyList<string> references = null )
	{
		referencePath = null;
		reason = "Select a custom model with a compatible Citizen or Human Citizen armature.";
		if ( target is null || (target.ModelFilePath is null && target.CustomVmdlPath is null) )
			return false;
		if ( string.IsNullOrEmpty( target.PreviewModelPath ) )
		{
			reason = "Waiting for the custom model's compiled skeleton.";
			return false;
		}
		try
		{
			var model = Model.Load( target.PreviewModelPath );
			if ( model is null || model.IsError ) return false;
			var skeleton = TargetPickers.SkeletonFromModel( model );
			var errors = new System.Collections.Generic.List<string>();
			foreach ( var path in references ?? ShippedReferences )
			{
				var reference = Model.Load( path );
				if ( reference is null || reference.IsError ) continue;
				var referenceSkeleton = TargetPickers.SkeletonFromModel( reference );
				var error = CitizenAnimationSetup.HierarchyError( skeleton, referenceSkeleton );
				if ( error is not null ) { errors.Add( $"{Path.GetFileNameWithoutExtension( path )}: {error}" ); continue; }
				var asset = AssetSystem.FindByPath( path );
				if ( asset is null || !File.Exists( asset.AbsolutePath ) ) continue;
				referencePath = path;
				reason = path == RetargetTargetSpec.SboxCitizenPath ? "Classic Citizen armature detected." : "Human Citizen armature detected.";
				if ( CitizenAnimationSetup.CompatibilityError( skeleton, referenceSkeleton ) is not null )
					reason += " Fitted bind pose: stock animations will be retargeted when you click Create.";
				return true;
			}
			reason = errors.Count > 0 ? string.Join( "\n", errors ) : "The shipped Citizen model sources are unavailable.";
		}
		catch ( Exception e ) { reason = e.Message; }
		return false;
	}

	/// <param name="references">The shipped models to copy the setup from, as for <see cref="TryDetect"/>.</param>
	internal static async Task<EditorPipeline.WriteResult> CreateAsync(
		TargetPickers.ResolvedTarget target, string outputFolder, string outputName, bool copyAnimGraph = false, Action<string> progress = null,
		bool groundModel = false, System.Collections.Generic.IReadOnlyList<string> references = null )
	{
		await EditorPipeline.SwitchToMainThread();
		if ( !TryDetect( target, out var referencePath, out var reason, references ) )
			throw new InvalidOperationException( reason );
		var destination = ProjectFile( outputFolder + "/" + outputName + ".vmdl" );
		if ( File.Exists( destination ) )
			throw new InvalidOperationException( "That output VMDL already exists. Choose a new name; existing models are not overwritten by this action." );
		var fitted = RequiresRetargeting( target, referencePath );
		var vmdl = BuildModelText( target, referencePath, outputFolder, fitted );
		var animGraph2 = OutputVersion( referencePath ) == AnimGraphVersion.AnimGraph2;
		var modelPath = outputFolder + "/" + outputName + ".vmdl";
		var sourcesFolder = outputFolder + "/" + outputName + "_citizen_sources";
		var shippedGraph2 = animGraph2 ? ReadGraph( vmdl, animGraph2: true ) : null;
		// The shipped AnimGraph2 graph plays clips compiled against the shipped skeleton, and compiles only where their
		// sources are: a fitted armature, or an editable copy of a graph whose skeleton and clips come compiled only, gets
		// the graph rebased onto a skeleton and clips of its own, as Smart Port ports an AnimGraph2 source
		var portGraph2 = animGraph2 && (fitted || copyAnimGraph && MissingAnimGraph2Sources( shippedGraph2 ).Count > 0);
		// A fitted armature on AnimGraph1 plays the stock sequences retargeted to it
		if ( fitted && !portGraph2 )
		{
			vmdl = AnimGraph2Document.Detach( vmdl );
			if ( string.IsNullOrEmpty( StockAnimationGraph.GraphName( vmdl ) ) )
				throw new InvalidOperationException( "This fitted armature needs the stock animations retargeted, which only an AnimGraph1 Citizen graph plays, and the shipped model has none." );
		}
		var additionalFiles = new System.Collections.Generic.List<string>();
		string graphFile = null;
		string graph = null;
		var graphPath = animGraph2 ? AnimGraph2Document.GraphPath( outputFolder, outputName ) : StockAnimationGraph.GraphPath( outputFolder, outputName );
		if ( copyAnimGraph || portGraph2 )
		{
			graphFile = ProjectFile( graphPath );
			if ( File.Exists( graphFile ) )
				throw new InvalidOperationException( "The copied animgraph already exists. Choose a new output name to preserve your edits." );
			if ( !portGraph2 )
				graph = animGraph2 ? AnimGraph2Document.CopyForModel( shippedGraph2, modelPath ) : StockAnimationGraph.CopyForModel( ReadGraph( vmdl ), modelPath );
		}
		if ( portGraph2 && Directory.Exists( ProjectFile( sourcesFolder ) ) )
			throw new InvalidOperationException( "Retargeted Citizen sources already exist. Choose a new output name to preserve them." );
		// All collision checks precede the more expensive fitted conversion.
		if ( fitted )
			(vmdl, additionalFiles) = await FittedCitizenAnimations.ConvertAsync( target, referencePath, vmdl, sourcesFolder, progress );
		if ( portGraph2 )
			graph = await PortAnimGraph2Async( target, referencePath, shippedGraph2, modelPath, sourcesFolder, outputName,
				groundModel ? GroundingLift( vmdl, target ) : 0, progress );
		if ( graphFile is not null )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
			File.WriteAllText( graphFile, graph );
			additionalFiles.Add( graphFile );
			vmdl = animGraph2 ? AnimGraph2Document.Attach( vmdl, graphPath ) : StockAnimationGraph.Attach( vmdl, graphPath );
		}
		if ( groundModel ) vmdl = ModelGrounding.Apply( vmdl, Model.Load( target.PreviewModelPath ).Bounds.Mins.z );
		progress?.Invoke( "Compiling the Citizen animation model and graph…" );
		var batch = new RetargetBatchResult { StandaloneVmdl = vmdl };
		var result = await EditorPipeline.WriteAndCompileAsync( batch, outputFolder,
			standaloneVmdlName: outputName, compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true, additionalAssetPaths: additionalFiles );
		await EditorPipeline.SwitchToMainThread();
		VerifyGraph( result, animGraph2 ? AnimGraph2Document.GraphName( vmdl ) : StockAnimationGraph.GraphName( vmdl ) );
		if ( result.Compiled )
		{
			var missing = Model.Load( referencePath ).AnimationNames.Except( Model.Load( result.VmdlAsset.Path ).AnimationNames ).ToArray();
			if ( missing.Length > 0 )
			{
				result.Compiled = false;
				result.Errors.Add( "The compiled model is missing stock sequences: " + string.Join( ", ", missing ) );
			}
		}
		return result;
	}

	/// <summary>
	/// <paramref name="graph"/> (the shipped Citizen's <c>.ag</c>) rebased for <paramref name="modelPath"/> onto a skeleton
	/// and clips of the target's own, written and compiled under <paramref name="folder"/>: the shipped clips retargeted
	/// with Smart Port's rig, as "Use the source's setup" ports an AnimGraph2 source. <paramref name="lift"/> raises the
	/// target's root as grounding the model does, which AnimGraph2 clips don't go through. Throws when a clip doesn't compile.
	/// </summary>
	static async Task<string> PortAnimGraph2Async( TargetPickers.ResolvedTarget target, string referencePath, string graph, string modelPath,
		string folder, string name, float lift, Action<string> progress )
	{
		await EditorPipeline.SwitchToMainThread();
		var skeleton = TargetPickers.SkeletonFromModel( Model.Load( target.PreviewModelPath ) );
		if ( lift != 0 )
			skeleton = HumanoidRetargeter.Core.Skeleton.Skeleton.Create( skeleton.Bones.Select( bone => new HumanoidRetargeter.Core.Skeleton.BoneDefinition(
				bone.Name, bone.ParentIndex < 0 ? null : skeleton[bone.ParentIndex].Name,
				ModelGrounding.SourceLocal( bone.RestLocal, bone.ParentIndex < 0, -lift ) ) ).ToArray() );
		var rig = SmartPortAnimGraph2Setup.Rig( Model.Load( referencePath ), skeleton, graph, AssetSystem.FindByPath( referencePath ).GetCompiledFile( true ) );
		progress?.Invoke( $"Retargeting the {AnimGraph2Document.Animations( graph ).Count} AnimGraph2 Citizen clips…" );
		var ported = await SmartPortAnimGraph2Setup.WriteAsync( graph, modelPath, null, null, folder, name, rig, CancellationToken.None );
		progress?.Invoke( "Compiling the AnimGraph2 skeleton and clips…" );
		var uncompiled = await SmartPortAnimGraph2Setup.UncompiledAsync( ported.Sources, EditorPipeline.MeshCompileTimeoutSeconds, CancellationToken.None );
		if ( uncompiled.Length > 0 )
			throw new InvalidOperationException( $"{uncompiled.Length} AnimGraph2 source(s) did not compile, first {uncompiled[0]} (see the console)." );
		return ported.Graph;
	}

	// How far grounding raises the target's model (see ModelGrounding.Apply)
	static float GroundingLift( string vmdl, TargetPickers.ResolvedTarget target )
		=> ModelGrounding.Offset( ModelGrounding.Apply( vmdl, Model.Load( target.PreviewModelPath ).Bounds.Mins.z ) ) - ModelGrounding.Offset( vmdl );

	/// <summary>Fails <paramref name="result"/> unless its model loads the graph at <paramref name="expectedPath"/>
	/// (an AnimGraph2 <c>.ag</c> or an AnimGraph1 graph) without error.</summary>
	internal static void VerifyGraph( EditorPipeline.WriteResult result, string expectedPath )
	{
		if ( !result.Compiled ) return;
		var model = Model.Load( result.VmdlAsset.Path );
		if ( expectedPath.EndsWith( ".ag", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( AnimGraphVersions.Of( model ) == AnimGraphVersion.AnimGraph2 && AnimGraphVersions.CanLoadAnimGraph2( expectedPath )
				&& string.Equals( AnimGraphVersions.GraphPathOf( model ), expectedPath, StringComparison.OrdinalIgnoreCase ) ) return;
			result.Compiled = false;
			result.Errors.Add( "The model compiled, but its AnimGraph2 graph did not load correctly: " + expectedPath );
			return;
		}
		var graph = model?.AnimGraph;
		if ( graph is not null && !graph.IsError && string.Equals( graph.Name, expectedPath, StringComparison.OrdinalIgnoreCase ) ) return;
		result.Compiled = false;
		result.Errors.Add( "The model compiled, but its animation graph did not load correctly: " + expectedPath );
	}

	/// <summary>The graph system the Citizen animation model will use: its shipped model's (see
	/// <see cref="AnimGraphVersions.Of(Model)"/>), on a fitted armature too.</summary>
	internal static AnimGraphVersion OutputVersion( string referencePath )
		=> AnimGraphVersions.Of( Model.Load( referencePath ) );

	internal static bool RequiresRetargeting( TargetPickers.ResolvedTarget target, string referencePath )
		=> CitizenAnimationSetup.CompatibilityError( TargetPickers.SkeletonFromModel( Model.Load( target.PreviewModelPath ) ),
			TargetPickers.SkeletonFromModel( Model.Load( referencePath ) ) ) is not null;

	internal static string BuildModelText( TargetPickers.ResolvedTarget target, string referencePath, string outputFolder, bool fitted = false )
	{
		var shipped = File.ReadAllText( AssetSystem.FindByPath( referencePath ).AbsolutePath );
		string custom;
		if ( target.ModelFilePath is not null )
		{
			if ( !EditorPipeline.PrepareModelTargetMesh( target, outputFolder, out var error ) )
				throw new InvalidOperationException( error );
			var spec = target.Spec;
			custom = VmdlWriter.GenerateStandalone( "", Array.Empty<AnimEntry>(), spec.VmdlScale,
				spec.DefaultRootBone, meshFilePath: spec.MeshFilePath, meshImportScale: spec.MeshImportScale,
				materialRemaps: spec.MaterialRemaps, meshImportNames: spec.MeshImportNames );
		}
		else if ( target.CustomVmdlPath is not null )
			custom = File.ReadAllText( target.CustomVmdlPath );
		else
			custom = shipped;
		return CitizenAnimationSetup.Apply( custom, shipped, preserveFittedSettings: fitted );
	}

	/// <summary>The source of the graph <paramref name="vmdl"/> names: its AnimGraph2 graph when <paramref name="animGraph2"/>,
	/// else its AnimGraph1 graph. The project copy first, then the mounted (shipped) file.</summary>
	internal static string ReadGraph( string vmdl, bool animGraph2 = false )
	{
		var path = animGraph2 ? AnimGraph2Document.GraphName( vmdl ) : StockAnimationGraph.GraphName( vmdl );
		if ( string.IsNullOrWhiteSpace( path ) ) throw new InvalidOperationException( "The model has no animation graph. Create its Citizen animation setup first." );
		var local = ProjectFile( path );
		var source = File.Exists( local ) ? local : AssetSystem.FindByPath( path )?.AbsolutePath;
		if ( source is null || !File.Exists( source ) ) throw new InvalidOperationException( "Editable animgraph source is unavailable: " + path );
		return File.ReadAllText( source );
	}

	/// <summary>
	/// The skeleton and clips AnimGraph2 <paramref name="graph"/> plays that have no source file. A graph only compiles
	/// where all of them have sources: the shipped Citizen's come compiled only, so its graph can be used as shipped but
	/// not compiled again from a project copy.
	/// </summary>
	internal static System.Collections.Generic.IReadOnlyList<string> MissingAnimGraph2Sources( string graph )
		=> AnimGraph2Document.Animations( graph ).Prepend( AnimGraph2Document.SkeletonOf( graph ) ).Where( path => !SourceExists( path ) ).ToArray();

	/// <summary>Why a project copy of a graph playing <paramref name="missing"/> (see <see cref="MissingAnimGraph2Sources"/>) can't compile.</summary>
	internal static string CompiledOnlyMessage( System.Collections.Generic.IReadOnlyList<string> missing )
		=> $"The Citizen's AnimGraph2 graph plays {missing[0]}{(missing.Count > 1 ? $" and {missing.Count - 1} more" : "")}, which come compiled only, "
			+ "without source files. AnimGraph2 can't compile an editable copy of the graph against those in this project.";

	static bool SourceExists( string path )
		=> File.Exists( ProjectFile( path ) ) || AssetSystem.FindByPath( path ) is { } asset && File.Exists( asset.GetSourceFile( true ) ?? "" );

	/// <summary>A graph or subgraph source by asset path: the project's own copy first, then the
	/// mounted (shipped) file. Null when neither exists.</summary>
	internal static string ReadGraphSource( string path )
	{
		var local = ProjectFile( path );
		var source = File.Exists( local ) ? local : AssetSystem.FindByPath( path )?.AbsolutePath;
		return source is not null && File.Exists( source ) ? File.ReadAllText( source ) : null;
	}

	internal static string ProjectFile( string relative )
	{
		var root = Path.GetFullPath( Project.Current?.GetAssetsPath() ?? throw new InvalidOperationException( "No project is open." ) )
			.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar;
		var path = Path.GetFullPath( Path.Combine( root, relative ) );
		if ( !path.StartsWith( root, StringComparison.OrdinalIgnoreCase ) || EditorPipeline.IsUnderEngineInstall( path ) )
			throw new InvalidOperationException( "Output must be inside your project's Assets folder, not the s&box installation." );
		return path;
	}
}
