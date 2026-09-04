using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SboxMcp.Registry;

namespace SboxMcp.Server;

/// <summary>Keeps the full tool catalog server-side and exposes bounded lookup plus invocation.</summary>
public sealed class ToolGateway
{
	public const string SearchToolName = "tool_search";
	public const string CallToolName = "tool_call";
	const int MaxResults = 5;

	static readonly McpToolDescriptor[] GatewayDescriptors =
	{
		new( SearchToolName,
			"Finds enabled s&box tools by exact name or intent. Returns at most 5 authoritative schemas. Use a precise query and limit=1 when possible, then use tool_call.",
			Schema( new
			{
				query = new { type = "string", description = "Exact tool name or specific intent" },
				limit = new { type = "integer", minimum = 1, maximum = MaxResults, @default = 3 },
				category = new { type = "string", description = "Optional category, such as Scene, Component, Asset, Code, or Editor" }
			}, "query" ) ),
		new( CallToolName,
			"Invokes a server-side tool found with tool_search. Its normal availability and permission checks still apply.",
			Schema( new
			{
				name = new { type = "string", description = "Exact name returned by tool_search" },
				arguments = new { type = "object", description = "Arguments matching the returned inputSchema" }
			}, "name" ) )
	};

	readonly ToolRegistry _registry;

	public IReadOnlyList<McpToolDescriptor> Descriptors => GatewayDescriptors;

	public ToolGateway( ToolRegistry registry ) => _registry = registry;

	public string Search( JsonElement? args )
	{
		var input = RequireObject( args );
		var query = RequireString( input, "query" ).Trim();
		if ( query.Length == 0 )
			throw new ToolArgumentException( "Argument 'query' cannot be empty" );
		if ( query.Length > 200 )
			throw new ToolArgumentException( "Argument 'query' must be at most 200 characters" );

		var limit = 3;
		if ( input.TryGetProperty( "limit", out var limitValue )
			&& (limitValue.ValueKind != JsonValueKind.Number
				|| !limitValue.TryGetInt32( out limit ) || limit is < 1 or > MaxResults) )
			throw new ToolArgumentException( $"Argument 'limit' must be an integer from 1 to {MaxResults}" );

		var category = input.TryGetProperty( "category", out var categoryValue )
			? categoryValue.GetString() : null;
		if ( category is not null && !Enum.TryParse<ToolCategory>( category, true, out _ ) )
			throw new ToolArgumentException( $"Unknown category '{category}'" );

		var matches = _registry.Search( query, limit, category ).Select( tool => new
		{
			name = tool.Meta.Name,
			description = tool.Descriptor.Description,
			category = tool.Meta.Category.ToString(),
			writes = tool.Meta.Writes,
			inputSchema = tool.Descriptor.InputSchema
		} );

		return JsonSerializer.Serialize( new { query, matches } );
	}

	public (RegisteredTool Tool, JsonElement? Arguments) ResolveCall( JsonElement? args )
	{
		var input = RequireObject( args );
		var name = RequireString( input, "name" );
		var tool = _registry.Find( name )
			?? throw new ToolArgumentException( $"Unknown tool '{name}'. Use {SearchToolName} first." );

		if ( tool.UnavailableReason is string reason )
			throw new ToolArgumentException( $"'{name}' is unavailable: {reason}" );

		JsonElement? toolArgs = null;
		if ( input.TryGetProperty( "arguments", out var value ) )
		{
			if ( value.ValueKind != JsonValueKind.Object )
				throw new ToolArgumentException( "Argument 'arguments' must be an object" );
			toolArgs = value;
		}

		return (tool, toolArgs);
	}

	static JsonElement RequireObject( JsonElement? value ) =>
		value is { ValueKind: JsonValueKind.Object } element
			? element : throw new ToolArgumentException( "Arguments must be an object" );

	static string RequireString( JsonElement value, string name ) =>
		value.TryGetProperty( name, out var property ) && property.ValueKind == JsonValueKind.String
			? property.GetString() : throw new ToolArgumentException( $"Missing required string argument '{name}'" );

	static JsonElement Schema( object properties, params string[] required ) =>
		JsonSerializer.SerializeToElement( new { type = "object", properties, required, additionalProperties = false } );
}
