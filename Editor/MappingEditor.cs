#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using HumanoidRetargeter.Core.Mapping;
using Sandbox;
using SkeletonModel = HumanoidRetargeter.Core.Skeleton.Skeleton;

namespace HumanoidRetargeter.EditorTools;

/// <summary>
/// Manual bone-mapping editor: one row per canonical <see cref="BoneRole"/>, grouped
/// anatomically (Body, then Arms / Legs / Fingers with left and right side by side), each with
/// a dropdown of the source skeleton's bones (plus <c>&lt;none&gt;</c>), pre-filled from the
/// entry's current mapping. A light per row shows mapped, missing-but-required and duplicate
/// bones. Apply produces a <see cref="MappingSource.Manual"/> <see cref="MappingResult"/> that
/// the window installs as the file's mapping override.
/// </summary>
public sealed class MappingEditor : Dialog
{
	static readonly BoneRole[] Body =
	{
		BoneRole.Hips, BoneRole.Spine0, BoneRole.Spine1, BoneRole.Spine2,
		BoneRole.Spine3, BoneRole.Spine4, BoneRole.Neck, BoneRole.Head,
	};

	/// <summary>Paired groups: the left-side roles; the right side is the same names ending in R.</summary>
	static readonly (string Group, string Icon, string[] Roles)[] Paired =
	{
		("Arms", "back_hand", new[] { "Clavicle", "UpperArm", "LowerArm", "Hand" }),
		("Legs", "directions_walk", new[] { "UpperLeg", "LowerLeg", "Foot", "Toe" }),
		("Fingers", "pan_tool", Enum.GetValues<BoneRole>().Select( r => r.ToString() )
			.Where( n => n.EndsWith( "L", StringComparison.Ordinal )
				&& new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }.Any( f => n.StartsWith( f, StringComparison.Ordinal ) ) )
			.Select( n => n[..^1] ).ToArray()),
	};

	/// <summary>The chains retargeting needs; everything else may stay unmapped.</summary>
	static readonly HashSet<BoneRole> Required = new()
	{
		BoneRole.Hips, BoneRole.Head,
		BoneRole.UpperArmL, BoneRole.LowerArmL, BoneRole.HandL, BoneRole.UpperArmR, BoneRole.LowerArmR, BoneRole.HandR,
		BoneRole.UpperLegL, BoneRole.LowerLegL, BoneRole.FootL, BoneRole.UpperLegR, BoneRole.LowerLegR, BoneRole.FootR,
	};

	readonly SkeletonModel _skeleton;
	readonly Dictionary<BoneRole, int> _selection;
	readonly List<(BoneRole Role, Widget Row, RoleLight Light)> _rows = new();
	readonly RtPill _countPill;
	readonly Label _problem;

	/// <summary>Invoked with the manual mapping when the user applies.</summary>
	public Action<MappingResult> Applied { get; set; }

	/// <summary>Creates the editor pre-filled from <paramref name="current"/>.</summary>
	public MappingEditor( Widget parent, string fileName, SkeletonModel skeleton, MappingResult current )
		: base( parent )
	{
		_skeleton = skeleton;
		_selection = new Dictionary<BoneRole, int>( current?.RoleToBone ?? new Dictionary<BoneRole, int>() );

		Window.WindowTitle = $"Bone Mapping - {fileName}";
		Window.SetWindowIcon( "device_hub" );
		Window.SetModal( true, true );
		Window.MinimumWidth = 640;
		Window.MinimumHeight = 520;

		Layout = Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;

		// ---- header: what this is, how much is mapped, a filter -------------------------------
		var header = Layout.Add( new RtCard( this ) );
		var title = header.Header( "device_hub", fileName );
		_countPill = title.Add( new RtPill( header, "", Theme.TextLight, "Humanoid roles with a source bone assigned" ) );
		title.AddStretchCell();
		var filter = title.Add( RtStyle.Framed( new LineEdit( header ) { PlaceholderText = "Filter roles or bones…", FixedWidth = 200 } ) );
		filter.TextEdited += ApplyFilter;
		header.Layout.Add( RtStyle.Muted( new Label( this )
		{
			Text = "Pick the source bone for each role. Roles marked red are required (the body and limb chains); "
				+ "missing fingers and toes are fine.",
			WordWrap = true,
		}, small: true ) );

		// ---- groups ---------------------------------------------------------------------------
		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroll.SetStyles( "background-color: transparent;" );
		var canvas = new Widget( scroll );
		canvas.SetStyles( "background-color: transparent;" );
		canvas.Layout = Layout.Column();
		canvas.Layout.Margin = new Sandbox.UI.Margin( 0, 0, 12, 0 );
		canvas.Layout.Spacing = 8;
		scroll.Canvas = canvas;

		var body = canvas.Layout.Add( new RtCard( canvas ) );
		body.Header( "accessibility_new", "Body" );
		var bodyColumns = body.Layout.AddRow();
		bodyColumns.Spacing = 16;
		var bodyLeft = bodyColumns.AddColumn( 1 );
		var bodyRight = bodyColumns.AddColumn( 1 );
		bodyLeft.Spacing = bodyRight.Spacing = 4;
		for ( var i = 0; i < Body.Length; i++ )
			(i < Body.Length / 2 ? bodyLeft : bodyRight).Add( BuildRoleRow( body, Body[i], Friendly( Body[i].ToString() ) ) );

		foreach ( var (group, icon, roles) in Paired )
		{
			var card = canvas.Layout.Add( new RtCard( canvas ) );
			card.Header( icon, group );
			var columns = card.Layout.AddRow();
			columns.Spacing = 16;
			var left = columns.AddColumn( 1 );
			var right = columns.AddColumn( 1 );
			left.Spacing = right.Spacing = 4;
			left.Add( new RtSection( card, "Left" ) );
			right.Add( new RtSection( card, "Right" ) );
			foreach ( var stem in roles )
			{
				left.Add( BuildRoleRow( card, Enum.Parse<BoneRole>( stem + "L" ), Friendly( stem ) ) );
				right.Add( BuildRoleRow( card, Enum.Parse<BoneRole>( stem + "R" ), Friendly( stem ) ) );
			}
		}
		canvas.Layout.AddStretchCell();

		// ---- footer ---------------------------------------------------------------------------
		var buttons = Layout.AddRow();
		buttons.Spacing = 8;
		_problem = buttons.Add( new Label( this ) { WordWrap = false, MinimumWidth = 20 }, 1 );
		buttons.Add( new RtButton( this, "Cancel", null, Close, "Close without changing the mapping", 28 ) );
		var apply = buttons.Add( new Button.Primary( "Apply Mapping" ) { Icon = "check", Tint = Theme.Green, FixedHeight = 28 } );
		apply.Clicked = Apply;

		Refresh();
		Window.Size = new Vector2( 720, 760 );
	}

	/// <summary>"UpperArm" → "Upper arm", "IndexProx" → "Index 1", "Spine0" → "Spine 0".</summary>
	static string Friendly( string stem )
	{
		foreach ( var (suffix, text) in new[] { ("Meta", " base"), ("Prox", " 1"), ("Mid", " 2"), ("Dist", " 3") } )
		{
			if ( stem.EndsWith( suffix, StringComparison.Ordinal ) )
				return stem[..^suffix.Length] + text;
		}
		var words = System.Text.RegularExpressions.Regex.Replace( stem, "(?<=[a-z])(?=[A-Z0-9])", " " );
		return words.Length == 0 ? stem : words[0] + words[1..].ToLowerInvariant();
	}

	Widget BuildRoleRow( Widget owner, BoneRole role, string caption )
	{
		var row = new Widget( owner );
		row.Layout = Layout.Row();
		row.Layout.Spacing = 6;

		var light = row.Layout.Add( new RoleLight( row ) );
		var label = row.Layout.Add( new Label( row ) { Text = caption, FixedWidth = 84, FixedHeight = RtStyle.FieldHeight, ToolTip = role.ToString() } );
		label.Alignment = TextFlag.LeftCenter;

		var combo = row.Layout.Add( RtStyle.Field( new ComboBox( row ) ), 1 );
		combo.AddItem( "<none>", "block", () => { _selection.Remove( role ); Refresh(); },
			selected: !_selection.ContainsKey( role ) );
		for ( var i = 0; i < _skeleton.Count; i++ )
		{
			var boneIndex = i;
			combo.AddItem( _skeleton[i].Name, null, () => { _selection[role] = boneIndex; Refresh(); },
				selected: _selection.TryGetValue( role, out var sel ) && sel == boneIndex );
		}

		_rows.Add( (role, row, light) );
		return row;
	}

	/// <summary>Row lights, the mapped count and the first problem, after any change.</summary>
	void Refresh()
	{
		var duplicates = _selection.GroupBy( kv => kv.Value ).Where( g => g.Count() > 1 )
			.SelectMany( g => g.Select( kv => kv.Key ) ).ToHashSet();
		foreach ( var (role, _, light) in _rows )
		{
			var mapped = _selection.ContainsKey( role );
			light.Set( duplicates.Contains( role ) ? Theme.Red : mapped ? Theme.Green : Required.Contains( role ) ? Theme.Red : Theme.TextDisabled,
				duplicates.Contains( role ) ? "This bone is assigned to another role too"
				: mapped ? "Mapped" : Required.Contains( role ) ? "Required: pick a bone" : "Optional" );
		}
		_countPill.Set( $"{_selection.Count} / {_rows.Count} ROLES", _selection.Count > 0 ? Theme.Green : Theme.TextLight );

		var missing = Required.Where( r => !_selection.ContainsKey( r ) ).ToList();
		_problem.Text = duplicates.Count > 0 ? $"A bone is assigned to more than one role ({string.Join( ", ", duplicates.Take( 3 ) )})."
			: missing.Count > 0 ? $"{missing.Count} required role(s) still need a bone."
			: "";
		_problem.SetStyles( $"color: {(duplicates.Count > 0 ? Theme.Red : Theme.Yellow).Hex};" );
	}

	void ApplyFilter( string text )
	{
		text = (text ?? "").Trim();
		foreach ( var (role, row, _) in _rows )
		{
			var bone = _selection.TryGetValue( role, out var index ) ? _skeleton[index].Name : "";
			row.Visible = text.Length == 0
				|| role.ToString().Contains( text, StringComparison.OrdinalIgnoreCase )
				|| Friendly( role.ToString() ).Contains( text, StringComparison.OrdinalIgnoreCase )
				|| bone.Contains( text, StringComparison.OrdinalIgnoreCase );
		}
	}

	void Apply()
	{
		// Reject duplicate assignments up front (the target rig builder would throw later).
		var duplicates = _selection.GroupBy( kv => kv.Value ).Where( g => g.Count() > 1 ).ToList();
		if ( duplicates.Count > 0 )
		{
			var first = duplicates[0];
			var roles = string.Join( ", ", first.Select( kv => kv.Key ) );
			new PopupWindow( "Duplicate assignment",
				$"Bone \"{_skeleton[first.Key].Name}\" is assigned to multiple roles: {roles}." )
				.Show();
			return;
		}

		var result = new MappingResult( "manual", MappingSource.Manual ) { Confidence = 1f };
		foreach ( var kv in _selection )
			result.RoleToBone[kv.Key] = kv.Value;
		result.Notes.Add( "Mapping assigned by hand in the mapping editor." );

		Applied?.Invoke( result );
		Close();
	}

	/// <summary>The small status light before each role.</summary>
	sealed class RoleLight : Widget
	{
		Color _color = Theme.TextDisabled;

		public RoleLight( Widget parent ) : base( parent )
		{
			FixedSize = 10;
		}

		public void Set( Color color, string tooltip )
		{
			_color = color;
			ToolTip = tooltip;
			Update();
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( _color );
			Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );
		}
	}
}
