using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Server status, start/stop, and copy-paste configs for every client.
/// </summary>
public class OverviewPage : Widget
{
	string _signature;
	ScrollArea _scroll;

	public OverviewPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Margin = 12;
		_scroll.Canvas.Layout.Spacing = 10;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroll, 1 );
		Rebuild();
	}

	public void Tick()
	{
		var server = McpHost.Server;
		var sig = $"{server?.IsRunning}|{server?.Port}|{server?.Sessions.Count}|{McpHost.LastError}|{string.Join( ",", server?.Sessions.Select( s => $"{s.ClientName}:{s.CallCount}" ) ?? Enumerable.Empty<string>() )}";

		if ( sig == _signature )
			return;

		_signature = sig;
		Rebuild();
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		var server = McpHost.Server;
		var url = server?.Url ?? $"http://127.0.0.1:{McpSettings.Port}/sbox-mcp";
		var running = server?.IsRunning ?? false;

		// ---- server card -------------------------------------------------
		var card = canvas.Layout.Add( new Card( canvas ) );
		AddCardTitle( card, "Server", "dns" );

		card.Layout.Add( new CodeSnippet( url, running ? Palette.Running : Palette.Stopped, card ) );

		var controls = card.Layout.AddRow();
		controls.Spacing = 6;

		if ( running )
		{
			controls.Add( new ActionButton( "Stop", "stop", card ) { Accent = Palette.Error, Clicked = McpHost.Stop } );
			controls.Add( new ActionButton( "Restart", "refresh", card ) { Accent = Palette.GradientA, Filled = false, Clicked = McpHost.Restart } );
		}
		else
		{
			controls.Add( new ActionButton( "Start server", "play_arrow", card ) { Accent = Palette.Running, Clicked = McpHost.Start } );
		}

		controls.AddStretchCell();

		if ( McpHost.LastError is not null )
		{
			var error = card.Layout.Add( new Label( McpHost.LastError, card ) );
			error.SetStyles( $"color: {Palette.Error.Hex}; font-size: 11px;" );

			card.Layout.Add( new ActionButton( $"Try port {McpSettings.Port + 1}", "swap_horiz", card )
			{
				Accent = Palette.Warning,
				Filled = false,
				Clicked = () =>
				{
					McpSettings.Port += 1;
					McpHost.Restart();
				}
			} );
		}

		// ---- sessions ----------------------------------------------------
		var sessions = server?.Sessions;
		if ( running )
		{
			var label = sessions is { Count: > 0 }
				? $"{sessions.Count} connected client{(sessions.Count == 1 ? "" : "s")}"
				: "No clients connected yet - copy a config below into your AI tool";

			var l = card.Layout.Add( new Label( label, card ) );
			l.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 11px;" );

			if ( sessions is not null )
			{
				foreach ( var s in sessions )
				{
					var row = card.Layout.Add( new Label( $"●  {s.ClientName}   ·   {s.CallCount} calls   ·   last seen {s.LastSeen:HH:mm:ss}", card ) );
					row.SetStyles( $"color: {Palette.Running.Hex}; font-size: 11px;" );
				}
			}
		}

		// ---- client config cards ------------------------------------------
		AddConfigCard( canvas, "Claude Code", Palette.ClaudeAccent,
			$"# terminal\nclaude mcp add --transport http sbox {url}\n\n# or .mcp.json\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{ \"type\": \"http\", \"url\": \"{url}\" }}\n  }}\n}}" );

		AddConfigCard( canvas, "Claude Desktop", Palette.ClaudeAccent,
			$"// claude_desktop_config.json (needs Node.js for npx)\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{\n      \"command\": \"npx\",\n      \"args\": [\"-y\", \"mcp-remote\", \"{url}\"]\n    }}\n  }}\n}}" );

		AddConfigCard( canvas, "Cursor", Palette.CursorAccent,
			$"// .cursor/mcp.json\n{{\n  \"mcpServers\": {{\n    \"sbox\": {{ \"url\": \"{url}\" }}\n  }}\n}}" );

		AddConfigCard( canvas, "VS Code", Palette.VsCodeAccent,
			$"// .vscode/mcp.json\n{{\n  \"servers\": {{\n    \"sbox\": {{ \"type\": \"http\", \"url\": \"{url}\" }}\n  }}\n}}" );

		canvas.Layout.AddStretchCell();
	}

	void AddConfigCard( Widget canvas, string client, Color accent, string snippet )
	{
		var card = canvas.Layout.Add( new Card( canvas ) { EdgeAccent = accent } );
		AddCardTitle( card, client, "integration_instructions" );
		card.Layout.Add( new CodeSnippet( snippet, accent, card ) );
	}

	internal static void AddCardTitle( Card card, string text, string icon )
	{
		var title = card.Layout.Add( new Label( text, card ) );
		title.SetStyles( $"color: {Palette.TextBright.Hex}; font-size: 12px; font-weight: 600;" );
	}
}
