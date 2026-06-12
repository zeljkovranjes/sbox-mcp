using System;
using Editor;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.UI;

/// <summary>
/// Gradient banner with the product mark and a live status pill.
/// </summary>
public class GradientHeader : Widget
{
	public string Title = "s&box MCP";
	public string Subtitle = "Model Context Protocol server";

	public string StatusText = "stopped";
	public Color StatusColor = Palette.Stopped;
	public float Pulse; // 0..1, driven by the dock's frame tick

	public GradientHeader( Widget parent ) : base( parent )
	{
		FixedHeight = 64;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.ClearPen();

		// three-stop gradient as two joined linear fills
		var left = LocalRect;
		left.Width *= 0.5f;
		var right = LocalRect;
		right.Left = left.Right;

		Paint.SetBrushLinear( left.TopLeft, left.TopRight, Palette.GradientA, Palette.GradientB );
		Paint.DrawRect( left );
		Paint.SetBrushLinear( right.TopLeft, right.TopRight, Palette.GradientB, Palette.GradientC );
		Paint.DrawRect( right );

		// subtle dark veil at the bottom for depth
		Paint.SetBrushLinear( LocalRect.TopLeft, LocalRect.BottomLeft,
			Color.Transparent, Color.Black.WithAlpha( 0.25f ) );
		Paint.DrawRect( LocalRect );

		// title + subtitle
		var textRect = LocalRect.Shrink( 16, 10 );
		Paint.SetPen( Color.White );
		Paint.SetDefaultFont( 13, 700 );
		Paint.DrawText( textRect, Title, TextFlag.LeftTop );

		Paint.SetPen( Color.White.WithAlpha( 0.75f ) );
		Paint.SetDefaultFont( 8 );
		Paint.DrawText( textRect, Subtitle, TextFlag.LeftBottom );

		// status pill, right-aligned
		Paint.SetDefaultFont( 8, 600 );
		var label = StatusText;
		var textSize = Paint.MeasureText( label );
		var pillWidth = textSize.x + 34;
		var pill = new Rect( LocalRect.Right - pillWidth - 16, LocalRect.Center.y - 12, pillWidth, 24 );

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.35f ) );
		Paint.DrawRect( pill, 12 );

		// glowing dot (DrawCircle's second arg is the diameter)
		var dot = new Vector2( pill.Left + 13, pill.Center.y );
		var glow = 4f + Pulse * 3f;
		Paint.SetBrush( StatusColor.WithAlpha( 0.25f ) );
		Paint.DrawCircle( dot, glow * 2 );
		Paint.SetBrush( StatusColor );
		Paint.DrawCircle( dot, 8f );

		Paint.SetPen( Color.White );
		Paint.DrawText( new Rect( pill.Left + 24, pill.Top, pill.Width - 28, pill.Height ), label, TextFlag.LeftCenter );
	}
}

/// <summary>
/// Small rounded chip in a category's color.
/// </summary>
public class CategoryChip : Widget
{
	public ToolCategory Category;
	public bool Toggled = true;
	public Action OnToggled;
	public bool Clickable;

	public CategoryChip( ToolCategory category, Widget parent, bool clickable = false ) : base( parent )
	{
		Category = category;
		Clickable = clickable;
		FixedHeight = 20;
		FixedWidth = 12 + Category.ToString().Length * 6.5f;
		Cursor = clickable ? CursorShape.Finger : CursorShape.Arrow;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		var color = Palette.For( Category );
		var active = !Clickable || Toggled;

		Paint.ClearPen();
		Paint.SetBrush( color.WithAlpha( active ? (Paint.HasMouseOver ? 0.35f : 0.22f) : 0.07f ) );
		Paint.DrawRect( LocalRect, 10 );

		Paint.SetPen( active ? color : Palette.TextDim );
		Paint.SetDefaultFont( 7, 600 );
		Paint.DrawText( LocalRect, Category.ToString(), TextFlag.Center );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( !Clickable )
			return;

		Toggled = !Toggled;
		OnToggled?.Invoke();
		Update();
	}
}

/// <summary>
/// Flat rounded button with an accent color, icon and hover lift.
/// </summary>
public class ActionButton : Widget
{
	public string Text;
	public string Icon;
	public Color Accent = Palette.GradientA;
	public Action Clicked;
	public bool Filled = true;

	public ActionButton( string text, string icon, Widget parent ) : base( parent )
	{
		Text = text;
		Icon = icon;
		FixedHeight = 28;
		FixedWidth = 36 + text.Length * 7.5f + (string.IsNullOrEmpty( icon ) ? 0 : 14);
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var bg = Filled
			? Accent.WithAlpha( Paint.HasMouseOver ? 0.95f : 0.8f )
			: Accent.WithAlpha( Paint.HasMouseOver ? 0.25f : 0.12f );

		Paint.SetBrush( bg );
		Paint.DrawRect( LocalRect, 6 );

		if ( Paint.HasMouseOver )
		{
			Paint.SetPen( Accent.WithAlpha( 0.9f ), 1 );
			Paint.ClearBrush();
			Paint.DrawRect( LocalRect.Shrink( 0.5f ), 6 );
			Paint.ClearPen();
		}

		var fg = Filled ? Color.White : Accent;
		var textRect = LocalRect;

		if ( !string.IsNullOrEmpty( Icon ) )
		{
			Paint.SetPen( fg );
			Paint.DrawIcon( new Rect( LocalRect.Left + 8, LocalRect.Top, 16, LocalRect.Height ), Icon, 14, TextFlag.Center );
			textRect.Left += 22;
		}
		else
		{
			textRect.Left += 6;
		}

		Paint.SetPen( fg );
		Paint.SetDefaultFont( 8, 600 );
		Paint.DrawText( textRect.Shrink( 4, 0 ), Text, TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		Clicked?.Invoke();
	}
}

/// <summary>
/// Dark monospace code block with a one-click copy button that flashes a
/// confirmation.
/// </summary>
public class CodeSnippet : Widget
{
	public string Code;
	public Color Accent;

