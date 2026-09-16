#nullable enable annotations
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Target;
using HumanoidRetargeter.Skeleton;

namespace HumanoidRetargeter.Editor;

/// <summary>Writes only project-owned assets; shipped sequences and graphs remain untouched.</summary>
internal static class StockAnimationReplacement
{
	internal static async Task<EditorPipeline.WriteResult> ReplaceAsync(
		TargetPickers.ResolvedTarget target, RetargetRequest request, StockAnimationSlot slot, string modelPath )
	{
		await EditorPipeline.SwitchToMainThread();
		var modelFile = CitizenAnimationModels.ProjectFile( modelPath );
		var folder = Path.GetDirectoryName( modelPath ).Replace( '\\', '/' );
		var name = Path.GetFileNameWithoutExtension( modelPath );
		var original = File.Exists( modelFile ) ? File.ReadAllText( modelFile ) : null;
		var resolved = original is not null
			? TargetPickers.FromModelAsset( AssetSystem.FindByPath( modelPath ), out _ ) : target;
		if ( resolved is null ) throw new InvalidOperationException( "Compile the destination model before replacing its animations." );
		// Built-in targets use the same compiled-armature check as custom models.
		if ( resolved.ModelFilePath is null && resolved.CustomVmdlPath is null )
			resolved = TargetPickers.FromModelAsset( AssetSystem.FindByPath( resolved.PreviewModelPath ), out _ );
		if ( !CitizenAnimationModels.TryDetect( resolved, out var referencePath, out var reason ) )
			throw new InvalidOperationException( reason );
		if ( original is null && CitizenAnimationModels.RequiresRetargeting( resolved, referencePath ) )
			throw new InvalidOperationException( "Create the Citizen animation model first so its complete stock library is retargeted to this fitted armature." );
		var vmdl = original ?? CitizenAnimationModels.BuildModelText( target, referencePath, folder );
		CitizenAnimationSetup.ValidateSourceScale( vmdl );
		var graphPath = StockAnimationGraph.GraphPath( folder, name );
		var graphFile = CitizenAnimationModels.ProjectFile( graphPath );
		var previousGraph = File.Exists( graphFile ) ? File.ReadAllText( graphFile ) : null;
		if ( previousGraph is not null && !string.Equals( StockAnimationGraph.GraphName( vmdl ).Replace( '\\', '/' ), graphPath, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "An unrelated graph already exists at " + graphPath + ". Choose another model name to preserve it." );
		var graph = CitizenAnimationModels.ReadGraph( vmdl );
		var spec = TargetSpec( referencePath, resolved.PreviewModelPath );
		var batch = await Task.Run( () => Retargeter.ConvertBatch( new[] { request }, spec,
			new BatchOptions { DmxFolderRelative = folder } ) );
		if ( batch.Clips.Count != 1 || !batch.Clips[0].Success || batch.Errors.Count > 0 )
			throw new InvalidOperationException( string.Join( "\n", batch.Errors.Concat( batch.Clips.Where( c => !c.Success ).Select( c => c.Error ) ) ) );
		var clip = batch.Clips[0];
		graph = StockAnimationGraph.Replace( graph, slot, clip.ClipName, modelPath, out _ );
		vmdl = VmdlAugmenter.Augment( vmdl, new[] { new AnimEntry
		{
			Name = clip.ClipName, SourceFilename = string.IsNullOrEmpty( folder ) ? clip.DmxFileName : folder + "/" + clip.DmxFileName,
			Looping = clip.Looping, Events = clip.FootstepEvents,
		} }, out _, new AugmentOptions { DefaultRootBone = spec.DefaultRootBone } );
		vmdl = StockAnimationGraph.Attach( vmdl, graphPath );
		batch.StandaloneVmdl = vmdl;
		batch.AugmentedVmdl = vmdl;
		await EditorPipeline.SwitchToMainThread();
		// Catch edits made while conversion was running, before touching either source.
		if ( (File.Exists( modelFile ) ? File.ReadAllText( modelFile ) : null) != original
			|| (File.Exists( graphFile ) ? File.ReadAllText( graphFile ) : null) != previousGraph )
			throw new InvalidOperationException( "The output changed during conversion. Retry to preserve those edits." );
		Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
		if ( previousGraph is not null ) File.Copy( graphFile, graphFile + ".bak", overwrite: true );
		File.WriteAllText( graphFile, graph );
		var compiled = false;
		try
		{
			var result = await EditorPipeline.WriteAndCompileAsync( batch, folder,
				augmentVmdlPath: original is null ? null : modelFile, standaloneVmdlName: name,
				additionalAssetPaths: new[] { graphFile } );
			await EditorPipeline.SwitchToMainThread();
			CitizenAnimationModels.VerifyGraph( result, graphPath );
			compiled = result.Compiled;
			return result;
		}
		finally
		{
			if ( !compiled )
			{
				// Keep diagnostic new files, but restore pre-existing user sources on failure.
				if ( original is not null ) File.WriteAllText( modelFile, original );
				if ( previousGraph is not null ) File.WriteAllText( graphFile, previousGraph );
			}
		}
	}

	internal static RetargetTargetSpec TargetSpec( string referencePath, string modelPath )
	{
		var spec = referencePath == RetargetTargetSpec.SboxCitizenPath
			? EditorPipeline.LoadSboxCitizenTarget() : EditorPipeline.LoadSboxDefaultTarget();
		var engine = TargetPickers.SkeletonFromModel( Sandbox.Model.Load( modelPath ) );
		var skeleton = HumanoidRetargeter.Skeleton.Skeleton.Create( engine.Bones.Select( bone => new BoneDefinition(
			bone.Name, bone.ParentIndex < 0 ? null : engine[bone.ParentIndex].Name,
			CompiledRigSourceSpace.FromEngineLocal( bone.RestLocal, bone.ParentIndex < 0, TargetUpAxis.YUpCm ) ) ).ToArray() );
		// Use the compiled destination's bind/facing, but keep the stock classification:
		// its graph, IK and CopyPinky constraints still own the same helper channels.
		spec.Rig = spec.Rig.WithBindPose( skeleton );
		spec.CompensateDmxRootYaw = true; // standalone mesh may be inside a VMDL prefab
		return spec;
	}
}
