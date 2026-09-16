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
		TargetPickers.ResolvedTarget target, string outputFolder, string outputName )
	{
		await EditorPipeline.SwitchToMainThread();
		if ( !TryDetect( target, out var referencePath, out var reason ) )
			throw new InvalidOperationException( reason );
		var destination = Path.Combine( Project.Current.GetAssetsPath(), outputFolder, outputName + ".vmdl" );
		if ( File.Exists( destination ) )
			throw new InvalidOperationException( "That output VMDL already exists. Choose a new name; existing models are not overwritten by this action." );
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
		else
			custom = File.ReadAllText( target.CustomVmdlPath );
		var shipped = File.ReadAllText( AssetSystem.FindByPath( referencePath ).AbsolutePath );
		var batch = new RetargetBatchResult { StandaloneVmdl = CitizenAnimationSetup.Apply( custom, shipped ) };
		return await EditorPipeline.WriteAndCompileAsync( batch, outputFolder,
			standaloneVmdlName: outputName, compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true );
	}
}
