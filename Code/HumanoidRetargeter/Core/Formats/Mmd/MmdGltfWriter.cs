#nullable enable annotations

using System.Numerics;
using System.Text.Json;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

// Use the existing glTF mesh/material adapters for ModelDoc; no second DMX implementation.
internal static class MmdGltfWriter
{
	internal static byte[] Write( MmdModel model, Func<string, string>? textureUri, bool engineSafeBoneNames )
	{
		using var buffer = new MemoryStream();
		using var binary = new BinaryWriter( buffer );
		var views = new List<object>();
		var accessors = new List<object>();
		int Accessor( int count, string type, int componentType, Action write )
		{
			while ( buffer.Position % 4 != 0 )
				binary.Write( (byte)0 );
			var start = buffer.Position;
			write();
			views.Add( new { buffer = 0, byteOffset = start, byteLength = buffer.Position - start } );
			accessors.Add( new { bufferView = views.Count - 1, componentType, count, type } );
			return accessors.Count - 1;
		}
		void Vector( Vector3 value )
		{
			binary.Write( value.X );
			binary.Write( value.Y );
			binary.Write( value.Z );
		}
		var count = model.Vertices.Count;
		var positions = Accessor( count, "VEC3", 5126, () =>
		{
			foreach ( var v in model.Vertices )
				Vector( MmdModel.Position( v.Position ) / 100f );
		} );
		var normals = Accessor( count, "VEC3", 5126, () =>
		{
			foreach ( var v in model.Vertices )
				Vector( new Vector3( v.Normal.X, v.Normal.Y, -v.Normal.Z ) );
		} );
		var uv = Accessor( count, "VEC2", 5126, () =>
		{
			foreach ( var v in model.Vertices )
			{
				binary.Write( v.Uv.X );
				binary.Write( v.Uv.Y );
			}
		} );
		var joints = Accessor( count, "VEC4", 5123, () =>
		{
			foreach ( var v in model.Vertices )
			{
				for ( var i = 0; i < 4; i++ )
					binary.Write( (ushort)(v.Weights[ i ] > 0 ? v.Bones[ i ] : 0) );
			}
		} );
		var weights = Accessor( count, "VEC4", 5126, () =>
		{
			foreach ( var v in model.Vertices )
			{
				foreach ( var weight in v.Weights )
					binary.Write( weight );
			}
		} );
		var inverseBinds = Accessor( model.Bones.Count, "MAT4", 5126, () =>
		{
			foreach ( var bone in model.Bones )
			{
				var position = MmdModel.Position( bone.Position ) / 100f;
				foreach ( var value in new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -position.X, -position.Y, -position.Z, 1 } )
					binary.Write( value );
			}
		} );
		var materials = new List<object>();
		var morphTargets = new List<object>();
		var morphNames = new List<string>();
		for ( var i = 0; i < model.Morphs.Count; i++ )
		{
			var offsets = MmdMorphs.VertexOffsets( model, i );
			if ( offsets.Count == 0 )
				continue;
			var position = Accessor( count, "VEC3", 5126, () =>
			{
				for ( var vertex = 0; vertex < count; vertex++ )
					Vector( MmdModel.Position( offsets.GetValueOrDefault( vertex ) ) / 100f );
			} );
			morphTargets.Add( new { POSITION = position } );
			morphNames.Add( model.Morphs[ i ].Name );
		}
		var images = new List<object>();
		var textures = new List<object>();
		var primitives = new List<object>();
		var imageByPath = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var firstIndex = 0;
		for ( var m = 0; m < model.Materials.Count; m++ )
		{
			var material = model.Materials[ m ];
			var pbr = new Dictionary<string, object>
			{
				[ "baseColorFactor" ] = new[] { material.Diffuse.X, material.Diffuse.Y, material.Diffuse.Z, material.Diffuse.W },
				[ "metallicFactor" ] = 0f,
				[ "roughnessFactor" ] = 0.8f,
			};
			if ( !string.IsNullOrWhiteSpace( material.Texture ) )
			{
				if ( !imageByPath.TryGetValue( material.Texture, out var texture ) )
				{
					texture = images.Count;
					images.Add( new { uri = textureUri?.Invoke( material.Texture ) ?? material.Texture.Replace( '\\', '/' ) } );
					textures.Add( new { source = texture } );
					imageByPath.Add( material.Texture, texture );
				}
				pbr[ "baseColorTexture" ] = new { index = texture };
			}
			materials.Add( new
			{
				name = material.Name,
				pbrMetallicRoughness = pbr,
				doubleSided = material.DoubleSided,
				alphaMode = material.Diffuse.W < 1f ? "BLEND" : material.Texture.Length > 0 ? "MASK" : "OPAQUE",
				alphaCutoff = 0.5f,
				extras = new { mmd = new
				{
					sphereTexture = material.SphereTexture.Length > 0 ? textureUri?.Invoke( material.SphereTexture ) ?? material.SphereTexture : "",
					toonTexture = material.ToonTexture.Length > 0 ? textureUri?.Invoke( material.ToonTexture ) ?? material.ToonTexture : "",
					sphereMode = material.SphereMode,
					edgeSize = material.EdgeSize,
					edgeColor = new[] { material.EdgeColor.X, material.EdgeColor.Y, material.EdgeColor.Z, material.EdgeColor.W },
				} },
			} );
			var materialIndex = materials.Count - 1;
			if ( material.IndexCount > 0 )
			{
				var indices = Accessor( material.IndexCount, "SCALAR", 5125, () =>
				{
					for ( var i = firstIndex; i < firstIndex + material.IndexCount; i += 3 )
					{
						// Reflecting Z reverses winding.
						binary.Write( model.Indices[ i ] );
						binary.Write( model.Indices[ i + 2 ] );
						binary.Write( model.Indices[ i + 1 ] );
					}
				} );
				primitives.Add( new
				{
					attributes = new { POSITION = positions, NORMAL = normals, TEXCOORD_0 = uv, JOINTS_0 = joints, WEIGHTS_0 = weights },
					indices,
					material = materialIndex,
					mode = 4,
					targets = morphTargets,
				} );
				if ( material.EdgeSize > 0 && material.EdgeColor.W > 0 )
				{
					var edgePositions = Accessor( count, "VEC3", 5126, () =>
					{
						foreach ( var vertex in model.Vertices )
							Vector( MmdModel.Position( vertex.Position + vertex.Normal * vertex.EdgeScale * material.EdgeSize * 0.03f ) / 100f );
					} );
					var edgeIndices = Accessor( material.IndexCount, "SCALAR", 5125, () =>
					{
						for ( var i = firstIndex; i < firstIndex + material.IndexCount; i++ )
							binary.Write( model.Indices[ i ] );
					} );
					materials.Add( new
					{
						name = material.Name + "_outline",
						pbrMetallicRoughness = new { baseColorFactor = new[] { material.EdgeColor.X, material.EdgeColor.Y, material.EdgeColor.Z, material.EdgeColor.W } },
						doubleSided = false,
						alphaMode = material.EdgeColor.W < 1 ? "BLEND" : "OPAQUE",
						extras = new { mmd = new { sphereTexture = "", toonTexture = "", sphereMode = 0, outline = true } },
					} );
					primitives.Add( new
					{
						attributes = new { POSITION = edgePositions, NORMAL = normals, TEXCOORD_0 = uv, JOINTS_0 = joints, WEIGHTS_0 = weights },
						indices = edgeIndices, material = materials.Count - 1, mode = 4, targets = morphTargets,
					} );
				}
			}
			firstIndex += material.IndexCount;
		}
		var nodes = new List<object>();
		var roots = new List<int>();
		for ( var i = 0; i < model.Bones.Count; i++ )
		{
			var bone = model.Bones[ i ];
			var offset = bone.Position - (bone.Parent < 0 ? Vector3.Zero : model.Bones[ bone.Parent ].Position);
			var position = MmdModel.Position( offset ) / 100f;
			var children = Enumerable.Range( 0, model.Bones.Count ).Where( j => model.Bones[ j ].Parent == i ).ToArray();
			nodes.Add( new { name = engineSafeBoneNames ? MmdBoneNames.Encode( bone.Name ) : bone.Name,
				translation = new[] { position.X, position.Y, position.Z }, children } );
			if ( bone.Parent < 0 )
				roots.Add( i );
		}
		roots.Add( nodes.Count );
		nodes.Add( new { name = "mmd_mesh", mesh = 0, skin = 0 } );
		return JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0", generator = "Humanoid Retargeter MMD import" },
			buffers = new[] { new { byteLength = buffer.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String( buffer.ToArray() ) } },
			bufferViews = views,
			accessors,
			nodes,
			skins = new[] { new { joints = Enumerable.Range( 0, model.Bones.Count ).ToArray(), inverseBindMatrices = inverseBinds } },
			meshes = new[] { new { name = "mmd_mesh", primitives, extras = new { targetNames = morphNames } } },
			materials,
			images,
			textures,
			scenes = new[] { new { nodes = roots } },
			scene = 0,
		} );
	}
}
