using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Editor;
using HumanoidRetargeter.Formats.Fbx;
using HumanoidRetargeter.Mapping;
using HumanoidRetargeter.Maths;
using HumanoidRetargeter.Target;
using Sandbox;
using SkeletonModel = HumanoidRetargeter.Skeleton.Skeleton;

namespace HumanoidRetargeter.Editor;

/// <summary>
/// Builds <see cref="RetargetTargetSpec"/>s for the window's target picker
/// (Task 9.5.2): the shipped s&amp;box default, a custom compiled model/vmdl asset, or a
/// custom FBX file. Custom targets run the same humanoid detection as sources and are
/// rejected with a clear message below the detection threshold.
/// </summary>
/// <remarks>
/// <b>Units.</b> The pipeline is unit-agnostic as long as one target is self-consistent:
/// the solver scales pelvis translation by the target/source hip-height RATIO (so cm
/// sources drive inch targets correctly) and all other channels are rotations; DMX
/// positions are emitted in whatever units the target skeleton uses.
/// <list type="bullet">
/// <item>Compiled model targets: <c>Model.Bones</c> bind pose is in engine units (inches)
/// → <c>VmdlScale = 1.0</c>, no conversion anywhere.</item>
/// <item>FBX targets: imported in source units normalized to cm by
/// <see cref="FbxImporter"/> → <c>VmdlScale = 0.3937</c> (cm → inch at compile time),
/// matching the s&amp;box citizen pipeline.</item>
/// </list>
/// </remarks>
public static class TargetPickers
{
	/// <summary>A resolved conversion target plus what the preview needs to render it.</summary>
	public sealed class ResolvedTarget
	{
		/// <summary>The spec handed to <see cref="Retargeter.ConvertBatch"/>.</summary>
		public RetargetTargetSpec Spec { get; set; }

		/// <summary>Short description for the window status line / target chip.</summary>
		public string Description { get; set; }

		/// <summary>Asset path of a compiled model whose bone names match the target rig -
		/// used by the skinned preview. Null when no engine model exists (FBX targets):
		/// the preview then shows a "no preview model" notice.</summary>
		public string PreviewModelPath { get; set; }

		/// <summary>Multiplier taking target-skeleton positions to engine units for the
		/// preview (0.3937 for cm rigs, 1.0 for engine-unit rigs).</summary>
		public float PreviewPositionScale { get; set; } = 1.0f;
	}

	/// <summary>The shipped s&amp;box human target. Throws when the rig JSON is missing.</summary>
	public static ResolvedTarget SboxDefault()
	{
		var spec = EditorPipeline.LoadSboxDefaultTarget();
		return new ResolvedTarget
		{
			Spec = spec,
			Description = "s&box Human (default)",
			PreviewModelPath = RetargetTargetSpec.SboxHumanMalePath,
			PreviewPositionScale = RetargetTargetSpec.SboxSourceScale,
		};
	}

	/// <summary>
	/// Builds a target from a compiled model asset (vmdl): skeleton from
	/// <c>Model.Bones</c> (engine units → VmdlScale 1.0), roles from preset detection /
	/// auto-mapping, bone classes from <see cref="BoneClassRules"/> name patterns.
	/// Returns null with <paramref name="error"/> set when the model fails to load or the
	/// armature is not recognized as humanoid.
	/// </summary>
	public static ResolvedTarget FromModelAsset( Asset asset, out string error )
	{
		error = null;
		var model = Model.Load( asset.Path );
		if ( model is null || model.IsError )
		{
			error = $"Could not load model '{asset.Path}'.";
			return null;
		}

		SkeletonModel skeleton;
		try
		{
			skeleton = SkeletonFromModel( model );
		}
		catch ( Exception e )
		{
			error = $"Could not read the model's skeleton: {e.Message}";
			return null;
		}

		var map = DetectHumanoid( skeleton, out error );
		if ( map is null )
			return null;

		var rig = TargetRig.FromSkeleton( skeleton, map );
		return new ResolvedTarget
		{
			Spec = new RetargetTargetSpec
			{
				Rig = rig,
				VmdlScale = 1.0f,                       // engine units already
				BaseModelPath = asset.Path,
				DefaultRootBone = RootBoneName( skeleton, map ),
				UpAxis = TargetUpAxis.ZUpEngine,        // Model.Bones bind pose is engine space
				DlWeights = DlAssets.TryLoadWeights(),
			},
			Description = $"Custom model: {asset.Name}",
			PreviewModelPath = asset.Path,
			PreviewPositionScale = 1.0f,
		};
	}

