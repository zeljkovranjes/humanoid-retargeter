#nullable enable annotations

using System;
using System.IO;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Target;
using Sandbox;

namespace HumanoidRetargeter.Editor;

/// <summary>Editor asset lookup and compilation for the opt-in Citizen animation setup.</summary>
internal static class CitizenAnimationModels
{
	internal static bool TryDetect( TargetPickers.ResolvedTarget target, out string referencePath, out string reason )
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
			foreach ( var path in new[] { RetargetTargetSpec.SboxHumanMalePath, RetargetTargetSpec.SboxCitizenPath } )
			{
				var reference = Model.Load( path );
				if ( reference is null || reference.IsError ) continue;
				var error = CitizenAnimationSetup.CompatibilityError( skeleton, TargetPickers.SkeletonFromModel( reference ) );
				if ( error is not null ) { errors.Add( $"{Path.GetFileNameWithoutExtension( path )}: {error}" ); continue; }
				var asset = AssetSystem.FindByPath( path );
				if ( asset is null || !File.Exists( asset.AbsolutePath ) ) continue;
				referencePath = path;
				reason = path == RetargetTargetSpec.SboxCitizenPath ? "Classic Citizen armature detected." : "Human Citizen armature detected.";
				return true;
			}
			reason = errors.Count > 0 ? string.Join( "\n", errors ) : "The shipped Citizen model sources are unavailable.";
		}
		catch ( Exception e ) { reason = e.Message; }
		return false;
	}

	internal static async Task<EditorPipeline.WriteResult> CreateAsync(
		TargetPickers.ResolvedTarget target, string outputFolder, string outputName, bool copyAnimGraph = false )
	{
		await EditorPipeline.SwitchToMainThread();
		if ( !TryDetect( target, out var referencePath, out var reason ) )
			throw new InvalidOperationException( reason );
		var destination = ProjectFile( outputFolder + "/" + outputName + ".vmdl" );
		if ( File.Exists( destination ) )
			throw new InvalidOperationException( "That output VMDL already exists. Choose a new name; existing models are not overwritten by this action." );
		var vmdl = BuildModelText( target, referencePath, outputFolder );
		string graphFile = null;
		if ( copyAnimGraph )
		{
			var graphPath = StockAnimationGraph.GraphPath( outputFolder, outputName );
			graphFile = ProjectFile( graphPath );
			if ( File.Exists( graphFile ) )
				throw new InvalidOperationException( "The copied animgraph already exists. Choose a new output name to preserve your edits." );
			var graph = StockAnimationGraph.CopyForModel( ReadGraph( vmdl ), outputFolder + "/" + outputName + ".vmdl" );
			Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
			File.WriteAllText( graphFile, graph );
			vmdl = StockAnimationGraph.Attach( vmdl, graphPath );
		}
		var batch = new RetargetBatchResult { StandaloneVmdl = vmdl };
		var result = await EditorPipeline.WriteAndCompileAsync( batch, outputFolder,
			standaloneVmdlName: outputName, compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true, additionalAssetPaths: graphFile is null ? null : new[] { graphFile } );
		await EditorPipeline.SwitchToMainThread();
		VerifyGraph( result, StockAnimationGraph.GraphName( vmdl ) );
		return result;
	}

	internal static void VerifyGraph( EditorPipeline.WriteResult result, string expectedPath )
	{
		if ( !result.Compiled ) return;
		var graph = Model.Load( result.VmdlAsset.Path )?.AnimGraph;
		if ( graph is not null && !graph.IsError && string.Equals( graph.Name, expectedPath, StringComparison.OrdinalIgnoreCase ) ) return;
		result.Compiled = false;
		result.Errors.Add( "The model compiled, but its animation graph did not load correctly: " + expectedPath );
	}

	internal static string BuildModelText( TargetPickers.ResolvedTarget target, string referencePath, string outputFolder )
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
		return CitizenAnimationSetup.Apply( custom, shipped );
	}

	internal static string ReadGraph( string vmdl )
	{
		var path = StockAnimationGraph.GraphName( vmdl );
		if ( string.IsNullOrWhiteSpace( path ) ) throw new InvalidOperationException( "The model has no animation graph. Create its Citizen animation setup first." );
		var local = ProjectFile( path );
		var source = File.Exists( local ) ? local : AssetSystem.FindByPath( path )?.AbsolutePath;
		if ( source is null || !File.Exists( source ) ) throw new InvalidOperationException( "Editable animgraph source is unavailable: " + path );
		return File.ReadAllText( source );
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
