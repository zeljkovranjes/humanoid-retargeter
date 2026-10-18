// Position prediction and distance projection adapted from masaka1024/mmd2gltf-cs-physics,
// Core/SoftBody.cs. Copyright (c) 2026 masaka1024, MIT; see THIRD_PARTY_NOTICES.md.
using System.Numerics;
using HumanoidRetargeter.Core.Maths;

namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

internal sealed class MmdSoftBodySolver
{
	private readonly MmdModel _model;
	private readonly MmdSoftBody _definition;
	private readonly Vector3[] _positions;
	private readonly Vector3[] _previous;
	private readonly Vector3[] _velocity;
	private readonly float[] _inverseMass;
	private readonly List<(int A, int B, float Length, float Stiffness)> _links = new();
	private readonly List<(int Node, int Body, Vector3 Pivot)> _anchors = new();
	private readonly XForm[] _bodies;
	internal int[] Vertices { get; }

	internal MmdSoftBodySolver( MmdModel model, MmdSoftBody definition, Vector3[] initial, XForm[] palette )
	{
		_model = model;
		_definition = definition;
		var start = 0;
		for ( var i = 0; i < definition.Material; i++ )
			start += model.Materials[ i ].IndexCount;
		var indices = model.Indices.Skip( start ).Take( model.Materials[ definition.Material ].IndexCount ).ToArray();
		Vertices = indices.Distinct().OrderBy( i => i ).ToArray();
		_positions = Vertices.Select( i => initial[ i ] ).ToArray();
		_previous = _positions.ToArray();
		_velocity = new Vector3[ Vertices.Length ];
		_inverseMass = Vertices.Select( i => definition.Pins.Contains( i ) || definition.Mass == 0 ? 0f : Vertices.Length / definition.Mass ).ToArray();
		_bodies = new XForm[ model.RigidBodies.Count ];
		UpdateBodies( palette );
		var nodes = Vertices.Select( (vertex, node) => (vertex, node) ).ToDictionary( pair => pair.vertex, pair => pair.node );
		var edges = new HashSet<(int, int)>();
		void Link( int a, int b, float stiffness )
		{
			if ( a == b || !edges.Add( (Math.Min( a, b ), Math.Max( a, b )) ) )
				return;
			_links.Add( (a, b, Vector3.Distance( _positions[ a ], _positions[ b ] ), Math.Clamp( stiffness, 0, 1 )) );
		}
		if ( definition.Shape == 1 )
		{
			for ( var i = 1; i < Vertices.Length; i++ )
				Link( i - 1, i, definition.Stiffness[ 0 ] );
		}
		else
		{
			for ( var i = 0; i < indices.Length; i += 3 )
			{
				Link( nodes[ indices[ i ] ], nodes[ indices[ i + 1 ] ], definition.Stiffness[ 0 ] );
				Link( nodes[ indices[ i + 1 ] ], nodes[ indices[ i + 2 ] ], definition.Stiffness[ 0 ] );
				Link( nodes[ indices[ i + 2 ] ], nodes[ indices[ i ] ], definition.Stiffness[ 0 ] );
			}
		}
		if ( (definition.Flags & 1) != 0 && definition.LinkDistance > 1 )
		{
			var adjacency = Enumerable.Range( 0, Vertices.Length ).Select( _ => new List<int>() ).ToArray();
			foreach ( var link in _links )
			{
				adjacency[ link.A ].Add( link.B );
				adjacency[ link.B ].Add( link.A );
			}
			for ( var source = 0; source < Vertices.Length; source++ )
			{
				var visited = new HashSet<int> { source };
				var queue = new Queue<(int Node, int Depth)>();
				queue.Enqueue( (source, 0) );
				while ( queue.Count > 0 )
				{
					var (node, depth) = queue.Dequeue();
					if ( depth == Math.Min( definition.LinkDistance, 16 ) )
					{
						Link( source, node, definition.Stiffness[ 1 ] );
						continue;
					}
					foreach ( var next in adjacency[ node ] )
						if ( visited.Add( next ) )
							queue.Enqueue( (next, depth + 1) );
				}
			}
		}
		foreach ( var anchor in definition.Anchors )
		{
			if ( !nodes.TryGetValue( anchor.Vertex, out var node ) )
				throw new FormatException( "PMX soft-body anchor is outside its material." );
			_anchors.Add( (node, anchor.Body, MmdSkinning.Point( _bodies[ anchor.Body ].Inverse(), _positions[ node ] )) );
		}
	}

