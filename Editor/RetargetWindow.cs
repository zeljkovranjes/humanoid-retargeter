#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using HumanoidRetargeter.Core;
using HumanoidRetargeter.Core.Cleanup;
using HumanoidRetargeter.Core.Mapping;
using HumanoidRetargeter.Core.Target;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// The Humanoid Retargeter dock window (design §7): add .fbx/.bvh/.glb/.gltf/.vrm/.anm/.an5/.cba
/// files (file dialog or asset-browser context menu), see each file's detected profile as a colored chip
/// (green = preset/user preset, amber = auto-mapped/needs review, red = failed), fix
/// mappings manually, preview the retargeted clip on the skinned target model, and batch
/// convert everything into one animation vmdl (standalone or augmenting an existing one).
/// A file with several animation takes unpacks into one list entry per take
/// ("file.fbx · TakeName"), each independently previewable/removable/convertible; the
/// mapping stays per FILE (one skeleton per file). Every file carries its own mapping -
/// a single batch may mix Mixamo, ActorCore and BVH sources.
/// </summary>
[Alias( "HumanoidRetargeter.Editor.RetargetWindow" )]
public sealed class RetargetWindow : Widget
{
	/// <summary>The window title and its View menu entry.</summary>
	public const string DockTitle = "Humanoid Retargeter";
	public const string DockIcon = "sync_alt";

	static RetargetWindow _instance;

	/// <summary>The open window instance, if any (dock windows are singletons here).</summary>
	public static RetargetWindow Instance => _instance.IsValid() ? _instance : null;

	readonly List<SourceFileEntry> _entries = new();

	TargetPickers.ResolvedTarget _target;
	string _targetError;

	// Options
	RootMotionMode _rootMotion = RootMotionMode.Off;
	bool _footPlant = true;
	bool _armIk = true;
	bool _naturalCarriage = true;
	bool _footstepEvents;
	bool _mirroredVariants;
	bool _additiveVariants;
	bool _copyAnimGraph = true;
	LineEdit _additiveReferenceEdit;
	bool _detectLocomotionSets;
	bool? _loopOverride;
	Checkbox _locomotionCheckbox;

	// Output
	bool _augmentMode;
	Asset _augmentAsset;
	LineEdit _outputFolderEdit;
	LineEdit _outputNameEdit;
	LineEdit _hipScaleHEdit;
	LineEdit _hipScaleVEdit;
	LineEdit _sampleFpsEdit;

	// UI
	Layout _listLayout;
	Button _convertButton;
	Button _citizenSetupButton;
	RtButton _pickAugmentButton;
	Label _statusLabel;
	Widget _progressBar;
	float _progress;
	bool _converting;
	Widget _reportGroup;
	Layout _reportLines;
	RtButton _reportToggle;

	/// <summary>Creates the retargeter inside its window (see <see cref="Open"/>).</summary>
	public RetargetWindow( Widget parent ) : base( parent )
	{
		_instance ??= this;

		Name = "HumanoidRetargeter";
		WindowTitle = DockTitle;
		SetWindowIcon( DockIcon );
		MinimumSize = new Vector2( 860, 540 );

		Layout = Layout.Column();
		BuildUi();

		TrySelectSboxTarget();
		RefreshAll();
	}

	// View menu: the entry opens a floating window (with the editor's dark title bar), like
	// the Weapon Importer, instead of a dock panel that snaps into the editor layout.
	[Event( "tools.editorwindow.createview" )]
	static void RegisterViewMenu( Menu menu ) => EditorWindow.DockManager.RegisterDockType( new DockManager.DockInfo
	{
		Title = DockTitle,
		Icon = DockIcon,
		CreateAction = () =>
		{
			Open();
			return null;
		},
	} );

	[Event( "tools.editorwindow.postcreateview" )]
	static void ConfigureViewMenu( Menu menu )
	{
		var option = menu.GetOption( DockTitle );
		if ( option is null )
			return;
		option.Toggled = null;
		option.Checkable = false;
		option.Triggered = () => Open();
	}

	/// <summary>Opens (or raises) the window and returns it.</summary>
	public static RetargetWindow Open()
	{
		if ( Instance is { } existing )
		{
			existing.GetWindow().Show();
			existing.Show();
			existing.GetWindow().Raise();
			return existing;
		}
		var dialog = new Dialog( null );
		dialog.Window.Title = DockTitle;
		dialog.Window.SetWindowIcon( DockIcon );
		dialog.Layout = Layout.Column();
		var window = dialog.Layout.Add( new RetargetWindow( dialog ), 1 );
		dialog.Window.MinimumSize = new Vector2( 860, 540 );
		dialog.Window.Size = new Vector2( 1100, 680 );
		dialog.Show();
		return window;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( _instance == this )
			_instance = null;
	}

	// ============================================================================ layout

	RtPill _countPill;
	RtDropZone _dropZone;
	ScrollArea _listScroll;
	Label _outputPath;
	Label _targetInfo;
	RtStatusDot _statusDot;
	Widget _augmentRow;

	const float SideWidth = 300f;
	const float CaptionWidth = 78f;

	/// <summary>
	/// The clip list on the left and every setting in a narrow column of cards on the right, so
	/// nothing is hidden: what goes in, onto which character, where it is written and how.
	/// Previewing a clip opens the preview window.
	/// </summary>
	void BuildUi()
	{
		Layout.Margin = 10;
		Layout.Spacing = 8;
		AcceptDrops = true;

		// ---- top bar ---------------------------------------------------------------------
		var top = Layout.AddRow();
		top.Spacing = 8;
		var add = top.Add( new Button.Primary( "Add Files…" ) { Icon = "add", Tint = Theme.Green, FixedHeight = 28 } );
		add.ToolTip = "Add .fbx / .bvh / .glb / .gltf / .vrm / .anm / .an5 / .cba animation files to convert (or drop them on this panel)";
		add.Clicked = AddFilesViaDialog;
		top.Add( new RtButton( this, "Smart Port…", "swap_horiz", () => new SmartPortDialog( this ).Show(),
			"Smart Port: bring a whole animation setup (clips, events and animgraph) from one model onto another character", 28 ) );
		top.AddStretchCell();
		_outputPath = top.Add( RtStyle.Muted( new Label( "", this ) { FixedHeight = 28, MinimumWidth = 20 }, small: true ) );
		_outputPath.Alignment = TextFlag.RightCenter;
		_convertButton = top.Add( new Button.Primary( "Convert All" ) { Icon = "play_arrow", Tint = Theme.Green, FixedHeight = 28 } );
		_convertButton.ToolTip = "Retarget every clip in the list and write the animation model";
		_convertButton.Clicked = () => _ = ConvertEntriesAsync( null );

		// ---- body: clips | settings -------------------------------------------------------
		var body = Layout.AddRow( 1 );
		body.Spacing = 8;

		var left = body.AddColumn( 1 );
		left.Spacing = 8;
		var list = left.Add( new RtCard( this ), 1 );
		var listHeader = list.Header( "movie", "Clips" );
		_countPill = listHeader.Add( new RtPill( list, "", Theme.TextLight, "Clips in the list" ) );
		listHeader.AddStretchCell();
		listHeader.Add( RtStyle.Icon( list, "add", AddFilesViaDialog, "Add animation files…", 22 ) );
		listHeader.Add( RtStyle.Icon( list, "playlist_remove", ClearEntries, "Remove every clip from the list", 22 ) );
		_dropZone = list.Layout.Add( new RtDropZone( list, AddFiles, AddFilesViaDialog ), 1 );
		_listScroll = list.Layout.Add( new ScrollArea( list ), 1 );
		_listScroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		_listScroll.Canvas = new Widget( _listScroll );
		_listScroll.SetStyles( "background-color: transparent;" );
		_listScroll.Canvas.SetStyles( "background-color: transparent;" );
		_listScroll.Canvas.Layout = Layout.Column();
		_listScroll.Canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 10, 0 );
		_listScroll.Canvas.Layout.Spacing = 4;
		_listLayout = _listScroll.Canvas.Layout;

		// "What happened" panel: import diagnostics, mapping notes, clip failures and write
		// warnings. Filled after every conversion; opens itself when something warned.
		var report = left.Add( new RtCard( this ) );
		_reportGroup = report;
		var reportHeader = report.Header( "receipt_long", "Conversion report" );
		reportHeader.AddStretchCell();
		reportHeader.Add( RtStyle.Icon( report, "close", () => _reportGroup.Visible = false, "Hide the report", 22 ) );
		var reportScroll = report.Layout.Add( new ScrollArea( report ) );
		reportScroll.MaximumHeight = 120;
		reportScroll.Canvas = new Widget( reportScroll );
		reportScroll.Canvas.Layout = Layout.Column();
		reportScroll.Canvas.Layout.Spacing = 2;
		_reportLines = reportScroll.Canvas.Layout;
		report.Visible = false;

