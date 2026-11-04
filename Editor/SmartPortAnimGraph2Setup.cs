#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Core.Maths;
using HumanoidRetargeter.Core.Target;
using HumanoidRetargeter.EditorTools.Embedded.Datamodel;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// The AnimGraph2 half of "Use the source's setup": the target's own skeleton, a clip per animation the source's graph
/// plays and that graph rebased onto them (see <see cref="SmartPortAnimGraph2Port"/>). A clip the source model also has
/// as a sequence reuses that sequence's retargeted file; any other is sampled from its compiled clip and retargeted the
/// same way.
/// </summary>
internal static class SmartPortAnimGraph2Setup
{
	// A one-frame clip reports no frame rate, but its DMX needs one
	const float SingleFrameRate = 30f;

	// How long to wait between asking for compiles the editor refused
	const int CompileRetryMilliseconds = 2000;

	// Root motion smaller than this (engine units, degrees) is no root motion: the Human's crouch clips drift a tenth of a
	// unit, and extracting that axis would take the pelvis' sway along it out of the pose
	const float MotionTolerance = 1f;

	// A sequence holds the same animation as a clip when its sampled bones are this close (engine units, radians): the
	// two compilers compress differently
	const float PositionTolerance = .05f;
	const float RotationTolerance = .01f;

	/// <summary>
	/// The rig an AnimGraph2 source's clips are retargeted onto <paramref name="target"/> with: <paramref name="source"/>'s
	/// skeleton, the IK goals <paramref name="graph"/> (its <c>.ag</c>) reaches for whose bones it has, and the attachment
	/// bones of <paramref name="compiledSource"/> (its compiled model). Throws <see cref="ArgumentException"/> when the two
	/// don't map.
	/// </summary>
	internal static SmartPortRig Rig( Model source, HumanoidRetargeter.Core.Skeleton.Skeleton target, string graph, string compiledSource )
		=> new( TargetPickers.SkeletonFromModel( source ), target, IkTargets( source, SmartPortAnimGraph2Port.IkTargets( graph ) ),
			SmartPortAttachments.BoneNames( compiledSource ) );

	// The IK goals both bones of which the source model has, by its own bone names
	static IReadOnlyDictionary<string, string> IkTargets( Model source, IReadOnlyDictionary<string, string> goals )
	{
		var bones = source.Bones.AllBones.ToDictionary( b => b.Name, b => b.Name, StringComparer.OrdinalIgnoreCase );
		return goals.Where( g => bones.ContainsKey( g.Key ) && bones.ContainsKey( g.Value ) ).ToDictionary( g => bones[g.Key], g => bones[g.Value], StringComparer.Ordinal );
	}