	internal void Step( XForm[] palette, Vector3[] animated, float dt )
	{
		UpdateBodies( palette );
		var damping = MathF.Pow( 1 - Math.Clamp( _definition.Config[ 1 ], 0, 1 ), dt * 60 );
		for ( var i = 0; i < _positions.Length; i++ )
		{
			_previous[ i ] = _positions[ i ];
			if ( _inverseMass[ i ] == 0 )
			{
				_positions[ i ] = animated[ Vertices[ i ] ];
				continue;
			}
			_velocity[ i ] = (_velocity[ i ] + new Vector3( 0, -98, 0 ) * dt) * damping;
			_positions[ i ] += _velocity[ i ] * dt;
		}
		for ( var iteration = 0; iteration < Math.Clamp( _definition.Iterations[ 1 ], 1, 128 ); iteration++ )
		{
			foreach ( var link in _links )
			{
				var mass = _inverseMass[ link.A ] + _inverseMass[ link.B ];
				var delta = _positions[ link.B ] - _positions[ link.A ];
				var length = delta.Length();
				if ( mass <= 0 || length < 1e-8f )
					continue;
				var correction = delta * ((length - link.Length) / length * link.Stiffness / mass);
				_positions[ link.A ] += correction * _inverseMass[ link.A ];
				_positions[ link.B ] -= correction * _inverseMass[ link.B ];
			}
			foreach ( var anchor in _anchors )
			{
				if ( _inverseMass[ anchor.Node ] > 0 )
					_positions[ anchor.Node ] = Vector3.Lerp( _positions[ anchor.Node ],
						MmdSkinning.Point( _bodies[ anchor.Body ], anchor.Pivot ), Math.Clamp( _definition.Config[ 11 ], 0, 1 ) );
			}
			Collide();
		}
		for ( var i = 0; i < _positions.Length; i++ )
		{
			_velocity[ i ] = (_positions[ i ] - _previous[ i ]) / dt;
			if ( !float.IsFinite( _positions[ i ].LengthSquared() ) )
				throw new FormatException( "MMD soft-body simulation became unstable." );
			animated[ Vertices[ i ] ] = _positions[ i ];
		}
	}

	private void UpdateBodies( XForm[] palette )
	{
		for ( var i = 0; i < _bodies.Length; i++ )
		{
			var body = _model.RigidBodies[ i ];
			var rotation = Quaternion.CreateFromAxisAngle( Vector3.UnitZ, body.Rotation.Z )
				* Quaternion.CreateFromAxisAngle( Vector3.UnitY, body.Rotation.Y ) * Quaternion.CreateFromAxisAngle( Vector3.UnitX, body.Rotation.X );
			var rest = new XForm( body.Position, rotation );
			_bodies[ i ] = body.Bone < 0 ? rest : XForm.Compose( palette[ body.Bone ], rest );
		}
	}

	private void Collide()
	{
		for ( var node = 0; node < _positions.Length; node++ )
		{
			if ( _inverseMass[ node ] == 0 )
				continue;
			_positions[ node ].Y = MathF.Max( _positions[ node ].Y, _definition.Margin );
			for ( var i = 0; i < _bodies.Length; i++ )
			{
				var body = _model.RigidBodies[ i ];
				if ( (_definition.ExcludedGroups & (1 << body.Group)) != 0 || (body.ExcludedGroups & (1 << _definition.Group)) != 0 )
					continue;
				var local = MmdSkinning.Point( _bodies[ i ].Inverse(), _positions[ node ] );
				var original = local;
				if ( body.Shape == 1 )
				{
					var size = body.Size + new Vector3( _definition.Margin );
					var distance = size - Vector3.Abs( local );
					if ( distance.X <= 0 || distance.Y <= 0 || distance.Z <= 0 )
						continue;
					if ( distance.X < distance.Y && distance.X < distance.Z )
						local.X = MathF.CopySign( size.X, local.X );
					else if ( distance.Y < distance.Z )
						local.Y = MathF.CopySign( size.Y, local.Y );
					else
						local.Z = MathF.CopySign( size.Z, local.Z );
				}
				else
				{
					var center = body.Shape == 2 ? new Vector3( 0, Math.Clamp( local.Y, -body.Size.Y * 0.5f, body.Size.Y * 0.5f ), 0 ) : Vector3.Zero;
					var delta = local - center;
					var length = delta.Length();
					var radius = body.Size.X + _definition.Margin;
					if ( length >= radius )
						continue;
					local = center + (length > 1e-8f ? delta / length : Vector3.UnitY) * radius;
				}
				_positions[ node ] = MmdSkinning.Point( _bodies[ i ], local );
				if ( local != original )
					_previous[ node ] = Vector3.Lerp( _previous[ node ], _positions[ node ], Math.Clamp( _definition.Config[ 6 ], 0, 1 ) );
			}
		}
	}
}