		var side = body.Add( new ScrollArea( this ) { FixedWidth = SideWidth + 12 } );
		side.HorizontalScrollbarMode = ScrollbarMode.Off;
		var canvas = new Widget( side );
		side.SetStyles( "background-color: transparent;" );
		canvas.SetStyles( "background-color: transparent;" );
		canvas.Layout = Layout.Column();
		canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 12, 0 ); // clear of the overlay scrollbar
		canvas.Layout.Spacing = 8;
		BuildSettings( canvas );
		canvas.Layout.AddStretchCell();
		side.Canvas = canvas;

		// ---- status line --------------------------------------------------------------------
		var strip = Layout.AddRow();
		strip.Spacing = 8;
		_statusDot = strip.Add( new RtStatusDot( this ) );
		_statusLabel = strip.Add( new Label( this ) { Text = "Add animation files to get started.", WordWrap = false,
			FixedHeight = RtStyle.ControlHeight, MinimumWidth = 40 }, 1 );
		_reportToggle = strip.Add( new RtButton( this, "Report", "receipt_long", () => _reportGroup.Visible = !_reportGroup.Visible,
			"Show or hide the conversion report" ) );
		_reportToggle.Visible = false;
		_progressBar = strip.Add( new Widget( this ) { FixedHeight = 8, FixedWidth = 160 } );
		_progressBar.OnPaintOverride = PaintProgress;
		_progressBar.Visible = false;

		UpdateSettingsSummary();
	}

	void BuildSettings( Widget canvas )
	{
		// -- character -----------------------------------------------------------------------
		var character = canvas.Layout.Add( new RtCard( canvas ) );
		character.Header( "accessibility_new", "Character" );
		var targetCombo = character.Layout.Add( RtStyle.Field( new ComboBox( character ) { ToolTip = "The character the animations are retargeted onto" } ) );
		targetCombo.AddItem( "s&box Human (default)", "person", TrySelectSboxTarget, selected: true );
		targetCombo.AddItem( "s&box Citizen (classic)", "person_outline", TrySelectSboxCitizenTarget );
		targetCombo.AddItem( "Custom model (.vmdl)…", "view_in_ar", PickCustomModelTarget );
		targetCombo.AddItem( "Custom model file (.fbx/.glb/.gltf)…", "category", PickCustomFbxTarget );
		_targetInfo = character.Layout.Add( RtStyle.Muted( new Label( "", character ) { WordWrap = true }, small: true ) );
		_citizenSetupButton = character.Layout.Add( new Button( "Create Citizen animation model", "accessibility_new" ) { FixedHeight = 26 } );
		_citizenSetupButton.Enabled = false;
		_citizenSetupButton.Clicked = () => _ = CreateCitizenAnimationModelAsync();
		RtStyle.Check( character.Layout, "Copy editable Citizen animgraph", _copyAnimGraph, v => _copyAnimGraph = v,
			"Setup copies the actual Citizen graph to graphs/<model name>.vanmgrph beside the output model. Stock replacements always use a project-owned copy." );

		// -- output ----------------------------------------------------------------------------
		var output = canvas.Layout.Add( new RtCard( canvas ) );
		output.Header( "inventory_2", "Output" );
		var modeRow = RtStyle.FieldRow( output, output.Layout, "Write to", CaptionWidth );
		var outputCombo = modeRow.Add( RtStyle.Field( new ComboBox( output ) ), 1 );
		outputCombo.AddItem( "New animation model", "note_add", () => SetAugmentMode( false ), selected: true );
		outputCombo.AddItem( "Add to existing model…", "library_add", () => SetAugmentMode( true ) );
		_augmentRow = output.Layout.Add( new Widget( output ) );
		_augmentRow.Layout = Layout.Row();
		_augmentRow.Layout.Spacing = 6;
		_augmentRow.Layout.Add( RtStyle.Muted( new Label( "Model", _augmentRow ) { FixedWidth = CaptionWidth, FixedHeight = RtStyle.ControlHeight } ) );
		_pickAugmentButton = _augmentRow.Layout.Add( new RtButton( _augmentRow, "Pick vmdl…", "folder_open", PickAugmentAsset,
			"The model the animations are added to" ) );
		_augmentRow.Layout.AddStretchCell();
		_augmentRow.Visible = false;
		var folderRow = RtStyle.FieldRow( output, output.Layout, "Folder", CaptionWidth );
		_outputFolderEdit = folderRow.Add( RtStyle.Field( new LineEdit( output ) { Text = "animations/retargeted" } ), 1 );
		_outputFolderEdit.ToolTip = "Assets-relative folder the DMX files (and the standalone vmdl) are written to.";
		_outputFolderEdit.TextEdited += _ => UpdateSettingsSummary();
		var nameRow = RtStyle.FieldRow( output, output.Layout, "Model name", CaptionWidth );
		_outputNameEdit = nameRow.Add( RtStyle.Field( new LineEdit( output ) { Text = "retargeted_animations" } ), 1 );
		_outputNameEdit.ToolTip = "Filename for New animation vmdl output. The .vmdl extension is optional; existing-vmdl mode ignores this field.";
		_outputNameEdit.TextEdited += _ => UpdateSettingsSummary();

		// -- motion ----------------------------------------------------------------------------
		var motion = canvas.Layout.Add( new RtCard( canvas ) );
		motion.Header( "directions_run", "Motion" );
		var rootRow = RtStyle.FieldRow( motion, motion.Layout, "Root motion", CaptionWidth );
		var rootCombo = rootRow.Add( RtStyle.Field( new ComboBox( motion ) ), 1 );
		rootCombo.AddItem( "Keep as authored", null, () => _rootMotion = RootMotionMode.Off, selected: true );
		rootCombo.AddItem( "In place (strip)", null, () => _rootMotion = RootMotionMode.InPlace );
		rootCombo.AddItem( "Extract to root", null, () => _rootMotion = RootMotionMode.Extract );
		var loopRow = RtStyle.FieldRow( motion, motion.Layout, "Looping", CaptionWidth );
		var loopCombo = loopRow.Add( RtStyle.Field( new ComboBox( motion ) ), 1 );
		loopCombo.AddItem( "From source", null, () => _loopOverride = null, selected: true );
		loopCombo.AddItem( "Force on", null, () => _loopOverride = true );
		loopCombo.AddItem( "Force off", null, () => _loopOverride = false );
		RtStyle.Check( motion.Layout, "Foot-plant cleanup", _footPlant, v => _footPlant = v,
			"Lock planted feet to the ground and keep them from sliding." );
		RtStyle.Check( motion.Layout, "Arm effector IK", _armIk, v => _armIk = v,
			"Pull wrists onto limb-length-normalized source hand positions. On by default so differently proportioned arms "
			+ "preserve the source hand path; disable to preserve exact limb directions instead." );
		RtStyle.Check( motion.Layout, "Natural shoulders, neck, head, feet", _naturalCarriage, v => _naturalCarriage = v,
			"Keep the s&box body's own shoulder line, neck posture, skull attitude and ankle anatomy, transferring only the "
			+ "source's motion (a source whose bind pose is itself posed - e.g. a fighting-stance rest - automatically keeps the head "
			+ "following the source's gaze instead). Untick to exactly copy the source rig's shoulder/neck/head/foot directions "
			+ "(can look slumped/hunched, tip the head and bend planted feet upward on differently-proportioned rigs)." );
		var hipRow = RtStyle.FieldRow( motion, motion.Layout, "Hip scale", CaptionWidth,
			"Scale of the pelvis translation, horizontal / vertical. Empty = automatic (target hip height / source hip height)." );
		_hipScaleHEdit = hipRow.Add( RtStyle.Field( new LineEdit( motion ) { PlaceholderText = "auto" } ), 1 );
		_hipScaleHEdit.ToolTip = "Scale of the pelvis translation perpendicular to the character up axis. "
			+ "Empty = automatic (target hip height / source hip height).";
		_hipScaleVEdit = hipRow.Add( RtStyle.Field( new LineEdit( motion ) { PlaceholderText = "auto" } ), 1 );
		_hipScaleVEdit.ToolTip = "Scale of the pelvis translation along the character up axis. "
			+ "Empty = automatic (hip-height ratio).";
		var fpsRow = RtStyle.FieldRow( motion, motion.Layout, "Sample fps", CaptionWidth );
		_sampleFpsEdit = fpsRow.Add( RtStyle.Field( new LineEdit( motion ) { PlaceholderText = "30" } ), 1 );
		_sampleFpsEdit.ToolTip = "Sample rate the source clips are resampled to on import. "
			+ "Empty or 0 = default (30 fps).";

		// -- extra outputs ------------------------------------------------------------------------
		var extras = canvas.Layout.Add( new RtCard( canvas ) );
		extras.Header( "library_add_check", "Extra outputs" );
		RtStyle.Check( extras.Layout, "Footstep events", _footstepEvents, v => _footstepEvents = v,
			"Generates AE_FOOTSTEP events from detected foot plants." );
		RtStyle.Check( extras.Layout, "Mirrored variants (_M)", _mirroredVariants, v => _mirroredVariants = v,
			"Also produce a left/right-mirrored twin of every clip, named <clip>_M." );
		var additiveRow = extras.Layout.AddRow();
		additiveRow.Spacing = 6;
		RtStyle.Check( additiveRow, "Additive variants, frame", _additiveVariants, v => _additiveVariants = v,
			"Also emit an additive '<clip>_delta' sequence per clip (AnimSubtract) for animgraph layering." );
		_additiveReferenceEdit = additiveRow.Add( RtStyle.Field( new LineEdit( extras ) { Text = "0", FixedWidth = 44 } ) );
		_additiveReferenceEdit.ToolTip = "Zero-based frame in the sampled output. Choose a neutral pose; additive variants carry no footstep events or motion extraction.";
		additiveRow.AddStretchCell();

		// Smart-disabled toggle: RefreshLocomotionCheckbox (run on every list refresh) only
		// enables it while the current take rows actually contain a complete directional
		// family. HIDDEN from the panel (user request 2026-07-04: "i don't want it to be
		// seen") - the detection plumbing, the gate hooks and the smart-disable scan stay
		// wired so the feature can return by flipping Visible.
		_locomotionCheckbox = extras.Layout.Add( new Checkbox( "Detect locomotion sets" ) { Value = _detectLocomotionSets } );
		_locomotionCheckbox.Clicked = () => _detectLocomotionSets = _locomotionCheckbox.Value;
		_locomotionCheckbox.Visible = false;
	}

	/// <summary>The top bar says where Convert All writes.</summary>
	void UpdateSettingsSummary()
	{
		if ( !_outputPath.IsValid() )
			return;
		_outputPath.Text = _augmentMode
			? "→ " + (_augmentAsset?.Name ?? "pick a model to add to")
			: $"→ {NormalizedOutputFolder()}/{NormalizedOutputName()}.vmdl";
		if ( _targetInfo.IsValid() )
		{
			// Only when it adds something to the picker: an error, a warning or a custom model.
			_targetInfo.Text = _target is null ? (_targetError ?? "No target selected.")
				: _target.Warning is { Length: > 0 } warning ? warning
				: _target.ModelFilePath is not null || _target.CustomVmdlPath is not null ? _target.Description : "";
			_targetInfo.Visible = _targetInfo.Text.Length > 0;
		}
	}

	bool PaintProgress()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( _progressBar.LocalRect, 4 );
		var r = _progressBar.LocalRect;
		r.Width *= _progress.Clamp( 0f, 1f );
		Paint.SetBrush( Theme.Green );
		Paint.DrawRect( r, 4 );
		return true;
	}

	// Drop animation files anywhere on the panel.
	public override void OnDragHover( DragEvent e )
	{
		if ( RtDrop.Paths( e.Data ).Count > 0 )
			e.Action = DropAction.Link;
	}

	public override void OnDragDrop( DragEvent e )
	{
		var paths = RtDrop.Paths( e.Data );
		if ( paths.Count == 0 )
			return;
		e.Action = DropAction.Link;
		AddFiles( paths );
	}

	void ClearEntries()
	{
		if ( _converting )
			return;
		_entries.Clear();
		RefreshAll();
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
			var resolved = TargetPickers.FromModelAsset( asset, out var error, out var rejected );
			ApplyPickedTarget( resolved, error, rejected );
		};
		picker.Show();
	}

	void PickCustomFbxTarget()
	{
		var path = EditorUtility.OpenFileDialog( "Select target model",
			"3D Model Files (*.fbx *.glb *.gltf)", null );
		if ( string.IsNullOrEmpty( path ) )
			return;
		var resolved = TargetPickers.FromModelFile( path, out var error, out var rejected );
		ApplyPickedTarget( resolved, error, rejected );
	}

	void ApplyPickedTarget( TargetPickers.ResolvedTarget resolved, string error,
		TargetPickers.RejectedTarget rejected = null )
	{
		if ( resolved is null )
		{
			// Skeleton loaded but wasn't auto-recognized: any humanoid-LIKE rig (a paw,
			// one finger, a missing hand) is still a valid target once its core bones are
			// pointed out - open the manual mapper instead of dead-ending.
			if ( rejected is not null )
			{
				OfferManualTargetMapping( rejected, error );
				return;
			}

			_targetError = error ?? "Target rejected.";
			RefreshCitizenSetupButton();
			SetStatus( _targetError, Theme.Red );
			return;
		}

		_target = resolved;
		_targetError = null;
		if ( resolved.Warning is not null )
			SetStatus( resolved.Warning, Theme.Yellow );
		else
			RefreshStatus();

		// Custom FBX target with a skin: compile a mesh-only preview vmdl in the background
		// so the preview shows the actual model (the wireframe skeleton covers the wait and
		// stays the fallback). Skeleton-only FBX picks (Warning set) have nothing to skin.
		RefreshFbxTargetPreview( resolved );
	}

	/// <summary>(Re)compiles the FBX target's preview model in the background. Also run
	/// after every conversion: the preview vmdl otherwise only refreshes on re-pick, so a
	/// project carrying a preview compiled by an older library version kept showing white
	/// placeholder materials in the preview while ModelDoc showed the fixed output.</summary>
	void RefreshFbxTargetPreview( TargetPickers.ResolvedTarget resolved )
	{
		if ( resolved?.ModelFilePath is not null && resolved.Warning is null )
			_fbxPreviewTask = CompileFbxTargetPreviewAsync( resolved );
	}

	/// <summary>Pending FBX-target preview compile; conversions await it because it also
	/// rebuilds the rig from the compiled model (the engine-authoritative skeleton).</summary>
	Task _fbxPreviewTask;

	async Task CreateCitizenAnimationModelAsync()
	{
		if ( _converting || _augmentMode ) return;
		var target = _target;
		_converting = true;
		RefreshStatus();
		SetStatus( "Setting up all Citizen animations and the animation graph…", Theme.Blue );
		try
		{
			var result = await CitizenAnimationModels.CreateAsync( target, NormalizedOutputFolder(), NormalizedOutputName(), _copyAnimGraph,
				message => SetStatus( message, Theme.Blue ), groundModel: true );
			await EditorPipeline.SwitchToMainThread();
			SetStatus( result.Compiled ? $"Citizen animation model ready: {result.VmdlAsset?.Path}"
				: result.Errors.FirstOrDefault() ?? "The Citizen animation model did not compile.",
				result.Compiled ? Theme.Green : Theme.Red );
			MainAssetBrowser.Instance?.Local?.UpdateAssetList();
		}
		catch ( Exception e )
		{
			await EditorPipeline.SwitchToMainThread();
			SetStatus( e.Message, Theme.Red );
		}
		finally
		{
			_converting = false;
			RefreshCitizenSetupButton();
			_convertButton.Enabled = _entries.Any( e => e.Scene is not null );
		}
	}

	void RefreshCitizenSetupButton()
	{
		if ( !_citizenSetupButton.IsValid() ) return;
		var detected = CitizenAnimationModels.TryDetect( _target, out _, out var reason );
		_citizenSetupButton.Enabled = detected && !_converting && !_augmentMode && _targetError is null;
		_citizenSetupButton.SetStyles( _citizenSetupButton.Enabled ? $"color: {Theme.Green.Hex};" : "" );
		_citizenSetupButton.ToolTip = _augmentMode ? "Choose New animation vmdl to create a Citizen-ready custom model."
			: reason + (detected ? " Includes all stock animations, the animgraph, IK and helper constraints. No animation files required." : "");
	}

	async Task CompileFbxTargetPreviewAsync( TargetPickers.ResolvedTarget resolved )
	{
		SetStatus( $"Target: {resolved.Description}   ·   compiling its preview model…", Theme.Blue );
		var ok = await EditorPipeline.CompileModelTargetPreviewAsync( resolved, NormalizedOutputFolder() );
		await EditorPipeline.SwitchToMainThread();
		if ( !ReferenceEquals( _target, resolved ) )
			return; // user picked something else meanwhile
		RefreshCitizenSetupButton();
		if ( ok )
			RefreshStatus();
		else
			SetStatus( $"Target: {resolved.Description}   ·   preview model could not be compiled - "
				+ "the preview will show the wireframe skeleton instead.", Theme.Yellow );
	}

	/// <summary>Manual-mapping fallback for unrecognized custom targets: the same
	/// <see cref="MappingEditor"/> sources use, prefilled with the cascade's best-effort
	/// map. Confirming builds the target AND saves the mapping as a user preset, so the
	/// rig resolves automatically on every future pick.</summary>
	void OfferManualTargetMapping( TargetPickers.RejectedTarget rejected, string detectError )
	{
		SetStatus( $"{detectError} Map the target's bones manually to use it.", Theme.Yellow );

		var editor = new MappingEditor( this, rejected.DisplayName, rejected.Skeleton, rejected.BestEffortMap )
		{
			Applied = mapping =>
			{
				var resolved = TargetPickers.FromManualMapping( rejected, mapping, out var buildError );
				if ( resolved is null )
				{
					_targetError = buildError ?? "Target rejected.";
					SetStatus( _targetError, Theme.Red );
					return;
				}

				try
				{
					var assetsPath = Project.Current?.GetAssetsPath();
					if ( assetsPath is not null )
					{
						UserPresets.Save( assetsPath,
							HumanoidRetargeter.Core.Mapping.SkeletonSignature.Compute( rejected.Skeleton ),
							rejected.Skeleton, mapping );
					}
				}
				catch ( Exception e )
				{
					Log.Warning( $"[humanoid-retargeter] could not save the target mapping as a preset: {e.Message}" );
				}

				ApplyPickedTarget( resolved, null );
			},
		};
		editor.Show();
	}

	void SetAugmentMode( bool augment )
	{
		_augmentMode = augment;
		_augmentRow.Visible = augment;
		_outputNameEdit.Enabled = !augment;
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
		fd.SetNameFilter( "Animation Files (*.fbx *.bvh *.glb *.gltf *.vrm *.anm *.an5 *.cba)" );
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
		_rowRemovedAt = Environment.TickCount64;
		_entries.Remove( entry );
		RefreshAll();
	}

	/// <summary>
	/// Explicit skeleton selection for formats whose animation package carries no joint names:
	/// RenderWare .anm/.an5 uses a model .dff, while EA ANT .cba uses an ordered joint-table
	/// JSON. Re-loads the entry against the selected companion file.
	/// </summary>
	void PickSkeletonFor( SourceFileEntry entry )
	{
		var isAnt = SourceFileEntry.IsAntAnimation( entry.FilePath );
		var path = EditorUtility.OpenFileDialog(
			isAnt
				? $"Select ANT joint table (.json) for {entry.FileName}"
				: $"Select skeleton model (.dff) for {entry.FileName}",
			isAnt ? "Joint Tables (*.json)" : "RenderWare Models (*.dff)",
			System.IO.Path.GetDirectoryName( entry.FilePath ) );
		if ( string.IsNullOrEmpty( path ) )
			return;

		var reloaded = SourceFileEntry.Load( entry.FilePath, Project.Current?.GetAssetsPath(), path );
		var index = _entries.IndexOf( entry );
		if ( index >= 0 )
			_entries[index] = reloaded;
		else
			_entries.Add( reloaded );

		if ( reloaded.Status == EntryStatus.Failed )
			SetStatus( $"{reloaded.FileName}: {reloaded.StatusDetail}", Theme.Red );
		else if ( reloaded.NeedsUserDecision )
			ShowNoProfileDialog( reloaded );
		RefreshAll();
	}

	/// <summary>Removes one take row; the file entry goes with its last take.</summary>
	/// <summary>When a row was last removed (Environment.TickCount64): the rows below slide up
	/// under the cursor, so a quick next click must not count as a double-click on them.</summary>
	long _rowRemovedAt;

	void RemoveTake( SourceTakeEntry take )
	{
		_rowRemovedAt = Environment.TickCount64;
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

		HumanoidRetargeter.Core.RetargetResult result;
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

		// Source-ghost data for the dialog's "Show source" overlay: the imported scene's
		// clip for THIS take (definition rows get their sliced range so the ghost matches
		// what was solved).
		var dialog = new PreviewDialog( this, take.DisplayName, result.Clips, target, entry.Mapping.Source,
			entry.Scene.Skeleton, SourceClipFor( take ), entry.Mapping )
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

	/// <summary>The imported source clip a take row represents, for the preview's ghost
	/// overlay: definition rows (Unity sidecar) locate their take by name (the facade's rule:
	/// match <see cref="HumanoidRetargeter.Core.Formats.ExternalClipDef.TakeName"/>, else the first
	/// take) and slice it to the definition's range; plain rows take the scene clip at the
	/// take index. Null when nothing sensible exists (the ghost toggle then stays disabled).</summary>
	static HumanoidRetargeter.Core.Skeleton.Clip SourceClipFor( SourceTakeEntry take )
	{
		var entry = take.File;
		var scene = entry.Scene;
		if ( scene is null || scene.Clips.Count == 0 )
			return null;

		if ( entry.ClipDefinitions is not null )
		{
			if ( take.TakeIndex >= entry.ClipDefinitions.Count )
				return null;
			var def = entry.ClipDefinitions[take.TakeIndex];
			var clip = scene.Clips.FirstOrDefault( c => string.Equals( c.Name, def.TakeName, StringComparison.Ordinal ) )
				?? scene.Clips[0];
			try
			{
				return HumanoidRetargeter.Core.Formats.UnityMeta.Slice( clip, def );
			}
			catch ( Exception )
			{
				return clip; // unsliceable definition: the whole take still beats no ghost
			}
		}

		return scene.Clips[Math.Clamp( take.TakeIndex, 0, scene.Clips.Count - 1 )];
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
	/// <see cref="HumanoidRetargeter.Core.Dl.DlMappingDeriver"/>) and stores it as a user preset
	/// named <c>user_dl_*</c> - below-threshold roles stay unmapped. The correlation runs
	/// over the PREVIEWED take (not hardcoded take 0) and against the source resampled on
	/// the same fps grid the DL clip used - the preview's request may carry a user Sample
	/// fps while the entry's cached scene was imported at the default rate, and a frame-index
	/// correlation across different grids is time-misaligned.</summary>
	void TrySaveDerivedPreset( SourceTakeEntry take, HumanoidRetargeter.Core.RetargetResult result,
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
			var dlClip = new HumanoidRetargeter.Core.Skeleton.Clip(
				clip.ClipName, clip.Fps, clip.Looping, clip.SolvedFrames );

			// The DL output's fps is the import sample rate the preview's request used
			// (BuildRequest passes the Sample fps box through). Re-import the source on
			// that grid when it differs from the entry's cached scene so source frame i
			// and DL frame i are the same instant in time.
			var scene = entry.Scene;
			if ( take.TakeIndex >= scene.Clips.Count
				|| MathF.Abs( scene.Clips[take.TakeIndex].Fps - clip.Fps ) > 0.01f )
			{
				scene = Retargeter.ImportSource(
					entry.Bytes, entry.FileName, clip.Fps, entry.SkeletonBytes );
			}

			var derived = HumanoidRetargeter.Core.Dl.DlMappingDeriver.Derive(
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
	/// <see cref="HumanoidRetargeter.Core.RetargetRequest.TakeIndex"/> so each row converts only
	/// its own take; single-take files keep the all-takes default (equivalent).
	/// IMPORTANT: the decision keys on <see cref="SourceFileEntry.ClipCount"/> (the imported
	/// scene's immutable clip count), NOT on the live UI take list — RemoveTake mutates
	/// <c>File.Takes</c>, so a multi-take file reduced to one visible row must still convert
	/// only that row's take, not every take in the file.
	/// Unity-sidecar files (<see cref="SourceFileEntry.ClipDefinitions"/>) pass the
	/// definitions through and ALWAYS set the row index — TakeIndex then addresses the
	/// definition the row represents, and the facade slices the take to its frame range
	/// (preview re-solves via this same request, so it previews the sliced range too).</summary>
	HumanoidRetargeter.Core.RetargetRequest BuildRequest( SourceTakeEntry take, StockAnimationSlot slot = null ) => new()
	{
		SourceData = take.File.Bytes,
		SourceFileName = take.File.FileName,
		ExternalBufferResolver = uri => TargetPickers.ReadGltfDependency( take.File.FilePath, uri ),
		SkeletonData = take.File.SkeletonBytes, // RenderWare companion .dff; null otherwise
		SourceId = take.SourceId, // full path + take index: rows must join results unambiguously
		ClipDefinitions = take.File.ClipDefinitions,
		TakeIndex = take.File.ClipDefinitions is not null
			? take.TakeIndex
			: take.File.ClipCount > 1 ? take.TakeIndex : null,
		MappingOverride = take.File.Mapping,
		Solver = take.File.UseDlSolver
			? HumanoidRetargeter.Core.SolverKind.DeepLearning
			: HumanoidRetargeter.Core.SolverKind.Geometric,
		RootMotion = slot is null ? _rootMotion : RootMotionMode.InPlace,
		FootPlantCleanup = _footPlant,
		ArmEffectorIk = _armIk,
		GenerateFootstepEvents = _footstepEvents,
		CreateMirroredVariant = slot is null && _mirroredVariants,
		CreateAdditiveVariant = slot is null && _additiveVariants,
		AdditiveReferenceFrame = int.TryParse( _additiveReferenceEdit?.Text ?? "0", out var referenceFrame ) ? referenceFrame : -1,
		LoopingOverride = slot?.Looping ?? _loopOverride,
		ClipNameOverride = slot is null ? null : slot.ReplacementPrefix + Guid.NewGuid().ToString( "N" )[..8],
		SampleFps = ParsePositive( _sampleFpsEdit ),
		Solve = new HumanoidRetargeter.Core.Solve.SolveOptions
		{
			HipScaleHorizontal = ParsePositive( _hipScaleHEdit ),
			HipScaleVertical = ParsePositive( _hipScaleVEdit ),
			// null = recommended defaults (clavicle/neck/head/feet keep the target's
			// natural carriage, plus the solver's posed-rest fallbacks); empty map =
			// legacy all-absolute direction matching.
			TransferModes = _naturalCarriage
				? null
				: new Dictionary<HumanoidRetargeter.Core.Mapping.BoneRole, HumanoidRetargeter.Core.Solve.RoleTransferMode>(),
		},
	};

	/// <summary>The batch options the Convert All pipeline runs with (shared with the UI
	/// smoke gate's plumbing probe so the toggle → option wiring is asserted on the REAL
	/// construction site).</summary>
	HumanoidRetargeter.Core.BatchOptions BuildBatchOptions( string outputFolder, string augmentText ) => new()
	{
		DmxFolderRelative = outputFolder,
		AugmentVmdlText = augmentText,
		DetectLocomotionSets = _detectLocomotionSets,
	};

	/// <summary>UI smoke gate hook: flips the output-variant / locomotion checkboxes'
	/// backing fields and returns what <see cref="BuildRequest"/> +
	/// <see cref="BuildBatchOptions"/> produce, so the gate can assert the footstep-events /
	/// mirrored-variants / additive-variants / locomotion-sets plumbing end to end.</summary>
	internal (HumanoidRetargeter.Core.RetargetRequest Request, HumanoidRetargeter.Core.BatchOptions Options) BuildRequestForGate(
		SourceTakeEntry take, bool footstepEvents, bool mirroredVariants,
		bool additiveVariants, bool detectLocomotionSets )
	{
		_footstepEvents = footstepEvents;
		_mirroredVariants = mirroredVariants;
		_additiveVariants = additiveVariants;
		_detectLocomotionSets = detectLocomotionSets;
		return (BuildRequest( take ), BuildBatchOptions( NormalizedOutputFolder(), null ));
	}

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
	internal static async Task<(HumanoidRetargeter.Core.RetargetBatchResult Batch, EditorPipeline.WriteResult Write)>
		ConvertAndWriteAsync(
			IReadOnlyList<HumanoidRetargeter.Core.RetargetRequest> requests,
			TargetPickers.ResolvedTarget target,
			HumanoidRetargeter.Core.BatchOptions options,
			string augmentVmdlPath,
			string standaloneVmdlName = "retargeted_animations",
			Action<HumanoidRetargeter.Core.RetargetBatchResult> batchReady = null )
	{
		// Stale-entry preflight (MAIN thread - engine asset lookups): probe every existing
		// AnimFile source of the augment target against the project + mounted content and
		// hand the missing ones to the facade, which prunes those entries from the augmented
		// vmdl. Left in, ONE deleted/moved DMX fails the entire model recompile
		// ("Node 'X' resolve failure") and every animation the batch just added with it -
		// the exact user-reported "vmdl did not compile: citizen_human_male.vmdl".
		if ( options.AugmentVmdlText is not null && options.MissingAnimSources is null )
		{
			var missing = FindMissingAnimSources( options.AugmentVmdlText );
			if ( missing.Count > 0 )
			{
				options = new HumanoidRetargeter.Core.BatchOptions
				{
					AugmentVmdlText = options.AugmentVmdlText,
					DmxFolderRelative = options.DmxFolderRelative,
					AutoSuffixCollisions = options.AutoSuffixCollisions,
					DetectLocomotionSets = options.DetectLocomotionSets,
					MissingAnimSources = missing,
				};
			}
		}

		// Heavy, engine-free math: off the main thread so the editor stays responsive.
		var conversionSpec = target.Spec;
		if ( options.AugmentVmdlText is not null && ModelGrounding.Offset( options.AugmentVmdlText ) != 0 )
		{
			var destination = TargetPickers.FromModelAsset( AssetSystem.FindByPath( augmentVmdlPath ), out var error );
			if ( !CitizenAnimationModels.TryDetect( destination, out var reference, out var reason ) )
				throw new InvalidOperationException( error ?? reason );
			// Compiled preview includes grounding; source clips must not bake that modifier a second time.
			conversionSpec = StockAnimationReplacement.TargetSpec( reference, destination.PreviewModelPath );
		}
		var batch = await Task.Run( () => Retargeter.ConvertBatch( requests, conversionSpec, options ) );

		// Task.Run continuations are not guaranteed to resume on the editor main thread;
		// everything from here on may touch widgets/assets, so hop explicitly.
		await EditorPipeline.SwitchToMainThread();
		foreach ( var warning in batch.Warnings )
			Log.Warning( $"[humanoid-retargeter] {warning}" );
		batchReady?.Invoke( batch );

		// Augment requested but no augmented vmdl produced: fail before anything is written.
		if ( options.AugmentVmdlText is not null && batch.AugmentedVmdl is null )
			return (batch, null);

		var write = await EditorPipeline.WriteAndCompileAsync(
			batch, options.DmxFolderRelative, augmentVmdlPath, standaloneVmdlName,
			// Mesh-embedding vmdls import the whole target FBX at compile time - a real
			// 27 MB character exceeded the plain 120 s poll while still compiling fine.
			compileTimeoutSeconds: string.IsNullOrEmpty( target.Spec.MeshFilePath )
				? 120f : EditorPipeline.MeshCompileTimeoutSeconds );
		await EditorPipeline.SwitchToMainThread();
		return (batch, write);
	}

	/// <summary>
	/// Existing AnimFile sources of an augment target that resolve NEITHER as a file under
	/// the open project's Assets NOR through the asset system (mounted content - the citizen
	/// addon's shipped DMX, library assets, ...). Uncertainty (no project, lookup throw)
	/// reports the source as present - a kept stale entry fails one recompile, a wrongly
	/// pruned sequence destroys user data. MAIN THREAD ONLY (asset-system lookups).
	/// </summary>
	internal static IReadOnlyList<string> FindMissingAnimSources( string augmentVmdlText )
	{
		var missing = new List<string>();
		try
		{
			var assetsPath = Project.Current?.GetAssetsPath();
			foreach ( var source in HumanoidRetargeter.Core.Target.VmdlAugmenter
				.CollectAnimSourcePaths( augmentVmdlText ) )
			{
				try
				{
					var relative = source.Replace( '\\', '/' );
					if ( assetsPath is not null && File.Exists( System.IO.Path.Combine(
						assetsPath, relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ) ) )
					{
						continue;
					}
					if ( AssetSystem.FindByPath( relative ) is not null )
						continue;
					missing.Add( source );
				}
				catch ( Exception )
				{
					// uncertain: treat as present, never prune on doubt
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] stale-entry preflight failed: {e.Message}" );
		}
		return missing;
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

		// Custom FBX target + standalone output: embed the target mesh in the generated
		// vmdl (copy the FBX + sidecar textures into the output folder, point the spec's
		// MeshFilePath at it). Without a base model OR a mesh source the vmdl compiles
		// into an empty model - 0 bones, 0 sequences - and "playing" it does nothing
		// (user report 2026-07-04). Runs BEFORE the accumulate branch below so an existing
		// output vmdl can be healed with the mesh node too.
		var prepNotes = new List<string>();
		if ( !_augmentMode
			&& !EditorPipeline.PrepareModelTargetMesh( _target, NormalizedOutputFolder(), out var meshError, prepNotes ) )
		{
			SetStatus( meshError, Theme.Red );
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
		else
		{
			// Standalone output ACCUMULATES. Every conversion regenerating
			// retargeted_animations.vmdl from ONLY the current batch clobbered everything
			// earlier runs had written (user report: convert a large batch, then convert
			// one more row - the vmdl ends up with just that row). When the output vmdl
			// already exists, run the batch through the augment machinery against it
			// instead: same-named pipeline-owned AnimFiles are replaced (idempotent
			// re-runs), everything else is appended. An unparseable existing file falls
			// back to a fresh standalone write (pipeline-owned - never fail the batch on
			// our own artifact).
			var assetsPath = Project.Current?.GetAssetsPath();
			if ( assetsPath is not null )
			{
				var standalonePath = System.IO.Path.Combine(
					assetsPath,
					NormalizedOutputFolder().Replace( '/', System.IO.Path.DirectorySeparatorChar ),
					NormalizedOutputName() + ".vmdl" );
				if ( File.Exists( standalonePath ) )
				{
					try
					{
						var text = File.ReadAllText( standalonePath );
						HumanoidRetargeter.Core.Target.Kv3.Parse( text ); // reject corrupt files up front

						var existing = HumanoidRetargeter.Core.Target.VmdlAugmenter
							.GetModelSource( text );
						var targetMesh = _target.Spec.MeshFilePath ?? "";
						var targetBase = _target.Spec.BaseModelPath ?? "";
						var targetChanged = !string.IsNullOrEmpty( existing.RenderMesh )
							? !string.Equals( existing.RenderMesh, targetMesh,
								StringComparison.OrdinalIgnoreCase )
							: !string.IsNullOrEmpty( existing.BaseModel )
								&& !string.Equals( existing.BaseModel, targetBase,
									StringComparison.OrdinalIgnoreCase );
						if ( targetChanged )
						{
							// Animations are authored against one skeleton. Keeping entries
							// from a different embedded target produces plausible-looking but
							// incorrect limbs in ModelDoc. This is our standalone artifact, so
							// a target switch starts it fresh; explicit existing-vmdl mode is
							// never routed through this branch.
							Log.Info( $"[humanoid-retargeter] New-output target changed from "
								+ $"'{existing.RenderMesh ?? existing.BaseModel}' to "
								+ $"'{(targetMesh.Length > 0 ? targetMesh : targetBase)}' - regenerating "
								+ $"{System.IO.Path.GetFileName( standalonePath )}." );
						}
						else
						{
							// Heal legacy custom-model outputs that predate mesh embedding.
							if ( !string.IsNullOrEmpty( targetMesh ) )
							{
								text = HumanoidRetargeter.Core.Target.VmdlAugmenter.EnsureMeshFile(
									text, targetMesh, _target.Spec.MeshImportScale,
									_target.Spec.MaterialRemaps, _target.Spec.MeshImportNames );
							}

							augmentPath = standalonePath;
							augmentText = text;
						}
					}
					catch ( Exception e )
					{
						Log.Warning( $"[humanoid-retargeter] Existing {standalonePath} could not be "
							+ $"parsed ({FirstLine( e.Message )}) - regenerating it from this batch only." );
					}
				}
			}
		}

		// FBX targets: the pick-time preview compile also rebuilds the rig from the
		// COMPILED model (the engine-authoritative skeleton, rig == bind by construction).
		// Converting before it finishes would solve onto the drift-prone importer rig.
		if ( !_augmentMode && _fbxPreviewTask is { IsCompleted: false } pendingPreview )
		{
			SetStatus( "Preparing the target model (first-time mesh compile)…", Theme.Blue );
			await pendingPreview;
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
			var requests = list.Select( take => BuildRequest( take ) ).ToList();
			// Custom FBX target: convert its own embedded takes alongside the batch so the
			// output model keeps the animations the FBX shipped with.
			if ( !_augmentMode )
				requests.AddRange( EditorPipeline.BuildEmbeddedTakeRequests( _target ) );
			var options = BuildBatchOptions( outputFolder, augmentText );
			var target = _target;

			var (batch, write) = await ConvertAndWriteAsync(
				requests, target, options, augmentPath, NormalizedOutputName(),
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
				SetStatus( $"Augmenting {System.IO.Path.GetFileName( augmentPath )} failed - nothing written. {detail}", Theme.Red );
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

			ShowConversionReport( BuildConversionReport( batch, write, prepNotes ) );

			if ( write.VmdlAsset is not null )
				MainAssetBrowser.Instance?.Local?.UpdateAssetList();

			// FBX targets: refresh the preview model too - it otherwise only recompiles on
			// re-pick, leaving previews from older library versions (white placeholder
			// materials) on screen while ModelDoc already shows the fixed output.
			if ( !_augmentMode )
				RefreshFbxTargetPreview( target );
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

	void ApplyClipResults( IReadOnlyList<SourceTakeEntry> list, IReadOnlyList<HumanoidRetargeter.Core.ClipResult> clips )
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

	string NormalizedOutputName()
	{
		var name = (_outputNameEdit?.Text ?? "").Trim();
		if ( name.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
			name = name[..^5];
		name = new string( name.Select( c => char.IsLetterOrDigit( c ) || c is '_' or '-'
			? c : '_' ).ToArray() ).Trim( '_' );
		return name.Length == 0 ? "retargeted_animations" : name;
	}

	// ============================================================================ report

	/// <summary>
	/// Flattens one conversion's diagnostics into report lines: target preparation notes,
	/// per-file mapping summaries with their import/pipeline notes (deduplicated - every
	/// take of a file shares the file's mapping report), clip failures, and batch/write
	/// warnings. All of this already existed; it just only went to the console log.
	/// </summary>
	List<(string Icon, Color Color, string Text)> BuildConversionReport(
		HumanoidRetargeter.Core.RetargetBatchResult batch, EditorPipeline.WriteResult write,
		List<string> prepNotes )
	{
		var lines = new List<(string, Color, string)>();

		foreach ( var note in prepNotes )
			lines.Add( ("build", Theme.Yellow, note) );

		// One block per source FILE: mapping summary first, then its notes (mapping +
		// importer + pipeline). Notes live on the shared per-file report, so dedup by file.
		var seenFiles = new HashSet<string>();
		foreach ( var clip in batch.Clips )
		{
			if ( clip.Mapping is not { } m || !seenFiles.Add( clip.SourceFileName ) )
				continue;
			lines.Add( ("badge", Theme.Blue,
				$"{clip.SourceFileName}: mapped via '{m.ProfileName}' "
				+ $"({m.Confidence:0%} confidence, {m.MappedRoleCount} roles)") );
			foreach ( var note in m.Notes )
				lines.Add( ("sticky_note_2", Theme.TextLight, $"{clip.SourceFileName}: {note}") );
		}

		foreach ( var clip in batch.Clips.Where( c => !c.Success ) )
			lines.Add( ("error", Theme.Red,
				$"{clip.SourceFileName} · {clip.ClipName}: {clip.Error ?? "failed."}") );

		foreach ( var warning in batch.Warnings )
			lines.Add( ("warning", Theme.Yellow, warning) );
		foreach ( var error in write?.Errors ?? Enumerable.Empty<string>() )
			lines.Add( ("warning", Theme.Yellow, FirstLine( error )) );
		if ( write is { Compiled: false } )
			lines.Add( ("error", Theme.Red, "The output vmdl did not compile - see the console log.") );

		return lines;
	}

	/// <summary>Populates the report panel. Auto-opens when anything warned or failed;
	/// otherwise stays behind the status-strip Report button.</summary>
	void ShowConversionReport( List<(string Icon, Color Color, string Text)> lines )
	{
		if ( !_reportGroup.IsValid() )
			return;

		_reportLines.Clear( true );
		if ( lines.Count == 0 )
			lines.Add( ("check_circle", Theme.Green, "Converted clean - nothing to report.") );

		foreach ( var (icon, color, text) in lines )
		{
			var row = _reportLines.AddRow();
			row.Spacing = 6;
			row.Add( new IconButton( icon ) { ToolTip = text } );
			var label = row.Add( new Label( this ) { Text = text, ToolTip = text }, 1 );
			label.SetStyles( $"color: {color.Hex};" );
		}

		var notable = lines.Count( l => l.Color == Theme.Red || l.Color == Theme.Yellow );
		_reportToggle.Text = notable > 0 ? $"Report ({notable})" : "Report";
		_reportToggle.Visible = true;
		if ( notable > 0 )
			_reportGroup.Visible = true;
	}

	// ============================================================================ refresh

	void RefreshAll()
	{
		RebuildList();
		RefreshLocomotionCheckbox();
		RefreshStatus();
	}

	/// <summary>
	/// Smart-disable for the "Detect locomotion sets" toggle, run on every list refresh
	/// (files/takes added or removed): dry-run scans ALL current take-row clip names —
	/// sanitized exactly the way conversion names the clips — through
	/// <see cref="HumanoidRetargeter.Core.Target.LocomotionSetDetector.ScanNames"/>. No
	/// complete directional family → the toggle is disabled AND forced off (the batch
	/// could not emit any blend, so a stale tick must not linger); otherwise it is enabled
	/// and the tooltip names every detected family.
	/// </summary>
	void RefreshLocomotionCheckbox()
		=> ApplyLocomotionScan( _entries.SelectMany( e => e.Takes ).Select( t => t.TakeName ) );

	/// <summary>The scan + checkbox-state core of <see cref="RefreshLocomotionCheckbox"/>
	/// (internal so the UI smoke gate can drive it with synthetic clip names and assert the
	/// smart-disable behavior); returns the applied state for the gate's assertions.</summary>
	internal (bool Enabled, bool Value, string ToolTip) ApplyLocomotionScan( IEnumerable<string> clipNames )
	{
		if ( !_locomotionCheckbox.IsValid() )
			return (false, false, "");

		// Conversion detects families on SANITIZED clip names (Retargeter.SanitizeClipName
		// maps runs of non-[A-Za-z0-9_] to '_'), so this dry-run must scan the same
		// spelling: takes named "Walk N"/"Walk Forward" convert into a complete Walk_N
		// family and must enable the toggle, not trip "no set detected".
		var complete = HumanoidRetargeter.Core.Target.LocomotionSetDetector.ScanNames(
				clipNames.Select( n => string.IsNullOrEmpty( n )
					? n : HumanoidRetargeter.Core.Retargeter.SanitizeClipName( n ) ) )
			.Where( f => f.Complete ).ToList();
		if ( complete.Count == 0 )
		{
			_locomotionCheckbox.Enabled = false;
			_locomotionCheckbox.Value = false;
			_detectLocomotionSets = false;
			_locomotionCheckbox.ToolTip = "No directional animation set detected - needs e.g. "
				+ "Walk_N/Walk_E/Walk_S/Walk_W (or Forward/Back/Left/Right).";
		}
		else
		{
			_locomotionCheckbox.Enabled = true;
			_locomotionCheckbox.ToolTip = "Detected: " + string.Join( ", ",
				complete.Select( f => $"{f.Stem} ({(f.MemberCount == 8 ? "8-way" : "4-way")})" ) );
		}

		return (_locomotionCheckbox.Enabled, _locomotionCheckbox.Value, _locomotionCheckbox.ToolTip);
	}

	void RebuildList()
	{
		if ( _listLayout is null )
			return;

		_listLayout.Clear( true );
		var empty = _entries.Count == 0;
		if ( _dropZone.IsValid() )
			_dropZone.Visible = empty;
		if ( _listScroll.IsValid() )
			_listScroll.Visible = !empty;

		// One row per TAKE: a multi-take file unpacks into individual entries, each
		// independently previewable/removable/convertible. Unreadable files (no takes) keep a
		// single file-level row.
		foreach ( var entry in _entries )
		{
			if ( entry.Takes.Count == 0 )
				_listLayout.Add( new ClipRow( this, entry, null ) );
			else
				foreach ( var take in entry.Takes )
					_listLayout.Add( new ClipRow( this, entry, take ) );
		}
		_listLayout.AddStretchCell();

		if ( _countPill.IsValid() )
		{
			var clips = _entries.Sum( e => Math.Max( e.Takes.Count, 1 ) );
			_countPill.Set( clips == 0 ? "" : clips == 1 ? "1 CLIP" : $"{clips} CLIPS", Theme.TextLight );
		}
	}

	void RefreshStatus()
	{
		RefreshCitizenSetupButton();
		UpdateSettingsSummary();
		if ( _convertButton.IsValid() )
			_convertButton.Enabled = !_converting && _entries.Any( e => e.Scene is not null );

		if ( _targetError is not null )
			SetStatus( _targetError, Theme.Red );
		else if ( !_converting && _target is not null )
			SetStatus( $"{_target.Description}   ·   {_entries.Sum( e => Math.Max( e.Takes.Count, 1 ) )} clip(s)", Theme.TextLight );
	}

	void SetStatus( string text, Color color )
	{
		if ( !_statusLabel.IsValid() )
			return;
		_statusLabel.Text = text;
		_statusLabel.ToolTip = text;
		_statusLabel.SetStyles( $"color: {(color == Theme.TextLight ? Theme.Text : color).Hex};" );
		if ( _statusDot.IsValid() )
			_statusDot.Color = color == Theme.TextLight ? Theme.Green : color;
	}

	// ============================================================================ row widget

	void OpenStockReplacement( SourceTakeEntry take )
	{
		if ( _converting || _target is null ) return;
		if ( _augmentMode && _augmentAsset is null ) { SetStatus( "Choose an existing output model first.", Theme.Yellow ); return; }
		var path = _augmentMode ? _augmentAsset.Path : NormalizedOutputFolder() + "/" + NormalizedOutputName() + ".vmdl";
		var dialog = new Dialog( this );
		dialog.Window.WindowTitle = "Replace stock animation";
		dialog.Window.SetWindowIcon( "swap_horiz" );
		dialog.Window.SetModal( true, true );
		dialog.Window.MinimumWidth = 520;
		dialog.Layout = Layout.Column();
		dialog.Layout.Margin = 10;
		dialog.Layout.Spacing = 8;

		var suggestion = take.SuggestLocomotion();
		StockAnimationSlot selected = suggestion is null ? null : StockAnimationGraph.Slots.FirstOrDefault( s => s.Id == suggestion.SlotId );

		var card = dialog.Layout.Add( new RtCard( dialog ) );
		card.Header( "swap_horiz", "Replace a stock animation" );
		card.Layout.Add( RtStyle.Muted( new Label( dialog )
		{
			Text = "Swap one of the Citizen graph's own animations (idle, walk, run, jump) for this clip. "
				+ "The model keeps every other stock animation.",
			WordWrap = true,
		}, small: true ) );

		const float caption = 72f;
		var clipRow = RtStyle.FieldRow( card, card.Layout, "Clip", caption );
		clipRow.Add( new Label( card ) { Text = take.DisplayName, FixedHeight = RtStyle.FieldHeight, ToolTip = take.File.FilePath } ).SetStyles( "font-weight: 600;" );
		clipRow.AddStretchCell();
		var modelRow = RtStyle.FieldRow( card, card.Layout, "Model", caption );
		modelRow.Add( RtStyle.Muted( new Label( card ) { Text = path, FixedHeight = RtStyle.FieldHeight, ToolTip = path } ), 1 );

		var slotRow = RtStyle.FieldRow( card, card.Layout, "Replace", caption );
		var combo = slotRow.Add( RtStyle.Field( new ComboBox( card ) ), 1 );
		if ( suggestion is not null )
			slotRow.Add( new RtPill( card, "SUGGESTED", Theme.Green,
				$"Suggested from {suggestion.Basis}: {suggestion.Family} {suggestion.Direction}. You can pick another slot." ) );

		var note = card.Layout.Add( new Label( card ) { WordWrap = true } );
		void ShowNote( StockAnimationSlot slot )
		{
			note.Text = slot?.Warning ?? "Choose which stock animation to replace.";
			note.SetStyles( $"color: {(slot?.Warning is { Length: > 0 } ? Theme.Yellow : Theme.TextLight).Hex}; font-size: 11px;" );
		}

		card.Layout.Add( new RtSection( card, "What happens" ) );
		card.Layout.Add( RtStyle.Muted( new Label( card )
		{
			Text = "Needs a compatible Citizen armature. The clip is retargeted in place with the slot's loop setting into an "
				+ "editable, project-owned copy of the graph; stock helper and CopyPinky constraints are kept. Mirrored and additive variants are not made.",
			WordWrap = true,
		}, small: true ) );

		dialog.Layout.AddStretchCell();
		var buttons = dialog.Layout.AddRow();
		buttons.Spacing = 8;
		buttons.AddStretchCell();
		buttons.Add( new RtButton( dialog, "Cancel", null, dialog.Close, "Close without changing anything", 28 ) );
		var apply = buttons.Add( new Button.Primary( "Retarget and replace" ) { Icon = "swap_horiz", Tint = Theme.Green, FixedHeight = 28, Enabled = selected is not null } );

		combo.AddItem( "Choose a stock slot…", null, () => { selected = null; apply.Enabled = false; ShowNote( null ); }, selected: selected is null );
		foreach ( var slot in StockAnimationGraph.Slots )
			combo.AddItem( slot.Label, null, () => { selected = slot; apply.Enabled = true; ShowNote( slot ); }, selected: slot == selected );
		ShowNote( selected );
		apply.Clicked = () => { var slot = selected; dialog.Close(); _ = ReplaceStockAsync( take, slot, path ); };
		dialog.Window.AdjustSize();
		dialog.Show();
	}

	async Task ReplaceStockAsync( SourceTakeEntry take, StockAnimationSlot slot, string modelPath )
	{
		if ( _converting || slot is null ) return;
		_converting = true;
		RefreshStatus();
		SetStatus( "Retargeting clip and updating the project animgraph…", Theme.Blue );
		try
		{
			var result = await StockAnimationReplacement.ReplaceAsync( _target, BuildRequest( take, slot ), slot, modelPath );
			await EditorPipeline.SwitchToMainThread();
			SetStatus( result.Compiled ? $"Replaced {slot.Label}. Editable graph: {StockAnimationGraph.GraphPath( Path.GetDirectoryName( modelPath ).Replace( '\\', '/' ), Path.GetFileNameWithoutExtension( modelPath ) )}"
				: string.Join( "\n", result.Errors ), result.Compiled ? Theme.Green : Theme.Red );
		}
		catch ( Exception e ) { await EditorPipeline.SwitchToMainThread(); SetStatus( e.Message, Theme.Red ); }
		finally { _converting = false; RefreshCitizenSetupButton(); _convertButton.Enabled = _entries.Any( e => e.Scene is not null ); }
	}

	/// <summary>
	/// One clip row (or a file-level row for unreadable files): a status light; the clip name
	/// over its file, length and rate; the rig pill (per file — the mapping is per file); a
	/// Preview button and icon actions. Double-clicking the row previews it too.
	/// </summary>
	sealed class ClipRow : Widget
	{
		readonly RetargetWindow _window;
		readonly SourceFileEntry _entry;
		readonly SourceTakeEntry _take; // null only for unreadable (takeless) files

		const float PillColumn = 132f;
		readonly bool _dismissable;
		bool _pressed;

		public ClipRow( RetargetWindow window, SourceFileEntry entry, SourceTakeEntry take ) : base( window )
		{
			_window = window;
			_entry = entry;
			_take = take;

			FixedHeight = 46;
			MouseTracking = true;
			Layout = Layout.Row();
			Layout.Margin = new Sandbox.UI.Margin( 34, 5, 6, 5 ); // left margin = status light
			Layout.Spacing = 6;

			var detailText = take?.StatusDetail is { Length: > 0 } takeDetail ? takeDetail : entry.StatusDetail;
			var suggestion = take?.SuggestLocomotion();
			var failed = Status() is EntryStatus.Failed && detailText.Length > 0;
			// A file that could not be read has nothing to convert: clicking its row removes it.
			_dismissable = Status() is EntryStatus.Failed;
			if ( _dismissable )
				Cursor = CursorShape.Finger;

			// Name over details.
			var text = Layout.AddColumn( 1 );
			text.Spacing = 1;
			// Elided lines: a long name or error message is cut short with "…" (full text in the
			// tooltip) instead of widening the row past the list and cutting off its buttons.
			var name = text.Add( new RtElidedLabel( this, 9, 600 )
			{
				Text = (take?.DisplayName ?? entry.FileName) + (suggestion is null ? "" : " · " + suggestion.Direction),
				ToolTip = entry.FilePath + (detailText.Length > 0 ? "\n" + detailText : ""),
			} );
			text.Add( new RtElidedLabel( this, 8 )
			{
				Text = failed ? "Click to remove  ·  " + detailText : Details(),
				Color = failed ? Theme.Red : Theme.TextLight,
				ToolTip = detailText,
			} );

			// Rig pill, right-aligned in a fixed column so the pills line up down the list.
			var pillCell = Layout.Add( new Widget( this ) { FixedWidth = PillColumn } );
			pillCell.Layout = Layout.Row();
			pillCell.Layout.AddStretchCell();
			pillCell.Layout.Add( new RtPill( pillCell, entry.ChipText, RtStyle.Tone( entry.Tone ), "Detected rig · mapping confidence" ) );

			var canPreview = entry.Scene is not null && take is not null;
			var preview = Layout.Add( new RtButton( this, "Preview", "play_arrow", () => _window.OpenPreview( take ),
				"Solve and preview this clip on the character before converting" ) );
			preview.Enabled = canPreview;

			// Animation without a resolvable companion skeleton: offer an explicit .dff or ANT
			// joint-table selection (see SourceFileEntry.ResolveSkeletonFile).
			if ( entry.NeedsSkeletonFile )
			{
				Layout.Add( RtStyle.Icon( this, "accessibility", () => _window.PickSkeletonFor( entry ),
					SourceFileEntry.IsAntAnimation( entry.FilePath )
						? "Pick skeleton: EA ANT animations carry joint indices only — select the ordered joint-table JSON"
						: "Pick skeleton: RenderWare animations carry no skeleton — select the character's model .dff" ) );
			}

			var mapping = Layout.Add( RtStyle.Icon( this, "device_hub", () => _window.OpenMappingEditor( entry ),
				entry.Takes.Count > 1 ? "Bone mapping (shared by every take of this file)" : "Bone mapping" ) );
			mapping.Enabled = canPreview;
			var replace = Layout.Add( RtStyle.Icon( this, "swap_horiz", () => _window.OpenStockReplacement( take ),
				suggestion is null ? "Replace a stock animation: choose an Idle, Walk, Run or Jump slot in a copied Citizen graph"
					: $"Replace a stock animation. Suggested: {suggestion.Family} {suggestion.Direction} ({suggestion.Basis})." ) );
			replace.Enabled = canPreview;
			Layout.Add( RtStyle.Icon( this, "close", Remove, take is not null && entry.Takes.Count > 1 ? "Remove this take from the list" : "Remove from the list" ) );
		}

		/// <summary>"file.fbx · take 2 of 5 · 1.2 s · 30 fps".</summary>
		string Details()
		{
			var parts = new List<string> { _entry.FileName };
			if ( _take is not null && _entry.Takes.Count > 1 )
				parts.Add( $"take {_take.TakeIndex + 1} of {_entry.ClipCount}" );
			var clips = _entry.Scene?.Clips;
			if ( _take is not null && clips is { Count: > 0 } && _entry.ClipDefinitions is null )
			{
				var clip = clips[Math.Clamp( _take.TakeIndex, 0, clips.Count - 1 )];
				parts.Add( $"{clip.Duration:0.0#} s" );
				parts.Add( $"{clip.Fps:0} fps" );
			}
			return string.Join( "  ·  ", parts );
		}

		EntryStatus Status() => _take?.EffectiveStatus ?? _entry.Status;

		(string Icon, Color Color, string Tip) StatusIcon() => Status() switch
		{
			EntryStatus.Ready => ("check_circle", Theme.Green, "Ready to convert"),
			EntryStatus.NeedsReview => ("warning", Theme.Yellow, "Check the bone mapping before converting"),
			EntryStatus.Converting => ("sync", Theme.Blue, "Converting…"),
			EntryStatus.Converted => ("task_alt", Theme.Green, "Converted"),
			_ => ("error", Theme.Red, "Failed: click the row to remove it"),
		};

		protected override void OnMouseEnter() => Update();
		protected override void OnMouseLeave() => Update();

		protected override void OnMousePress( MouseEvent e )
		{
			base.OnMousePress( e );
			_pressed = true;
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );
			if ( _dismissable && e.LeftMouseButton )
				Remove();
		}

		void Remove()
		{
			if ( _take is not null )
				_window.RemoveTake( _take );
			else
				_window.RemoveEntry( _entry );
		}

		protected override void OnDoubleClick( MouseEvent e )
		{
			base.OnDoubleClick( e );
			// Only a double-click that started on this row: removing a row slides the next one
			// under the cursor, and a quick second click on the remove button used to land
			// here as a double-click and open that clip's preview.
			if ( _pressed && Environment.TickCount64 - _window._rowRemovedAt > 800 && _take is not null && _entry.Scene is not null )
				_window.OpenPreview( _take );
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.SetPen( Theme.ControlBackground.Lighten( .4f ), 1 );
			Paint.SetBrush( Paint.HasMouseOver ? Theme.WindowBackground.Lighten( .35f ) : Theme.WindowBackground );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 5 );

			var (icon, color, tip) = StatusIcon();
			ToolTip = tip;
			Paint.SetPen( color );
			Paint.DrawIcon( new Rect( 9, (Height - 18) * 0.5f, 18, 18 ), icon, 17 );
		}
	}
}
