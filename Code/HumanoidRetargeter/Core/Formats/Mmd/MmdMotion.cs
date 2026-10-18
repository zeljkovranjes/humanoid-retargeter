namespace HumanoidRetargeter.Core.Formats.Mmd;

internal sealed class MmdMotion
{
	internal Dictionary<string, List<MmdKeyframe>> Tracks = new( StringComparer.Ordinal );
	internal Dictionary<string, List<(int Frame, bool Enabled)>> IkStates = new( StringComparer.Ordinal );
	internal List<string> Notes = new();
	internal Dictionary<string, List<(int Frame, float Weight)>> Morphs = new( StringComparer.Ordinal );

	internal void Add( string name, MmdKeyframe key )
	{
		if ( !Tracks.TryGetValue( name, out var keys ) )
		{
			keys = new List<MmdKeyframe>();
			Tracks.Add( name, keys );
		}
		keys.Add( key );
	}

	internal void Sort()
	{
		foreach ( var name in Morphs.Keys.ToArray() )
			Morphs[ name ] = Morphs[ name ].GroupBy( key => key.Frame ).Select( group => group.Last() ).OrderBy( key => key.Frame ).ToList();
		foreach ( var name in Tracks.Keys.ToArray() )
			Tracks[ name ] = Tracks[ name ].GroupBy( key => key.Frame ).Select( group => group.Last() ).OrderBy( key => key.Frame ).ToList();
		foreach ( var name in IkStates.Keys.ToArray() )
			IkStates[ name ] = IkStates[ name ].OrderBy( key => key.Frame ).ToList();
	}
}
