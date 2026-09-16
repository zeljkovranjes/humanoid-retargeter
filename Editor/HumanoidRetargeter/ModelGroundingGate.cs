#nullable enable annotations
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Target;
using Sandbox;

namespace HumanoidRetargeter.Editor;

/// <summary>Additional regression: compile the same fitted library with a final grounding modifier.</summary>
internal static class ModelGroundingGate
{
	internal static async Task RunAsync( EditorPipeline.WriteResult original, string folder, string reference )
	{
		var model = Model.Load( original.VmdlAsset.Path );
		var before = File.ReadAllText( original.VmdlAsset.AbsolutePath );
		var groundedText = ModelGrounding.Apply( before, model.Bounds.Mins.z );
		var offset = ModelGrounding.Offset( groundedText ) - ModelGrounding.Offset( before );
		if ( offset <= 0 ) throw new Exception( "Grounding fixture must start below the floor." );
		CitizenAnimationSetup.ValidateSourceScale( groundedText );
		var result = await EditorPipeline.WriteAndCompileAsync( new RetargetBatchResult { StandaloneVmdl = groundedText },
			folder, standaloneVmdlName: "fitted_grounded", allowAnimationSetupOnly: true );
		await EditorPipeline.SwitchToMainThread();
		if ( !result.Compiled ) throw new Exception( string.Join( "; ", result.Errors ) );
		var grounded = Model.Load( result.VmdlAsset.Path );
		var floor = grounded.GetVertices().Min( v => v.Position.z );
		if ( MathF.Abs( floor ) > .05f ) throw new Exception( $"Grounded mesh floor is {floor}, expected zero." );
		var sourceBind = TargetPickers.SkeletonFromModel( model );
		var groundedBind = TargetPickers.SkeletonFromModel( grounded );
		foreach ( var bone in groundedBind.Bones )
		{
			var local = ModelGrounding.SourceLocal( bone.RestLocal, bone.ParentIndex < 0, offset );
			var expected = sourceBind[sourceBind.IndexOf( bone.Name )].RestLocal;
			if ( System.Numerics.Vector3.Distance( local.Pos, expected.Pos ) > .01f
				|| HumanoidRetargeter.Maths.MathQ.AngleBetween( local.Rot, expected.Rot ) > .001f )
				throw new Exception( "Grounding changed relative bind: " + bone.Name );
		}
		var ungroundedSpec = StockAnimationReplacement.TargetSpec( reference, original.VmdlAsset.Path );
		var groundedSpec = StockAnimationReplacement.TargetSpec( reference, result.VmdlAsset.Path );
		if ( CitizenAnimationSetup.CompatibilityError( ungroundedSpec.Rig.Skeleton, groundedSpec.Rig.Skeleton ) is not null )
			throw new Exception( "Replacement clips would apply the grounding offset twice." );
		if ( model.AnimationNames.Except( grounded.AnimationNames ).Any() || grounded.AnimGraph is null || grounded.AnimGraph.IsError )
			throw new Exception( "Grounding lost sequences or the animgraph." );
		var world = new SceneWorld();
		var a = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false };
		var b = new SceneModel( world, grounded, Transform.Zero ) { UseAnimGraph = false };
		try
		{
			foreach ( var name in model.AnimationNames.Where( n => n is "IdlePose_Default" or "Walk_N" or "Run_N"
				|| n.Contains( "debug_animation_scaling" ) ) )
			foreach ( var time in new[] { 0f, .5f, .9f } )
			{
				a.CurrentSequence.Name = b.CurrentSequence.Name = name;
				a.CurrentSequence.TimeNormalized = b.CurrentSequence.TimeNormalized = time;
				a.Update( 0 ); b.Update( 0 );
				foreach ( var bone in sourceBind.Bones )
				{
					var expected = a.GetBoneWorldTransform( bone.Name );
					var actual = b.GetBoneWorldTransform( bone.Name );
					if ( (actual.Position - expected.Position - Vector3.Up * offset).Length > .05f
						|| actual.Rotation.Distance( expected.Rotation ) > .1f )
						throw new Exception( $"Grounding changed animation {name}/{bone.Name}: {actual.Position - expected.Position}, expected +{offset} Z." );
				}
			}
		}
		finally { a.Delete(); b.Delete(); world.Delete(); }
		var entry = SourceFileEntry.Load( Environment.GetEnvironmentVariable( "HR_UI_FIXTURE_STEPS" ), Project.Current.GetAssetsPath() );
		var request = new RetargetRequest { SourceData = entry.Bytes, SourceFileName = entry.FileName,
			MappingOverride = entry.Mapping, RootMotion = RootMotionMode.InPlace, ClipNameOverride = "grounded_added_clip" };
		var picked = TargetPickers.FromModelAsset( result.VmdlAsset, out var pickError );
		if ( picked is null ) throw new Exception( pickError );
		var augmented = await RetargetWindow.ConvertAndWriteAsync( new[] { request }, picked,
			new BatchOptions { AugmentVmdlText = groundedText, DmxFolderRelative = folder }, result.VmdlAsset.AbsolutePath );
		await EditorPipeline.SwitchToMainThread();
		if ( augmented.Write?.Compiled != true ) throw new Exception( "Grounded augmentation did not compile: " + string.Join( "; ", augmented.Batch.Errors ) );
		var clip = augmented.Batch.Clips.Single();
		var frame = clip.SolvedFrames.Count / 2;
		var expectedPose = new HumanoidRetargeter.Skeleton.Pose( clip.SolvedFrames[frame] ).ToWorld( groundedSpec.Rig.Skeleton );
		var probeWorld = new SceneWorld();
		var probe = new SceneModel( probeWorld, Model.Load( result.VmdlAsset.Path ), Transform.Zero ) { UseAnimGraph = false };
		try
		{
			probe.CurrentSequence.Name = request.ClipNameOverride;
			probe.CurrentSequence.Time = frame / clip.Fps;
			probe.Update( 0 );
			foreach ( var name in new[] { "pelvis", "ankle_L", "ankle_R" } )
			{
				var expected = expectedPose[groundedSpec.Rig.Skeleton.IndexOf( name )].Pos;
				var expectedEngine = new Vector3( expected.X, -expected.Z, expected.Y ) * RetargetTargetSpec.SboxSourceScale + Vector3.Up * offset;
				if ( probe.GetBoneWorldTransform( name ).Position.Distance( expectedEngine ) > .1f )
					throw new Exception( "Grounded augmentation applied an incorrect offset: " + name );
			}
		}
		finally { probe.Delete(); probeWorld.Delete(); }
		Log.Info( $"[hr-ui-smoke] Grounding: mesh floor {floor}, bind and animated bones translated by {offset}, replacement source bind unchanged." );
	}
}
