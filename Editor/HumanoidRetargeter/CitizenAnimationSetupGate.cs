#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Target;
using Sandbox;

namespace HumanoidRetargeter.Editor;

/// <summary>Additional scratch-project regression gate, called only by UiSmokeGate.</summary>
internal static class CitizenAnimationSetupGate
{
	internal static async Task RunAsync()
	{
		await EditorPipeline.SwitchToMainThread();
		if ( !(Project.Current?.GetRootPath() ?? "").Contains( "hr-editor-rig", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Citizen setup gate requires the scratch project." );
		if ( CitizenAnimationModels.TryDetect( TargetPickers.SboxDefault(), out _, out _ ) )
			throw new Exception( "Built-in targets must not enable the custom-model setup button." );
		AssertButtonState( TargetPickers.SboxDefault(), false );
		await FittedCitizenAnimationGate.RunAsync();
		foreach ( var path in new[] { RetargetTargetSpec.SboxCitizenPath, RetargetTargetSpec.SboxHumanMalePath } )
		{
			var sourceAsset = AssetSystem.FindByPath( path );
			var sourceModel = Model.Load( path );
			var doc = Kv3.Parse( File.ReadAllText( sourceAsset.AbsolutePath ) );
			var root = (KvObject)((KvObject)doc.Root)["rootNode"];
			var children = (KvArray)root["children"];
			// A mesh-only custom VMDL, retaining the same armature. No animations or graph.
			children.Items.RemoveAll( n => n is KvObject o && o.GetString( "_class" ) is
				"AnimationList" or "AnimConstraintList" or "BoneMarkupList" or "AttachmentList"
				or "IKData" or "PoseParamList" or "WeightListList" or "GameDataList" );
			root["anim_graph_name"] = new KvString( "" );
			var folder = UiSmokeGate.OutputFolder + "/citizen_setup";
			var absoluteFolder = Path.Combine( Project.Current.GetAssetsPath(), folder );
			Directory.CreateDirectory( absoluteFolder );
			var name = Path.GetFileNameWithoutExtension( path );
			var customPath = Path.Combine( absoluteFolder, name + "_source.vmdl" );
			File.WriteAllText( customPath, Kv3.Serialize( doc ) );
			var target = TargetPickers.FromModelAsset( sourceAsset, out var error );
			if ( target is null ) throw new Exception( error );
			target.CustomVmdlPath = customPath;
			if ( !CitizenAnimationModels.TryDetect( target, out var detected, out error ) || detected != path )
				throw new Exception( $"Wrong Citizen detection: {detected}: {error}" );
			AssertButtonState( target, true );
			var result = await CitizenAnimationModels.CreateAsync( target, folder, name );
			await EditorPipeline.SwitchToMainThread();
			if ( !result.Compiled ) throw new Exception( string.Join( "; ", result.Errors ) );
			var model = Model.Load( result.VmdlAsset.Path );
			if ( model is null || model.IsError || model.AnimGraph is null || model.AnimGraph.IsError )
				throw new Exception( "Citizen setup has no working animation graph." );
			if ( model.AnimGraph.Name != sourceModel.AnimGraph.Name )
				throw new Exception( $"Wrong graph: {model.AnimGraph.Name}; expected {sourceModel.AnimGraph.Name}." );
			var missing = sourceModel.AnimationNames.Except( model.AnimationNames ).ToArray();
			if ( missing.Length > 0 ) throw new Exception( "Missing stock animations: " + string.Join( ", ", missing ) );
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
				for ( var i = 0; i < 30; i++ ) scene.Update( 1f / 30f );
				var ankle = model.Bones.GetBone( "ankle_L" ).Index;
				var start = scene.GetBoneWorldTransform( ankle ).Position;
				var movement = 0f;
				for ( var i = 0; i < 30; i++ )
				{
					scene.Update( 1f / 30f );
					movement = MathF.Max( movement, start.Distance( scene.GetBoneWorldTransform( ankle ).Position ) );
				}
				if ( movement < 0.1f ) throw new Exception( "Citizen animgraph locomotion did not move the model's leg." );
				foreach ( var bone in model.Bones.AllBones )
				{
					var p = scene.GetBoneWorldTransform( bone.Index ).Position;
					if ( !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) )
						throw new Exception( "Non-finite graph pose: " + bone.Name );
				}
			}
			finally { scene.Delete(); world.Delete(); }
			Log.Info( $"[hr-ui-smoke] Citizen setup {name}: graph={model.AnimGraph.Name}, all {sourceModel.AnimationCount} stock sequences present." );

			// Exercise the actual FBX picker and compiled-bind detection, not only VMDL cloning.
			var meshPath = path == RetargetTargetSpec.SboxCitizenPath
				? "models/citizen/citizen.fbx" : "models/citizen_human/bodies/male/citizen_human_body_male.fbx";
			var meshAsset = AssetSystem.FindByPath( meshPath );
			var fileTarget = TargetPickers.FromModelFile( meshAsset.AbsolutePath, out error );
			if ( fileTarget is null ) throw new Exception( error );
			if ( CitizenAnimationModels.TryDetect( fileTarget, out _, out _ ) )
				throw new Exception( "Setup must remain disabled until the source model is compiled." );
			AssertButtonState( fileTarget, false );
			if ( !await EditorPipeline.CompileModelTargetPreviewAsync( fileTarget, folder + "/fbx_" + name ) )
				throw new Exception( "Could not compile stock-armature FBX fixture." );
			await EditorPipeline.SwitchToMainThread();
			if ( !CitizenAnimationModels.TryDetect( fileTarget, out detected, out error ) || detected != path )
				throw new Exception( $"FBX armature detection failed: {error}" );
			AssertButtonState( fileTarget, true );
			var fileResult = await CitizenAnimationModels.CreateAsync( fileTarget, folder + "/fbx_" + name, "with_animations" );
			await EditorPipeline.SwitchToMainThread();
			if ( !fileResult.Compiled ) throw new Exception( string.Join( "; ", fileResult.Errors ) );
			var fileModel = Model.Load( fileResult.VmdlAsset.Path );
			if ( fileModel.AnimGraph is null || fileModel.AnimGraph.IsError || fileModel.AnimGraph.Name != sourceModel.AnimGraph.Name
				|| sourceModel.AnimationNames.Except( fileModel.AnimationNames ).Any() )
				throw new Exception( "FBX model did not receive the complete Citizen animation setup." );
			Log.Info( $"[hr-ui-smoke] Citizen FBX setup {name}: complete." );
			await StockAnimationReplacementGate.RunAsync( target, folder, name, sourceModel );
		}
	}

	static void AssertButtonState( TargetPickers.ResolvedTarget target, bool expected )
	{
		var window = new RetargetWindow( null );
		try
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var type = typeof(RetargetWindow);
			type.GetField( "_target", flags ).SetValue( window, target );
			type.GetMethod( "RefreshCitizenSetupButton", flags ).Invoke( window, null );
			var button = (Button)type.GetField( "_citizenSetupButton", flags ).GetValue( window );
			if ( button.Enabled != expected ) throw new Exception( "Citizen setup button has incorrect enabled state." );
			type.GetField( "_augmentMode", flags ).SetValue( window, true );
			type.GetMethod( "RefreshCitizenSetupButton", flags ).Invoke( window, null );
			if ( button.Enabled ) throw new Exception( "Citizen setup button must not overwrite an existing VMDL." );
		}
		finally { window.Destroy(); }
	}
}
