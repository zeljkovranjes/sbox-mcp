using System.Collections.Generic;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.UI;

/// <summary>
/// The visual identity: a category color for every tool family, and the
/// signature indigo -> violet -> pink gradient.
/// </summary>
public static class Palette
{
	public static readonly Color GradientA = (Color)"#6366F1"; // indigo
	public static readonly Color GradientB = (Color)"#8B5CF6"; // violet
	public static readonly Color GradientC = (Color)"#EC4899"; // pink

	public static readonly Color Running = (Color)"#34D399";
	public static readonly Color Stopped = (Color)"#9CA3AF";
	public static readonly Color Error = (Color)"#F87171";
	public static readonly Color Warning = (Color)"#FBBF24";

	public static readonly Color CardBackground = (Color)"#1B1D23";
	public static readonly Color CardBorder = (Color)"#2A2D36";
	public static readonly Color SnippetBackground = (Color)"#0D1117";
	public static readonly Color TextBright = (Color)"#F3F4F6";
	public static readonly Color TextDim = (Color)"#9CA3AF";

	static readonly Dictionary<ToolCategory, Color> Categories = new()
	{
		[ToolCategory.Scene] = (Color)"#4F8DFD",
		[ToolCategory.GameObject] = (Color)"#6AA5FF",
		[ToolCategory.Component] = (Color)"#8FB8FF",
		[ToolCategory.Prefab] = (Color)"#3DCFB6",
		[ToolCategory.Asset] = (Color)"#FFA94D",
		[ToolCategory.ModelDoc] = (Color)"#A78BFA",
		[ToolCategory.AnimGraph] = (Color)"#F472B6",
		[ToolCategory.ShaderGraph] = (Color)"#2DD4BF",
		[ToolCategory.ActionGraph] = (Color)"#A3E635",
		[ToolCategory.Code] = (Color)"#4ADE80",
		[ToolCategory.Editor] = (Color)"#FACC15"
	};

	public static Color For( ToolCategory category ) =>
		Categories.TryGetValue( category, out var c ) ? c : TextDim;

	// per-client accents for the config cards
	public static readonly Color ClaudeAccent = (Color)"#D97757";
	public static readonly Color CursorAccent = (Color)"#7C8AFF";
	public static readonly Color VsCodeAccent = (Color)"#3EA7FF";
}
