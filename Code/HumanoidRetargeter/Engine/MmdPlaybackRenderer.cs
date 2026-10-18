#if SANDBOX
using Sandbox;
using HumanoidRetargeter.Core.Formats.Mmd;
using HumanoidRetargeter.Resources;

namespace HumanoidRetargeter.Engine;

internal sealed class MmdPlaybackRenderer : IDisposable
{
	private readonly MmdPlaybackState _state;
	private readonly List<MmdRenderPart> _parts = new();
	internal float Duration => (_state.Data.FrameCount - 1) / _state.Data.Fps;
	internal bool Looping => _state.Data.Looping;

	internal MmdPlaybackRenderer( SceneWorld world, MmdAnimation animation )
	{
		_state = new MmdPlaybackState( MmdPlaybackData.FromJson( animation.Payload ) );
		var model = _state.Model;
		if ( animation.Materials.Length != model.Materials.Count )
			throw new FormatException( "MMD playback material count does not match the model." );
		var start = 0;
		try
		{
			for ( var material = 0; material < model.Materials.Count; material++ )
			{
				var count = model.Materials[ material ].IndexCount;
				if ( count > 0 )
				{
					Add( world, material, animation.Materials[ material ], start, count, false );
					if ( material < animation.Outlines.Length && animation.Outlines[ material ] is { } outline )
						Add( world, material, outline, start, count, true );
				}
				start += count;
			}
			Update( 0, Transform.Zero );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	private void Add( SceneWorld world, int materialIndex, Material material, int start, int count, bool outline )
	{
		var source = _state.Model.Indices.Skip( start ).Take( count ).ToArray();
		var vertices = source.Distinct().ToArray();
		var mapping = vertices.Select( (vertex, index) => (vertex, index) ).ToDictionary( value => value.vertex, value => value.index );
		var indices = source.Select( index => mapping[ index ] ).ToArray();
		// MMD to engine changes handedness; the outline shell faces inward.
		if ( !outline )
			for ( var i = 0; i < indices.Length; i += 3 )
				(indices[ i + 1 ], indices[ i + 2 ]) = (indices[ i + 2 ], indices[ i + 1 ]);
		var buffer = new MmdRenderVertex[ vertices.Length ];
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer<MmdRenderVertex>( buffer.Length, MmdRenderVertex.Layout, buffer.AsSpan() );
		mesh.CreateIndexBuffer( indices.Length, indices.AsSpan() );
		var sceneObject = new SceneObject( world, Model.Builder.AddMesh( mesh ).Create(), Transform.Zero );
		_parts.Add( new MmdRenderPart
		{
			Material = materialIndex, Outline = outline, Vertices = vertices,
			Buffer = buffer, Mesh = mesh, Object = sceneObject,
		} );
	}

	internal void Update( float seconds, Transform transform )
	{
		_state.Evaluate( seconds * _state.Data.Fps );
		foreach ( var part in _parts )
		{
			var material = _state.Materials[ part.Material ];
			var bounds = new BBox();
			for ( var i = 0; i < part.Vertices.Length; i++ )
			{
				var vertex = part.Vertices[ i ];
				var position = _state.Positions[ vertex ];
				var normal = _state.Normals[ vertex ];
				if ( part.Outline )
					position += normal * (_state.Model.Vertices[ vertex ].EdgeScale * material.EdgeSize * 0.03f);
				var engineNormal = Direction( normal );
				var tangent = Vector3.Cross( engineNormal, MathF.Abs( engineNormal.z ) > 0.9f ? Vector3.Right : Vector3.Up ).Normal;
				var uv = _state.Uvs[ 0 ][ vertex ];
				var sphereUv = _state.Uvs[ 1 ][ vertex ];
				part.Buffer[ i ] = new MmdRenderVertex
				{
					Position = Direction( position ) * (MmdModel.UnitScaleCm * Core.RetargetTargetSpec.SboxSourceScale),
					Normal = part.Outline ? -engineNormal : engineNormal,
					Tangent = new Vector4( tangent.x, tangent.y, tangent.z, 1 ),
					Uv = new Vector2( uv.X, uv.Y ), SphereUv = new Vector2( sphereUv.X, sphereUv.Y ),
				};
				bounds = i == 0 ? new BBox( part.Buffer[ i ].Position, part.Buffer[ i ].Position ) : bounds.AddPoint( part.Buffer[ i ].Position );
			}
			part.Mesh.SetVertexBufferData<MmdRenderVertex>( part.Buffer.AsSpan() );
			part.Mesh.Bounds = bounds;
			part.Object.Transform = transform;
			part.Object.Bounds = bounds.Transform( transform );
			var attributes = part.Object.Attributes;
			attributes.Set( "MmdDiffuse", Four( part.Outline ? material.EdgeColor : material.Diffuse ) );
			attributes.Set( "MmdTextureTint", Four( material.TextureTint ) );
			attributes.Set( "MmdSphereTint", Four( material.SphereTint ) );
			attributes.Set( "MmdToonTint", Four( material.ToonTint ) );
			attributes.Set( "MmdSpecular", new Vector3( material.Specular.X, material.Specular.Y, material.Specular.Z ) );
			attributes.Set( "MmdAmbient", new Vector3( material.Ambient.X, material.Ambient.Y, material.Ambient.Z ) );
			attributes.Set( "MmdSpecularPower", material.SpecularPower );
		}
	}

	private static Vector3 Direction( System.Numerics.Vector3 value ) => new( value.X, value.Z, value.Y );
	private static Vector4 Four( System.Numerics.Vector4 value ) => new( value.X, value.Y, value.Z, value.W );

	public void Dispose()
	{
		foreach ( var part in _parts )
			part.Object.Delete();
		_parts.Clear();
	}
}
#endif
