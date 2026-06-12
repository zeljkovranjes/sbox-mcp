using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Integration;

namespace SboxMcp.UI;

/// <summary>
/// Live feed of tool calls, with pending approval cards pinned on top.
/// </summary>
public class ActivityPage : Widget
{
	string _signature;
	ScrollArea _scroll;

	public ActivityPage( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		_scroll = new ScrollArea( this );
		_scroll.Canvas = new Widget( _scroll );
		_scroll.Canvas.Layout = Layout.Column();
		_scroll.Canvas.Layout.Margin = 12;
		_scroll.Canvas.Layout.Spacing = 6;
		_scroll.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroll.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroll, 1 );
		Rebuild();
	}

	public void Tick()
	{
		var sig = $"{ActivityLog.Version}|{PermissionGate.Version}";
		if ( sig == _signature )
			return;

		_signature = sig;
		Rebuild();
	}

	void Rebuild()
	{
		var canvas = _scroll.Canvas;
		canvas.Layout.Clear( true );

		// ---- pending approvals -------------------------------------------
		foreach ( var request in PermissionGate.Pending )
		{
			var card = canvas.Layout.Add( new Card( canvas ) { EdgeAccent = Palette.Warning } );

			var title = card.Layout.Add( new Label( $"Approve  {request.ToolName} ?", card ) );
			title.SetStyles( $"color: {Palette.Warning.Hex}; font-size: 12px; font-weight: 700;" );

			if ( !string.IsNullOrEmpty( request.ArgsSummary ) )
			{
				var args = card.Layout.Add( new Label( request.ArgsSummary, card ) );
				args.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 10px; font-family: Consolas;" );
				args.WordWrap = true;
			}

			var buttons = card.Layout.AddRow();
			buttons.Spacing = 6;
			buttons.Add( new ActionButton( "Approve", "check", card ) { Accent = Palette.Running, Clicked = request.Approve } );
			buttons.Add( new ActionButton( "Deny", "close", card ) { Accent = Palette.Error, Filled = false, Clicked = request.Deny } );
			buttons.AddStretchCell();
		}

		// ---- header row ---------------------------------------------------
		var header = canvas.Layout.AddRow();
		header.Spacing = 6;

		var count = header.Add( new Label( $"{ActivityLog.TotalCalls} total calls", canvas ) );
		count.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 11px;" );
		header.AddStretchCell();
		header.Add( new ActionButton( "Clear", "delete_sweep", canvas ) { Accent = Palette.TextDim, Filled = false, Clicked = ActivityLog.Clear } );

		// ---- feed ----------------------------------------------------------
		var records = ActivityLog.Records;

		if ( records.Count == 0 )
		{
			var empty = canvas.Layout.Add( new Label( "No tool calls yet. Connect a client and watch the AI work in here.", canvas ) );
			empty.SetStyles( $"color: {Palette.TextDim.Hex}; font-size: 11px;" );
		}

		foreach ( var record in records.Take( 100 ) )
			canvas.Layout.Add( new ActivityRow( record, canvas ) );

		canvas.Layout.AddStretchCell();
	}
}

/// <summary>
/// One painted feed row: category chip, tool name, args, duration, status.
/// </summary>
public class ActivityRow : Widget
{
	readonly ActivityRecord _record;

	public ActivityRow( ActivityRecord record, Widget parent ) : base( parent )
	{
		_record = record;
		FixedHeight = 30;
		ToolTip = record.Error ?? record.ArgsDigest;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var categoryColor = Palette.For( _record.Category );

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.03f ) );
			Paint.DrawRect( LocalRect, 5 );
		}

		// status icon
		Paint.SetPen( _record.Ok ? Palette.Running : Palette.Error );
		Paint.DrawIcon( new Rect( LocalRect.Left + 2, LocalRect.Top, 18, LocalRect.Height ), _record.Ok ? "check_circle" : "error", 14, TextFlag.Center );

		// category chip
		var chipText = _record.Category.ToString();
		Paint.SetDefaultFont( 7, 600 );
		var chipWidth = Paint.MeasureText( chipText ).x + 12;
		var chip = new Rect( LocalRect.Left + 24, LocalRect.Center.y - 8, chipWidth, 16 );
		Paint.ClearPen();
		Paint.SetBrush( categoryColor.WithAlpha( 0.2f ) );
		Paint.DrawRect( chip, 8 );
		Paint.SetPen( categoryColor );
		Paint.DrawText( chip, chipText, TextFlag.Center );

		// tool name
		var x = chip.Right + 8;
		Paint.SetPen( Palette.TextBright );
		Paint.SetDefaultFont( 8, 600 );
		var nameWidth = Paint.MeasureText( _record.ToolName ).x;
		Paint.DrawText( new Rect( x, LocalRect.Top, nameWidth + 4, LocalRect.Height ), _record.ToolName, TextFlag.LeftCenter );

		// args digest
		x += nameWidth + 12;
		var right = LocalRect.Right - 86;
		if ( right > x )
		{
			Paint.SetPen( Palette.TextDim );
			Paint.SetFont( "Consolas", 7 );
			var digest = _record.Error ?? _record.ArgsDigest;
			Paint.DrawText( new Rect( x, LocalRect.Top, right - x, LocalRect.Height ), digest ?? "", TextFlag.LeftCenter | TextFlag.SingleLine );
		}

		// duration + time
		Paint.SetPen( Palette.TextDim );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( new Rect( LocalRect.Right - 84, LocalRect.Top, 80, LocalRect.Height ),
			$"{_record.DurationMs} ms  {_record.Time:HH:mm:ss}", TextFlag.RightCenter );
	}
}
