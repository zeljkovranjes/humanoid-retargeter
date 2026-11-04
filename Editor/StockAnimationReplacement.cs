#nullable enable annotations
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.Core.Skeleton;
using HumanoidRetargeter.Engine;

namespace HumanoidRetargeter.EditorTools;

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
		// The model's AnimGraph2 graph drives it whenever it names one (on engines that have AnimGraph2)
		var animGraph2 = AnimGraphVersions.IsAnimGraph2Available && !string.IsNullOrEmpty( AnimGraph2Document.GraphName( vmdl ) );
		var graphPath = animGraph2 ? AnimGraph2Document.GraphPath( folder, name ) : StockAnimationGraph.GraphPath( folder, name );
		var graphFile = CitizenAnimationModels.ProjectFile( graphPath );
		var previousGraph = File.Exists( graphFile ) ? File.ReadAllText( graphFile ) : null;
		var currentGraph = animGraph2 ? AnimGraph2Document.GraphName( vmdl ) : StockAnimationGraph.GraphName( vmdl );
		if ( previousGraph is not null && !string.Equals( currentGraph.Replace( '\\', '/' ), graphPath, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "An unrelated graph already exists at " + graphPath + ". Choose another model name to preserve it." );
		var graph = CitizenAnimationModels.ReadGraph( vmdl, animGraph2 );
		if ( animGraph2 && CitizenAnimationModels.MissingAnimGraph2Sources( graph ) is { Count: > 0 } missing )
			throw new InvalidOperationException( CitizenAnimationModels.CompiledOnlyMessage( missing ) + " Stock animations can only be replaced in a graph that compiles in the project." );
		var spec = TargetSpec( referencePath, resolved.PreviewModelPath );
		var batch = await Task.Run( () => Retargeter.ConvertBatch( new[] { request }, spec,
			new BatchOptions { DmxFolderRelative = folder } ) );
		if ( batch.Clips.Count != 1 || !batch.Clips[0].Success || batch.Errors.Count > 0 )
			throw new InvalidOperationException( string.Join( "\n", batch.Errors.Concat( batch.Clips.Where( c => !c.Success ).Select( c => c.Error ) ) ) );
		var clip = batch.Clips[0];
		var clipSource = string.IsNullOrEmpty( folder ) ? clip.DmxFileName : folder + "/" + clip.DmxFileName;
		// AnimGraph2 plays the clip as an .anim on the graph's own skeleton; AnimGraph1 as the model's sequence
		var animPath = Path.ChangeExtension( clipSource, ".anim" );
		var replacement = animGraph2
			? StockAnimationGraph.ReplaceAnimGraph2( graph, slot, animPath, modelPath )
			: StockAnimationGraph.Replace( graph, slot, clip.ClipName, modelPath,
				CitizenAnimationModels.ReadGraphSource, StockAnimationGraph.SubgraphFolder( folder, name ) );
		var anim = animGraph2 ? AnimGraph2Sources.Clip( clipSource, AnimGraph2Document.SkeletonOf( graph ), StockClipScale( graph ) ) : null;
		graph = replacement.Graph;
		vmdl = VmdlAugmenter.Augment( vmdl, new[] { new AnimEntry
		{
			Name = clip.ClipName, SourceFilename = clipSource,
			Looping = clip.Looping, Events = clip.FootstepEvents,
		} }, out _, new AugmentOptions { DefaultRootBone = spec.DefaultRootBone } );
		vmdl = animGraph2 ? AnimGraph2Document.Attach( vmdl, graphPath ) : StockAnimationGraph.Attach( vmdl, graphPath );
		batch.StandaloneVmdl = vmdl;
		batch.AugmentedVmdl = vmdl;
		await EditorPipeline.SwitchToMainThread();
		// Catch edits made while conversion was running, before touching either source.
		if ( (File.Exists( modelFile ) ? File.ReadAllText( modelFile ) : null) != original
			|| (File.Exists( graphFile ) ? File.ReadAllText( graphFile ) : null) != previousGraph )
			throw new InvalidOperationException( "The output changed during conversion. Retry to preserve those edits." );
		Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
		if ( previousGraph is not null ) File.Copy( graphFile, graphFile + ".bak", overwrite: true );
		// Subgraph copies (or the AnimGraph2 clip) first: the graph only compiles against the subgraphs it now names.
		var subgraphFiles = replacement.Subgraphs.ToDictionary( s => CitizenAnimationModels.ProjectFile( s.Key ), s => s.Value );
		if ( anim is not null )
			subgraphFiles[CitizenAnimationModels.ProjectFile( animPath )] = anim;
		var previousSubgraphs = subgraphFiles.Keys.ToDictionary( f => f, f => File.Exists( f ) ? File.ReadAllText( f ) : null );
		foreach ( var (file, text) in subgraphFiles )
		{
			Directory.CreateDirectory( Path.GetDirectoryName( file ) );
			File.WriteAllText( file, text );
		}
		File.WriteAllText( graphFile, graph );
		var compiled = false;
		try
		{
			var result = await EditorPipeline.WriteAndCompileAsync( batch, folder,
				augmentVmdlPath: original is null ? null : modelFile, standaloneVmdlName: name,
				additionalAssetPaths: subgraphFiles.Keys.Append( graphFile ).ToArray() );
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
				foreach ( var (file, text) in previousSubgraphs )
					if ( text is not null ) File.WriteAllText( file, text );
			}
		}
	}

	// The retargeted DMX is in the stock source units (centimetres, as ValidateSourceScale requires): scaled to the
	// graph's skeleton, a shipped one or one Create Citizen animation model ported in engine units
	static double StockClipScale( string graph )
		=> SmartPortAnimGraph2.CentimetreClipScale( CitizenAnimationModels.ReadGraphSource( AnimGraph2Document.SkeletonOf( graph ) )
			?? throw new InvalidOperationException( "The graph's skeleton has no source: " + AnimGraph2Document.SkeletonOf( graph ) ) );

	internal static RetargetTargetSpec TargetSpec( string referencePath, string modelPath )
	{
		var spec = referencePath == RetargetTargetSpec.SboxCitizenPath
			? EditorPipeline.LoadSboxCitizenTarget() : EditorPipeline.LoadSboxDefaultTarget();
		var engine = TargetPickers.SkeletonFromModel( Sandbox.Model.Load( modelPath ) );
		var sourceFile = AssetSystem.FindByPath( modelPath )?.AbsolutePath;
		var offset = sourceFile is not null && File.Exists( sourceFile ) ? ModelGrounding.Offset( File.ReadAllText( sourceFile ) ) : 0;
		var skeleton = HumanoidRetargeter.Core.Skeleton.Skeleton.Create( engine.Bones.Select( bone => new BoneDefinition(
			bone.Name, bone.ParentIndex < 0 ? null : engine[bone.ParentIndex].Name,
			CompiledRigSourceSpace.FromEngineLocal( ModelGrounding.SourceLocal( bone.RestLocal, bone.ParentIndex < 0, offset ),
				bone.ParentIndex < 0, TargetUpAxis.YUpCm ) ) ).ToArray() );
		// Use the compiled destination's bind/facing, but keep the stock classification:
		// its graph, IK and CopyPinky constraints still own the same helper channels.
		spec.Rig = spec.Rig.WithBindPose( skeleton );
		spec.CompensateDmxRootYaw = true; // standalone mesh may be inside a VMDL prefab
		return spec;
	}
}
