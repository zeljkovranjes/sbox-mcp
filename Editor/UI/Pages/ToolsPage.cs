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

	int _builtSignature = -1;

	public ToolsPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;

		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Search tools..." } );
		_search.TextEdited += _ => Rebuild();

		// FlowRow wraps the chips to new lines on narrow docks instead of
		// letting them overlap
		var chipFlow = Layout.Add( new FlowRow( this ) );

		foreach ( var category in Enum.GetValues<ToolCategory>() )
		{
			var chip = new CategoryChip( category, chipFlow, clickable: true );
			chip.OnToggled = Rebuild;
			_chips.Add( chip );
			chipFlow.AddItem( chip );
		}

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Spacing = 2;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		Layout.Add( _scroll, 1 );

		Rebuild();
	}

	/// <summary>
	/// The dock restores before McpHost initializes, so the registry is empty
	/// at construction time - poll until tools appear.
	/// </summary>
	public void Tick()
	{
		var sig = Signature();
		if ( sig == _builtSignature )
			return;

		Rebuild();
	}

	static int Signature()
	{
		var tools = McpHost.Registry?.Tools;
		return tools is null ? 0 : tools.Count * 1000 + tools.Count( t => t.IsAvailable );
	}

	void Rebuild()
	{
		_builtSignature = Signature();

		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		var query = _search.Text;
		var enabled = _chips.Where( c => c.Toggled ).Select( c => c.Category ).ToHashSet();

		var tools = (McpHost.Registry?.Tools ?? (IReadOnlyList<RegisteredTool>)Array.Empty<RegisteredTool>())
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

		var unavailable = _tool.UnavailableReason;
		var disabled = unavailable is not null;
		var accent = Palette.For( _tool.Meta.Category );

		if ( disabled )
			accent = accent.WithAlpha( 0.35f );

		if ( Paint.HasMouseOver && !disabled )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.03f ) );
			Paint.DrawRect( LocalRect, 5 );
		}

		// category color tick
		Paint.SetBrush( accent );
		Paint.DrawRect( new Rect( LocalRect.Left + 2, LocalRect.Top + 8, 3, LocalRect.Height - 16 ), 1.5f );

		// name
		Paint.SetPen( disabled ? Palette.TextDim.WithAlpha( 0.6f ) : Palette.TextBright );
		Paint.SetFont( "Consolas", 8, 600 );
		var nameWidth = Paint.MeasureText( _tool.Meta.Name ).x;
		Paint.DrawText( new Rect( LocalRect.Left + 14, LocalRect.Top + 4, nameWidth + 4, 14 ), _tool.Meta.Name, TextFlag.LeftCenter );

		var badgeLeft = LocalRect.Left + 20 + nameWidth;

		// writes badge
		if ( _tool.Meta.Writes && !disabled )
		{
			var badge = new Rect( badgeLeft, LocalRect.Top + 5, 44, 13 );
			Paint.SetBrush( Palette.Error.WithAlpha( 0.18f ) );
			Paint.DrawRect( badge, 6 );
			Paint.SetPen( Palette.Error );
			Paint.SetDefaultFont( 6, 700 );
			Paint.DrawText( badge, "WRITES", TextFlag.Center );
		}

		// unavailable badge, e.g. "Not Installed"
		if ( disabled )
		{
			Paint.SetDefaultFont( 6, 700 );
			var badgeWidth = Paint.MeasureText( unavailable ).x + 12;
			var badge = new Rect( badgeLeft, LocalRect.Top + 5, badgeWidth, 13 );
			Paint.SetBrush( Palette.TextDim.WithAlpha( 0.15f ) );
			Paint.DrawRect( badge, 6 );
			Paint.SetPen( Palette.TextDim );
			Paint.DrawText( badge, unavailable, TextFlag.Center );
		}

		// description
		Paint.SetPen( disabled ? Palette.TextDim.WithAlpha( 0.5f ) : Palette.TextDim );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( new Rect( LocalRect.Left + 14, LocalRect.Top + 20, LocalRect.Width - 20, 14 ),
			_tool.Meta.Description, TextFlag.LeftCenter | TextFlag.SingleLine );
	}
}
