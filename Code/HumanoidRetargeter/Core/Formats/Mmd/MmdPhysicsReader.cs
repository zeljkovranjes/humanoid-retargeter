namespace HumanoidRetargeter.Core.Formats.Mmd;

internal static class MmdPhysicsReader
{
	internal static void ReadPmx( MmdReader reader, MmdModel model, int boneSize, int morphSize, int rigidSize, int vertexSize, int materialSize )
	{
		if ( reader.Remaining == 0 )
			return;
		var displays = reader.Count( 13, 4096 );
		for ( var i = 0; i < displays; i++ )
		{
			reader.Text();
			reader.Text();
			reader.Byte();
			var entries = reader.Count( 2 );
			for ( var j = 0; j < entries; j++ )
				reader.Index( reader.Byte() == 0 ? boneSize : morphSize );
		}
		ReadBodies( reader, model, true, boneSize );
		ReadJoints( reader, model, true, rigidSize );
		if ( reader.Remaining > 0 )
			ReadSoftBodies( reader, model, rigidSize, vertexSize, materialSize );
	}

	internal static void ReadPmd( MmdReader reader, MmdModel model )
	{
		if ( reader.Remaining == 0 )
			return;
		reader.Skip( reader.Byte() * 2 );
		var displays = reader.Byte();
		reader.Skip( displays * 50 );
		reader.Skip( reader.Count( 3 ) * 3 );
		if ( reader.Remaining == 0 )
			return;
		if ( reader.Byte() != 0 )
			reader.Skip( 276 + model.Bones.Count * 20 + model.Morphs.Count * 20 + displays * 50 );
		var toons = new string[ 10 ];
		for ( var i = 0; i < toons.Length; i++ )
			toons[ i ] = reader.JapaneseText( 100 );
		foreach ( var material in model.Materials )
		{
			if ( material.ToonIndex >= 0 && material.ToonIndex < toons.Length )
			{
				var name = toons[ material.ToonIndex ];
				// MMD's built-in ramps are not normally included with a character archive.
				if ( !name.StartsWith( "toon", StringComparison.OrdinalIgnoreCase ) || !name.EndsWith( ".bmp", StringComparison.OrdinalIgnoreCase ) )
					material.ToonTexture = name;
			}
		}
		if ( reader.Remaining == 0 )
			return;
		ReadBodies( reader, model, false, 2 );
		ReadJoints( reader, model, false, 4 );
	}

	private static void ReadBodies( MmdReader reader, MmdModel model, bool pmx, int boneSize )
	{
		var count = reader.Count( pmx ? 69 : 83, 1024 );
		for ( var i = 0; i < count; i++ )
		{
			ReadName( reader, pmx );
			var body = new MmdRigidBody
			{
				Bone = reader.Index( boneSize ),
				Group = reader.Byte(),
				ExcludedGroups = reader.U16(),
				Shape = reader.Byte(),
				Size = reader.Vector(),
				Position = reader.Vector(),
				Rotation = reader.Vector(),
				Mass = reader.Float(),
				LinearDamping = reader.Float(),
				AngularDamping = reader.Float(),
				Restitution = reader.Float(),
				Friction = reader.Float(),
				Mode = reader.Byte(),
			};
			if ( body.Bone < -1 || body.Bone >= model.Bones.Count || body.Group > 15
				|| body.Shape > 2 || body.Mode > 2 || body.Mass < 0
				|| body.Size.X <= 0 || body.Size.Y < 0 || body.Size.Z < 0 )
				throw new FormatException( "Invalid MMD rigid body." );
			if ( !pmx && body.Bone >= 0 )
				body.Position += model.Bones[ body.Bone ].Position;
			model.RigidBodies.Add( body );
		}
	}

