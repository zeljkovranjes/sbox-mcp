using System;
using System.Collections.Generic;
using System.Linq;

namespace SboxMcp.Registry;

/// <summary>Small deterministic lookup kept separate from registry mechanics.</summary>
public static class ToolSearch
{
	public static IReadOnlyList<RegisteredTool> Search(
		this ToolRegistry registry, string query, int limit, string category = null )
	{
		var tools = registry.Tools.Where( tool => tool.IsAvailable
			&& (category is null || string.Equals( tool.Meta.Category.ToString(), category, StringComparison.OrdinalIgnoreCase )) );

		var exact = tools.FirstOrDefault( tool => string.Equals( tool.Meta.Name, query, StringComparison.OrdinalIgnoreCase ) );
		if ( exact is not null )
			return new[] { exact };

		var words = query.Split( new[] { ' ', '_', '-', '.', '/' }, StringSplitOptions.RemoveEmptyEntries )
			.Where( word => word.Length > 2 )
			.ToArray();

		return tools
			.Select( tool => new
			{
				Tool = tool,
				Score = words.Sum( word =>
					tool.Meta.Name.Contains( word, StringComparison.OrdinalIgnoreCase ) ? 3 :
					tool.Meta.Description.Contains( word, StringComparison.OrdinalIgnoreCase ) ? 1 : 0 )
			} )
			.Where( match => match.Score > 0 )
			.OrderByDescending( match => match.Score )
			.ThenBy( match => match.Tool.Meta.Name, StringComparer.Ordinal )
			.Take( limit )
			.Select( match => match.Tool )
			.ToArray();
	}
}
