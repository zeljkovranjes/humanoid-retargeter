using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Cleanup;
using HumanoidRetargeter.Mapping;
using Sandbox;

namespace HumanoidRetargeter.Editor;

/// <summary>
/// The Humanoid Retargeter dock window (design §7): add .fbx/.bvh files (file dialog or
/// asset-browser context menu), see each file's detected profile as a colored chip
/// (green = preset/user preset, amber = auto-mapped/needs review, red = failed), fix
/// mappings manually, preview the retargeted clip on the skinned target model, and batch
/// convert everything into one animation vmdl (standalone or augmenting an existing one).
/// A file with several animation takes unpacks into one list entry per take
/// ("file.fbx · TakeName"), each independently previewable/removable/convertible; the
/// mapping stays per FILE (one skeleton per file). Every file carries its own mapping -
/// a single batch may mix Mixamo, ActorCore and BVH sources.
/// </summary>
[Dock( "Editor", "Humanoid Retargeter", "sync_alt" )]
public sealed class RetargetWindow : Widget
{
	static RetargetWindow _instance;

	/// <summary>The open window instance, if any (dock windows are singletons here).</summary>
	public static RetargetWindow Instance => _instance.IsValid() ? _instance : null;

	readonly List<SourceFileEntry> _entries = new();

	TargetPickers.ResolvedTarget _target;
	string _targetError;

	// Options
	RootMotionMode _rootMotion = RootMotionMode.Off;
	bool _footPlant = true;
	bool _armIk;
	bool _naturalCarriage = true;
	bool? _loopOverride;

	// Output
	bool _augmentMode;
	Asset _augmentAsset;
	LineEdit _outputFolderEdit;
	LineEdit _hipScaleHEdit;
	LineEdit _hipScaleVEdit;
	LineEdit _sampleFpsEdit;

	// UI
	Layout _listLayout;
	Button _convertButton;
	Button _pickAugmentButton;
	Label _statusLabel;
	Widget _progressBar;
	float _progress;
	bool _converting;

	/// <summary>Dock constructor (called by the editor's dock manager).</summary>
	public RetargetWindow( Widget parent ) : base( parent )
	{
		_instance ??= this;

		Name = "HumanoidRetargeter";
		WindowTitle = "Humanoid Retargeter";
		SetWindowIcon( "sync_alt" );
		MinimumSize = new Vector2( 720, 420 );

		Layout = Layout.Column();
		BuildUi();

		TrySelectSboxTarget();
		RefreshAll();
	}

	/// <summary>Opens (or raises) the window and returns it.</summary>
	public static RetargetWindow Open()
	{
		var window = Instance ?? EditorWindow.DockManager.Create<RetargetWindow>();
		EditorWindow.DockManager.RaiseDock( window );
		return window;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( _instance == this )
			_instance = null;
	}

	// ============================================================================ layout

