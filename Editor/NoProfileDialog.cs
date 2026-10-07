#nullable enable annotations

using System;
using Editor;
using Sandbox;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// The "No known profile found for this rig" decision dialog (design §6 no-profile flow),
/// shown per file when <c>NeedsUserDecision</c> is set after adding it: no preset profile
/// matched and the auto-mapper's confidence is below the detection threshold. Offers
/// auto-map (recommended), the deep-learning solver (enabled when the SAME weight asset
/// is installed - see <see cref="DlAssets"/>), and manual mapping.
/// </summary>
public sealed class NoProfileDialog : Dialog
{
	/// <summary>"Auto-map blindly" chosen: proceed with the best-effort auto mapping.</summary>
	public Action AutoMapChosen { get; set; }

	/// <summary>"Deep learning (experimental)" chosen: solve with the skeleton-agnostic
	/// DL solver and preview (design §6 option 2 / §10).</summary>
	public Action DeepLearningChosen { get; set; }

	/// <summary>"Manual mapping…" chosen: open the mapping editor.</summary>
	public Action ManualChosen { get; set; }

	/// <summary>Dialog dismissed without choosing (file stays in needs-review state).</summary>
	public Action Dismissed { get; set; }

	bool _chose;

	/// <summary>Creates the dialog for one source file. <paramref name="dlAvailable"/>
	/// enables the deep-learning option (the committed weight asset was found).</summary>
	public NoProfileDialog( Widget parent, string fileName, float autoConfidence, bool dlAvailable = false ) : base( parent )
	{
		Window.WindowTitle = "No known profile";
		Window.SetWindowIcon( "person_search" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 660;

		Layout = Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;

		var card = Layout.Add( new RtCard( this ) );
		var header = card.Header( "person_search", "No known profile for this rig" );
		header.AddStretchCell();
		header.Add( new RtPill( card, $"AUTO {autoConfidence * 100f:0}%", autoConfidence >= 0.75f ? Theme.Green : autoConfidence >= 0.45f ? Theme.Yellow : Theme.Red,
			"How sure the automatic mapper is about its best-effort mapping" ) );
		card.Layout.Add( RtStyle.Muted( new Label( this )
		{
			Text = $"\"{fileName}\" doesn't match a built-in profile (Mixamo, ActorCore/CC, UE Mannequin, Rigify, 3ds Max Biped and others) "
				+ "or a saved preset. Choose how to map its bones:",
			WordWrap = true,
		} ) );

		card.Layout.Add( new ChoiceRow( card, "auto_fix_high", "Auto-map", "RECOMMENDED", Theme.Green,
			$"Use the automatic mapping ({autoConfidence * 100f:0}% confidence). Check it in the preview before converting.",
			() => Choose( AutoMapChosen ) ) );
		card.Layout.Add( new ChoiceRow( card, "device_hub", "Map bones manually", null, Theme.TextLight,
			"Pick the source bone for each role yourself; it can be saved as a profile for this rig.",
			() => Choose( ManualChosen ) ) );
		var dl = card.Layout.Add( new ChoiceRow( card, "psychology", "Deep learning", "EXPERIMENTAL", Theme.Yellow,
			dlAvailable
				? "A neural retarget (SAME) that needs no mapping. Expect imperfect hands. Non-commercial license (CC BY-NC 4.0)."
				: "Not installed: Assets/humanoid_retargeter/dl/same_v1.weights was not found.",
			() => Choose( DeepLearningChosen ) ) );
		dl.Enabled = dlAvailable;

		var buttons = Layout.AddRow();
		buttons.AddStretchCell();
		buttons.Add( new RtButton( this, "Decide later", null, Close, "Keep the file in the list as needing review", 28 ) );

		Window.AdjustSize();
	}

	void Choose( Action action )
	{
		_chose = true;
		action?.Invoke();
		Close();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		if ( !_chose )
			Dismissed?.Invoke();
	}

	/// <summary>One clickable choice: icon, title and tag, and a line explaining it.</summary>
	sealed class ChoiceRow : Widget
	{
		readonly string _icon, _title, _tag, _text;
		readonly Color _tagColor;
		readonly Action _clicked;

		public ChoiceRow( Widget parent, string icon, string title, string tag, Color tagColor, string text, Action clicked ) : base( parent )
		{
			_icon = icon;
			_title = title;
			_tag = tag;
			_tagColor = tagColor;
			_text = text;
			_clicked = clicked;
			FixedHeight = 56;
			Cursor = CursorShape.Finger;
			MouseTracking = true;
			ToolTip = text;
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
			var hover = Paint.HasMouseOver && Enabled;
			Paint.SetPen( hover ? Theme.Green.WithAlpha( .6f ) : Theme.ControlBackground.Lighten( .45f ), 1 );
			Paint.SetBrush( hover ? Theme.WindowBackground.Lighten( .35f ) : Theme.WindowBackground );
			Paint.DrawRect( LocalRect.Shrink( .5f ), 6 );

			Paint.SetPen( !Enabled ? Theme.TextDisabled : Theme.Green );
			Paint.DrawIcon( new Rect( 12, (Height - 24) * .5f, 24, 24 ), _icon, 22 );

			var x = 48f;
			Paint.SetDefaultFont( 9, 600 );
			Paint.SetPen( Enabled ? Theme.Text : Theme.TextDisabled );
			var titleWidth = Paint.MeasureText( _title ).x;
			Paint.DrawText( new Rect( x, 9, titleWidth + 4, 18 ), _title, TextFlag.LeftCenter );
			if ( !string.IsNullOrEmpty( _tag ) )
			{
				Paint.SetDefaultFont( 7, 600 );
				var tagWidth = Paint.MeasureText( _tag ).x + 16;
				var tag = new Rect( x + titleWidth + 10, 9, tagWidth, 18 );
				var color = Enabled ? _tagColor : Theme.TextDisabled;
				Paint.ClearPen();
				Paint.SetBrush( color.WithAlpha( .18f ) );
				Paint.DrawRect( tag, 9 );
				Paint.SetPen( color );
				Paint.DrawText( tag, _tag, TextFlag.Center );
			}
			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextLight );
			var width = Width - x - 12;
			Paint.DrawText( new Rect( x, 29, width, 18 ), Paint.GetElidedText( _text, width, ElideMode.Right, TextFlag.LeftCenter ), TextFlag.LeftCenter );
		}
	}
}
