#nullable enable annotations

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>A separate, non-destructive source-to-target model port workflow.</summary>
public sealed class SmartPortDialog : Dialog
{
	readonly ModelTile _sourcePick, _targetPick;
	readonly Button _port;
	readonly LineEdit _folder, _name;
	readonly Label _status, _description;
	readonly RtStatusDot _statusDot;
	readonly ComboBox _mode;
	readonly CancellationTokenSource _cancel = new();
	Asset _source, _target;
	bool _extend, _busy, _destroyed;

	public SmartPortDialog( Widget parent ) : base( parent )
	{
		Window.WindowTitle = "Smart Port";
		Window.SetWindowIcon( "swap_horiz" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 620;
		Layout = Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;

		// ---- the two models ------------------------------------------------------------------
		var models = Layout.Add( new RtCard( this ) );
		models.Header( "swap_horiz", "Bring an animation setup to another character" );
		var tiles = models.Layout.AddRow();
		tiles.Spacing = 8;
		_sourcePick = tiles.Add( new ModelTile( models, "directions_run", "SOURCE", "Model with the animations", () => Pick( true ) ), 1 );
		tiles.Add( new Arrow( models ) );
		_targetPick = tiles.Add( new ModelTile( models, "accessibility_new", "TARGET", "Your character", () => Pick( false ) ), 1 );

		const float caption = 72f;
		var modeRow = RtStyle.FieldRow( models, models.Layout, "Mode", caption );
		_mode = modeRow.Add( RtStyle.Field( new ComboBox( models ) ), 1 );
		_mode.AddItem( "Use the source's setup", "call_split", () => SetMode( false ), selected: true,
			description: "Source animations, events, rig settings and animgraph; target mesh, materials and collision" );
		_mode.AddItem( "Keep the target's setup, add source clips", "library_add", () => SetMode( true ),
			description: "The target's own animations and graph stay the default; source clips are added through a selector" );
		_description = models.Layout.Add( RtStyle.Muted( new Label( this ) { WordWrap = true }, small: true ) );

		// ---- output ---------------------------------------------------------------------------
		var output = Layout.Add( new RtCard( this ) );
		output.Header( "inventory_2", "Output" );
		var folderRow = RtStyle.FieldRow( output, output.Layout, "Folder", caption, "A folder inside Assets" );
		_folder = folderRow.Add( RtStyle.Field( new LineEdit( output ) { Text = "models/smart_port" } ), 1 );
		var nameRow = RtStyle.FieldRow( output, output.Layout, "Model name", caption );
		_name = nameRow.Add( RtStyle.Field( new LineEdit( output ) { Text = "ported_character" } ), 1 );
		output.Layout.Add( RtStyle.Muted( new Label( this )
		{
			WordWrap = true,
			Text = "Local and cloud models can be picked. Compiled models are recovered in-process (ValveResourceFormat); no separate program is needed. "
				+ "Recovery may not reproduce every authoring feature, so review the new model in game. Originals are never overwritten. "
				+ "Use assets you have permission to reuse.",
		}, small: true ) );

		// ---- status and actions ------------------------------------------------------------------
		var status = Layout.AddRow();
		status.Spacing = 8;
		_statusDot = status.Add( new RtStatusDot( this ) );
		_status = status.Add( new Label( this ) { WordWrap = true, MinimumHeight = RtStyle.ControlHeight }, 1 );
		status.Add( new RtButton( this, "Close", null, Close, "Close Smart Port", 28 ) );
		_port = status.Add( new Button.Primary( "Smart Port" ) { Icon = "auto_fix_high", Tint = Theme.Green, FixedHeight = 28 } );
		_port.Clicked = () => _ = PortAsync();

		Refresh();
		Window.AdjustSize();
	}

	void SetMode( bool extend )
	{
		_extend = extend;
		Refresh();
	}

	void Pick( bool source )
	{
		if ( _busy )
			return;
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Window.Title = source ? "Smart Port — source animation model (local or cloud)" : "Smart Port — target character (local or cloud)";
		picker.OnAssetPicked = assets =>
		{
			if ( _destroyed || _busy ) return;
			var asset = assets.FirstOrDefault();
			if ( asset is null ) return;
			if ( source ) { _source = asset; _sourcePick.SetModel( asset.Path ); }
			else { _target = asset; _targetPick.SetModel( asset.Path ); }
			Refresh();
		};
		picker.Show();
	}

	void Refresh()
	{
		_description.Text = _extend
			? "The target keeps its animations, graph logic and character setup. Source clips are retargeted and added through a new graph selector; the source graph's logic is not merged. A new model and editable graph are created; both originals stay untouched."
			: "The source supplies animations, events, rig settings and its animgraph. The target supplies its mesh, materials and collision. Matching armatures copy directly; other recognized humanoids are retargeted automatically.";
		string reason;
		try { reason = _extend ? SmartPortModels.CheckExtended( _source, _target ) : SmartPortModels.Check( _source, _target ); }
		catch ( Exception e ) { reason = e.Message; }
		_port.Enabled = !_busy && reason is null;
		SetStatus( reason ?? "Compatible armatures. Ready to create a new model and project-owned animgraph.",
			reason is null ? Theme.Green : _source is null || _target is null ? Theme.TextLight : Theme.Yellow );
	}

	void SetStatus( string text, Color color )
	{
		_status.Text = text;
		_status.SetStyles( $"color: {(color == Theme.TextLight ? Theme.Text : color).Hex};" );
		_statusDot.Color = color == Theme.TextLight ? Theme.TextLight : color;
	}

	async Task PortAsync()
	{
		if ( _busy ) return;
		_busy = true;
		_status.ToolTip = "";
		_port.Enabled = _sourcePick.Enabled = _targetPick.Enabled = _folder.Enabled = _name.Enabled = _mode.Enabled = false;
		try
		{
			Action<string> progress = message => { if ( !_destroyed ) SetStatus( message, Theme.Blue ); };
			var result = _extend
				? await SmartPortModels.CreateExtendedAsync( _source, _target, _folder.Text, _name.Text, progress, _cancel.Token )
				: await SmartPortModels.CreateAsync( _source, _target, _folder.Text, _name.Text, progress, _cancel.Token );
			if ( !_destroyed )
			{
				if ( result.Compiled )
					SetStatus( "Created and verified: " + result.VmdlAsset.Path
						+ (_extend ? "\nExisting animations remain the default. Clip controls are listed in the output's _smart_port/clips.txt." : ""), Theme.Green );
				else
					SetStatus( "Port did not pass verification:\n" + string.Join( "\n", result.Errors ), Theme.Red );
			}
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] Smart Port failed: {e}" );
			if ( !_destroyed )
			{
				SetStatus( "Smart Port did not finish.\n" + e.Message, Theme.Red );
				_status.ToolTip = e.Message;
			}
		}
		finally
		{
			_busy = false;
			if ( !_destroyed )
			{
				_sourcePick.Enabled = _targetPick.Enabled = _folder.Enabled = _name.Enabled = _mode.Enabled = true;
				var text = _status.Text;
				var tooltip = _status.ToolTip;
				Refresh();
				// Keep the outcome on screen; Refresh only restores the Port button state.
				_status.Text = text;
				_status.ToolTip = tooltip;
			}
		}
	}

