using System;
using System.Linq;
using System.Reflection;
using static Sandbox.Internal.GlobalToolsNamespace;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Live API discovery over the whole s&box + game type surface. This is what
/// lets the AI do "anything": find the type it needs (PlayerController,
/// Rigidbody, a custom component...) and read its real members before using
/// component_add / component_set_property / code_write_file.
/// </summary>
public static class ApiTools
{
	[McpTool( "api_search", "Searches the entire s&box + project API by type name. Use this to discover components, systems and helpers to work with (e.g. 'player', 'rigidbody', 'sound').", ToolCategory.Editor )]
	public static object Search(
		[Desc( "Name substring, case-insensitive" )] string query,
		[Desc( "Only components you can add to a GameObject" )] bool componentsOnly = false,
		int max = 40 )
	{
		if ( string.IsNullOrWhiteSpace( query ) )
			throw new ArgumentException( "query must not be empty" );

		var types = EditorTypeLibrary.GetTypes()
			.Where( t => t.TargetType is not null )
			.Where( t => !componentsOnly || typeof( Sandbox.Component ).IsAssignableFrom( t.TargetType ) )
			.Where( t => (t.Name?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false)
				|| (t.TargetType.FullName?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false) )
			.OrderByDescending( t => string.Equals( t.Name, query, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( t => t.Name )
			.Take( max )
			.Select( t => new
			{
				name = t.Name,
				fullName = t.TargetType.FullName,
				isComponent = typeof( Sandbox.Component ).IsAssignableFrom( t.TargetType ),
				baseType = t.TargetType.BaseType?.Name,
				title = t.Title,
				group = t.Group
			} )
			.ToArray();

		return new { count = types.Length, note = "Use api_get_type for full members, or component_add for the component ones.", types };
	}

	[McpTool( "api_get_type", "Gets a type's public properties and methods with their real signatures - read this before setting properties or calling methods on a type.", ToolCategory.Editor )]
	public static object GetType(
		[Desc( "Type name or full name, e.g. 'PlayerController' or 'Sandbox.Rigidbody'" )] string typeName )
	{
		var all = EditorTypeLibrary.GetTypes().Where( t => t.TargetType is not null ).ToList();

		var desc = all.FirstOrDefault( t => string.Equals( t.TargetType.FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? all.FirstOrDefault( t => string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? throw new InvalidOperationException( $"No type '{typeName}' - use api_search to find it" );

		var type = desc.TargetType;
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		var properties = type.GetProperties( flags )
			.Where( p => p.GetIndexParameters().Length == 0 )
			.Select( p => new
			{
				name = p.Name,
				type = FriendlyType( p.PropertyType ),
				access = (p.CanRead ? "get" : "") + (p.CanWrite ? " set" : "")
			} )
			.OrderBy( p => p.name )
			.ToArray();

		var methods = type.GetMethods( flags )
			.Where( m => !m.IsSpecialName )
			.Select( m => new
			{
				name = m.Name,
				returns = FriendlyType( m.ReturnType ),
				parameters = m.GetParameters().Select( p => $"{FriendlyType( p.ParameterType )} {p.Name}" ).ToArray(),
				isStatic = m.IsStatic
			} )
			.OrderBy( m => m.name )
			.Take( 120 )
			.ToArray();

		return new
		{
			name = desc.Name,
			fullName = type.FullName,
			baseType = type.BaseType?.Name,
			isComponent = typeof( Sandbox.Component ).IsAssignableFrom( type ),
			isAbstract = type.IsAbstract,
			description = desc.Description,
			properties,
			methods
		};
	}

	static string FriendlyType( Type t )
	{
		if ( t == typeof( void ) ) return "void";
		var underlying = Nullable.GetUnderlyingType( t );
		if ( underlying is not null ) return FriendlyType( underlying ) + "?";

		if ( t.IsGenericType )
		{
			var name = t.Name.Split( '`' )[0];
			var args = string.Join( ", ", t.GetGenericArguments().Select( FriendlyType ) );
			return $"{name}<{args}>";
		}

		return t.Name;
	}
}