	/// <summary>
	/// Writes the target's skeleton and clips under <paramref name="dataFolder"/> and returns <paramref name="graph"/> (the
	/// source's <c>.ag</c>) rebased onto them for <paramref name="modelPath"/>, with the skeleton and clip sources written.
	/// <paramref name="vmdl"/> is the ported model, whose animation files are the recovered, retargeted sequences of
	/// <paramref name="compiledSource"/> (the source's compiled model); both are null when there are no such sequences, and
	/// every clip is sampled. A file that already holds what would be written is left as it is, so running this again
	/// leaves it, and its compiled file, alone. Throws when the source graph's skeleton or a clip it plays doesn't load.
	/// </summary>
	internal static async Task<(string Graph, IReadOnlyList<string> Sources)> WriteAsync( string graph, string modelPath, string vmdl, string compiledSource, string dataFolder,
		string name, SmartPortRig rig, CancellationToken token )
	{
		await EditorPipeline.SwitchToMainThread();
		var folder = dataFolder + "/animgraph2";
		var skeletonPath = AnimGraph2Document.SkeletonOf( graph );
		var compiledSkeleton = AssetSystem.FindByPath( skeletonPath )?.GetCompiledFile( true );
		if ( string.IsNullOrEmpty( compiledSkeleton ) || !File.Exists( compiledSkeleton ) )
			throw new InvalidOperationException( "The source graph's skeleton has no compiled file: " + skeletonPath );
		var export = new SmartPortAnimationExport( rig );
		var bindPose = folder + "/" + name + "_skeleton.dmx";
		var additiveBase = folder + "/additive_base.dmx";
		var skeleton = folder + "/" + name + ".skel";
		Directory.CreateDirectory( CitizenAnimationModels.ProjectFile( folder + "/clips" ) );
		await WriteIfChangedAsync( bindPose, Dmx( bindPose, () => export.Write( identity: false ) ), token );
		await WriteIfChangedAsync( additiveBase, Dmx( additiveBase, () => export.Write( identity: true ) ), token );
		var masks = CompiledAssetRecovery.ReadData( compiledSkeleton, skeletonPath );
		await WriteIfChangedAsync( skeleton, Encoding.UTF8.GetBytes(
			SmartPortAnimGraph2Port.Skeleton( bindPose, masks, rig.BoneNames, rig.Target.Bones.Select( b => b.Name ).ToArray() ) ), token );

		var sequences = vmdl is null ? new Dictionary<string, string>() : SmartPortAnimGraph2.AnimationFiles( vmdl );
		var samples = compiledSource is null ? new Dictionary<string, (bool, int, XForm[][])>()
			: await Task.Run( () => SmartPortAnimationExport.ReadSamples( compiledSource, rig ), token );
		await EditorPipeline.SwitchToMainThread();
		var animations = new Dictionary<string, string>( StringComparer.Ordinal );
		var files = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var invalid = Path.GetInvalidFileNameChars();
		foreach ( var animation in AnimGraph2Document.Animations( AnimGraph2Document.CopyForModel( graph, modelPath ) ) )
		{
			token.ThrowIfCancellationRequested();
			var clip = SourceClip.Load( animation );
			var stem = AnimGraph2Document.AnimationStem( animation );
			// Lowercase, like the compiled files the engine writes beside them
			var file = string.Concat( stem.ToLowerInvariant().Select( c => invalid.Contains( c ) ? '_' : c ) );
			for ( var suffix = 2; !files.Add( file ); suffix++ ) file = stem.ToLowerInvariant() + "_" + suffix;
			// A sequence of the source model's with the clip's name and data is already recovered and retargeted
			var source = sequences.TryGetValue( stem, out var sequence ) && samples.TryGetValue( stem, out var sample ) && clip.Matches( sample, rig.Source )
				? sequence : folder + "/clips/" + file + ".dmx";
			if ( source != sequence )
			{
				var fps = clip.FrameRate > 0 ? clip.FrameRate : SingleFrameRate;
				var frames = clip.Sample( rig.Source );
				var movement = clip.Movement( fps );
				var dmx = await Task.Run( () => Dmx( source, () => export.Write( frames, fps, clip.IsAdditive, movement ) ), token );
				await EditorPipeline.SwitchToMainThread();
				await WriteIfChangedAsync( source, dmx, token );
			}
			var anim = folder + "/clips/" + file + ".anim";
			// The clip's events (footsteps) are in its compiled data; the sequence's are in the model, which AnimGraph2 doesn't read
			var compiledClip = AssetSystem.FindByPath( animation )?.GetCompiledFile( true );
			var events = string.IsNullOrEmpty( compiledClip ) || !File.Exists( compiledClip ) ? null : CompiledAssetRecovery.ReadData( compiledClip, animation );
			await WriteIfChangedAsync( anim, Encoding.UTF8.GetBytes(
				SmartPortAnimGraph2Port.Clip( source, skeleton, clip.IsAdditive ? additiveBase : null, events, clip.RootMotionAxes() ) ), token );
			animations[animation] = anim;
		}
		return (SmartPortAnimGraph2Port.Rebase( graph, modelPath, skeleton, animations, rig.BoneNames ), animations.Values.Prepend( skeleton ).ToArray());
	}

	// A DMX with element ids derived from its path rather than random, so the same content writes the same bytes
	static byte[] Dmx( string path, Func<byte[]> write )
	{
		using var ids = ElementIds.Deterministic( path );
		return write();
	}

	// Writes a generated source (asset path) unless it already holds exactly this, which keeps its compiled file current
	static async Task WriteIfChangedAsync( string path, byte[] content, CancellationToken token )
	{
		var file = CitizenAnimationModels.ProjectFile( path );
		if ( File.Exists( file ) && new FileInfo( file ).Length == content.Length && (await File.ReadAllBytesAsync( file, token )).AsSpan().SequenceEqual( content ) )
			return;
		await File.WriteAllBytesAsync( file, content, token );
	}

