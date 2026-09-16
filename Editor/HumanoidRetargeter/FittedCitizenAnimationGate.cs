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

/// <summary>Opt-in real fitted-armature regression, isolated from the user's original assets.</summary>
internal static class FittedCitizenAnimationGate
{
	internal static async Task RunAsync()
	{
		var fixture = Environment.GetEnvironmentVariable( "HR_UI_FITTED_CITIZEN" );
		if ( string.IsNullOrEmpty( fixture ) ) return;
		if ( !(Project.Current?.GetRootPath() ?? "").Contains( "hr-editor-rig", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Fitted Citizen gate requires the scratch project." );
		var folder = UiSmokeGate.OutputFolder + "/fitted_citizen";
		var doc = Kv3.Parse( File.ReadAllText( fixture ) );
		var children = (KvArray)((KvObject)((KvObject)doc.Root)["rootNode"])["children"];
		var meshes = children.Items.OfType<KvObject>().Single( n => n.GetString( "_class" ) == "RenderMeshList" );
		foreach ( var mesh in ((KvArray)meshes["children"]).Items.OfType<KvObject>() )
		{
			var original = Path.Combine( Path.GetDirectoryName( fixture ), Path.GetFileName( mesh.GetString( "filename" ) ) );
			var relative = folder + "/" + Path.GetFileName( original );
			var destination = CitizenAnimationModels.ProjectFile( relative );
			Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
			File.Copy( original, destination, overwrite: true );
			AssetSystem.RegisterFile( destination );
			mesh["filename"] = new KvString( relative );
		}
		var input = await EditorPipeline.WriteAndCompileAsync( new RetargetBatchResult { StandaloneVmdl = Kv3.Serialize( doc ) },
			folder, standaloneVmdlName: "fitted_input", allowAnimationSetupOnly: true );
		await EditorPipeline.SwitchToMainThread();
		if ( !input.Compiled ) throw new Exception( string.Join( "; ", input.Errors ) );
		var target = TargetPickers.FromModelAsset( input.VmdlAsset, out var error );
		if ( target is null || !CitizenAnimationModels.TryDetect( target, out var reference, out error ) )
			throw new Exception( "Fitted hierarchy was not accepted: " + error );
		if ( !CitizenAnimationModels.RequiresRetargeting( target, reference ) )
			throw new Exception( "Fitted fixture must exercise bind retargeting, not direct copying." );
		var window = new RetargetWindow( null );
		try
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(RetargetWindow).GetField( "_target", flags ).SetValue( window, target );
			typeof(RetargetWindow).GetMethod( "RefreshCitizenSetupButton", flags ).Invoke( window, null );
			if ( !((Button)typeof(RetargetWindow).GetField( "_citizenSetupButton", flags ).GetValue( window )).Enabled )
				throw new Exception( "Complete fitted armature must enable the Citizen button." );
		}
		finally { window.Destroy(); }
		var result = await CitizenAnimationModels.CreateAsync( target, folder, "fitted_animated", true,
			message => Log.Info( "[hr-ui-smoke] " + message ) );
		await EditorPipeline.SwitchToMainThread();
		if ( !result.Compiled ) throw new Exception( string.Join( "; ", result.Errors ) );
		var model = Model.Load( result.VmdlAsset.Path );
		var stock = Model.Load( reference );
		var missing = stock.AnimationNames.Except( model.AnimationNames ).ToArray();
		if ( missing.Length > 0 ) throw new Exception( "Fitted output lost sequences: " + string.Join( ", ", missing ) );
		var bind = TargetPickers.SkeletonFromModel( model );
		if ( CitizenAnimationSetup.CompatibilityError( bind, TargetPickers.SkeletonFromModel( Model.Load( target.PreviewModelPath ) ) ) is not null )
			throw new Exception( "Retargeting must not change the custom model's bind skeleton." );
		var world = new SceneWorld();
		var scene = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false };
		var stockScene = new SceneModel( world, stock, Transform.Zero ) { UseAnimGraph = false };
		try
		{
			foreach ( var name in stock.AnimationNames.Where( n => n.Contains( "debug_animation_scaling" ) ) )
			{
				scene.CurrentSequence.Name = stockScene.CurrentSequence.Name = name;
				scene.CurrentSequence.TimeNormalized = stockScene.CurrentSequence.TimeNormalized = 0.5f;
				scene.Update( 0 ); stockScene.Update( 0 );
				foreach ( var bone in bind.Bones )
				{
					var original = stock.Bones.GetBone( bone.Name );
					if ( original is null ) continue;
					var expected = stockScene.GetParentSpaceBone( original.Index ).UniformScale;
					var actual = scene.GetParentSpaceBone( model.Bones.GetBone( bone.Name ).Index ).UniformScale;
					if ( MathF.Abs( actual - expected ) > .02f )
						throw new Exception( $"Fitted scaling channel changed for {bone.Name}: {actual} vs {expected}." );
				}
				var expectedRotation = stockScene.GetBoneWorldTransform( "pelvis" ).Rotation;
				var actualRotation = scene.GetBoneWorldTransform( "pelvis" ).Rotation;
				if ( expectedRotation.Distance( actualRotation ) > 2 )
					throw new Exception( $"Scaled animation changed facing: {expectedRotation} vs {actualRotation}." );
			}
			foreach ( var name in new[] { "IdlePose_Default", "Walk_N", "Run_N" }.Where( model.AnimationNames.Contains ) )
			{
				scene.CurrentSequence.Name = name;
				scene.CurrentSequence.TimeNormalized = 0.5f;
				scene.Update( 0.001f );
				foreach ( var bone in bind.Bones.Where( b => b.Name.StartsWith( "finger_" ) || b.Name.StartsWith( "arm_lower_" ) ) )
				{
					var length = scene.GetParentSpaceBone( model.Bones.GetBone( bone.Name ).Index ).Position.Length;
					if ( !float.IsFinite( length ) || MathF.Abs( length - bone.RestLocal.Pos.Length() ) > 0.5f )
						throw new Exception( $"Fitted {name} stretches {bone.Name}: {length} vs bind {bone.RestLocal.Pos.Length()}." );
				}
			}
			scene.UseAnimGraph = true;
			scene.SetAnimParameter( "b_grounded", true );
			scene.SetAnimParameter( "move_x", 100f );
			scene.SetAnimParameter( "move_speed", 100f );
			scene.SetAnimParameter( "move_groundspeed", 100f );
			scene.SetAnimParameter( "wish_x", 100f );
			scene.SetAnimParameter( "wish_speed", 100f );
			for ( var i = 0; i < 60; i++ ) scene.Update( 1f / 30 );
			var start = scene.GetBoneWorldTransform( "ankle_L" ).Position;
			var movement = 0f;
			for ( var i = 0; i < 30; i++ )
			{
				scene.Update( 1f / 30 );
				movement = MathF.Max( movement, start.Distance( scene.GetBoneWorldTransform( "ankle_L" ).Position ) );
			}
			if ( !float.IsFinite( movement ) || movement < 0.1f ) throw new Exception( "Fitted graph did not animate the legs." );
		}
		finally { stockScene.Delete(); scene.Delete(); world.Delete(); }
		Log.Info( $"[hr-ui-smoke] Fitted Citizen: enabled button, retained bind, all {stock.AnimationCount} sequences, no stretched limbs and working editable graph." );
	}
}
