using System;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Port, autostart, permission mode and maintenance actions.
/// </summary>
public class SettingsPage : Widget
{
	readonly LineEdit _port;

	public SettingsPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 10;

		// ---- permission mode ----------------------------------------------
		var permCard = Layout.Add( new Card( this ) );
		OverviewPage.AddCardTitle( permCard, "Permissions", "shield" );

		var hint = permCard.Layout.Add( new Label(
			"Controls what connected AI clients may do. Write tools modify your project.", permCard ) );
		hint.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 11px;" );
		hint.WordWrap = true;

		var modes = permCard.Layout.AddRow();
		modes.Spacing = 6;
		modes.Add( new ModeButton( PermissionMode.FullAccess, "Full access", "bolt", Palette.Error, permCard ) );
		modes.Add( new ModeButton( PermissionMode.ApproveWrites, "Approve writes", "how_to_reg", Palette.Warning, permCard ) );
		modes.Add( new ModeButton( PermissionMode.ReadOnly, "Read-only", "visibility", Palette.Running, permCard ) );
		modes.AddStretchCell();

		// ---- server settings ------------------------------------------------
		var serverCard = Layout.Add( new Card( this ) );
		OverviewPage.AddCardTitle( serverCard, "Server", "settings_ethernet" );

		var portRow = serverCard.Layout.AddRow();
		portRow.Spacing = 6;

		var portLabel = portRow.Add( new Label( "Port", serverCard ) );
		portLabel.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 11px;" );

		_port = portRow.Add( new LineEdit( serverCard ) { Text = McpSettings.Port.ToString() } );
		_port.FixedWidth = 80;

		portRow.Add( new ActionButton( "Apply & restart", "save", serverCard )
		{
			Accent = Palette.GradientA,
			Clicked = ApplyPort
		} );
		portRow.AddStretchCell();

		serverCard.Layout.Add( new AutoStartButton( serverCard ) );

		// ---- maintenance ---------------------------------------------------
		var maintCard = Layout.Add( new Card( this ) );
		OverviewPage.AddCardTitle( maintCard, "Maintenance", "cleaning_services" );

		var maintRow = maintCard.Layout.AddRow();
		maintRow.Spacing = 6;
		maintRow.Add( new ActionButton( "Clear activity", "delete_sweep", maintCard ) { Accent = Palette.TextDim, Filled = false, Clicked = ActivityLog.Clear } );
		maintRow.Add( new ActionButton( "Clear logs", "playlist_remove", maintCard ) { Accent = Palette.TextDim, Filled = false, Clicked = LogCapture.Clear } );
		maintRow.AddStretchCell();

		Layout.AddStretchCell();
	}

	void ApplyPort()
	{
		if ( !int.TryParse( _port.Text, out var port ) || port is < 1024 or > 65535 )
		{
			_port.Text = McpSettings.Port.ToString();
			return;
		}

		McpSettings.Port = port;
		McpHost.Restart();
	}
}

/// <summary>
/// Segmented permission-mode button; paints itself from the live setting.
/// </summary>
public class ModeButton : Widget
{
	readonly PermissionMode _mode;
	readonly string _text;
	readonly string _icon;
	readonly Color _accent;

	public ModeButton( PermissionMode mode, string text, string icon, Color accent, Widget parent ) : base( parent )
	{
		_mode = mode;
		_text = text;
		_icon = icon;
		_accent = accent;
		FixedHeight = 30;
		FixedWidth = 46 + text.Length * 7f;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var selected = McpSettings.Mode == _mode;

		Paint.SetBrush( selected ? _accent.WithAlpha( 0.22f ) : Color.White.WithAlpha( Paint.HasMouseOver ? 0.06f : 0.02f ) );
		Paint.DrawRect( LocalRect, 6 );

		if ( selected )
		{
			Paint.SetPen( _accent, 1.5f );
			Paint.ClearBrush();
			Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
			Paint.ClearPen();
		}

		var fg = selected ? _accent : Palette.TextDim;
		Paint.SetPen( fg );
		Paint.DrawIcon( new Rect( LocalRect.Left + 8, LocalRect.Top, 16, LocalRect.Height ), _icon, 13, TextFlag.Center );
		Paint.SetDefaultFont( 8, selected ? 700 : 400 );
		Paint.DrawText( new Rect( LocalRect.Left + 28, LocalRect.Top, LocalRect.Width - 32, LocalRect.Height ), _text, TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		McpSettings.Mode = _mode;
		Parent?.Update();
		Update();
	}
}

/// <summary>
/// Autostart toggle painted from the live setting.
/// </summary>
public class AutoStartButton : Widget
{
	public AutoStartButton( Widget parent ) : base( parent )
	{
		FixedHeight = 24;
		FixedWidth = 220;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var on = McpSettings.AutoStart;
		var accent = on ? Palette.Running : Palette.TextDim;

		// toggle track
		var track = new Rect( LocalRect.Left, LocalRect.Center.y - 8, 30, 16 );
		Paint.SetBrush( on ? accent.WithAlpha( 0.4f ) : Color.White.WithAlpha( 0.08f ) );
		Paint.DrawRect( track, 8 );

		// knob (DrawCircle takes the diameter)
		Paint.SetBrush( accent );
		Paint.DrawCircle( new Vector2( on ? track.Right - 8 : track.Left + 8, track.Center.y ), 12 );

		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( new Rect( track.Right + 8, LocalRect.Top, LocalRect.Width - 40, LocalRect.Height ),
			"Start server when the editor opens", TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		McpSettings.AutoStart = !McpSettings.AutoStart;
		Update();
	}
}