	RealTimeSince _copiedFlash = 999;

	public CodeSnippet( string code, Color accent, Widget parent ) : base( parent )
	{
		Code = code;
		Accent = accent;

		var lines = code.Split( '\n' ).Length;
		FixedHeight = 18 + lines * 14 + 10;
		Cursor = CursorShape.Finger;
		ToolTip = "Click to copy";
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Palette.SnippetBackground );
		Paint.DrawRect( LocalRect, 6 );

		// accent edge
		Paint.SetBrush( Accent );
		Paint.DrawRect( new Rect( LocalRect.Left, LocalRect.Top + 6, 3, LocalRect.Height - 12 ), 1.5f );

		// code text
		Paint.SetPen( Paint.HasMouseOver ? Palette.TextBright : (Color)"#C9D1D9" );
		Paint.SetFont( "Consolas", 8 );

		var y = LocalRect.Top + 9;
		foreach ( var line in Code.Split( '\n' ) )
		{
			Paint.DrawText( new Rect( LocalRect.Left + 14, y, LocalRect.Width - 50, 14 ), line, TextFlag.LeftCenter | TextFlag.SingleLine );
			y += 14;
		}

		// copy affordance; keep repainting while the flash is visible so it
		// actually expires (nothing else schedules a repaint)
		var justCopied = _copiedFlash < 1.2f;
		if ( justCopied )
			Update();
		Paint.SetPen( justCopied ? Palette.Running : (Paint.HasMouseOver ? Palette.TextBright : Palette.TextDim) );

		if ( justCopied )
		{
			Paint.SetDefaultFont( 7, 600 );
			Paint.DrawText( new Rect( LocalRect.Right - 70, LocalRect.Top + 6, 60, 16 ), "Copied ✓", TextFlag.RightCenter );
		}
		else
		{
			Paint.DrawIcon( new Rect( LocalRect.Right - 28, LocalRect.Top + 6, 18, 18 ), "content_copy", 13, TextFlag.Center );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		EditorUtility.Clipboard.Copy( Code );
		_copiedFlash = 0;
		Update();
	}
}

/// <summary>
/// Rounded card container with a painted background.
/// </summary>
public class Card : Widget
{
	public Color? EdgeAccent;

	public Card( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 6;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		Paint.SetPen( Palette.CardBorder, 1 );
		Paint.SetBrush( Palette.CardBackground );
		Paint.DrawRect( LocalRect.Shrink( 0.5f ), 8 );

		if ( EdgeAccent is not null )
		{
			Paint.ClearPen();
			Paint.SetBrush( EdgeAccent.Value );
			Paint.DrawRect( new Rect( LocalRect.Left, LocalRect.Top + 8, 3, LocalRect.Height - 16 ), 1.5f );
		}
	}
}

/// <summary>
/// One tab in the dock's tab bar, with a colored underline when active.
/// </summary>
public class TabButton : Widget
{
	public string Text;
	public string Icon;
	public Color Accent;
	public bool Active;
	public Action Clicked;
	public int Badge;

	public TabButton( string text, string icon, Color accent, Widget parent ) : base( parent )
	{
		Text = text;
		Icon = icon;
		Accent = accent;
		FixedHeight = 32;
		FixedWidth = 46 + text.Length * 7f;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		if ( Paint.HasMouseOver && !Active )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.04f ) );
			Paint.DrawRect( LocalRect, 4 );
		}

		var fg = Active ? Palette.TextBright : Palette.TextDim;

		Paint.SetPen( Active ? Accent : fg );
		Paint.DrawIcon( new Rect( LocalRect.Left + 8, LocalRect.Top, 16, LocalRect.Height ), Icon, 14, TextFlag.Center );

		Paint.SetPen( fg );
		Paint.SetDefaultFont( 8, Active ? 700 : 400 );
		Paint.DrawText( new Rect( LocalRect.Left + 28, LocalRect.Top, LocalRect.Width - 30, LocalRect.Height ), Text, TextFlag.LeftCenter );

		if ( Badge > 0 )
		{
			var b = new Rect( LocalRect.Right - 16, LocalRect.Top + 6, 14, 14 );
			Paint.ClearPen();
			Paint.SetBrush( Palette.Warning );
			Paint.DrawCircle( b.Center, 14 );
			Paint.SetPen( Color.Black );
			Paint.SetDefaultFont( 7, 700 );
			Paint.DrawText( b, Badge > 9 ? "9" : Badge.ToString(), TextFlag.Center );
		}

		if ( Active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Accent );
			Paint.DrawRect( new Rect( LocalRect.Left + 6, LocalRect.Bottom - 3, LocalRect.Width - 12, 3 ), 1.5f );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		Clicked?.Invoke();
	}
}
