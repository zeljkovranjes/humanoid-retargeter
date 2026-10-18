#nullable enable annotations

using System.Numerics;
using System.Text;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Skeleton;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

/// <summary>Imports PMX/PMD rigs and VMD/VPD skeletal motion without external applications.</summary>
public static class MmdImporter
{
	/// <summary>Imports a model or motion/pose using a companion model or an explicitly enabled standard humanoid rig.</summary>
	public static SourceScene Import( byte[] data, byte[]? skeletonData = null, float sampleFps = 30f, string clipName = "mmd", bool useStandardSkeleton = false )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( !float.IsFinite( sampleFps ) || sampleFps <= 0f || sampleFps > 240f )
			throw new ArgumentOutOfRangeException( nameof(sampleFps), "MMD sample rate must be between 0 and 240 fps." );
		var kind = Detect( data );
		if ( kind is null )
			throw new FormatException( "Expected PMX, PMD, VMD or VPD data." );
		var isModel = kind is "pmx" or "pmd";
		if ( !isModel && skeletonData is null && !useStandardSkeleton )
			throw new FormatException( "VMD/VPD motion has no rest skeleton. Pick its character's PMX or PMD model using Pick skeleton." );
		var motion = isModel ? null : kind == "vmd" ? MmdMotionReader.ReadVmd( data ) : MmdMotionReader.ReadVpd( data );
		var standard = !isModel && skeletonData is null;
		var model = standard ? MmdStandardSkeleton.Create( motion! ) : MmdModelReader.Read( isModel ? data : skeletonData! );
		var skeleton = model.CreateSkeleton();
		var clips = new List<Clip>();
		if ( motion is not null )
		{
			model.Notes.AddRange( motion.Notes.Distinct() );
			var tracks = new Dictionary<int, List<MmdKeyframe>>();
			foreach ( var (name, keys) in motion.Tracks )
			{
				var index = skeleton.IndexOf( name );
				if ( index >= 0 )
					tracks[ index ] = keys;
			}
			var morphNames = model.Morphs.Select( morph => morph.Name ).ToHashSet( StringComparer.Ordinal );
			var morphTracks = motion.Morphs.Where( entry => standard || morphNames.Contains( entry.Key ) ).ToDictionary( entry => entry.Key, entry => entry.Value );
			if ( standard && tracks.Count == 0 )
				throw new FormatException( "MMD motion has no standard humanoid bone tracks. Select its PMX/PMD source model for custom bones; camera/light-only motions cannot animate an armature." );
			if ( tracks.Count == 0 && morphTracks.Count == 0 )
				throw new FormatException( "MMD motion has no bone tracks matching the selected model. Choose the matching PMX/PMD; camera-only and morph-only motions cannot be retargeted." );
			if ( tracks.Count != motion.Tracks.Count )
				model.Notes.Add( $"MMD: {motion.Tracks.Count - tracks.Count} bone tracks do not exist in the selected model and were ignored." );
			var end = Math.Max( tracks.Count > 0 ? tracks.Values.Max( keys => keys[ ^1 ].Frame ) : 0,
				morphTracks.Count > 0 ? morphTracks.Values.Max( keys => keys[ ^1 ].Frame ) : 0 );
			var frameCount = (int)Math.Ceiling( end / 30.0 * sampleFps ) + 1;
			if ( (long)frameCount * skeleton.Count > 8_000_000 )
				throw new FormatException( "MMD clip is too large to sample. Reduce the sample rate or split the motion." );
			var frames = new List<XForm[]>( frameCount );
			var evaluator = new MmdPoseEvaluator( model, skeleton );
			var morphSamples = morphTracks.ToDictionary( entry => entry.Key, _ => new float[ frameCount ] );
			for ( var i = 0; i < frameCount; i++ )
			{
				var frame = MathF.Min( i * 30f / sampleFps, end );
				var pose = Pose.Rest( skeleton ).Locals;
				foreach ( var (index, keys) in tracks )
				{
					var value = Sample( keys, frame );
					pose[ index ] = new XForm( pose[ index ].Pos + MmdModel.Position( value.Pos ), MmdModel.Rotation( value.Rot ) );
				}
				var weights = new Dictionary<string, float>();
				foreach ( var (name, keys) in morphTracks )
				{
					var weight = MmdMorphs.Sample( keys, frame );
					weights[ name ] = weight;
					morphSamples[ name ][ i ] = weight;
				}
				MmdMorphs.ApplyBones( model, skeleton, pose, weights );
				evaluator.Apply( pose, motion, frame );
				frames.Add( pose );
			}
			var clip = new Clip( clipName, sampleFps, false, frames, nativeFps: 30f );
			foreach ( var (name, values) in morphSamples )
				clip.MorphWeights.Add( name, values );
			MmdPhysics.Bake( model, skeleton, clip );
			clips.Add( clip );
		}
		return new SourceScene( skeleton, clips, MmdModel.UnitScaleCm, notes: model.Notes ) { HasMmdAuxiliaryMotion = true };
	}

	/// <summary>Converts a PMX/PMD skinned mesh into glTF for the existing model-DMX adapter. The callback resolves each diffuse texture to a URI.</summary>
	public static byte[] ConvertModelToGltf( byte[] data, Func<string, string>? textureUri = null, bool engineSafeBoneNames = false )
	{
		ArgumentNullException.ThrowIfNull( data );
		var model = MmdModelReader.Read( data );
		if ( model.Vertices.Count == 0 || model.Indices.Length == 0 )
			throw new FormatException( "MMD model has no mesh triangles." );
		return MmdGltfWriter.Write( model, textureUri, engineSafeBoneNames );
	}

	internal static string? Detect( byte[] data )
	{
		var text = Encoding.ASCII.GetString( data, 0, Math.Min( data.Length, 32 ) );
		if ( text.StartsWith( "PMX ", StringComparison.Ordinal ) )
			return "pmx";
		if ( text.StartsWith( "Pmd", StringComparison.Ordinal ) )
			return "pmd";
		if ( text.StartsWith( "Vocaloid Motion Data", StringComparison.Ordinal ) )
			return "vmd";
		if ( text.StartsWith( "Vocaloid Pose Data file", StringComparison.Ordinal )
			|| (data.Length >= 3 && data[ 0 ] == 0xef && data[ 1 ] == 0xbb && data[ 2 ] == 0xbf
				&& Encoding.UTF8.GetString( data, 3, Math.Min( data.Length - 3, 24 ) ).StartsWith( "Vocaloid Pose Data file", StringComparison.Ordinal )) )
			return "vpd";
		return null;
	}

	private static XForm Sample( List<MmdKeyframe> keys, float frame )
	{
		if ( frame <= keys[ 0 ].Frame )
			return new XForm( keys[ 0 ].Position, keys[ 0 ].Rotation );
		if ( frame >= keys[ ^1 ].Frame )
			return new XForm( keys[ ^1 ].Position, keys[ ^1 ].Rotation );
		var low = 0;
		var high = keys.Count - 1;
		while ( high - low > 1 )
		{
			var middle = (low + high) / 2;
			if ( keys[ middle ].Frame <= frame )
				low = middle;
			else
				high = middle;
		}
		var a = keys[ low ];
		var b = keys[ high ];
		var time = (frame - a.Frame) / (b.Frame - a.Frame);
		var delta = b.Position - a.Position;
		return new XForm( a.Position + new Vector3(
			delta.X * Curve( b.Interpolation, 0, time ),
			delta.Y * Curve( b.Interpolation, 1, time ),
			delta.Z * Curve( b.Interpolation, 2, time ) ),
			Quaternion.Slerp( a.Rotation, b.Rotation, Curve( b.Interpolation, 3, time ) ) );
	}

	private static float Curve( byte[] data, int channel, float time )
	{
		if ( data.Length == 0 )
			return time;
		var x1 = data[ channel ] / 127f;
		var y1 = data[ channel + 4 ] / 127f;
		var x2 = data[ channel + 8 ] / 127f;
		var y2 = data[ channel + 12 ] / 127f;
		var low = 0f;
		var high = 1f;
		for ( var i = 0; i < 20; i++ )
		{
			var t = (low + high) * 0.5f;
			if ( Bezier( t, x1, x2 ) < time )
				low = t;
			else
				high = t;
		}
		return Bezier( (low + high) * 0.5f, y1, y2 );
	}

	private static float Bezier( float t, float a, float b )
		=> 3f * (1f - t) * (1f - t) * t * a + 3f * (1f - t) * t * t * b + t * t * t;
}
