using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;
using SboxMcp.Registry;

namespace SboxMcp.UI;

/// <summary>
/// Searchable, category-filterable browser of every tool the server exposes.
/// Doubles as documentation.
/// </summary>
public class ToolsPage : Widget
{
	readonly LineEdit _search;
	readonly List<CategoryChip> _chips = new();
	readonly ScrollArea _scroll;

	public ToolsPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;

		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Search tools..." } );
		_search.TextEdited += _ => Rebuild();

		var chipRow = Layout.AddRow();
		chipRow.Spacing = 4;

		foreach ( var category in Enum.GetValues<ToolCategory>() )
		{
			var chip = new CategoryChip( category, this, clickable: true );
			chip.OnToggled = Rebuild;
			_chips.Add( chip );
			chipRow.Add( chip );
		}

		chipRow.AddStretchCell();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Spacing = 2;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		Layout.Add( _scroll, 1 );

		Rebuild();
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		var query = _search.Text;
		var enabled = _chips.Where( c => c.Toggled ).Select( c => c.Category ).ToHashSet();

		var tools = (McpHost.Server is null
				? Enumerable.Empty<RegisteredTool>()
				: ToolsOf( McpHost.Server ))
			.Where( t => enabled.Contains( t.Meta.Category ) )
			.Where( t => string.IsNullOrWhiteSpace( query )
				|| t.Meta.Name.Contains( query, StringComparison.OrdinalIgnoreCase )
				|| t.Meta.Description.Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		var count = canvas.Layout.Add( new Label( $"{tools.Count} tools", canvas ) );
		count.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 10px;" );

		foreach ( var tool in tools )
			canvas.Layout.Add( new ToolRow( tool, canvas ) );

		canvas.Layout.AddStretchCell();
	}

	static IEnumerable<RegisteredTool> ToolsOf( Server.McpServer server ) => McpHost.Registry?.Tools ?? Enumerable.Empty<RegisteredTool>();
}

/// <summary>
/// One tool entry: name (mono), write badge, wrapped description.
/// </summary>
public class ToolRow : Widget
{
	readonly RegisteredTool _tool;

	public ToolRow( RegisteredTool tool, Widget parent ) : base( parent )
	{
		_tool = tool;
		FixedHeight = 40;
		ToolTip = tool.Meta.Description;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var accent = Palette.For( _tool.Meta.Category );

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.03f ) );
			Paint.DrawRect( LocalRect, 5 );
		}

		// category color tick
		Paint.SetBrush( accent );
		Paint.DrawRect( new Rect( LocalRect.Left + 2, LocalRect.Top + 8, 3, LocalRect.Height - 16 ), 1.5f );

		// name
		Paint.SetPen( Palette.TextBright );
		Paint.SetFont( "Consolas", 8, 600 );
		var nameWidth = Paint.MeasureText( _tool.Meta.Name ).x;
		Paint.DrawText( new Rect( LocalRect.Left + 14, LocalRect.Top + 4, nameWidth + 4, 14 ), _tool.Meta.Name, TextFlag.LeftCenter );

		// writes badge
		if ( _tool.Meta.Writes )
		{
			var badge = new Rect( LocalRect.Left + 20 + nameWidth, LocalRect.Top + 5, 44, 13 );
			Paint.SetBrush( Palette.Error.WithAlpha( 0.18f ) );
			Paint.DrawRect( badge, 6 );
			Paint.SetPen( Palette.Error );
			Paint.SetDefaultFont( 6, 700 );
			Paint.DrawText( badge, "WRITES", TextFlag.Center );
		}

		// description
		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( new Rect( LocalRect.Left + 14, LocalRect.Top + 20, LocalRect.Width - 20, 14 ),
			_tool.Meta.Description, TextFlag.LeftCenter | TextFlag.SingleLine );
	}
}