	void BuildUi()
	{
		// ---- top bar -------------------------------------------------------------------
		var top = Layout.AddRow();
		top.Margin = 8;
		top.Spacing = 8;

		var add = top.Add( new Button.Primary( "Add Files…" ) { Icon = "add" } );
		add.ToolTip = "Add .fbx / .bvh / .glb / .gltf animation files to convert";
		add.Clicked = AddFilesViaDialog;

		top.AddSpacingCell( 8 );
		top.Add( new Label( this ) { Text = "Target:" } );
		var targetCombo = top.Add( new ComboBox( this ) { MinimumWidth = 190 } );
		targetCombo.AddItem( "s&box Human (default)", "person", TrySelectSboxTarget, selected: true );
		targetCombo.AddItem( "s&box Citizen (classic)", "person_outline", TrySelectSboxCitizenTarget );
		targetCombo.AddItem( "Custom model (.vmdl)…", "view_in_ar", PickCustomModelTarget );
		targetCombo.AddItem( "Custom FBX…", "category", PickCustomFbxTarget );

		top.AddSpacingCell( 8 );
		top.Add( new Label( this ) { Text = "Output:" } );
		var outputCombo = top.Add( new ComboBox( this ) { MinimumWidth = 200 } );
		outputCombo.AddItem( "New animation vmdl", "note_add", () => SetAugmentMode( false ), selected: true );
		outputCombo.AddItem( "Add to existing vmdl…", "library_add", () => SetAugmentMode( true ) );

		_pickAugmentButton = top.Add( new Button( "Pick vmdl…", "folder_open" ) );
		_pickAugmentButton.Visible = false;
		_pickAugmentButton.Clicked = PickAugmentAsset;

		top.AddStretchCell();

		_convertButton = top.Add( new Button.Primary( "Convert All" ) { Icon = "play_arrow" } );
		_convertButton.Tint = Theme.Green;
		_convertButton.Clicked = () => _ = ConvertEntriesAsync( null );

		// ---- file list -------------------------------------------------------------------
		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.Canvas = new Widget( scroll );
		scroll.Canvas.Layout = Layout.Column();
		scroll.Canvas.Layout.Margin = new Sandbox.UI.Margin( 8, 4, 16, 4 );
		scroll.Canvas.Layout.Spacing = 2;
		_listLayout = scroll.Canvas.Layout;

		// ---- options ---------------------------------------------------------------------
		var options = Layout.Add( new Group( this ) { Title = "Options", Icon = "tune" } );
		options.Layout = Layout.Row();
		options.Layout.Margin = new Sandbox.UI.Margin( 14, 30, 14, 12 );
		options.Layout.Spacing = 16;

		options.Layout.Add( new Label( this ) { Text = "Root motion:" } );
		var rootCombo = options.Layout.Add( new ComboBox( this ) { MinimumWidth = 150 } );
		rootCombo.AddItem( "Keep as authored", null, () => _rootMotion = RootMotionMode.Off, selected: true );
		rootCombo.AddItem( "In place (strip)", null, () => _rootMotion = RootMotionMode.InPlace );
		rootCombo.AddItem( "Extract to root", null, () => _rootMotion = RootMotionMode.Extract );

		options.Layout.Add( new Label( this ) { Text = "Looping:" } );
		var loopCombo = options.Layout.Add( new ComboBox( this ) { MinimumWidth = 120 } );
		loopCombo.AddItem( "From source", null, () => _loopOverride = null, selected: true );
		loopCombo.AddItem( "Force on", null, () => _loopOverride = true );
		loopCombo.AddItem( "Force off", null, () => _loopOverride = false );

		var footPlant = options.Layout.Add( new Checkbox( "Foot-plant cleanup" ) { Value = _footPlant } );
		footPlant.Clicked = () => _footPlant = footPlant.Value;

		var carriage = options.Layout.Add( new Checkbox( "Natural shoulder/neck carriage" ) { Value = _naturalCarriage } );
		carriage.ToolTip = "Keep the s&box body's own shoulder line and neck posture, transferring only the source's motion. "
			+ "Untick to exactly copy the source rig's shoulder/neck directions (can look slumped/hunched on differently-proportioned rigs).";
		carriage.Clicked = () => _naturalCarriage = carriage.Value;

		var armIk = options.Layout.Add( new Checkbox( "Arm effector IK" ) { Value = _armIk } );
		armIk.ToolTip = "Pull wrists onto limb-length-normalized source hand positions. "
			+ "Off by default - only useful for reach-critical clips.";
		armIk.Clicked = () => _armIk = armIk.Value;

		options.Layout.Add( new Label( this ) { Text = "Hip scale H/V:" } );
		_hipScaleHEdit = options.Layout.Add( new LineEdit( this ) { PlaceholderText = "auto", FixedWidth = 46 } );
		_hipScaleHEdit.ToolTip = "Scale of the pelvis translation perpendicular to the character up axis. "
			+ "Empty = automatic (target hip height / source hip height).";
		_hipScaleVEdit = options.Layout.Add( new LineEdit( this ) { PlaceholderText = "auto", FixedWidth = 46 } );
		_hipScaleVEdit.ToolTip = "Scale of the pelvis translation along the character up axis. "
			+ "Empty = automatic (hip-height ratio).";

		options.Layout.Add( new Label( this ) { Text = "Sample fps:" } );
		_sampleFpsEdit = options.Layout.Add( new LineEdit( this ) { PlaceholderText = "30", FixedWidth = 46 } );
		_sampleFpsEdit.ToolTip = "Sample rate the source clips are resampled to on import. "
			+ "Empty or 0 = default (30 fps).";

		options.Layout.Add( new Label( this ) { Text = "Output folder:" } );
		_outputFolderEdit = options.Layout.Add( new LineEdit( this ) { Text = "animations/retargeted", MinimumWidth = 180 }, 1 );
		_outputFolderEdit.ToolTip = "Assets-relative folder the DMX files (and the standalone vmdl) are written to.";

		// ---- status strip -----------------------------------------------------------------
		var strip = Layout.AddRow();
		strip.Margin = new Sandbox.UI.Margin( 8, 4, 8, 6 );
		strip.Spacing = 8;
		_statusLabel = strip.Add( new Label( this ) { Text = "Add animation files to get started." }, 1 );
		_progressBar = strip.Add( new Widget( this ) { FixedHeight = 12, FixedWidth = 200 } );
		_progressBar.OnPaintOverride = PaintProgress;
		_progressBar.Visible = false;
	}

