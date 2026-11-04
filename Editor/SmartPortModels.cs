#nullable enable annotations

using System;
using System.Collections.Generic;
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
		if ( extend && AnimGraphVersions.Of( custom ) == AnimGraphVersion.AnimGraph2 )
		{
			// Extending follows the target's own graph system: an AnimGraph2 target gets its .ag extended
			if ( string.IsNullOrEmpty( AnimGraphVersions.GraphPathOf( custom ) ) ) return "The target's AnimGraph2 graph did not load.";
			if ( AnimGraph2SourceError( AnimGraphVersions.GraphPathOf( custom ) ) is { } missing ) return missing;
		}
		else if ( !extend && AnimGraph2Of( reference ) is { } sourceGraph )
		{
			// An AnimGraph2 source has its .ag ported, onto a skeleton and clips made for the target
			if ( AssetSystem.FindByPath( sourceGraph ) is not { } graphAsset || ReadSource( graphAsset ) is null )
				return $"The source's AnimGraph2 graph {sourceGraph} has no editable .ag source, only its compiled file, so Smart Port can't port it.";
		}
		else if ( graphOwner.AnimGraph is null || graphOwner.AnimGraph.IsError )
			return extend ? "The target needs a working animation graph to extend." : "The source has no working animation graph.";
		if ( TargetPickers.FromModelAsset( target, out var error ) is null ) return error;
		// An AnimGraph2 source's clips are retargeted even onto the same armature: the target gets its own copies
		if ( ArmatureError( custom, reference ) is null && (extend || AnimGraph2Of( reference ) is null) ) return null;
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
		var graph2 = extend ? AnimGraph2Of( Model.Load( target.Path ) ) : null;
		var sourceGraph2 = extend ? null : AnimGraph2Of( Model.Load( source.Path ) );
		var graphPath = dataFolder + "/graphs/" + name + (graph2 is null ? ".vanmgrph" : ".ag");
		var graph2Path = dataFolder + "/graphs/" + name + ".ag";
		if ( File.Exists( modelFile ) || File.Exists( modelFile + "_c" ) || Directory.Exists( dataRoot ) )
			throw new InvalidOperationException( "That Smart Port output already exists. Choose another name; existing files are never overwritten." );
		var sourceText = ReadSource( source );
		var targetText = ReadSource( target );
		var reference = Model.Load( source.Path );
		var custom = Model.Load( target.Path );
		var needsRetargeting = ArmatureError( custom, reference ) is not null || sourceGraph2 is not null;
		var sourceGraph2Text = sourceGraph2 is null ? null : ReadSource( AssetSystem.FindByPath( sourceGraph2 ) );
		// An AnimGraph2 source may have no AnimGraph1 graph; one it has is ported beside its .ag, as before
		var graphOwner = extend ? custom : reference;
		var graphAsset = AssetSystem.FindByPath( graph2 ?? graphOwner.AnimGraph?.Name ?? "" );
		if ( graphAsset is null && sourceGraph2 is null ) throw new InvalidOperationException( "Animgraph asset could not be resolved." );
		var graphText = graphAsset is null ? null : ReadSource( graphAsset );
		IReadOnlyList<string> graph2Sources = null;
		string[] graph2Uncompiled = null;
		if ( graphText is null && graphAsset is not null ) graphText = await SmartPortDecompiler.RecoverAsync( graphAsset, dataFolder + "/graph_source", token );
		var rig = !needsRetargeting ? null
			: sourceGraph2Text is not null ? SmartPortAnimGraph2Setup.Rig( reference, TargetPickers.SkeletonFromModel( custom ), sourceGraph2Text, source.GetCompiledFile( true ) )
			: new SmartPortRig( TargetPickers.SkeletonFromModel( reference ), TargetPickers.SkeletonFromModel( custom ),
				!extend ? SmartPortIkTargets.Read( source.GetCompiledFile( true ), graphText ) : null,
				!extend ? SmartPortAttachments.BoneNames( source.GetCompiledFile( true ) ) : null );
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
		if ( sourceGraph2 is not null && graphText is null ) vmdl = StockAnimationGraph.Attach( vmdl, "" );
		// Calibrate the merged list so newly copied sockets get the same correction as fitted ones.
		if ( rig is not null && !extend ) vmdl = SmartPortAttachments.Align( vmdl, sourceText, rig );
		var expectedAnimations = reference.AnimationNames.ToArray();
		if ( extend )
		{
			// An AnimGraph2 target keeps its AnimGraph1 graph, if it has one, beside the extended .ag
			vmdl = SmartPortExtension.MergeModel( targetText, vmdl, graph2 is null ? graphPath : custom.AnimGraph?.Name ?? "", out var names );
			var playable = clips.Where( c => !c.Hidden ).Select( c => c with { Name = names[c.Name] } ).ToArray();
			expectedAnimations = custom.AnimationNames.Concat( names.Values ).ToArray();
			if ( graph2 is not null )
			{
				vmdl = AnimGraph2Document.Attach( vmdl, graphPath );
				graphText = await ExtendAnimGraph2Async( graphText, vmdl, modelPath, dataFolder, playable, token );
			}
			else
			{
				graphText = SmartPortExtension.ExtendGraph( graphText, modelPath, playable, out var parameter );
				var guide = "Smart Port added clips\n\nThe original graph is the default (0). Select a clip with:\n"
					+ $"renderer.Set(\"{parameter}\", 1);\nReturn to the original graph with renderer.Set(\"{parameter}\", 0);\n"
					+ "Non-looping clips hold their final frame until you select 0. Set 0 before replaying the same clip.\n"
					+ "Additive clips layer over the original graph. Hidden/world-space clips are retained in the model for manual graph editing.\n\n"
					+ string.Join( "\n", playable.Select( (c, i) => $"{i + 1}: {c.Name} ({(c.Additive ? "additive, " : "")}{(c.Looping ? "loop" : "one-shot")})" ) );
				await File.WriteAllTextAsync( Path.Combine( dataRoot, "clips.txt" ), guide, token );
			}
		}
		else if ( rig is not null && graphText is not null ) graphText = SmartPortSetup.Rebase( graphText, rig.BoneNames );
		if ( sourceGraph2 is not null )
		{
			progress?.Invoke( "Porting the source's AnimGraph2 graph and clips…" );
			var ported = await SmartPortAnimGraph2Setup.WriteAsync( sourceGraph2Text, modelPath, vmdl, source.GetCompiledFile( true ), dataFolder, name, rig, token );
			graph2Sources = ported.Sources;
			// Hundreds of new clips start the editor's own content build, which refuses other compiles while it runs:
			// have the skeleton and clips compiled before the model and graphs
			progress?.Invoke( "Compiling the AnimGraph2 skeleton and clips…" );
			graph2Uncompiled = await SmartPortAnimGraph2Setup.UncompiledAsync( graph2Sources, EditorPipeline.MeshCompileTimeoutSeconds, token );
			var ported2File = CitizenAnimationModels.ProjectFile( graph2Path );
			Directory.CreateDirectory( Path.GetDirectoryName( ported2File ) );
			await File.WriteAllTextAsync( ported2File, ported.Graph, token );
			vmdl = AnimGraph2Document.Attach( vmdl, graph2Path );
		}
		if ( graphText is not null )
		{
			if ( graph2 is null ) graphText = StockAnimationGraph.CopyForModel( graphText, modelPath );
			var graphFile = CitizenAnimationModels.ProjectFile( graphPath );
			Directory.CreateDirectory( Path.GetDirectoryName( graphFile ) );
			await File.WriteAllTextAsync( graphFile, graphText, token );
		}
		token.ThrowIfCancellationRequested();
		progress?.Invoke( "Compiling and verifying the ported model and graph…" );
		var result = await EditorPipeline.WriteAndCompileAsync( new RetargetBatchResult { StandaloneVmdl = vmdl }, folder,
			standaloneVmdlName: name, compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true, additionalAssetPaths: Directory.GetFiles( dataRoot, "*", SearchOption.AllDirectories )
				.Where( file => graph2Sources is null || !(file.EndsWith( ".skel", StringComparison.OrdinalIgnoreCase ) || file.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase )) ).ToArray() );
		await EditorPipeline.SwitchToMainThread();
		if ( graphText is not null ) CitizenAnimationModels.VerifyGraph( result, graphPath );
		if ( sourceGraph2 is not null ) CitizenAnimationModels.VerifyGraph( result, graph2Path );
		// An AnimGraph2 graph compiles, and loads, with its skeleton or clips missing: check they compiled
		var uncompiled = graph2Uncompiled ?? Array.Empty<string>();
		if ( uncompiled.Length > 0 )
		{
			result.Compiled = false;
			result.Errors.Add( $"{uncompiled.Length} AnimGraph2 source(s) did not compile, first {uncompiled[0]} (see the console)" );
		}
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

	/// <summary>The AnimGraph2 graph that drives <paramref name="model"/>, or null when AnimGraph1 does.</summary>
	static string AnimGraph2Of( Model model )
		=> AnimGraphVersions.Of( model ) == AnimGraphVersion.AnimGraph2 ? AnimGraphVersions.GraphPathOf( model ) : null;

	// AnimGraph2 compiles a graph only against a skeleton and clips whose source files it can read: a graph on shipped
	// content that comes compiled only (the Human's human.ag) can be played but not extended
	static string AnimGraph2SourceError( string graphPath )
	{
		var graph = AssetSystem.FindByPath( graphPath ) is { } asset ? ReadSource( asset ) : null;
		if ( graph is null ) return $"The target's AnimGraph2 graph {graphPath} has no editable .ag source, only its compiled file, so Smart Port can't extend it.";
		var references = new List<string> { AnimGraph2Document.SkeletonOf( graph ) };
		AnimGraph2Document.RewriteAnimations( graph, animation => { references.Add( animation ); return null; }, out _ );
		var missing = references.Distinct( StringComparer.OrdinalIgnoreCase )
			.Where( path => AssetSystem.FindByPath( path ) is not { } file || !File.Exists( file.GetSourceFile( true ) ?? "" ) ).ToArray();
		return missing.Length == 0 ? null
			: $"The target's AnimGraph2 graph {graphPath} plays {missing[0]}{(missing.Length > 1 ? $" and {missing.Length - 1} more" : "")}, which "
				+ "come compiled only, without source files. AnimGraph2 can't compile a graph against those in this project, so Smart Port can't extend it.";
	}

	// AnimGraph2 plays clips (.anim), not model sequences: each ported clip gets one on the graph's skeleton, from the
	// file its recovered sequence plays. Then the graph selects them, documented like the AnimGraph1 port.
	static async Task<string> ExtendAnimGraph2Async( string graph, string vmdl, string modelPath, string dataFolder,
		SmartPortClip[] playable, CancellationToken token )
	{
		var skeleton = AnimGraph2Document.SkeletonOf( graph );
		var scale = SmartPortAnimGraph2.ClipImportScale( ReadSource( AssetSystem.FindByPath( skeleton ) ) );
		var files = SmartPortAnimGraph2.AnimationFiles( vmdl );
		var invalid = Path.GetInvalidFileNameChars();
		var animations = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var clip in playable )
		{
			if ( !files.TryGetValue( clip.Name, out var file ) ) throw new InvalidOperationException( "Recovery wrote no animation file for clip " + clip.Name );
			var path = dataFolder + "/clips/" + string.Concat( clip.Name.Select( c => invalid.Contains( c ) ? '_' : c ) ) + ".anim";
			var output = CitizenAnimationModels.ProjectFile( path );
			Directory.CreateDirectory( Path.GetDirectoryName( output ) );
			await File.WriteAllTextAsync( output, AnimGraph2Sources.Clip( file, skeleton, scale ), token );
			animations[clip.Name] = path;
		}
		var extended = SmartPortAnimGraph2.ExtendGraph( graph, modelPath, playable, animations, out var parameter );
		var guide = "Smart Port added clips (AnimGraph2)\n\nThe original graph is the default (0, \"" + SmartPortExtension.OriginalOption + "\"). "
			+ "renderer.Set doesn't reach AnimGraph2 parameters: select a clip with the library's bridge, HumanoidRetargeter.Engine.AnimGraphParameters:\n"
			+ $"AnimGraphParameters.Set(renderer, \"{parameter}\", 1);\nor by its name: AnimGraphParameters.Set(renderer, \"{parameter}\", \"{playable[0].Name}\");\n"
			+ $"Return to the original graph with AnimGraphParameters.Set(renderer, \"{parameter}\", 0);\n"
			+ "Non-looping clips hold their final frame until you select 0. Set 0 before replaying the same clip.\n"
			+ "Additive clips layer over the original graph. Hidden/world-space clips are retained in the model for manual graph editing.\n\n"
			+ string.Join( "\n", playable.Select( (c, i) => $"{i + 1}: {c.Name} ({(c.Additive ? "additive, " : "")}{(c.Looping ? "loop" : "one-shot")})" ) );
		await File.WriteAllTextAsync( CitizenAnimationModels.ProjectFile( dataFolder + "/clips.txt" ), guide, token );
		return extended;
	}

	static string ReadSource( Asset asset )
	{
		var path = asset.GetSourceFile( true );
		return !string.IsNullOrEmpty( path ) && File.Exists( path ) ? File.ReadAllText( path ) : null;
	}
}
