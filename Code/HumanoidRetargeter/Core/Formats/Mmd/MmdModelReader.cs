using System.Numerics;
using System.Text;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

internal static class MmdModelReader
{
	internal static MmdModel Read( byte[] data )
	{
		var reader = new MmdReader( data );
		var magic = reader.FixedText( 3, Encoding.ASCII );
		var model = magic switch
		{
			"PMX" => ReadPmx( reader ),
			"Pmd" => ReadPmd( reader ),
			_ => throw new FormatException( "Select a PMX or PMD model as the MMD skeleton." ),
		};
		model.Validate();
		model.Notes.Add( "MMD: native C# motion, IK and Bullet physics. The MMD playback resource preserves advanced deformation and material data; standard VMDL uses linear skinning and position morphs." );
		return model;
	}

	private static MmdModel ReadPmx( MmdReader reader )
	{
		if ( reader.Byte() != 32 )
			throw new FormatException( "Invalid PMX signature." );
		var version = reader.Float();
		if ( MathF.Abs( version - 2f ) > 0.001f && MathF.Abs( version - 2.1f ) > 0.001f )
			throw new FormatException( "Only PMX 2.0 and 2.1 are supported." );
		var header = reader.Bytes( reader.Byte() );
		if ( header.Length < 8 || header[ 0 ] > 1 || header[ 1 ] > 4 )
			throw new FormatException( "Invalid PMX header." );
		for ( var i = 2; i < 8; i++ )
		{
			if ( header[ i ] is not (1 or 2 or 4) )
				throw new FormatException( "Invalid PMX index width." );
		}
		reader.TextEncoding = header[ 0 ] == 0 ? Encoding.Unicode : Encoding.UTF8;
		var vertexSize = header[ 2 ];
		var textureSize = header[ 3 ];
		var boneSize = header[ 5 ];
		for ( var i = 0; i < 4; i++ )
			reader.Text();
		var model = new MmdModel();
		var vertexCount = reader.Count( 38 );
		for ( var i = 0; i < vertexCount; i++ )
		{
			var vertex = new MmdVertex
			{
				Position = reader.Vector(),
				Normal = reader.Vector(),
				Uv = new Vector2( reader.Float(), reader.Float() ),
			};
			vertex.AdditionalUv = new Vector4[ header[ 1 ] ];
			for ( var uv = 0; uv < vertex.AdditionalUv.Length; uv++ )
				vertex.AdditionalUv[ uv ] = reader.Vector4();
			var weightType = reader.Byte();
			vertex.Deform = weightType;
			var influences = weightType switch
			{
				0 => 1,
				1 or 3 => 2,
				2 or 4 => 4,
				_ => throw new FormatException( "Unknown PMX skinning type." ),
			};
			for ( var j = 0; j < influences; j++ )
				vertex.Bones[ j ] = reader.Index( boneSize );
			vertex.Weights[ 0 ] = influences == 1 ? 1f : reader.Float();
			if ( influences == 2 )
				vertex.Weights[ 1 ] = 1f - vertex.Weights[ 0 ];
			if ( influences == 4 )
			{
				for ( var j = 1; j < 4; j++ )
					vertex.Weights[ j ] = reader.Float();
			}
			if ( weightType == 3 )
			{
				vertex.SdefC = reader.Vector();
				vertex.SdefR0 = reader.Vector();
				vertex.SdefR1 = reader.Vector();
			}
			vertex.EdgeScale = reader.Float();
			model.Vertices.Add( vertex );
		}
		model.Indices = new int[ reader.Count( vertexSize ) ];
		for ( var i = 0; i < model.Indices.Length; i++ )
			model.Indices[ i ] = reader.VertexIndex( vertexSize );
		var textures = new string[ reader.Count( 4 ) ];
		for ( var i = 0; i < textures.Length; i++ )
			textures[ i ] = reader.Text();
		var materialCount = reader.Count( 70 );
		for ( var i = 0; i < materialCount; i++ )
		{
			reader.Text();
			reader.Text();
			var material = new MmdMaterial
			{
				// Stable ASCII identities avoid material-name collisions and engine path restrictions.
				Name = $"mmd_material_{i}",
				Diffuse = new Vector4( reader.Float(), reader.Float(), reader.Float(), reader.Float() ),
			};
			material.Specular = reader.Vector();
			material.SpecularPower = reader.Float();
			material.Ambient = reader.Vector();
			var flags = reader.Byte();
			material.DoubleSided = (flags & 1) != 0;
			material.EdgeColor = new Vector4( reader.Float(), reader.Float(), reader.Float(), reader.Float() );
			material.EdgeSize = reader.Float();
			if ( (flags & 0x10) == 0 )
				material.EdgeSize = 0;
			var texture = reader.Index( textureSize );
			if ( texture < -1 || texture >= textures.Length )
				throw new FormatException( "Invalid PMX texture index." );
			material.Texture = texture < 0 ? "" : textures[ texture ];
			var sphere = reader.Index( textureSize );
			material.SphereMode = reader.Byte();
			if ( sphere >= 0 && sphere < textures.Length )
				material.SphereTexture = textures[ sphere ];
			var sharedToon = reader.Byte();
			if ( sharedToon > 1 )
				throw new FormatException( "Invalid PMX toon flag." );
			var toon = sharedToon == 1 ? reader.Byte() : reader.Index( textureSize );
			if ( sharedToon == 0 && toon >= 0 && toon < textures.Length )
				material.ToonTexture = textures[ toon ];
			reader.Text();
			material.IndexCount = reader.Int();
			if ( material.IndexCount < 0 || material.IndexCount % 3 != 0 )
				throw new FormatException( "Invalid PMX material triangle count." );
			model.Materials.Add( material );
		}
		var boneCount = reader.Count( 22, 4096 );
		for ( var i = 0; i < boneCount; i++ )
		{
			var bone = new MmdBone { Name = reader.Text() };
			var englishName = reader.Text();
			if ( string.IsNullOrWhiteSpace( bone.Name ) )
				bone.Name = string.IsNullOrWhiteSpace( englishName ) ? $"bone_{i}" : englishName;
			bone.Position = reader.Vector();
			bone.Parent = reader.Index( boneSize );
			bone.Layer = reader.Int();
			bone.Flags = reader.U16();
			reader.Skip( (bone.Flags & 1) != 0 ? boneSize : 12 );
			if ( (bone.Flags & 0x300) != 0 )
			{
				bone.AppendParent = reader.Index( boneSize );
				bone.AppendWeight = reader.Float();
			}
			if ( (bone.Flags & 0x400) != 0 )
				reader.Skip( 12 );
			if ( (bone.Flags & 0x800) != 0 )
				reader.Skip( 24 );
			if ( (bone.Flags & 0x2000) != 0 )
				reader.Skip( 4 );
			if ( (bone.Flags & 0x20) != 0 )
			{
				bone.IkTarget = reader.Index( boneSize );
				bone.Iterations = reader.Int();
				bone.AngleLimit = reader.Float();
				var links = reader.Count( boneSize + 1, 256 );
				for ( var j = 0; j < links; j++ )
				{
					var index = reader.Index( boneSize );
					var limited = reader.Byte() != 0;
					bone.Links.Add( new MmdIkLink( index, limited,
						limited ? reader.Vector() : Vector3.Zero,
						limited ? reader.Vector() : Vector3.Zero ) );
				}
			}
			model.Bones.Add( bone );
		}
		MmdMorphReader.ReadPmx( reader, model, vertexSize, boneSize, header[ 4 ], header[ 6 ], header[ 7 ] );
		MmdPhysicsReader.ReadPmx( reader, model, boneSize, header[ 6 ], header[ 7 ], vertexSize, header[ 4 ] );
		return model;
	}