	/// <summary>
	/// Of <paramref name="sources"/> (asset paths), those that still have no current compiled file after
	/// <paramref name="timeoutSeconds"/>. The editor refuses compiles while its own content build runs, which hundreds of
	/// new clips start, and that build compiles them in the meantime: keep asking until each is compiled or the time is up.
	/// Sources a run left unchanged are current already and cost nothing.
	/// </summary>
	internal static async Task<string[]> UncompiledAsync( IReadOnlyList<string> sources, float timeoutSeconds, CancellationToken token )
	{
		await EditorPipeline.SwitchToMainThread();
		var watch = System.Diagnostics.Stopwatch.StartNew();
		string[] missing;
		while ( (missing = sources.Where( path => !IsCompiled( path ) ).ToArray()).Length > 0 && watch.Elapsed.TotalSeconds < timeoutSeconds )
		{
			// Dependency order: the skeleton before the clips compiled against it
			foreach ( var path in missing.OrderBy( p => p.EndsWith( ".skel", StringComparison.OrdinalIgnoreCase ) ? 0 : 1 ) )
				if ( (AssetSystem.FindByPath( path ) ?? AssetSystem.RegisterFile( CitizenAnimationModels.ProjectFile( path ) )) is { } asset )
					await asset.CompileIfNeededAsync();
			await Task.Delay( CompileRetryMilliseconds, token );
		}
		return missing;
	}

