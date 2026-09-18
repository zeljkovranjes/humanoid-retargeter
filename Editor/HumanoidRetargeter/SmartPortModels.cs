#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Target;
using Sandbox;

namespace HumanoidRetargeter.Editor;

internal static class SmartPortModels
{
	internal static string Check( Asset source, Asset target )
		=> CheckCore( source, target, false );

	internal static string CheckExtended( Asset source, Asset target )
		=> CheckCore( source, target, true );

	static string CheckCore( Asset source, Asset target, bool extend )
	{
		if ( source is null || target is null ) return "Choose an animation source and a target character.";
		var reference = Model.Load( source.Path );
		var custom = Model.Load( target.Path );
		if ( reference is null || reference.IsError || custom is null || custom.IsError ) return "Both models must load successfully.";
		if ( reference.AnimationCount == 0 ) return "The source has no animations.";
		var graphOwner = extend ? custom : reference;
		if ( graphOwner.AnimGraph is null || graphOwner.AnimGraph.IsError )
			return extend ? "The target needs a working animation graph to extend." : "The source has no working animation graph.";
		if ( TargetPickers.FromModelAsset( target, out var error ) is null ) return error;
		if ( ArmatureError( custom, reference ) is null ) return null;
		try { _ = new SmartPortRig( TargetPickers.SkeletonFromModel( reference ), TargetPickers.SkeletonFromModel( custom ) ); }
		catch ( ArgumentException e ) { return e.Message; }
		return null;
	}

	static string ArmatureError( Model target, Model source )
	{
		var error = SmartPortSetup.CompatibilityError( TargetPickers.SkeletonFromModel( target ), TargetPickers.SkeletonFromModel( source ) );
		if ( error is not null ) return error;
		// XForm intentionally has no scale; compare it on the authoritative engine bones.
		var bones = target.Bones.AllBones.ToDictionary( b => b.Name, StringComparer.Ordinal );
		foreach ( var bone in source.Bones.AllBones )
		{
			if ( !bones.TryGetValue( bone.Name, out var other ) ) return "Target is missing source bone: " + bone.Name;
			var difference = bone.LocalTransform.Scale.Distance( other.LocalTransform.Scale );
			if ( !float.IsFinite( difference ) || difference > .0001f ) return "Bone has a different bind scale: " + bone.Name;
		}
		return null;
	}

	internal static Task<EditorPipeline.WriteResult> CreateAsync( Asset source, Asset target, string folder, string name,
		Action<string> progress = null, CancellationToken token = default )
		=> CreateCoreAsync( source, target, folder, name, progress, token, false );

	internal static Task<EditorPipeline.WriteResult> CreateExtendedAsync( Asset source, Asset target, string folder, string name,
		Action<string> progress = null, CancellationToken token = default )
		=> CreateCoreAsync( source, target, folder, name, progress, token, true );