	private static MmdModel ReadPmd( MmdReader reader )
	{
		if ( reader.Float() != 1f )
			throw new FormatException( "Only PMD 1.0 is supported." );
		reader.Skip( 276 );
		var model = new MmdModel();
		var count = reader.Count( 38 );
		for ( var i = 0; i < count; i++ )
		{
			var vertex = new MmdVertex
			{
				Position = reader.Vector(),
				Normal = reader.Vector(),
				Uv = new Vector2( reader.Float(), reader.Float() ),
			};
			vertex.Bones[ 0 ] = reader.U16();
			vertex.Bones[ 1 ] = reader.U16();
			vertex.Weights[ 0 ] = reader.Byte() / 100f;
			vertex.Weights[ 1 ] = 1f - vertex.Weights[ 0 ];
			vertex.EdgeScale = reader.Byte() == 0 ? 1 : 0;
			model.Vertices.Add( vertex );
		}
		model.Indices = new int[ reader.Count( 2 ) ];
		for ( var i = 0; i < model.Indices.Length; i++ )
			model.Indices[ i ] = reader.U16();
		var materialCount = reader.Count( 70 );
		for ( var i = 0; i < materialCount; i++ )
		{
			var material = new MmdMaterial
			{
				Name = $"mmd_material_{i}",
				Diffuse = new Vector4( reader.Float(), reader.Float(), reader.Float(), reader.Float() ),
			};
			material.SpecularPower = reader.Float();
			material.Specular = reader.Vector();
			material.Ambient = reader.Vector();
			material.ToonIndex = reader.Byte();
			material.EdgeSize = reader.Byte() != 0 ? 1f : 0f;
			material.EdgeColor = new Vector4( 0, 0, 0, 1 );
			material.IndexCount = reader.Int();
			if ( material.IndexCount < 0 || material.IndexCount % 3 != 0 )
				throw new FormatException( "Invalid PMD material triangle count." );
			var references = reader.JapaneseText( 20 ).Split( '*' );
			material.Texture = references.FirstOrDefault( p =>
				!p.EndsWith( ".sph", StringComparison.OrdinalIgnoreCase )
				&& !p.EndsWith( ".spa", StringComparison.OrdinalIgnoreCase ) ) ?? "";
			material.SphereTexture = references.FirstOrDefault( p => p.EndsWith( ".sph", StringComparison.OrdinalIgnoreCase ) || p.EndsWith( ".spa", StringComparison.OrdinalIgnoreCase ) ) ?? "";
			material.SphereMode = material.SphereTexture.EndsWith( ".spa", StringComparison.OrdinalIgnoreCase ) ? 2 : material.SphereTexture.Length > 0 ? 1 : 0;
			material.DoubleSided = material.Diffuse.W < 1f;
			model.Materials.Add( material );
		}
		var boneCount = reader.U16();
		if ( boneCount > 4096 || boneCount > reader.Remaining / 39 )
			throw new FormatException( "Invalid PMD bone count." );
		for ( var i = 0; i < boneCount; i++ )
		{
			var bone = new MmdBone { Name = reader.JapaneseText( 20 ) };
			bone.Parent = reader.U16();
			if ( bone.Parent == 65535 )
				bone.Parent = -1;
			reader.U16(); // tail
			var type = reader.Byte();
			var source = reader.U16();
			if ( type == 5 )
			{
				bone.AppendParent = source;
				bone.AppendWeight = 1;
				bone.Flags = 0x100;
			}
			bone.Position = reader.Vector();
			model.Bones.Add( bone );
		}
		var ikCount = reader.U16();
		if ( ikCount > boneCount || ikCount > reader.Remaining / 11 )
			throw new FormatException( "Invalid PMD IK count." );
		for ( var i = 0; i < ikCount; i++ )
		{
			var controller = reader.U16();
			if ( controller >= boneCount )
				throw new FormatException( "Invalid PMD IK controller." );
			var bone = model.Bones[ controller ];
			bone.IkTarget = reader.U16();
			var links = reader.Byte();
			bone.Iterations = reader.U16();
			bone.AngleLimit = reader.Float() * 4f;
			for ( var j = 0; j < links; j++ )
			{
				var index = reader.U16();
				if ( index >= boneCount )
					throw new FormatException( "Invalid PMD IK link." );
				var knee = model.Bones[ index ].Name.Contains( "ひざ", StringComparison.Ordinal );
				bone.Links.Add( new MmdIkLink( index, knee,
					new Vector3( -MathF.PI, 0, 0 ), new Vector3( -0.002f, 0, 0 ) ) );
			}
		}
		MmdMorphReader.ReadPmd( reader, model );
		MmdPhysicsReader.ReadPmd( reader, model );
		return model;
	}
}
