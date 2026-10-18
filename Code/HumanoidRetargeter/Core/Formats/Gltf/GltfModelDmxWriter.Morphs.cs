using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using HumanoidRetargeter.Core.Formats.Fbx;

namespace HumanoidRetargeter.Core.Formats.Gltf;

using Vector3 = System.Numerics.Vector3;

public static partial class GltfModelDmxWriter
{
	private static Dictionary<string, Vector3[]> ReadMorphs( GltfDocument document, JsonElement mesh,
		JsonElement primitive, Matrix4x4[] transforms )
	{
		var result = new Dictionary<string, Vector3[]>( StringComparer.Ordinal );
		if ( !primitive.TryGetProperty( "targets", out var targets ) )
			return result;
		var names = mesh.TryGetProperty( "extras", out var extras ) && extras.TryGetProperty( "targetNames", out var targetNames )
			? targetNames : default;
		var index = 0;
		foreach ( var target in targets.EnumerateArray() )
		{
			var name = names.ValueKind == JsonValueKind.Array && index < names.GetArrayLength()
				? names[ index ].GetString() : $"morph_{index}";
			index++;
			if ( !target.TryGetProperty( "POSITION", out var property ) )
				continue;
			var accessor = new Accessor( document, property.GetInt32(), 3 );
			RequireCount( accessor, transforms.Length, "morph POSITION" );
			var deltas = new Vector3[ transforms.Length ];
			for ( var i = 0; i < deltas.Length; i++ )
			{
				var delta = new Vector3( accessor.Float( i, 0 ), accessor.Float( i, 1 ), accessor.Float( i, 2 ) );
				deltas[ i ] = Vector3.TransformNormal( delta, transforms[ i ] ) * MetersToCentimeters;
			}
			if ( string.IsNullOrWhiteSpace( name ) || !result.TryAdd( name, deltas ) )
				throw new FormatException( "glTF morph target names must be nonempty and unique." );
		}
		return result;
	}

	private static void WriteMorphDeltas( Kv2Writer writer, MeshPart part )
	{
		if ( part.Morphs.Count == 0 )
			return;
		writer.BeginArray( "deltaStates" );
		var morphIndex = 0;
		foreach ( var (name, deltas) in part.Morphs )
		{
			writer.BeginArrayElement( "DmeVertexDeltaData" );
			writer.Attr( "name", "string", HumanoidRetargeter.Core.Formats.Dmx.DmxWriter.MorphName( name ) );
			writer.BeginArray( "vertexFormat", "string_array" );
			writer.Value( "position$0", true );
			writer.EndArray();
			var indices = Enumerable.Range( 0, deltas.Length ).Where( i => deltas[ i ].LengthSquared() > 1e-16f ).ToArray();
			WriteVectors( writer, "position$0", "vector3_array", indices.Select( i => deltas[ i ] ).ToArray(), Vec );
			WriteScalars( writer, "position$0Indices", "int_array", indices, i => i.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
			writer.EndArrayElement( ++morphIndex == part.Morphs.Count );
		}
		writer.EndArray();
		foreach ( var property in new[] { "deltaStateWeights", "deltaStateWeightsLagged" } )
		{
			writer.BeginArray( property, "vector2_array" );
			for ( var i = 0; i < part.Morphs.Count; i++ )
				writer.Value( "0 0", i == part.Morphs.Count - 1 );
			writer.EndArray();
		}
	}

	private static void WriteMorphControls( Kv2Writer writer, IReadOnlyList<MeshPart> parts, string[] meshIds )
	{
		var names = parts.SelectMany( part => part.Morphs.Keys ).Distinct( StringComparer.Ordinal )
			.Select( HumanoidRetargeter.Core.Formats.Dmx.DmxWriter.MorphName ).ToArray();
		if ( names.Length == 0 )
			return;
		writer.BeginInline( "combinationOperator", "DmeCombinationOperator" );
		writer.Attr( "name", "string", "combinationOperator" );
		writer.BeginArray( "controls" );
		for ( var i = 0; i < names.Length; i++ )
		{
			writer.BeginArrayElement( "DmeCombinationInputControl" );
			writer.Attr( "name", "string", names[ i ] );
			writer.Attr( "stereo", "bool", "0" );
			writer.Attr( "eyelid", "bool", "0" );
			writer.BeginArray( "rawControlNames", "string_array" );
			writer.Value( names[ i ], true );
			writer.EndArray();
			writer.BeginArray( "wrinkleScales", "float_array" );
			writer.Value( "0", true );
			writer.EndArray();
			writer.EndArrayElement( i == names.Length - 1 );
		}
		writer.EndArray();
		foreach ( var property in new[] { "controlValues", "controlValuesLagged" } )
		{
			writer.BeginArray( property, "vector3_array" );
			for ( var i = 0; i < names.Length; i++ )
				writer.Value( "0 0.5 0.5", i == names.Length - 1 );
			writer.EndArray();
		}
		WriteRefs( writer, "targets", meshIds );
		writer.EndInline();
	}
}