	static async Task<EditorPipeline.WriteResult> CreateCoreAsync( Asset source, Asset target, string folder, string name,
		Action<string> progress, CancellationToken token, bool extend )
	{
		await EditorPipeline.SwitchToMainThread();
		var error = CheckCore( source, target, extend );
		if ( error is not null ) throw new InvalidOperationException( error );
		if ( string.IsNullOrWhiteSpace( name ) || name.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 || name.Contains( '/' ) || name.Contains( '\\' ) || name is "." or ".." )
			throw new InvalidOperationException( "Use a plain filename for the new model." );
		folder = folder.Trim().Replace( '\\', '/' ).TrimEnd( '/' );
		name = name.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) ? name[..^5] : name;
		if ( string.IsNullOrWhiteSpace( folder ) || string.IsNullOrWhiteSpace( name ) ) throw new InvalidOperationException( "Specify an output folder and model name." );
		var modelPath = folder + "/" + name + ".vmdl";
		var modelFile = CitizenAnimationModels.ProjectFile( modelPath );
		var dataFolder = folder + "/" + name + "_smart_port";
		var dataRoot = CitizenAnimationModels.ProjectFile( dataFolder );
		var graphPath = dataFolder + "/graphs/" + name + ".vanmgrph";
		if ( File.Exists( modelFile ) || File.Exists( modelFile + "_c" ) || Directory.Exists( dataRoot ) )
			throw new InvalidOperationException( "That Smart Port output already exists. Choose another name; existing files are never overwritten." );
		var sourceText = ReadSource( source );
		var targetText = ReadSource( target );
		var reference = Model.Load( source.Path );
		var custom = Model.Load( target.Path );
		var needsRetargeting = ArmatureError( custom, reference ) is not null;
		var graphAsset = AssetSystem.FindByPath( (extend ? custom : reference).AnimGraph.Name );
		if ( graphAsset is null ) throw new InvalidOperationException( "Animgraph asset could not be resolved." );
		var graphText = ReadSource( graphAsset );
		if ( graphText is null ) graphText = await SmartPortDecompiler.RecoverAsync( graphAsset, dataFolder + "/graph_source", token );
		var rig = needsRetargeting ? new SmartPortRig( TargetPickers.SkeletonFromModel( reference ), TargetPickers.SkeletonFromModel( custom ),
			!extend ? SmartPortIkTargets.Read( source.GetCompiledFile( true ), graphText ) : null ) : null;
		SmartPortClip[] clips = null;
		if ( extend )
		{
			var compiled = source.GetCompiledFile( true );
			var probeRig = rig ?? new SmartPortRig( TargetPickers.SkeletonFromModel( reference ), TargetPickers.SkeletonFromModel( custom ) );
			clips = await Task.Run( () => SmartPortAnimationExport.ReadClips( compiled, probeRig ), token );
		}
		var recoverModels = extend || rig is not null || sourceText is null || targetText is null;
		if ( !recoverModels )
		{
			// Different source units/modifiers need compiled-space recovery, even when the
			// resulting compiled skeletons match. Never double-scale the imported clips.
			try { _ = SmartPortSetup.Apply( targetText, sourceText, graphPath ); }
			catch ( InvalidOperationException ) { recoverModels = true; }
		}
		token.ThrowIfCancellationRequested();
		Directory.CreateDirectory( dataRoot );
		if ( recoverModels )
		{
			progress?.Invoke( "Recovering source ModelDoc and animations…" );
			sourceText = await SmartPortDecompiler.RecoverAsync( source, dataFolder + "/source", token, rig );
			progress?.Invoke( "Recovering the target mesh and armature…" );
			targetText = await SmartPortDecompiler.RecoverAsync( target, dataFolder + "/target", token );
		}
		var vmdl = rig is null ? SmartPortSetup.Apply( targetText, sourceText, graphPath )
			: SmartPortSetup.ApplyRetargeted( targetText, sourceText, graphPath, rig );
		// Calibrate the merged list so newly copied sockets get the same correction as fitted ones.
		if ( rig is not null && !extend ) vmdl = SmartPortAttachments.Align( vmdl, sourceText, rig );
		var expectedAnimations = reference.AnimationNames.ToArray();
		if ( extend )
		{
			vmdl = SmartPortExtension.MergeModel( targetText, vmdl, graphPath, out var names );
			var playable = clips.Where( c => !c.Hidden ).Select( c => c with { Name = names[c.Name] } ).ToArray();
			graphText = SmartPortExtension.ExtendGraph( graphText, modelPath, playable, out var parameter );
			expectedAnimations = custom.AnimationNames.Concat( names.Values ).ToArray();
			var guide = "Smart Port added clips\n\nThe original graph is the default (0). Select a clip with:\n"
				+ $"renderer.Set(\"{parameter}\", 1);\nReturn to the original graph with renderer.Set(\"{parameter}\", 0);\n"
				+ "Non-looping clips hold their final frame until you select 0. Set 0 before replaying the same clip.\n"
				+ "Additive clips layer over the original graph. Hidden/world-space clips are retained in the model for manual graph editing.\n\n"
				+ string.Join( "\n", playable.Select( (c, i) => $"{i + 1}: {c.Name} ({(c.Additive ? "additive, " : "")}{(c.Looping ? "loop" : "one-shot")})" ) );
			await File.WriteAllTextAsync( Path.Combine( dataRoot, "clips.txt" ), guide, token );
		}
		else if ( rig is not null ) graphText = SmartPortSetup.Rebase( graphText, rig.BoneNames );
		graphText = StockAnimationGraph.CopyForModel( graphText, modelPath );
		var graphFile = CitizenAnimationModels.ProjectFile( graphPath );
		Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
		await File.WriteAllTextAsync( graphFile, graphText, token );
		token.ThrowIfCancellationRequested();
		progress?.Invoke( "Compiling and verifying the ported model and graph…" );
		var result = await EditorPipeline.WriteAndCompileAsync( new RetargetBatchResult { StandaloneVmdl = vmdl }, folder,
			standaloneVmdlName: name, compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true, additionalAssetPaths: Directory.GetFiles( dataRoot, "*", SearchOption.AllDirectories ) );
		await EditorPipeline.SwitchToMainThread();
		CitizenAnimationModels.VerifyGraph( result, graphPath );
		if ( result.Compiled )
		{
			var output = Model.Load( result.VmdlAsset.Path );
			var missing = expectedAnimations.Except( output.AnimationNames ).ToArray();
			if ( missing.Length > 0 ) result.Errors.Add( "Recovery omitted sequences: " + string.Join( ", ", missing ) );
			var changed = ArmatureError( output, Model.Load( target.Path ) );
			if ( changed is not null ) result.Errors.Add( "Compiled target armature changed: " + changed );
			result.Compiled = result.Errors.Count == 0;
		}
		return result;
	}

	static string ReadSource( Asset asset )
	{
		var path = asset.GetSourceFile( true );
		return !string.IsNullOrEmpty( path ) && File.Exists( path ) ? File.ReadAllText( path ) : null;
	}
}
