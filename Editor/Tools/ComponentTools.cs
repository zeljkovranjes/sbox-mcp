using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class ComponentTools
{
	[McpTool( "component_list_types", "Searches available component types (ModelRenderer, Rigidbody, custom components...).", ToolCategory.Component )]
	public static object ListTypes(
		[Desc( "Name filter (case-insensitive substring); omit for all" )] string search = null,
		int max = 50 )
	{
		var types = EditorTypeLibrary.GetTypes<Component>()
			.Where( t => !t.IsAbstract )
			.Where( t => search is null
				|| t.Name.Contains( search, StringComparison.OrdinalIgnoreCase )
				|| (t.FullName?.Contains( search, StringComparison.OrdinalIgnoreCase ) ?? false) )
			.OrderBy( t => t.Name )
			.Take( max )
			.Select( t => new { name = t.Name, fullName = t.FullName, title = t.Title, group = t.Group } )
			.ToArray();

		return new { count = types.Length, types };
	}

	[McpTool( "component_add", "Adds a component to a GameObject.", ToolCategory.Component, Writes = true )]
	public static object Add(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name, e.g. 'ModelRenderer'" )] string type )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var typeDesc = FindComponentType( type );

		using var undo = session.UndoScope( $"MCP: add {typeDesc.Name}" ).WithComponentCreations().Push();

		var component = go.Components.Create( typeDesc );
		return new { added = component.GetType().Name, to = go.Name, properties = component.Serialize() };
	}

	[McpTool( "component_remove", "Removes a component from a GameObject.", ToolCategory.Component, Writes = true )]
	public static object Remove(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );
		var name = component.GetType().Name;

		using var undo = session.UndoScope( $"MCP: remove {name}" )
			.WithComponentDestructions( new[] { component } ).Push();

		component.Destroy();
		return new { removed = name, from = go.Name };
	}

	[McpTool( "component_get_properties", "Gets all serialized properties of a component as JSON.", ToolCategory.Component )]
	public static object GetProperties(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type )
	{
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		return new
		{
			type = component.GetType().Name,
			gameObject = go.Name,
			enabled = component.Enabled,
			properties = component.Serialize()
		};
	}

	[McpTool( "component_set_property", "Sets one property on a component. Value is raw JSON (number, string, bool, array, or object). Resource properties accept asset paths as strings.", ToolCategory.Component, Writes = true )]
	public static object SetProperty(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property name as shown by component_get_properties" )] string property,
		[Desc( "New value as JSON" )] JsonElement value )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		if ( component.Serialize() is not JsonObject node )
			throw new InvalidOperationException( $"Component '{type}' did not serialize to an object" );

		var key = node.Select( kv => kv.Key )
			.FirstOrDefault( k => string.Equals( k, property, StringComparison.OrdinalIgnoreCase ) ) ?? property;

		using var undo = session.UndoScope( $"MCP: set {key}" )
			.WithComponentChanges( new[] { component } ).Push();

		node[key] = JsonNode.Parse( value.GetRawText() );
		component.Deserialize( node );

		return new { set = key, on = component.GetType().Name, now = component.Serialize() };
	}

	[McpTool( "component_set_enabled", "Enables or disables a component.", ToolCategory.Component, Writes = true )]
	public static object SetEnabled(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		bool enabled )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		using var undo = session.UndoScope( $"MCP: component enabled {enabled}" )
			.WithComponentChanges( new[] { component } ).Push();

		component.Enabled = enabled;
		return new { type = component.GetType().Name, enabled = component.Enabled };
	}
}
