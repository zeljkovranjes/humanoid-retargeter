namespace HumanoidRetargeter.Core.Formats.Mmd;

using Vector3 = System.Numerics.Vector3;

// A generic humanoid in MMD units. No mesh, model assets or external runtime needed.
internal static class MmdStandardSkeleton
{
	internal static MmdModel Create( MmdMotion motion )
	{
		var model = new MmdModel();
		int Add( string name, int parent, Vector3 position )
		{
			model.Bones.Add( new MmdBone { Name = name, Parent = parent, Position = position } );
			return model.Bones.Count - 1;
		}
		string Name( params string[] names )
			=> names.FirstOrDefault( name => motion.Tracks.ContainsKey( name ) || motion.IkStates.ContainsKey( name ) ) ?? names[ 0 ];
		int Optional( string name, int parent, Vector3 position )
			=> motion.Tracks.ContainsKey( name ) ? Add( name, parent, position ) : parent;

		var root = Add( "全ての親", -1, Vector3.Zero );
		var center = Add( "センター", root, new Vector3( 0, 8, 0 ) );
		var groove = Optional( "グルーブ", center, new Vector3( 0, 8, 0 ) );
		var waist = Optional( "腰", groove, new Vector3( 0, 11.5f, 0 ) );
		var hips = Add( "下半身", waist, new Vector3( 0, 11.5f, 0 ) );
		var spine = Add( "上半身", waist, new Vector3( 0, 12, 0 ) );
		spine = Optional( Name( "上半身2", "上半身２" ), spine, new Vector3( 0, 14, 0 ) );
		spine = Optional( Name( "上半身3", "上半身３" ), spine, new Vector3( 0, 15, 0 ) );
		var neck = Add( "首", spine, new Vector3( 0, 16, 0 ) );
		var head = Add( "頭", neck, new Vector3( 0, 17.3f, 0 ) );
		var eyes = Optional( "両目", head, new Vector3( 0, 18, -0.7f ) );

		foreach ( var (prefix, side) in new[] { ("左", 1f), ("右", -1f) } )
		{
			Vector3 Point( float x, float y, float z = 0 ) => new( side * x, y, z );
			Optional( prefix + "目", eyes, Point( 0.35f, 18, -0.7f ) );
			var shoulderParent = Optional( prefix + "肩P", spine, Point( 1, 15.8f ) );
			var shoulder = Add( prefix + "肩", shoulderParent, Point( 1, 15.8f ) );
			var shoulderCancel = Optional( prefix + "肩C", shoulder, Point( 1.9f, 15.8f ) );
			if ( shoulderParent != spine && shoulderCancel != shoulder )
			{
				model.Bones[ shoulderCancel ].Flags = 0x100;
				model.Bones[ shoulderCancel ].AppendParent = shoulderParent;
				model.Bones[ shoulderCancel ].AppendWeight = -1;
			}
			var arm = Add( prefix + "腕", shoulderCancel, Point( 1.9f, 15.8f ) );
			arm = Optional( prefix + "腕捩", arm, Point( 3.4f, 14.7f ) );
			var elbow = Add( prefix + "ひじ", arm, Point( 4.9f, 13.6f ) );
			elbow = Optional( prefix + "手捩", elbow, Point( 6.2f, 12.6f ) );
			var hand = Add( prefix + "手首", elbow, Point( 7.5f, 11.6f ) );
			var thumb = Add( Name( prefix + "親指０", prefix + "親指0" ), hand, Point( 7.7f, 11.4f, -0.3f ) );
			thumb = Add( Name( prefix + "親指１", prefix + "親指1" ), thumb, Point( 8, 11.1f, -0.6f ) );
			Add( Name( prefix + "親指２", prefix + "親指2" ), thumb, Point( 8.3f, 10.9f, -0.7f ) );
			var fingers = new[] { "人指", "中指", "薬指", "小指" };
			for ( var finger = 0; finger < fingers.Length; finger++ )
			{
				var parent = hand;
				for ( var joint = 1; joint <= 3; joint++ )
				{
					var name = Name( prefix + fingers[ finger ] + (char)('０' + joint), prefix + fingers[ finger ] + joint );
					parent = Add( name, parent, Point( 7.8f + joint * 0.3f, 11.4f - joint * 0.22f, -0.35f + finger * 0.25f ) );
				}
			}

			var thigh = Add( prefix + "足", hips, Point( 0.9f, 11.3f ) );
			var knee = Add( prefix + "ひざ", thigh, Point( 0.9f, 6.5f, -0.1f ) );
			var anklePosition = Point( 0.9f, 1.5f );
			var toePosition = Point( 0.9f, 0.3f, -1.4f );
			var ankle = Add( prefix + "足首", knee, anklePosition );
			var toe = Add( prefix + "つま先", ankle, toePosition );
			var ikParent = Optional( Name( prefix + "足IK親", prefix + "足ＩＫ親" ), root, Point( 0.9f, 0 ) );
			var footIk = Add( Name( prefix + "足ＩＫ", prefix + "足IK" ), ikParent, anklePosition );
			var toeIk = Add( Name( prefix + "つま先ＩＫ", prefix + "つま先IK" ), footIk, toePosition );
			// FK-only poses must not be pulled back to unanimated IK controllers.
			if ( motion.Tracks.ContainsKey( model.Bones[ footIk ].Name )
				|| motion.IkStates.ContainsKey( model.Bones[ footIk ].Name ) )
			{
				var controller = model.Bones[ footIk ];
				controller.IkTarget = ankle;
				controller.Iterations = 64;
				controller.AngleLimit = 0.5f;
				controller.Links.Add( new MmdIkLink( knee, true, new Vector3( -MathF.PI, 0, 0 ), Vector3.Zero ) );
				controller.Links.Add( new MmdIkLink( thigh, false, Vector3.Zero, Vector3.Zero ) );
			}
			if ( motion.Tracks.ContainsKey( model.Bones[ toeIk ].Name ) )
			{
				var controller = model.Bones[ toeIk ];
				controller.IkTarget = toe;
				controller.Iterations = 16;
				controller.AngleLimit = 0.5f;
				controller.Links.Add( new MmdIkLink( ankle, false, Vector3.Zero, Vector3.Zero ) );
			}
		}
		model.Notes.Add( "MMD: using the built-in standard source skeleton. Select a PMX/PMD source model for exact proportions, custom bones and model physics." );
		model.Validate();
		return model;
	}
}
