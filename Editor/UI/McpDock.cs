using System;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// The MCP dock: gradient header, colorful tab bar, and the four pages.
/// Open it from the editor's View menu.
/// </summary>
[Dock( "Editor", "MCP", "hub" )]
public class McpDock : Widget
{
	readonly GradientHeader _header;
	readonly TabButton[] _tabs;
	readonly Widget[] _pages;
	readonly OverviewPage _overview;
	readonly ActivityPage _activity;
	readonly ToolsPage _tools;

	int _active;

	// Widget.MinimumWidth is a no-op for docks; Qt asks this instead
	protected override Vector2 MinimumSizeHint() => new( 360, 220 );

	public McpDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		_header = Layout.Add( new GradientHeader( this ) );

		var tabRow = Layout.AddRow();
		tabRow.Margin = new Sandbox.UI.Margin( 8, 4, 8, 0 );
		tabRow.Spacing = 2;

		_tabs = new[]
		{
			new TabButton( "Overview", "dashboard", Palette.GradientA, this ),
			new TabButton( "Activity", "bolt", Palette.GradientC, this ),
			new TabButton( "Tools", "construction", Palette.For( Registry.ToolCategory.ShaderGraph ), this ),
			new TabButton( "Settings", "tune", Palette.Warning, this )
		};

		for ( var i = 0; i < _tabs.Length; i++ )
		{
			var index = i;
			_tabs[i].Clicked = () => SetActive( index );
			tabRow.Add( _tabs[i] );
		}

		tabRow.AddStretchCell();

		var content = Layout.Add( new Widget( this ), 1 );
		content.Layout = Layout.Column();

		_overview = new OverviewPage( content );
		_activity = new ActivityPage( content );
		_tools = new ToolsPage( content );
		var settings = new SettingsPage( content );

		_pages = new Widget[] { _overview, _activity, _tools, settings };

		foreach ( var page in _pages )
			content.Layout.Add( page, 1 );

		SetActive( 0 );
		// no EditorEvent.Register(this) - QObject already registers every
		// widget; doing it again would run Tick twice per frame
	}

	void SetActive( int index )
	{
		_active = index;

		for ( var i = 0; i < _pages.Length; i++ )
		{
			_pages[i].Visible = i == index;
			_tabs[i].Active = i == index;
			_tabs[i].Update();
		}
	}

	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !IsValid )
			return;

		var server = McpHost.Server;
		var running = server?.IsRunning ?? false;
		var sessions = server?.Sessions.Count ?? 0;

		_header.StatusColor = running ? Palette.Running : (McpHost.LastError is null ? Palette.Stopped : Palette.Error);
		_header.StatusText = !running
			? (McpHost.LastError is null ? "stopped" : "error")
			: sessions > 0 ? $"running · {sessions} client{(sessions == 1 ? "" : "s")}" : "running";
		_header.Pulse = running ? (MathF.Sin( RealTime.Now * 3f ) + 1f) * 0.5f : 0f;
		_header.Update();

		// badge pending approvals on the Activity tab
		var pending = PermissionGate.Pending.Count;
		if ( _tabs[1].Badge != pending )
		{
			_tabs[1].Badge = pending;
			_tabs[1].Update();
		}

		_overview.Tick();
		_activity.Tick();
		_tools.Tick();
	}
}
