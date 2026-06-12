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
/// Every file carries its own mapping - a single batch may mix Mixamo, ActorCore and BVH
/// sources.
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
	bool? _loopOverride;

	// Output
	bool _augmentMode;
	Asset _augmentAsset;
	LineEdit _outputFolderEdit;
	LineEdit _hipScaleHEdit;
	LineEdit _hipScaleVEdit;

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
		add.ToolTip = "Add .fbx / .bvh animation files to convert";
		add.Clicked = AddFilesViaDialog;

		top.AddSpacingCell( 8 );
		top.Add( new Label( this ) { Text = "Target:" } );
		var targetCombo = top.Add( new ComboBox( this ) { MinimumWidth = 190 } );
		targetCombo.AddItem( "s&box Human (default)", "person", TrySelectSboxTarget, selected: true );
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
		fd.SetNameFilter( "Animation Files (*.fbx *.bvh)" );
		if ( !fd.Execute() )
			return;

		AddFiles( fd.SelectedFiles );
	}

	/// <summary>Adds source files (used by Add Files and the asset context menu). Each file
	/// is inspected immediately; rigs with no matching profile raise the no-profile dialog.</summary>
	public void AddFiles( IEnumerable<string> paths )
	{
		var assetsPath = Project.Current?.GetAssetsPath();
		var needDecision = new List<SourceFileEntry>();

		foreach ( var path in paths ?? Enumerable.Empty<string>() )
		{
			if ( _entries.Any( e => string.Equals( e.FilePath, path, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			var entry = SourceFileEntry.Load( path, assetsPath );
			_entries.Add( entry );
			if ( entry.NeedsUserDecision )
				needDecision.Add( entry );
		}

		RefreshAll();

		foreach ( var entry in needDecision )
			ShowNoProfileDialog( entry );
	}

	void ShowNoProfileDialog( SourceFileEntry entry )
	{
		var dialog = new NoProfileDialog( this, entry.FileName, entry.Mapping?.Confidence ?? 0f )
		{
			AutoMapChosen = () =>
			{
				entry.MappingConfirmed = true;
				entry.NeedsUserDecision = false;
				entry.Status = EntryStatus.Ready;
				RefreshAll();
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

				// Design §6: manual mapping flows straight into the preview for confirmation.
				OpenPreview( entry );
			},
		};
		editor.Show();
	}

	void RemoveEntry( SourceFileEntry entry )
	{
		_entries.Remove( entry );
		RefreshAll();
	}

	// ============================================================================ preview

	async void OpenPreview( SourceFileEntry entry )
	{
		if ( _converting || entry.Scene is null || _target is null )
			return;

		SetStatus( $"Solving preview for {entry.FileName}…", Theme.Blue );
		var request = BuildRequest( entry );
		var target = _target;

		HumanoidRetargeter.RetargetResult result;
		try
		{
			result = await Task.Run( () => Retargeter.Convert( request, target.Spec ) );
		}
		catch ( Exception e )
		{
			entry.Status = EntryStatus.Failed;
			entry.StatusDetail = e.Message;
			RefreshAll();
			return;
		}

		if ( !result.Clips.Any( c => c.Success ) )
		{
			entry.Status = EntryStatus.Failed;
			entry.StatusDetail = result.Errors.FirstOrDefault() ?? "No clip solved.";
			RefreshAll();
			return;
		}

		RefreshStatus();

		var dialog = new PreviewDialog( this, entry.FileName, result.Clips, target, entry.Mapping.Source )
		{
			Confirmed = savePreset =>
			{
				if ( savePreset )
					TrySaveUserPreset( entry );
				entry.MappingConfirmed = true;
				entry.NeedsUserDecision = false;
				if ( entry.Status is EntryStatus.NeedsReview )
					entry.Status = EntryStatus.Ready;
				RefreshAll();
				_ = ConvertEntriesAsync( new[] { entry } );
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

	// ============================================================================ convert

	HumanoidRetargeter.RetargetRequest BuildRequest( SourceFileEntry entry ) => new()
	{
		SourceData = entry.Bytes,
		SourceFileName = entry.FileName,
		MappingOverride = entry.Mapping,
		RootMotion = _rootMotion,
		FootPlantCleanup = _footPlant,
		ArmEffectorIk = _armIk,
		LoopingOverride = _loopOverride,
		Solve = new HumanoidRetargeter.Solve.SolveOptions
		{
			HipScaleHorizontal = ParseScale( _hipScaleHEdit ),
			HipScaleVertical = ParseScale( _hipScaleVEdit ),
		},
	};

	/// <summary>Empty / non-numeric / non-positive = null (automatic hip-height ratio).</summary>
	static float? ParseScale( LineEdit edit )
		=> float.TryParse( edit?.Text, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var v ) && v > 0f
			? v : null;

	async Task ConvertEntriesAsync( IReadOnlyList<SourceFileEntry> only )
	{
		if ( _converting )
			return;

		var list = (only ?? _entries).Where( e => e.Scene is not null && e.Mapping is not null ).ToList();
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
		foreach ( var entry in list )
			entry.Status = EntryStatus.Converting;
		RefreshAll();
		SetStatus( $"Converting {list.Count} file(s)…", Theme.Blue );

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

			var batch = await Task.Run( () => Retargeter.ConvertBatch( requests, target.Spec, options ) );
			_progress = 0.55f;
			_progressBar.Update();

			ApplyClipResults( list, batch.Clips );
			RefreshAll();
			SetStatus( "Compiling…", Theme.Blue );

			var write = await EditorPipeline.WriteAndCompileAsync( batch, outputFolder, augmentPath );
			_progress = 1f;

			var failures = batch.Clips.Count( c => !c.Success );
			if ( write.Errors.Count > 0 )
			{
				SetStatus( write.Errors[0], Theme.Red );
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
			foreach ( var entry in list.Where( x => x.Status == EntryStatus.Converting ) )
			{
				entry.Status = EntryStatus.Failed;
				entry.StatusDetail = e.Message;
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

	void ApplyClipResults( IReadOnlyList<SourceFileEntry> list, IReadOnlyList<HumanoidRetargeter.ClipResult> clips )
	{
		foreach ( var entry in list )
		{
			entry.LastClips.Clear();
			entry.LastClips.AddRange( clips.Where( c => c.SourceFileName == entry.FileName ) );

			var failed = entry.LastClips.Where( c => !c.Success ).ToList();
			if ( entry.LastClips.Count == 0 )
			{
				entry.Status = EntryStatus.Failed;
				entry.StatusDetail = "No clips produced.";
			}
			else if ( failed.Count > 0 )
			{
				entry.Status = EntryStatus.Failed;
				entry.StatusDetail = failed[0].Error ?? "Clip failed.";
			}
			else
			{
				entry.Status = EntryStatus.Converted;
				entry.StatusDetail = $"{entry.LastClips.Count} clip(s) converted.";
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
				Text = "No files yet. Use \"Add Files…\" or right-click .fbx/.bvh files in the Asset Browser "
					+ "and choose \"Retarget to s&box rig…\".",
				WordWrap = true,
			} );
			empty.SetStyles( $"color: {Theme.TextLight.Hex}; margin: 12px;" );
		}
		else
		{
			foreach ( var entry in _entries )
				_listLayout.Add( new FileRow( this, entry ) );
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

	/// <summary>One file row: status icon, file name, profile chip (green/amber/red),
	/// confidence, clip count, and Mapping/Preview/Remove actions.</summary>
	sealed class FileRow : Widget
	{
		readonly RetargetWindow _window;
		readonly SourceFileEntry _entry;

		public FileRow( RetargetWindow window, SourceFileEntry entry ) : base( window )
		{
			_window = window;
			_entry = entry;

			FixedHeight = 34;
			Layout = Layout.Row();
			Layout.Margin = new Sandbox.UI.Margin( 32, 4, 8, 4 ); // left margin = status icon space
			Layout.Spacing = 8;

			var name = Layout.Add( new Label( this ) { Text = entry.FileName } );
			name.SetStyles( "font-weight: 600;" );
			name.ToolTip = entry.FilePath + (entry.StatusDetail.Length > 0 ? "\n" + entry.StatusDetail : "");

			Layout.Add( new Chip( this, entry.ChipText, ToneColor( entry.Tone ) ) );

			if ( entry.ClipCount > 0 )
			{
				var clips = Layout.Add( new Label( this )
				{
					Text = entry.ClipCount == 1 ? "1 clip" : $"{entry.ClipCount} clips",
				} );
				clips.SetStyles( $"color: {Theme.TextLight.Hex};" );
			}

			if ( entry.StatusDetail.Length > 0 && entry.Status is EntryStatus.Failed )
			{
				var detail = Layout.Add( new Label( this ) { Text = entry.StatusDetail }, 1 );
				detail.SetStyles( $"color: {Theme.Red.Hex};" );
			}

			Layout.AddStretchCell();

			if ( entry.Scene is not null )
			{
				var mapping = Layout.Add( new Button( "Mapping…", "device_hub" ) );
				mapping.ToolTip = "Review / edit the bone mapping";
				mapping.Clicked = () => _window.OpenMappingEditor( entry );

				var preview = Layout.Add( new Button( "Preview…", "preview" ) );
				preview.ToolTip = "Solve and preview the retargeted clip before converting";
				preview.Clicked = () => _window.OpenPreview( entry );
			}

			var remove = Layout.Add( new IconButton( "close" ) );
			remove.ToolTip = "Remove from the list";
			remove.OnClick = () => _window.RemoveEntry( entry );
		}

		static Color ToneColor( ChipTone tone ) => tone switch
		{
			ChipTone.Green => Theme.Green,
			ChipTone.Amber => Theme.Yellow,
			_ => Theme.Red,
		};

		(string Icon, Color Color) StatusIcon() => _entry.Status switch
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
