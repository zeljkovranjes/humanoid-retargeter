#nullable enable annotations
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Sandbox;

namespace HumanoidRetargeter.Editor;

internal static class StockAnimationReplacementGate
{
	internal static async Task RunAsync( TargetPickers.ResolvedTarget target, string folder, string name, Model stock )
	{
		var copiedName = name + "_editable";
		var result = await CitizenAnimationModels.CreateAsync( target, folder, copiedName, copyAnimGraph: true );
		await EditorPipeline.SwitchToMainThread();
		if ( !result.Compiled ) throw new Exception( string.Join( "; ", result.Errors ) );
		var graphPath = StockAnimationGraph.GraphPath( folder, copiedName );
		var graphFile = CitizenAnimationModels.ProjectFile( graphPath );
		var model = Model.Load( result.VmdlAsset.Path );
		if ( model.AnimGraph is null || model.AnimGraph.IsError || model.AnimGraph.Name != graphPath )
			throw new Exception( "Editable graph copy was not compiled and attached: " + model.AnimGraph?.Name );
		if ( stock.AnimationNames.Except( model.AnimationNames ).Any() ) throw new Exception( "Graph copy lost stock animations." );
		var stockSource = File.ReadAllText( global::Editor.AssetSystem.FindByPath( stock.Name ).AbsolutePath );
		var copiedSource = File.ReadAllText( result.VmdlPath );
		foreach ( var category in new[] { "AnimationList", "BoneMarkupList", "AttachmentList", "IKData", "PoseParamList", "WeightListList", "GameDataList" } )
		{
			KvValue Find( string source ) => ((KvArray)((KvObject)((KvObject)Kv3.Parse( source ).Root)["rootNode"])["children"])
				.Items.OfType<KvObject>().SingleOrDefault( n => n.GetString( "_class" ) == category );
			var expected = Find( stockSource );
			if ( expected is not null && !KvValue.DeepEquals( expected, Find( copiedSource ) ) )
				throw new Exception( "Stock animation setup metadata changed: " + category );
		}
		var entry = SourceFileEntry.Load( Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_STEPS" ), Project.Current.GetAssetsPath() );
		if ( entry.Scene is null ) throw new Exception( entry.StatusDetail );
		foreach ( var slotId in new[] { "walk_n", "walk_s" } )
		{
			var slot = StockAnimationGraph.Slots.Single( s => s.Id == slotId );
			var request = new RetargetRequest
			{
				SourceData = entry.Bytes, SourceFileName = entry.FileName, TakeIndex = 0,
				MappingOverride = entry.Mapping, RootMotion = RootMotionMode.InPlace,
				LoopingOverride = true, ClipNameOverride = slot.ReplacementPrefix + "gate", GenerateFootstepEvents = true,
			};
			result = await StockAnimationReplacement.ReplaceAsync( target, request, slot, folder + "/" + copiedName + ".vmdl" );
			await EditorPipeline.SwitchToMainThread();
			if ( !result.Compiled ) throw new Exception( string.Join( "; ", result.Errors ) );
			model = Model.Load( result.VmdlAsset.Path );
			if ( !model.AnimationNames.Contains( request.ClipNameOverride ) || model.AnimGraph is null || model.AnimGraph.IsError )
				throw new Exception( "Replacement sequence or graph failed to compile." );
			// Catch the embedded-mesh compiler's root-yaw conversion, which a finite-pose
			// or sequence-name check cannot detect.
			var spec = StockAnimationReplacement.TargetSpec( stock.Name, result.VmdlAsset.Path );
			var solved = Retargeter.Convert( request, spec ).Clips.Single();
			var frame = solved.SolvedFrames.Count / 2;
			var expectedWorld = new Pose( solved.SolvedFrames[frame] ).ToWorld( spec.Rig.Skeleton );
			var left = spec.Rig.BoneForRole( BoneRole.UpperLegL ).Value;
			var right = spec.Rig.BoneForRole( BoneRole.UpperLegR ).Value;
			var hipLine = expectedWorld[left].Pos - expectedWorld[right].Pos;
			var expectedLine = new Vector3( hipLine.X, -hipLine.Z, hipLine.Y ).Normal;
			var probeWorld = new SceneWorld();
			var probe = new SceneModel( probeWorld, model, Transform.Zero ) { UseAnimGraph = false };
			try
			{
				probe.CurrentSequence.Name = request.ClipNameOverride;
				probe.CurrentSequence.Time = frame / solved.Fps;
				probe.Update( 0.001f );
				var actualLine = (probe.GetBoneWorldTransform( spec.Rig.Skeleton[left].Name ).Position
					- probe.GetBoneWorldTransform( spec.Rig.Skeleton[right].Name ).Position).Normal;
				if ( Vector3.Dot( expectedLine, actualLine ) < 0.99f )
					throw new Exception( $"Replacement compiled facing differs from preview: expected {expectedLine}, actual {actualLine}." );
			}
			finally { probe.Delete(); probeWorld.Delete(); }
		}
		var text = File.ReadAllText( graphFile );
		if ( !text.Contains( "hr_replace_walk_n_gate" ) || !text.Contains( "hr_replace_walk_s_gate" ) )
			throw new Exception( "Replacing a second slot lost the first replacement." );
		if ( !File.Exists( graphFile + ".bak" ) ) throw new Exception( "Editable graph backup is missing." );
		var world = new SceneWorld();
		var scene = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = true };
		try
		{
			scene.SetAnimParameter( "b_grounded", true );
			scene.SetAnimParameter( "move_x", 100f );
			scene.SetAnimParameter( "move_speed", 100f );
			scene.SetAnimParameter( "move_groundspeed", 100f );
			scene.SetAnimParameter( "wish_x", 100f );
			scene.SetAnimParameter( "wish_speed", 100f );
			for ( var i = 0; i < 60; i++ ) scene.Update( 1f / 30 );
			foreach ( var bone in model.Bones.AllBones )
			{
				var p = scene.GetBoneWorldTransform( bone.Index ).Position;
				if ( !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) || p.Length > 200 )
					throw new Exception( "Invalid replacement graph pose: " + bone.Name );
			}
		}
		finally { scene.Delete(); world.Delete(); }
		Log.Info( $"[hr-ui-smoke] Editable graph and two stock replacements {name}: complete." );
	}
}