	private static void ReadJoints( MmdReader reader, MmdModel model, bool pmx, int rigidSize )
	{
		var count = reader.Count( pmx ? 107 : 124, 4096 );
		for ( var i = 0; i < count; i++ )
		{
			ReadName( reader, pmx );
			var kind = pmx ? reader.Byte() : 0;
			var joint = new MmdJoint
			{
				BodyA = reader.Index( rigidSize ),
				BodyB = reader.Index( rigidSize ),
				Position = reader.Vector(),
				Rotation = reader.Vector(),
				LinearMin = reader.Vector(),
				LinearMax = reader.Vector(),
				AngularMin = reader.Vector(),
				AngularMax = reader.Vector(),
				LinearSpring = reader.Vector(),
				AngularSpring = reader.Vector(),
			};
			if ( joint.BodyA < -1 || joint.BodyA >= model.RigidBodies.Count
				|| joint.BodyB < -1 || joint.BodyB >= model.RigidBodies.Count )
				throw new FormatException( "Invalid MMD joint body index." );
			if ( kind == 0 && joint.BodyA >= 0 && joint.BodyB >= 0 && joint.BodyA != joint.BodyB )
				model.Joints.Add( joint );
			else
				model.Notes.Add( "An unsupported or disconnected PMX joint was skipped." );
		}
	}

	private static void ReadName( MmdReader reader, bool pmx )
	{
		if ( pmx )
		{
			reader.Text();
			reader.Text();
		}
		else
		{
			reader.Skip( 20 );
		}
	}

	private static void ReadSoftBodies( MmdReader reader, MmdModel model, int rigidSize, int vertexSize, int materialSize )
	{
		var count = reader.Count( 140, 256 );
		for ( var i = 0; i < count; i++ )
		{
			var body = new MmdSoftBody { Name = reader.Text() };
			reader.Text();
			body.Shape = reader.Byte();
			body.Material = reader.Index( materialSize );
			body.Group = reader.Byte();
			body.ExcludedGroups = reader.U16();
			body.Flags = reader.Byte();
			body.LinkDistance = reader.Int();
			body.Clusters = reader.Int();
			body.Mass = reader.Float();
			body.Margin = reader.Float();
			body.AeroModel = reader.Int();
			if ( body.Shape > 1 || body.Material < 0 || body.Material >= model.Materials.Count || body.Group > 15
				|| body.Mass < 0 || body.Margin < 0 || body.LinkDistance < 0 || body.Clusters < 0 )
				throw new FormatException( "Invalid PMX soft body." );
			for ( var j = 0; j < body.Config.Length; j++ )
				body.Config[ j ] = reader.Float();
			for ( var j = 0; j < body.Cluster.Length; j++ )
				body.Cluster[ j ] = reader.Float();
			for ( var j = 0; j < body.Iterations.Length; j++ )
			{
				body.Iterations[ j ] = reader.Int();
				if ( body.Iterations[ j ] < 0 || body.Iterations[ j ] > 1024 )
					throw new FormatException( "Invalid PMX soft-body iteration count." );
			}
			for ( var j = 0; j < body.Stiffness.Length; j++ )
				body.Stiffness[ j ] = reader.Float();
			var anchors = reader.Count( rigidSize + vertexSize + 1 );
			for ( var j = 0; j < anchors; j++ )
			{
				var rigid = reader.Index( rigidSize );
				var vertex = reader.VertexIndex( vertexSize );
				var near = reader.Byte();
				if ( rigid < 0 || rigid >= model.RigidBodies.Count || vertex < 0 || vertex >= model.Vertices.Count || near > 1 )
					throw new FormatException( "Invalid PMX soft-body anchor." );
				body.Anchors.Add( (rigid, vertex, near == 1) );
			}
			var pins = reader.Count( vertexSize );
			for ( var j = 0; j < pins; j++ )
			{
				var vertex = reader.VertexIndex( vertexSize );
				if ( vertex < 0 || vertex >= model.Vertices.Count )
					throw new FormatException( "Invalid PMX soft-body pin." );
				body.Pins.Add( vertex );
			}
			model.SoftBodies.Add( body );
		}
	}
}