	// Whether the compiled file beside the source is newer than every file it is compiled from; the engine writes
	// compiled names in lowercase
	static bool IsCompiled( string path )
	{
		var file = CitizenAnimationModels.ProjectFile( path );
		var folder = Path.GetDirectoryName( file );
		var compiled = Directory.Exists( folder ) ? Directory.EnumerateFiles( folder, Path.GetFileName( file ) + "_c",
			new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive } ).FirstOrDefault() : null;
		if ( compiled is null ) return false;
		var stamp = File.GetLastWriteTimeUtc( compiled );
		return Inputs( path ).All( input => File.GetLastWriteTimeUtc( input ) <= stamp );
	}

	// The project files a skeleton or clip source (asset path) is compiled from: itself, the DMX it imports and, for a
	// clip, its additive base and its skeleton's inputs. Files outside the project don't change
	static IEnumerable<string> Inputs( string path )
	{
		var file = Path.Combine( Project.Current.GetAssetsPath(), path );
		if ( !File.Exists( file ) ) yield break;
		yield return file;
		if ( !path.EndsWith( ".skel", StringComparison.OrdinalIgnoreCase ) && !path.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) ) yield break;
		var source = (KvObject)Kv3.Parse( File.ReadAllText( file ) ).Root;
		foreach ( var key in new[] { "m_sourceFilename", "m_additiveBaseFilename", "m_animationSkeletonName" } )
			if ( source.GetString( key ) is { Length: > 0 } input )
				foreach ( var dependency in Inputs( input ) )
					yield return dependency;
	}

	/// <summary>A compiled AnimGraph2 clip (<c>Sandbox.Animation.Clip</c>), reached by reflection: older engines have none.</summary>
	sealed class SourceClip
	{
		Func<float, int, Transform> _local;
		Func<float, Transform> _rootMotion;
		Dictionary<string, int> _bones;

		public bool IsAdditive { get; private init; }
		public int FrameCount { get; private init; }
		public float FrameRate { get; private init; }
		public float Duration { get; private init; }

		public static SourceClip Load( string path )
		{
			var type = typeof( Model ).Assembly.GetType( "Sandbox.Animation.Clip" ) ?? throw new InvalidOperationException( "This engine has no AnimGraph2 clips." );
			var clip = type.GetMethod( "Load", new[] { typeof( string ) } )?.Invoke( null, new object[] { path } );
			T Get<T>( object owner, string property ) => (T)owner.GetType().GetProperty( property )!.GetValue( owner )!;
			if ( clip is null || Get<bool>( clip, "IsError" ) ) throw new InvalidOperationException( "A clip the source graph plays did not load: " + path );
			var skeleton = Get<object>( clip, "Skeleton" );
			var bones = new Dictionary<string, int>( StringComparer.Ordinal );
			var index = 0;
			foreach ( var bone in Get<System.Collections.IEnumerable>( skeleton, "Bones" ) )
				bones[Get<string>( bone, "Name" )] = index++;
			return new SourceClip
			{
				_local = (Func<float, int, Transform>)Delegate.CreateDelegate( typeof( Func<float, int, Transform> ), clip, type.GetMethod( "GetLocalTransform" )! ),
				_rootMotion = (Func<float, Transform>)Delegate.CreateDelegate( typeof( Func<float, Transform> ), clip, type.GetMethod( "GetRootMotion" )! ),
				_bones = bones,
				IsAdditive = Get<bool>( clip, "IsAdditive" ),
				FrameCount = Math.Max( Get<int>( clip, "FrameCount" ), 1 ),
				FrameRate = Get<float>( clip, "FrameRate" ),
				Duration = Get<float>( clip, "Duration" ),
			};
		}

		// The clip API samples by cycle, 0 to 1 over the clip
		float Cycle( int frame ) => FrameCount > 1 ? frame / (float)(FrameCount - 1) : 0;

		/// <summary>Whether a sequence's <paramref name="sample"/> (see <see cref="SmartPortAnimationExport.ReadSamples"/>) holds
		/// this clip's animation: as additive, as long, and posed the same at the sampled frames.</summary>
		public bool Matches( (bool Additive, int FrameCount, XForm[][] Poses) sample, HumanoidRetargeter.Core.Skeleton.Skeleton skeleton )
		{
			if ( sample.Additive != IsAdditive || sample.FrameCount != FrameCount ) return false;
			var frames = SmartPortAnimationExport.SampleFrames( FrameCount );
			for ( var f = 0; f < frames.Length; f++ )
				for ( var b = 0; b < skeleton.Count; b++ )
				{
					if ( !_bones.TryGetValue( skeleton[b].Name, out var index ) ) continue;
					var pose = ToXForm( _local( Cycle( frames[f] ), index ) );
					var other = sample.Poses[f][b];
					var dot = MathF.Min( MathF.Abs( System.Numerics.Quaternion.Dot( pose.Rot, other.Rot ) ), 1 );
					if ( System.Numerics.Vector3.Distance( pose.Pos, other.Pos ) > PositionTolerance || 2 * MathF.Acos( dot ) > RotationTolerance ) return false;
				}
			return true;
		}

		/// <summary>Each frame's local transforms in <paramref name="skeleton"/>'s bone order (deltas for an additive clip);
		/// bones the clip lacks keep their bind pose, or no change.</summary>
		public XForm[][] Sample( HumanoidRetargeter.Core.Skeleton.Skeleton skeleton )
		{
			var indices = skeleton.Bones.Select( b => _bones.TryGetValue( b.Name, out var i ) ? i : -1 ).ToArray();
			var frames = new XForm[FrameCount][];
			for ( var f = 0; f < FrameCount; f++ )
			{
				var cycle = Cycle( f );
				frames[f] = new XForm[indices.Length];
				for ( var b = 0; b < indices.Length; b++ )
					frames[f][b] = indices[b] < 0 ? (IsAdditive ? XForm.Identity : skeleton[b].RestLocal) : ToXForm( _local( cycle, indices[b] ) );
			}
			return frames;
		}

		/// <summary>The root motion (position, yaw in degrees) at a time in seconds for a clip sampled at <paramref name="fps"/>,
		/// or null when the clip has none. Sampled here: the DMX is written off the main thread.</summary>
		public Func<float, (System.Numerics.Vector3 Position, float Angle)> Movement( float fps )
		{
			var motion = RootMotion();
			if ( RootMotionAxes() is null ) return null;
			return time => motion[Math.Clamp( (int)MathF.Round( time * fps ), 0, motion.Length - 1 )];
		}

		/// <summary>The axes the clip's root motion moves along, or null when it has none.</summary>
		public (bool X, bool Y, bool Z, bool Yaw)? RootMotionAxes()
		{
			var motion = RootMotion();
			bool Moves( Func<(System.Numerics.Vector3 Position, float Angle), float> axis ) => motion.Any( m => MathF.Abs( axis( m ) ) > MotionTolerance );
			var axes = (Moves( m => m.Position.X ), Moves( m => m.Position.Y ), Moves( m => m.Position.Z ), Moves( m => m.Angle ));
			return axes is (false, false, false, false) ? null : axes;
		}

		(System.Numerics.Vector3 Position, float Angle)[] _motion;

		(System.Numerics.Vector3 Position, float Angle)[] RootMotion()
			=> _motion ??= Enumerable.Range( 0, FrameCount ).Select( f => _rootMotion( Cycle( f ) ) )
				.Select( m => (new System.Numerics.Vector3( m.Position.x, m.Position.y, m.Position.z ), m.Rotation.Yaw()) ).ToArray();

		static XForm ToXForm( Transform transform ) => new( new System.Numerics.Vector3( transform.Position.x, transform.Position.y, transform.Position.z ),
			new System.Numerics.Quaternion( transform.Rotation.x, transform.Rotation.y, transform.Rotation.z, transform.Rotation.w ) );
	}
}
