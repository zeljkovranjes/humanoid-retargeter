using System.Numerics;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;
using Xunit;

namespace HumanoidRetargeter.Tests.Target;

/// <summary>
/// User report (2026-10-13, Neutral_throw_ball_001__A057.bvh): "the wrist hands are flat, the arms
/// are behind the back". The solved body matched the source, but root_IK (an unmapped scene-root
/// bone resting at the origin, nearer a foot than the hips) was re-anchored by the orphan-helper
/// pass after IK baking, so every IK target swung with a foot and the engine's hand IK pulled the
/// arms there. The baked relationships (IkBoneBakerTests) must survive the whole pipeline.
/// </summary>
public class IkTargetsTrackLimbsTests
{
	private const float PosTolCm = 0.5f;
	private const float RotTolDeg = 0.5f;

	private static readonly (string Target, string Limb)[] Pairs =
	{
		( "hand_L_IK_target", "hand_L" ),
		( "hand_R_IK_target", "hand_R" ),
		( "foot_L_IK_target", "ankle_L" ),
		( "foot_R_IK_target", "ankle_R" ),
	};

	public static IEnumerable<object[]> Rigs() => new[]
	{
		new object[] { "target_rig_sbox.json" },
		new object[] { "target_rig_sbox_citizen.json" },
	};

	[Theory]
	[MemberData( nameof( Rigs ) )]
	public void IkTargets_StayOnTheirLimbs_AfterTheFullPipeline( string rigFile )
	{
		var rigJson = File.ReadAllText( TestUtil.RepoFile( "Assets", "data", "humanoid_retargeter", rigFile ) );
		var spec = rigFile == "target_rig_sbox.json"
			? RetargetTargetSpec.SboxDefault( rigJson )
			: RetargetTargetSpec.SboxCitizen( rigJson );
		var request = new RetargetRequest
		{
			SourceData = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "fixtures", "fbx", "Zombie Crawl.fbx" ) ),
			SourceFileName = "Zombie Crawl.fbx",
		};

		var result = Retargeter.Convert( request, spec );

		Assert.True( result.Success, string.Join( "; ", result.Errors ) );
		var frames = Assert.Single( result.Clips ).SolvedFrames!;
		var skeleton = spec.Rig.Skeleton;

		foreach ( var (target, limb) in Pairs )
		{
			var targetIndex = skeleton.IndexOf( target );
			var limbIndex = skeleton.IndexOf( limb );
			Assert.True( targetIndex >= 0 && limbIndex >= 0, $"{rigFile} lacks {target} or {limb}" );

			for ( var f = 0; f < frames.Count; f++ )
			{
				var world = new Pose( frames[ f ] ).ToWorld( skeleton );
				var distance = Vector3.Distance( world[ targetIndex ].Pos, world[ limbIndex ].Pos );
				var angle = MathQ.AngleBetween( world[ targetIndex ].Rot, world[ limbIndex ].Rot ) * 180f / MathF.PI;

				Assert.True( distance < PosTolCm, $"{rigFile} frame {f}: {target} is {distance:0.0} cm off {limb}" );
				Assert.True( angle < RotTolDeg, $"{rigFile} frame {f}: {target} is {angle:0.0} deg off {limb}" );
			}
		}
	}
}
