#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Formats.Dmx;
using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Skeleton;
using HumanoidRetargeter.Target;
using Sandbox;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace HumanoidRetargeter.Editor;

/// <summary>Uses the engine's source importers, then rebinds raw channels before the original ModelDoc processing.</summary>
internal static class FittedCitizenAnimations
{
	internal static async Task<(string Vmdl, List<string> Files)> ConvertAsync(
		TargetPickers.ResolvedTarget target, string referencePath, string vmdl, string folder, Action<string> progress )
	{
		var directory = CitizenAnimationModels.ProjectFile( folder );
		if ( Directory.Exists( directory ) )
			throw new InvalidOperationException( "Retargeted Citizen sources already exist. Choose a new output name to preserve them." );
		var plan = new CitizenAnimationSources( vmdl, path =>
			File.ReadAllText( AssetSystem.FindByPath( path )?.AbsolutePath ?? throw new InvalidOperationException( "Missing Citizen source: " + path ) ) );
		var shipped = File.ReadAllText( AssetSystem.FindByPath( referencePath ).AbsolutePath );
		progress?.Invoke( $"Importing {plan.Sources.Count} stock animation sources for fitted-armature retargeting…" );
		var captured = await EditorPipeline.WriteAndCompileAsync(
			new RetargetBatchResult { StandaloneVmdl = plan.SamplingModel( shipped ) }, folder,
			standaloneVmdlName: "reference", compileTimeoutSeconds: EditorPipeline.MeshCompileTimeoutSeconds,
			allowAnimationSetupOnly: true );
		await EditorPipeline.SwitchToMainThread();
		if ( !captured.Compiled ) throw new InvalidOperationException( "Stock source import failed: " + string.Join( "; ", captured.Errors ) );
		var model = Model.Load( captured.VmdlAsset.Path );
		var source = TargetPickers.SkeletonFromModel( model );
		var destination = TargetPickers.SkeletonFromModel( Model.Load( target.PreviewModelPath ) );
		var offset = ModelGrounding.Offset( vmdl );
		if ( offset != 0 ) destination = HumanoidRetargeter.Skeleton.Skeleton.Create( destination.Bones.Select( bone => new BoneDefinition(
			bone.Name, bone.ParentIndex < 0 ? null : destination[bone.ParentIndex].Name,
			ModelGrounding.SourceLocal( bone.RestLocal, bone.ParentIndex < 0, offset ) ) ).ToArray() );
		var transfer = new FittedCitizenPose( source, destination );
		var spec = StockAnimationReplacement.TargetSpec( referencePath, target.PreviewModelPath );
		var sourceIndices = source.Bones.Select( b => model.Bones.GetBone( b.Name ).Index ).ToArray();
		var scaleIndices = destination.Bones.Select( b => source.IndexOf( b.Name ) ).ToArray();
		var files = new List<string>();
		var world = new SceneWorld();
		var scene = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false };
		try
		{
			foreach ( var entry in plan.Sources )
			{
				progress?.Invoke( $"Retargeting Citizen animations {files.Count + 1}/{plan.Sources.Count}: {Path.GetFileNameWithoutExtension( entry.Filename )}" );
				scene.CurrentSequence.Name = entry.Name + "_frames";
				scene.Update( 0 );
				var intervals = (int)MathF.Round( scene.CurrentSequence.Duration );
				scene.CurrentSequence.Name = entry.Name;
				scene.Update( 0 );
				var duration = scene.CurrentSequence.Duration;
				if ( intervals < 0 || intervals > 100000 || !float.IsFinite( duration ) || duration < 0 )
					throw new InvalidOperationException( "Invalid stock animation duration: " + entry.Filename );
				var fps = intervals > 0 && duration > 0 ? intervals / duration : 30f;
				var frames = new List<XForm[]>( intervals + 1 );
				var scales = new List<float[]>( intervals + 1 );
				var hasScale = false;
				for ( var frame = 0; frame <= intervals; frame++ )
				{
					scene.CurrentSequence.Time = intervals == 0 ? 0 : duration * frame / intervals;
					scene.Update( 0 );
					var pose = new XForm[source.Count];
					var sourceScales = new float[source.Count];
					for ( var b = 0; b < source.Count; b++ )
					{
						var transform = scene.GetParentSpaceBone( sourceIndices[b] );
						pose[b] = new XForm( new NVector3( transform.Position.x, transform.Position.y, transform.Position.z ),
							new NQuaternion( transform.Rotation.x, transform.Rotation.y, transform.Rotation.z, transform.Rotation.w ) );
						sourceScales[b] = transform.UniformScale;
					}
					var fitted = transfer.Transfer( pose );
					var frameScales = new float[destination.Count];
					for ( var b = 0; b < fitted.Length; b++ )
					{
						fitted[b] = CompiledRigSourceSpace.FromEngineLocal( fitted[b], destination[b].ParentIndex < 0, TargetUpAxis.YUpCm );
						frameScales[b] = scaleIndices[b] < 0 ? 1 : sourceScales[scaleIndices[b]];
						hasScale |= MathF.Abs( frameScales[b] - 1 ) > 0.00001f;
					}
					frames.Add( fitted );
					scales.Add( frameScales );
					if ( frame % 128 == 0 ) { await Task.Delay( 1 ); await EditorPipeline.SwitchToMainThread(); }
				}
				var filename = folder + "/" + entry.Name + (hasScale ? ".fbx" : ".dmx");
				var file = CitizenAnimationModels.ProjectFile( filename );
				// The file is an integer-rate native frame grid; each AnimFile retains its original playback rate.
				var clip = new Clip( entry.Name, 30, false, Retargeter.PrepareDmxFrames( frames, spec ) );
				if ( hasScale )
					File.WriteAllBytes( file, await Task.Run( () => FbxAnimationWriter.Write( spec.Rig.Skeleton, clip, scales ) ) );
				else
					File.WriteAllText( file, await Task.Run( () => DmxWriter.Write( spec.Rig.Skeleton, clip,
						new DmxWriteOptions { Name = entry.Name, SourceNote = entry.Filename } ) ) );
				files.Add( file );
				entry.SetConvertedFile( filename, fps );
				await EditorPipeline.SwitchToMainThread();
			}
		}
		finally { await EditorPipeline.SwitchToMainThread(); scene.Delete(); world.Delete(); }
		return (plan.ModelText, files);
	}
}
