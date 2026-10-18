namespace HumanoidRetargeter.Core.Formats.Mmd;

// Propagate each group once in dependency order, even when a morph has several parents.
internal sealed class MmdMorphWeights
{
	private readonly MmdModel _model;
	private readonly int[] _order;
	internal float[] Values { get; }

	internal MmdMorphWeights( MmdModel model )
	{
		_model = model;
		Values = new float[ model.Morphs.Count ];
		var order = new List<int>();
		var visited = new bool[ Values.Length ];
		void Visit( int index )
		{
			if ( visited[ index ] )
				return;
			visited[ index ] = true;
			foreach ( var child in model.Morphs[ index ].Groups )
				Visit( child.Morph );
			order.Add( index );
		}
		for ( var i = 0; i < Values.Length; i++ )
			Visit( i );
		order.Reverse();
		_order = order.ToArray();
	}

	internal void Sample( IReadOnlyDictionary<string, float[]> tracks, float frame )
	{
		Array.Clear( Values, 0, Values.Length );
		for ( var i = 0; i < Values.Length; i++ )
		{
			if ( !tracks.TryGetValue( _model.Morphs[ i ].Name, out var values ) || values.Length == 0 )
				continue;
			var index = Math.Clamp( (int)frame, 0, values.Length - 1 );
			Values[ i ] = values[ index ] + (values[ Math.Min( index + 1, values.Length - 1 ) ] - values[ index ])
				* Math.Clamp( frame - index, 0, 1 );
		}
		Expand();
	}

	internal void Expand()
	{
		foreach ( var index in _order )
			foreach ( var child in _model.Morphs[ index ].Groups )
				Values[ child.Morph ] += Values[ index ] * child.Weight;
	}
}