	/// <summary>
	/// Builds a target from an FBX file: skeleton via <see cref="FbxImporter"/> (cm →
	/// VmdlScale 0.3937). No engine model exists for it, so the standalone vmdl gets an
	/// empty <c>base_model_name</c> (the user can point it at their own mesh model later)
	/// and the preview is unavailable.
	/// </summary>
	public static ResolvedTarget FromFbxFile( string filePath, out string error )
	{
		error = null;
		SkeletonModel skeleton;
		try
		{
			skeleton = FbxImporter.Import( File.ReadAllBytes( filePath ) ).Skeleton;
		}
		catch ( Exception e )
		{
			error = $"Could not import '{Path.GetFileName( filePath )}': {e.Message}";
			return null;
		}

		var map = DetectHumanoid( skeleton, out error );
		if ( map is null )
			return null;

		var rig = TargetRig.FromSkeleton( skeleton, map );
		return new ResolvedTarget
		{
			Spec = new RetargetTargetSpec
			{
				Rig = rig,
				VmdlScale = RetargetTargetSpec.SboxSourceScale, // cm-authored skeleton
				BaseModelPath = "",
				DefaultRootBone = RootBoneName( skeleton, map ),
				DlWeights = DlAssets.TryLoadWeights(),
			},
			Description = $"Custom FBX: {Path.GetFileName( filePath )}",
			PreviewModelPath = null,
			PreviewPositionScale = RetargetTargetSpec.SboxSourceScale,
		};
	}

	/// <summary>The facade's single mapping cascade, with this picker's own rejection rule
	/// on top: targets must be trustworthy (design §9's best-effort rule is for sources), so
	/// a below-threshold auto map is rejected with a clear message instead of proceeding.</summary>
	static MappingResult DetectHumanoid( SkeletonModel skeleton, out string error )
	{
		error = null;
		var (map, report) = Retargeter.ResolveMapping( skeleton );
		if ( report.NeedsUserDecision )
		{
			error = $"Armature not recognized as humanoid (mapping confidence "
				+ $"{map.Confidence * 100f:0}% < {ProfileDetector.DetectionThreshold * 100f:0}%).";
			return null;
		}

		return map;
	}

	/// <summary>Converts <c>Model.Bones</c> to the library's skeleton model.
	/// <c>BoneCollection.Bone.LocalTransform</c> is the parent-relative bind transform in
	/// engine units; <see cref="SkeletonModel.Create"/> tolerates any bone order.</summary>
	static SkeletonModel SkeletonFromModel( Model model )
	{
		var definitions = new List<HumanoidRetargeter.Skeleton.BoneDefinition>();
		foreach ( var bone in model.Bones.AllBones )
		{
			var local = bone.LocalTransform;
			definitions.Add( new HumanoidRetargeter.Skeleton.BoneDefinition(
				bone.Name,
				bone.Parent?.Name,
				new XForm(
					new System.Numerics.Vector3( local.Position.x, local.Position.y, local.Position.z ),
					new System.Numerics.Quaternion( local.Rotation.x, local.Rotation.y, local.Rotation.z, local.Rotation.w ) ) ) );
		}

		return SkeletonModel.Create( definitions );
	}

	/// <summary>The topmost ancestor of the hips bone - the bone vmdl ExtractMotion nodes
	/// should operate on (<c>default_root_bone_name</c>). Falls back to the first root.</summary>
	static string RootBoneName( SkeletonModel skeleton, MappingResult map )
	{
		var index = map.RoleToBone.TryGetValue( BoneRole.Hips, out var hips ) ? hips : 0;
		while ( skeleton[index].ParentIndex >= 0 )
			index = skeleton[index].ParentIndex;
		return skeleton[index].Name;
	}
}