	bool PaintProgress()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( _progressBar.LocalRect, 3 );
		var r = _progressBar.LocalRect;
		r.Width *= _progress.Clamp( 0f, 1f );
		Paint.ClearPen();
		Paint.SetBrush( Theme.Green );
		Paint.DrawRect( r, 3 );
		return true;
	}

	// ============================================================================ target

	void TrySelectSboxTarget()
	{
		try
		{
			_target = TargetPickers.SboxDefault();
			_targetError = null;
		}
		catch ( Exception e )
		{
			_target = null;
			_targetError = e.Message;
		}
		RefreshStatus();
	}

	void TrySelectSboxCitizenTarget()
	{
		try
		{
			_target = TargetPickers.SboxCitizen();
			_targetError = null;
		}
		catch ( Exception e )
		{
			_target = null;
			_targetError = e.Message;
		}
		RefreshStatus();
	}

	void PickCustomModelTarget()
	{
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Window.Title = "Select target model";
		picker.OnAssetPicked = assets =>
		{
			var asset = assets.FirstOrDefault();
			if ( asset is null )
				return;
			var resolved = TargetPickers.FromModelAsset( asset, out var error );
			ApplyPickedTarget( resolved, error );
		};
		picker.Show();
	}

	void PickCustomFbxTarget()
	{
		var path = EditorUtility.OpenFileDialog( "Select target FBX", "FBX Files (*.fbx)", null );
		if ( string.IsNullOrEmpty( path ) )
			return;
		var resolved = TargetPickers.FromFbxFile( path, out var error );
		ApplyPickedTarget( resolved, error );
	}

	void ApplyPickedTarget( TargetPickers.ResolvedTarget resolved, string error )
	{
		if ( resolved is null )
		{
			_targetError = error ?? "Target rejected.";
			SetStatus( _targetError, Theme.Red );
			return;
		}

		_target = resolved;
		_targetError = null;
		RefreshStatus();
	}

	void SetAugmentMode( bool augment )
	{
		_augmentMode = augment;
		_pickAugmentButton.Visible = augment;
		if ( augment && _augmentAsset is null )
			PickAugmentAsset();
		RefreshStatus();
	}

	void PickAugmentAsset()
	{
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Window.Title = "Select vmdl to add animations to";
		picker.OnAssetPicked = assets =>
		{
			_augmentAsset = assets.FirstOrDefault();
			_pickAugmentButton.Text = _augmentAsset is null ? "Pick vmdl…" : _augmentAsset.Name;
			RefreshStatus();
		};
		picker.Show();
	}

	// ============================================================================ files

	void AddFilesViaDialog()
	{
		var fd = new FileDialog( null ) { Title = "Add animation files…" };
		fd.SetFindExistingFiles();
		fd.SetModeOpen();
		fd.SetNameFilter( "Animation Files (*.fbx *.bvh *.glb *.gltf)" );
		if ( !fd.Execute() )
			return;

		AddFiles( fd.SelectedFiles );
	}

	/// <summary>Adds source files (used by Add Files and the asset context menu). Files are
	/// parsed on a background task so big batches never freeze the editor; rows appear as
	/// each entry completes, and rigs with no matching profile raise the no-profile dialog
	/// once the whole batch has loaded.</summary>
	public void AddFiles( IEnumerable<string> paths )
	{
		var list = (paths ?? Enumerable.Empty<string>()).ToList();
		if ( list.Count == 0 )
			return;
		_ = AddFilesAsync( list );
	}

	async Task AddFilesAsync( IReadOnlyList<string> paths )
	{
		var assetsPath = Project.Current?.GetAssetsPath();
		var needDecision = new List<SourceFileEntry>();

		foreach ( var path in paths )
		{
			if ( _entries.Any( e => string.Equals( e.FilePath, path, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			SetStatus( $"Loading {System.IO.Path.GetFileName( path )}…", Theme.Blue );

			// Parse off the UI thread; Task.Run continuations are not guaranteed to resume
			// on the editor main thread, so hop back explicitly before touching the UI.
			var entry = await Task.Run( () => SourceFileEntry.Load( path, assetsPath ) );
			await EditorPipeline.SwitchToMainThread();

			if ( !this.IsValid() )
				return; // window closed while loading
			if ( _entries.Any( e => string.Equals( e.FilePath, path, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			_entries.Add( entry );
			if ( entry.NeedsUserDecision )
				needDecision.Add( entry );

			RefreshAll(); // row appears as soon as the entry is ready
		}

		// No-profile dialogs prompt after loading completes, one per affected file.
		foreach ( var entry in needDecision )
			ShowNoProfileDialog( entry );
	}

	void ShowNoProfileDialog( SourceFileEntry entry )
	{
		var dialog = new NoProfileDialog( this, entry.FileName, entry.Mapping?.Confidence ?? 0f, DlAssets.Available )
		{
			AutoMapChosen = () =>
			{
				entry.MappingConfirmed = true;
				entry.NeedsUserDecision = false;
				entry.Status = EntryStatus.Ready;
				RefreshAll();
			},
			// Design §6 option 2: the DL solver needs no mapping - solve and flow straight
			// into the preview; confirming there marks the entry ready (and offers saving a
			// trajectory-derived preset, see TrySaveDerivedPreset).
			DeepLearningChosen = () =>
			{
				entry.UseDlSolver = true;
				RefreshAll();
				// DL applies to the whole file; preview its first take as representative.
				if ( entry.Takes.Count > 0 )
					OpenPreview( entry.Takes[0] );
			},
			ManualChosen = () => OpenMappingEditor( entry ),
		};
		dialog.Show();
	}

	void OpenMappingEditor( SourceFileEntry entry )
	{
		if ( entry.Scene is null )
			return;

		var editor = new MappingEditor( this, entry.FileName, entry.Scene.Skeleton, entry.Mapping )
		{
			Applied = mapping =>
			{
				entry.Mapping = mapping;
				entry.MappingConfirmed = true;
				entry.NeedsUserDecision = false;
				entry.Status = EntryStatus.Ready;
				RefreshAll();

				// Design §6: manual mapping flows straight into the preview for confirmation
				// (the first take stands in for the file - the mapping is per file).
				if ( entry.Takes.Count > 0 )
					OpenPreview( entry.Takes[0] );
			},
		};
		editor.Show();
	}

	void RemoveEntry( SourceFileEntry entry )
	{
		_entries.Remove( entry );
		RefreshAll();
	}

	/// <summary>Removes one take row; the file entry goes with its last take.</summary>
	void RemoveTake( SourceTakeEntry take )
	{
		take.File.Takes.Remove( take );
		if ( take.File.Takes.Count == 0 )
			_entries.Remove( take.File );
		RefreshAll();
	}

	// ============================================================================ preview

	/// <summary>Solves and previews ONE take (per-take rows preview their own take; the
	/// request carries the take index so only that clip is solved).</summary>
	async void OpenPreview( SourceTakeEntry take )
	{
		var entry = take.File;
		if ( _converting || entry.Scene is null || _target is null )
			return;

		SetStatus( $"Solving preview for {take.DisplayName}…", Theme.Blue );
		var request = BuildRequest( take );
		var target = _target;

		HumanoidRetargeter.RetargetResult result;
		try
		{
			result = await Task.Run( () => Retargeter.Convert( request, target.Spec ) );
			await EditorPipeline.SwitchToMainThread();
		}
		catch ( Exception e )
		{
			await EditorPipeline.SwitchToMainThread();
			take.ConversionStatus = EntryStatus.Failed;
			take.StatusDetail = e.Message;
			RefreshAll();
			return;
		}

		if ( !result.Clips.Any( c => c.Success ) )
		{
			take.ConversionStatus = EntryStatus.Failed;
			take.StatusDetail = result.Errors.FirstOrDefault() ?? "No clip solved.";
			RefreshAll();
			return;
		}

		RefreshStatus();

		var dialog = new PreviewDialog( this, take.DisplayName, result.Clips, target, entry.Mapping.Source )
		{
			Confirmed = savePreset =>
			{
				if ( savePreset )
				{
					// DL entries save a preset DERIVED from the previewed alignment
					// (trajectory correlation) - the rig then takes the deterministic
					// geometric path on every later conversion (design §6).
					if ( entry.UseDlSolver )
						TrySaveDerivedPreset( take, result, target );
					else
						TrySaveUserPreset( entry );
				}
				entry.MappingConfirmed = true;
				entry.NeedsUserDecision = false;
				if ( entry.Status is EntryStatus.NeedsReview )
					entry.Status = EntryStatus.Ready;
				RefreshAll();
				_ = ConvertEntriesAsync( new[] { take } );
			},
		};
		dialog.Show();
	}

	void TrySaveUserPreset( SourceFileEntry entry )
	{
		var assetsPath = Project.Current?.GetAssetsPath();
		if ( assetsPath is null || entry.Scene is null || entry.Mapping is null )
			return;

		try
		{
			UserPresets.Save( assetsPath, entry.Signature, entry.Scene.Skeleton, entry.Mapping );
			SetStatus( $"Saved user preset profile for {entry.FileName}.", Theme.Green );
		}
		catch ( Exception e )
		{
			SetStatus( $"Could not save user preset: {e.Message}", Theme.Red );
		}
	}

	/// <summary>"Save as profile" on a confirmed DL preview: derives the role↔bone mapping
	/// implied by the DL alignment (trajectory correlation over the previewed clip,
	/// <see cref="HumanoidRetargeter.Dl.DlMappingDeriver"/>) and stores it as a user preset
	/// named <c>user_dl_*</c> - below-threshold roles stay unmapped. The correlation runs
	/// over the PREVIEWED take (not hardcoded take 0) and against the source resampled on
	/// the same fps grid the DL clip used - the preview's request may carry a user Sample
	/// fps while the entry's cached scene was imported at the default rate, and a frame-index
	/// correlation across different grids is time-misaligned.</summary>
	void TrySaveDerivedPreset( SourceTakeEntry take, HumanoidRetargeter.RetargetResult result,
		TargetPickers.ResolvedTarget target )
	{
		var entry = take.File;
		var assetsPath = Project.Current?.GetAssetsPath();
		if ( assetsPath is null || entry.Scene is null )
			return;

		var clip = result.Clips.FirstOrDefault( c => c.Success && c.SolvedFrames is { Count: > 0 } );
		if ( clip is null )
			return;

		try
		{
			var dlClip = new HumanoidRetargeter.Skeleton.Clip(
				clip.ClipName, clip.Fps, clip.Looping, clip.SolvedFrames );

			// The DL output's fps is the import sample rate the preview's request used
			// (BuildRequest passes the Sample fps box through). Re-import the source on
			// that grid when it differs from the entry's cached scene so source frame i
			// and DL frame i are the same instant in time.
			var scene = entry.Scene;
			if ( take.TakeIndex >= scene.Clips.Count
				|| MathF.Abs( scene.Clips[take.TakeIndex].Fps - clip.Fps ) > 0.01f )
			{
				scene = Retargeter.ImportSource( entry.Bytes, entry.FileName, clip.Fps );
			}

			var derived = HumanoidRetargeter.Dl.DlMappingDeriver.Derive(
				scene, take.TakeIndex, dlClip, target.Spec.Rig );
			derived.Notes.Add( "derived from DL" );

			// Hips alone is structural, not evidence of an alignment - don't save that.
			if ( derived.RoleToBone.Count <= 1 )
			{
				SetStatus( "Could not derive a mapping from the DL preview (trajectories too "
					+ "ambiguous) - no preset saved.", Theme.Yellow );
				return;
			}

			// scene.Skeleton == entry.Scene.Skeleton structurally (the sample fps only
			// changes clip resampling, never the rig) - save with the skeleton the derived
			// bone indices actually reference.
			UserPresets.Save( assetsPath, entry.Signature, scene.Skeleton, derived, "user_dl" );
			SetStatus( $"Saved DL-derived preset ({derived.RoleToBone.Count} roles, mean correlation "
				+ $"{derived.Confidence:0.00}) for {entry.FileName}.", Theme.Green );
		}
		catch ( Exception e )
		{
			SetStatus( $"Could not save DL-derived preset: {e.Message}", Theme.Red );
		}
	}

	// ============================================================================ convert

	/// <summary>One facade request per take row. Files whose SCENE has multiple takes set
	/// <see cref="HumanoidRetargeter.RetargetRequest.TakeIndex"/> so each row converts only
	/// its own take; single-take files keep the all-takes default (equivalent).
	/// IMPORTANT: the decision keys on <see cref="SourceFileEntry.ClipCount"/> (the imported
	/// scene's immutable clip count), NOT on the live UI take list — RemoveTake mutates
	/// <c>File.Takes</c>, so a multi-take file reduced to one visible row must still convert
	/// only that row's take, not every take in the file.
	/// Unity-sidecar files (<see cref="SourceFileEntry.ClipDefinitions"/>) pass the
	/// definitions through and ALWAYS set the row index — TakeIndex then addresses the
	/// definition the row represents, and the facade slices the take to its frame range
	/// (preview re-solves via this same request, so it previews the sliced range too).</summary>
	HumanoidRetargeter.RetargetRequest BuildRequest( SourceTakeEntry take ) => new()
	{
		SourceData = take.File.Bytes,
		SourceFileName = take.File.FileName,
		SourceId = take.SourceId, // full path + take index: rows must join results unambiguously
		ClipDefinitions = take.File.ClipDefinitions,
		TakeIndex = take.File.ClipDefinitions is not null
			? take.TakeIndex
			: take.File.ClipCount > 1 ? take.TakeIndex : null,
		MappingOverride = take.File.Mapping,
		Solver = take.File.UseDlSolver
			? HumanoidRetargeter.SolverKind.DeepLearning
			: HumanoidRetargeter.SolverKind.Geometric,
		RootMotion = _rootMotion,
		FootPlantCleanup = _footPlant,
		ArmEffectorIk = _armIk,
		LoopingOverride = _loopOverride,
		SampleFps = ParsePositive( _sampleFpsEdit ),
		Solve = new HumanoidRetargeter.Solve.SolveOptions
		{
			HipScaleHorizontal = ParsePositive( _hipScaleHEdit ),
			HipScaleVertical = ParsePositive( _hipScaleVEdit ),
			// null = recommended defaults (clavicle/neck keep the target's natural
			// carriage); empty map = legacy all-absolute direction matching.
			TransferModes = _naturalCarriage
				? null
				: new Dictionary<HumanoidRetargeter.Mapping.BoneRole, HumanoidRetargeter.Solve.RoleTransferMode>(),
		},
	};

	/// <summary>Empty / non-numeric / non-positive = null (use the automatic default).</summary>
	static float? ParsePositive( LineEdit edit )
		=> float.TryParse( edit?.Text, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var v ) && v > 0f
			? v : null;

	/// <summary>
	/// The convert pipeline shared by the Convert All button and the UI smoke gate
	/// (HR_UI_SMOKE_AUGMENT drives this exact path headlessly): the heavy pure-C# batch
	/// conversion runs on a background task, everything that touches engine state
	/// (asset registration, compiling) is marshalled to the editor main thread inside
	/// <see cref="EditorPipeline.WriteAndCompileAsync"/>. <paramref name="batchReady"/>
	/// fires on the main thread between the two stages (progress/status updates).
	/// Returns a null Write when augmenting was requested but the batch produced no
	/// augmented vmdl - nothing is written then (no silent standalone fallback).
	/// The returned task completes on the editor main thread.
	/// </summary>
	internal static async Task<(HumanoidRetargeter.RetargetBatchResult Batch, EditorPipeline.WriteResult Write)>
		ConvertAndWriteAsync(
			IReadOnlyList<HumanoidRetargeter.RetargetRequest> requests,
			TargetPickers.ResolvedTarget target,
			HumanoidRetargeter.BatchOptions options,
			string augmentVmdlPath,
			Action<HumanoidRetargeter.RetargetBatchResult> batchReady = null )
	{
		// Heavy, engine-free math: off the main thread so the editor stays responsive.
		var batch = await Task.Run( () => Retargeter.ConvertBatch( requests, target.Spec, options ) );

		// Task.Run continuations are not guaranteed to resume on the editor main thread;
		// everything from here on may touch widgets/assets, so hop explicitly.
		await EditorPipeline.SwitchToMainThread();
		batchReady?.Invoke( batch );

		// Augment requested but no augmented vmdl produced: fail before anything is written.
		if ( options.AugmentVmdlText is not null && batch.AugmentedVmdl is null )
			return (batch, null);

		var write = await EditorPipeline.WriteAndCompileAsync(
			batch, options.DmxFolderRelative, augmentVmdlPath );
		await EditorPipeline.SwitchToMainThread();
		return (batch, write);
	}

	async Task ConvertEntriesAsync( IReadOnlyList<SourceTakeEntry> only )
	{
		if ( _converting )
			return;

		// Convert All (null) = every take row of every readable file; per-take requests keep
		// each row independently convertible.
		var list = (only ?? _entries.SelectMany( e => e.Takes ))
			.Where( t => t.File.Scene is not null && t.File.Mapping is not null ).ToList();
		if ( list.Count == 0 )
		{
			SetStatus( "Nothing to convert - add readable animation files first.", Theme.Yellow );
			return;
		}
		if ( _target is null )
		{
			SetStatus( _targetError ?? "No conversion target selected.", Theme.Red );
			return;
		}

		string augmentPath = null;
		string augmentText = null;
		if ( _augmentMode )
		{
			if ( _augmentAsset is null )
			{
				SetStatus( "Pick the vmdl to add the animations to first.", Theme.Yellow );
				return;
			}
			augmentPath = _augmentAsset.AbsolutePath;
			if ( EditorPipeline.IsUnderEngineInstall( augmentPath ) )
			{
				SetStatus( "Cannot modify models inside the s&box installation - "
					+ "copy the model into your project first.", Theme.Red );
				return;
			}
			try
			{
				augmentText = File.ReadAllText( augmentPath );
			}
			catch ( Exception e )
			{
				SetStatus( $"Could not read {augmentPath}: {e.Message}", Theme.Red );
				return;
			}
		}

		_converting = true;
		_progress = 0.05f;
		_progressBar.Visible = true;
		foreach ( var take in list )
			take.ConversionStatus = EntryStatus.Converting;
		RefreshAll();
		SetStatus( $"Converting {list.Count} clip(s)…", Theme.Blue );

		try
		{
			var outputFolder = NormalizedOutputFolder();
			var requests = list.Select( BuildRequest ).ToList();
			var options = new HumanoidRetargeter.BatchOptions
			{
				DmxFolderRelative = outputFolder,
				AugmentVmdlText = augmentText,
			};
			var target = _target;

			var (batch, write) = await ConvertAndWriteAsync( requests, target, options, augmentPath,
				batchReady: b =>
				{
					_progress = 0.55f;
					_progressBar.Update();

					ApplyClipResults( list, b.Clips );
					RefreshAll();

					// Batch-level problems (clip failures, augmentation failures) go to the
					// log in full; the status strip shows the first one.
					foreach ( var error in b.Errors )
						Log.Warning( $"[humanoid-retargeter] {error}" );

					SetStatus( "Compiling…", Theme.Blue );
				} );

			// Augment requested but no augmented vmdl produced: the operation FAILS - never
			// silently write a standalone vmdl the user did not ask for.
			if ( write is null )
			{
				var detail = batch.Errors.FirstOrDefault(
					e => e.Contains( "augment", StringComparison.OrdinalIgnoreCase ) )
					?? "vmdl augmentation failed.";
				foreach ( var take in list )
				{
					take.ConversionStatus = EntryStatus.Failed;
					take.StatusDetail = detail;
				}
				RefreshAll();
				SetStatus( $"Augmenting {_augmentAsset?.Name} failed - nothing written. {detail}", Theme.Red );
				return;
			}

			_progress = 1f;

			// The heavy payloads (DMX text + solved frames) are on disk now; previews
			// re-solve on demand, so the retained clip results only need their metadata.
			foreach ( var clip in batch.Clips )
				clip.ReleaseHeavyData();

			foreach ( var error in write.Errors )
				Log.Warning( $"[humanoid-retargeter] {error}" );

			var failures = batch.Clips.Count( c => !c.Success );
			if ( write.Errors.Count > 0 )
			{
				SetStatus( FirstLine( write.Errors[0] ), Theme.Red );
			}
			else if ( failures > 0 )
			{
				SetStatus( $"Converted with {failures} failed clip(s) - {write.VmdlAsset?.Path}", Theme.Yellow );
			}
			else
			{
				SetStatus( $"Done: {batch.Clips.Count} clip(s) → {write.VmdlAsset?.Path}"
					+ (write.Compiled ? " (compiled)" : " (compile failed!)"),
					write.Compiled ? Theme.Green : Theme.Red );
			}

			if ( write.VmdlAsset is not null )
				MainAssetBrowser.Instance?.Local?.UpdateAssetList();
		}
		catch ( Exception e )
		{
			// The exception may surface on a pool thread - back to main before touching UI.
			await EditorPipeline.SwitchToMainThread();
			foreach ( var take in list.Where( x => x.ConversionStatus == EntryStatus.Converting ) )
			{
				take.ConversionStatus = EntryStatus.Failed;
				take.StatusDetail = e.Message;
			}
			SetStatus( $"Conversion failed: {e.Message}", Theme.Red );
		}
		finally
		{
			_converting = false;
			_progressBar.Visible = false;
			RefreshAll();
		}
	}

	static string FirstLine( string text )
	{
		var newline = text.IndexOf( '\n' );
		return newline < 0 ? text : text.Substring( 0, newline ).TrimEnd( '\r' );
	}

	void ApplyClipResults( IReadOnlyList<SourceTakeEntry> list, IReadOnlyList<HumanoidRetargeter.ClipResult> clips )
	{
		foreach ( var take in list )
		{
			take.LastClips.Clear();
			// Join on SourceId (full path + take index, as BuildRequest supplied) -
			// same-named files/takes must map back to their own rows.
			take.LastClips.AddRange( clips.Where( c => c.SourceId == take.SourceId ) );

			var failed = take.LastClips.Where( c => !c.Success ).ToList();
			if ( take.LastClips.Count == 0 )
			{
				take.ConversionStatus = EntryStatus.Failed;
				take.StatusDetail = "No clips produced.";
			}
			else if ( failed.Count > 0 )
			{
				take.ConversionStatus = EntryStatus.Failed;
				take.StatusDetail = failed[0].Error ?? "Clip failed.";
			}
			else
			{
				take.ConversionStatus = EntryStatus.Converted;
				take.StatusDetail = take.LastClips.Count == 1
					? "Converted."
					: $"{take.LastClips.Count} clip(s) converted.";
			}
		}
	}

	string NormalizedOutputFolder()
	{
		var folder = (_outputFolderEdit?.Text ?? "").Trim().Replace( '\\', '/' ).Trim( '/' );
		return folder.Length == 0 ? "animations/retargeted" : folder;
	}

	// ============================================================================ refresh

	void RefreshAll()
	{
		RebuildList();
		RefreshStatus();
	}

	void RebuildList()
	{
		if ( _listLayout is null )
			return;

		_listLayout.Clear( true );

		if ( _entries.Count == 0 )
		{
			var empty = _listLayout.Add( new Label( this )
			{
				Text = "No files yet. Use \"Add Files…\" or right-click .fbx/.bvh/.glb/.gltf files in the "
					+ "Asset Browser and choose \"Retarget to s&box rig…\".",
				WordWrap = true,
			} );
			empty.SetStyles( $"color: {Theme.TextLight.Hex}; margin: 12px;" );
		}
		else
		{
			// One row per TAKE: a multi-take file unpacks into individual entries
			// ("file.fbx · TakeName"), each independently previewable/removable/convertible.
			// Unreadable files (no takes) keep a single file-level row.
			foreach ( var entry in _entries )
			{
				if ( entry.Takes.Count == 0 )
					_listLayout.Add( new FileRow( this, entry, null ) );
				else
					foreach ( var take in entry.Takes )
						_listLayout.Add( new FileRow( this, entry, take ) );
			}
		}

		_listLayout.AddStretchCell();
	}

	void RefreshStatus()
	{
		if ( _convertButton.IsValid() )
			_convertButton.Enabled = !_converting && _entries.Any( e => e.Scene is not null );

		if ( _targetError is not null )
			SetStatus( _targetError, Theme.Red );
		else if ( !_converting && _target is not null )
			SetStatus( $"Target: {_target.Description}   ·   {_entries.Count} file(s)", Theme.TextLight );
	}

	void SetStatus( string text, Color color )
	{
		if ( !_statusLabel.IsValid() )
			return;
		_statusLabel.Text = text;
		_statusLabel.SetStyles( $"color: {color.Hex};" );
	}

	// ============================================================================ row widget

	/// <summary>One take row (or a file-level row for unreadable files): status icon, label
	/// ("file.fbx · TakeName" for multi-take files), profile chip (green/amber/red, file
	/// level — the mapping is per file), and Mapping/Preview/Remove actions. Preview and
	/// conversion act on THIS take only.</summary>
	sealed class FileRow : Widget
	{
		readonly RetargetWindow _window;
		readonly SourceFileEntry _entry;
		readonly SourceTakeEntry _take; // null only for unreadable (takeless) files

		public FileRow( RetargetWindow window, SourceFileEntry entry, SourceTakeEntry take ) : base( window )
		{
			_window = window;
			_entry = entry;
			_take = take;

			FixedHeight = 34;
			Layout = Layout.Row();
			Layout.Margin = new Sandbox.UI.Margin( 32, 4, 8, 4 ); // left margin = status icon space
			Layout.Spacing = 8;

			var detailText = take?.StatusDetail is { Length: > 0 } takeDetail ? takeDetail : entry.StatusDetail;

			var name = Layout.Add( new Label( this ) { Text = take?.DisplayName ?? entry.FileName } );
			name.SetStyles( "font-weight: 600;" );
			name.ToolTip = entry.FilePath + (detailText.Length > 0 ? "\n" + detailText : "");

			Layout.Add( new Chip( this, entry.ChipText, ToneColor( entry.Tone ) ) );

			if ( take is not null && entry.Takes.Count > 1 )
			{
				var takeLabel = Layout.Add( new Label( this )
				{
					Text = $"take {take.TakeIndex + 1}/{entry.ClipCount}",
				} );
				takeLabel.SetStyles( $"color: {Theme.TextLight.Hex};" );
			}

			if ( detailText.Length > 0 && Status() is EntryStatus.Failed )
			{
				var detail = Layout.Add( new Label( this ) { Text = detailText }, 1 );
				detail.SetStyles( $"color: {Theme.Red.Hex};" );
			}

			Layout.AddStretchCell();

			if ( entry.Scene is not null && take is not null )
			{
				var mapping = Layout.Add( new Button( "Mapping…", "device_hub" ) );
				mapping.ToolTip = entry.Takes.Count > 1
					? "Review / edit the bone mapping (shared by every take of this file)"
					: "Review / edit the bone mapping";
				mapping.Clicked = () => _window.OpenMappingEditor( entry );

				var preview = Layout.Add( new Button( "Preview…", "preview" ) );
				preview.ToolTip = "Solve and preview this take on the target before converting";
				preview.Clicked = () => _window.OpenPreview( take );
			}

			var remove = Layout.Add( new IconButton( "close" ) );
			remove.ToolTip = take is not null && entry.Takes.Count > 1
				? "Remove this take from the list"
				: "Remove from the list";
			remove.OnClick = () =>
			{
				if ( take is not null )
					_window.RemoveTake( take );
				else
					_window.RemoveEntry( entry );
			};
		}

		EntryStatus Status() => _take?.EffectiveStatus ?? _entry.Status;

		static Color ToneColor( ChipTone tone ) => tone switch
		{
			ChipTone.Green => Theme.Green,
			ChipTone.Amber => Theme.Yellow,
			_ => Theme.Red,
		};

		(string Icon, Color Color) StatusIcon() => Status() switch
		{
			EntryStatus.Ready => ("check_circle", Theme.Green),
			EntryStatus.NeedsReview => ("warning", Theme.Yellow),
			EntryStatus.Converting => ("sync", Theme.Blue),
			EntryStatus.Converted => ("task_alt", Theme.Green),
			_ => ("error", Theme.Red),
		};

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Paint.HasMouseOver ? Theme.ControlBackground.Lighten( 0.3f ) : Theme.ControlBackground );
			Paint.DrawRect( LocalRect, 4 );

			var (icon, color) = StatusIcon();
			Paint.SetPen( color );
			Paint.DrawIcon( new Rect( 8, (Height - 18) * 0.5f, 18, 18 ), icon, 16 );
		}
	}

	/// <summary>Rounded status pill, e.g. <c>mixamo · 100%</c> in the tone color.</summary>
	sealed class Chip : Widget
	{
		readonly string _text;
		readonly Color _color;

		public Chip( Widget parent, string text, Color color ) : base( parent )
		{
			_text = text;
			_color = color;
			FixedHeight = 20;
			FixedWidth = 7.2f * text.Length + 18;
			ToolTip = "Profile · mapping confidence";
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( _color.WithAlpha( 0.18f ) );
			Paint.DrawRect( LocalRect, LocalRect.Height * 0.5f );
			Paint.SetPen( _color );
			Paint.SetDefaultFont( 7, 600 );
			Paint.DrawText( LocalRect, _text );
		}
	}
}