	public override void OnDestroyed()
	{
		_destroyed = true;
		_cancel.Cancel();
		base.OnDestroyed();
	}

	/// <summary>A clickable tile for one of the two models: role, icon and the chosen path.</summary>
	sealed class ModelTile : Widget
	{
		readonly string _icon, _role, _hint;
		readonly Action _clicked;
		string _path;

		public ModelTile( Widget parent, string icon, string role, string hint, Action clicked ) : base( parent )
		{
			_icon = icon;
			_role = role;
			_hint = hint;
			_clicked = clicked;
			FixedHeight = 64;
			MinimumWidth = 200;
			Cursor = CursorShape.Finger;
			MouseTracking = true;
			ToolTip = $"{hint}: click to choose a model (local or cloud)";
		}

		public void SetModel( string path )
		{
			_path = path;
			Update();
		}

		protected override void OnMouseEnter() => Update();
		protected override void OnMouseLeave() => Update();

		protected override void OnMouseClick( MouseEvent e )
		{
			base.OnMouseClick( e );
			if ( Enabled && e.LeftMouseButton )
				_clicked();
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			var chosen = _path is not null;
			var hover = Paint.HasMouseOver && Enabled;
			Paint.SetPen( chosen ? Theme.Green.WithAlpha( .55f ) : Theme.ControlBackground.Lighten( hover ? .7f : .45f ), 1 );
			Paint.SetBrush( hover ? Theme.WindowBackground.Lighten( .35f ) : Theme.WindowBackground );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 6 );

			Paint.SetPen( chosen ? Theme.Green : Theme.TextLight );
			Paint.DrawIcon( new Rect( 12, (Height - 28) * .5f, 28, 28 ), _icon, 26 );

			var x = 50f;
			var width = Width - x - 12;
			Paint.SetDefaultFont( 7, 600 );
			Paint.SetPen( Theme.TextLight );
			Paint.DrawText( new Rect( x, 12, width, 14 ), $"{_role} · {_hint}", TextFlag.LeftCenter );
			Paint.SetDefaultFont( 9, chosen ? 600 : 400 );
			Paint.SetPen( !Enabled ? Theme.TextDisabled : chosen ? Theme.Text : Theme.TextLight );
			var text = chosen ? _path : "Choose…";
			Paint.DrawText( new Rect( x, 30, width, 20 ), Paint.GetElidedText( text, width, ElideMode.Left, TextFlag.LeftCenter ), TextFlag.LeftCenter );
		}
	}

	/// <summary>The arrow between the two tiles.</summary>
	sealed class Arrow : Widget
	{
		public Arrow( Widget parent ) : base( parent )
		{
			FixedSize = new Vector2( 24, 64 );
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( LocalRect, "arrow_forward", 20 );
		}
	}
}
