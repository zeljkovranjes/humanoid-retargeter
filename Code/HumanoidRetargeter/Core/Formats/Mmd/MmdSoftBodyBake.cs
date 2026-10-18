namespace HumanoidRetargeter.Core.Formats.Mmd;

internal static class MmdSoftBodyBake
{
	internal static void Apply( MmdPlaybackData data, MmdModel model )
	{
		if ( model.SoftBodies.Count == 0 )
			return;
		var state = new MmdPlaybackState( data, model );
		state.Evaluate( 0 );
		var solvers = model.SoftBodies.Select( definition => new MmdSoftBodySolver( model, definition, state.Positions, state.Palette ) ).ToArray();
		var vertices = solvers.SelectMany( solver => solver.Vertices ).Distinct().OrderBy( i => i ).ToArray();
		if ( (long)vertices.Length * data.FrameCount > 16_000_000 )
			throw new FormatException( "MMD soft-body bake is too large. Split the clip or reduce its sample rate." );
		var output = new float[ vertices.Length * data.FrameCount * 3 ];
		var steps = Math.Max( 1, (int)MathF.Ceiling( 120 / data.Fps ) );
		for ( var frame = 0; frame < data.FrameCount; frame++ )
		{
			if ( frame > 0 )
			{
				for ( var step = 1; step <= steps; step++ )
				{
					state.Evaluate( frame - 1 + step / (float)steps );
					foreach ( var solver in solvers )
						solver.Step( state.Palette, state.Positions, 1 / (data.Fps * steps) );
				}
			}
			for ( var i = 0; i < vertices.Length; i++ )
			{
				var position = state.Positions[ vertices[ i ] ];
				var offset = (frame * vertices.Length + i) * 3;
				output[ offset ] = position.X;
				output[ offset + 1 ] = position.Y;
				output[ offset + 2 ] = position.Z;
			}
		}
		data.SoftVertices = vertices;
		data.SoftPositions = output;
	}
}
