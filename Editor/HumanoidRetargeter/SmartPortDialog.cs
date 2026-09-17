#nullable enable annotations

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Editor;

namespace HumanoidRetargeter.Editor;

/// <summary>A separate, non-destructive source-to-target model port workflow.</summary>
public sealed class SmartPortDialog : Dialog
{
	readonly Button _sourcePick, _targetPick, _port;
	readonly LineEdit _folder, _name;
	readonly Label _status;
	readonly CancellationTokenSource _cancel = new();
	Asset _source, _target;
	bool _busy, _destroyed;

	public SmartPortDialog( Widget parent ) : base( parent )
	{
		Window.WindowTitle = "Smart Port";
		Window.SetWindowIcon( "swap_horiz" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 620;
		Layout = Layout.Column();
		Layout.Margin = 20;
		Layout.Spacing = 12;
		Layout.Add( new Label.Subtitle( "Bring an animation setup to another character" ) );
		Layout.Add( new Label( this ) { WordWrap = true, Text =
			"Source supplies animations, events, rig settings and the actual animgraph. Target supplies its mesh, materials and collision. "
			+ "Matching armatures copy directly. Other recognized humanoids are retargeted automatically, retaining the graph's helper bones." } );
		_sourcePick = Layout.Add( new Button( "Source: choose animation model…", "directions_run" ) );
		_targetPick = Layout.Add( new Button( "Target: choose your character…", "accessibility_new" ) );
		_sourcePick.Clicked = () => Pick( true );
		_targetPick.Clicked = () => Pick( false );
		Layout.Add( new Label( this ) { Text = "Output folder (inside Assets):" } );
		_folder = Layout.Add( new LineEdit( this ) { Text = "models/smart_port" } );
		Layout.Add( new Label( this ) { Text = "New model name:" } );
		_name = Layout.Add( new LineEdit( this ) { Text = "ported_character" } );
		Layout.Add( new Label( this ) { WordWrap = true, Text =
			"Local and cloud assets are available in the picker. Compiled recovery is built in using ValveResourceFormat source (s2v.app); no separate program is needed. "
			+ "Recovery may not reproduce every authoring feature; review the generated model in game. Use assets you have permission to reuse." } );
		_status = Layout.Add( new Label( this ) { WordWrap = true } );
		var actions = Layout.AddRow();
		actions.Spacing = 8;
		_port = actions.Add( new Button( "Smart Port", "auto_fix_high" ) );
		_port.Clicked = () => _ = PortAsync();
		actions.AddStretchCell();
		var close = actions.Add( new Button( "Close" ) );
		close.Clicked = Close;
		Refresh();
		Window.AdjustSize();
	}

	void Pick( bool source )
	{
		var picker = AssetPicker.Create( this, AssetType.Model );
		picker.Window.Title = source ? "Smart Port — source animation model (local or cloud)" : "Smart Port — target character (local or cloud)";
		picker.OnAssetPicked = assets =>
		{
			if ( _destroyed || _busy ) return;
			var asset = assets.FirstOrDefault();
			if ( asset is null ) return;
			if ( source ) { _source = asset; _sourcePick.Text = "Source: " + asset.Path; }
			else { _target = asset; _targetPick.Text = "Target: " + asset.Path; }
			Refresh();
		};
		picker.Show();
	}

	void Refresh()
	{
		string reason;
		try { reason = SmartPortModels.Check( _source, _target ); }
		catch ( Exception e ) { reason = e.Message; }
		_port.Enabled = !_busy && reason is null;
		_port.SetStyles( reason is null ? "background-color: #287d46;" : "" );
		_status.Text = reason ?? "Compatible armatures. Ready to create a new model and project-owned animgraph.";
	}

	async Task PortAsync()
	{
		if ( _busy ) return;
		_busy = true;
		_status.ToolTip = "";
		_port.Enabled = _sourcePick.Enabled = _targetPick.Enabled = _folder.Enabled = _name.Enabled = false;
		try
		{
			var result = await SmartPortModels.CreateAsync( _source, _target, _folder.Text, _name.Text,
				message => { if ( !_destroyed ) _status.Text = message; }, _cancel.Token );
			if ( !_destroyed ) _status.Text = result.Compiled ? "Created and verified: " + result.VmdlAsset.Path
				: "Port did not pass verification:\n" + string.Join( "\n", result.Errors );
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			Log.Warning( $"[humanoid-retargeter] Smart Port failed: {e}" );
			if ( !_destroyed )
			{
				_status.Text = "Smart Port did not finish.\n" + e.Message;
				_status.ToolTip = e.Message;
			}
		}
		finally
		{
			_busy = false;
			if ( !_destroyed )
			{
				_sourcePick.Enabled = _targetPick.Enabled = _folder.Enabled = _name.Enabled = true;
				var status = _status.Text;
				Refresh();
				_status.Text = status;
			}
		}
	}

	public override void OnDestroyed()
	{
		_destroyed = true;
		_cancel.Cancel();
		base.OnDestroyed();
	}
}
