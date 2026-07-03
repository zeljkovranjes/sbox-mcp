using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
		// properties INCLUDE inherited ones - component_set_property accepts
		// inherited members like Enabled/WorldPosition, so listing only declared
		// members would mislead. Methods stay declared-only to avoid Object noise.
		const BindingFlags propFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
		const BindingFlags flags = propFlags | BindingFlags.DeclaredOnly;

		var properties = type.GetProperties( propFlags )
			.Where( p => p.GetIndexParameters().Length == 0 )
			.GroupBy( p => p.Name ).Select( g => g.First() ) // dedupe overridden/shadowed
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

	/// <summary>Where the auto-maintained reference and its version marker live.</summary>
	const string ReferenceFile = ".sbox-mcp/api_surface.txt";
	const string VersionFile = ".sbox-mcp/api_version.txt";

	[McpTool( "api_version", "Reports the running editor's build version. The API reference auto-regenerates when this changes, so information is always current.", ToolCategory.Editor )]
	public static object Version()
	{
		var (key, display) = EngineBuild();
		var cached = ReadCachedVersion();

		return new
		{
			build = display,
			versionKey = key,
			referenceUpToDate = cached == key,
			cachedBuild = cached,
			note = cached == key
				? "The exported API reference matches this build."
				: "The API reference is stale or missing - call api_reference to refresh it."
		};
	}

	[McpTool( "api_reference", "Returns the path to a complete API reference for the CURRENT editor build, regenerating it automatically if the editor was updated since it was last written. This is the always-up-to-date full reference; api_search/api_get_type are the live per-query lookups.", ToolCategory.Editor, Writes = true )]
	public static object Reference(
		[Desc( "Force a fresh regeneration even if the build is unchanged" )] bool force = false )
	{
		var (key, display) = EngineBuild();
		var absoluteRef = AssetTools.ResolveInProject( ReferenceFile );
		var regenerated = false;

		if ( force || ReadCachedVersion() != key || !File.Exists( absoluteRef ) )
		{
			var (types, bytes) = WriteDump( absoluteRef, null, key, display );
			File.WriteAllText( AssetTools.ResolveInProject( VersionFile ), key );
			regenerated = true;

			return new { path = ReferenceFile, build = display, types, bytes, regenerated,
				note = "Regenerated for the current build. Read it with asset_read_raw or code_read_file." };
		}

		return new { path = ReferenceFile, build = display, regenerated,
			note = "Already current for this build. Read it with asset_read_raw or code_read_file." };
	}

	static string ReadCachedVersion()
	{
		try
		{
			var p = AssetTools.ResolveInProject( VersionFile );
			return File.Exists( p ) ? File.ReadAllText( p ).Trim() : null;
		}
		catch { return null; }
	}

	/// <summary>(stable key, human display) for the running editor build.</summary>
	static (string Key, string Display) EngineBuild()
	{
		// Application.Version / VersionDate are the real per-build identity;
		// the engine assembly version is static (1.0.1.0) so it can't be the key
		var version = Sandbox.Application.Version;
		DateTime date;
		try { date = Sandbox.Application.VersionDate; }
		catch { date = default; }

		if ( string.IsNullOrWhiteSpace( version ) )
			version = typeof( Sandbox.Component ).Assembly.GetName().Version?.ToString() ?? "unknown";

		var hasDate = date.Year > 2000;
		var key = hasDate ? $"{version}+{date:yyyyMMddHHmmss}" : version;
		var display = hasDate ? $"{version} ({date:yyyy-MM-dd HH:mm})" : version;
		return (key, display);
	}

	[McpTool( "api_dump", "Exports the ENTIRE live API surface (every public type and member from the loaded s&box + project assemblies) to a text file you choose. Always reflects the current installed build. Warning: large (~1MB). For the auto-maintained reference, prefer api_reference.", ToolCategory.Editor, Writes = true )]
	public static object Dump(
		[Desc( "Output path, e.g. 'api_surface.txt' (project-relative)" )] string path = "api_surface.txt",
		[Desc( "Only namespaces starting with this, e.g. 'Sandbox'; omit for everything" )] string namespaceFilter = null )
	{
		var (key, display) = EngineBuild();
		var absolute = AssetTools.ResolveInProject( path );
		var (types, bytes) = WriteDump( absolute, namespaceFilter, key, display );

		return new { written = path, build = display, types, bytes, note = "Complete current-build API reference." };
	}

	static (int Types, int Bytes) WriteDump( string absolutePath, string namespaceFilter, string versionKey, string versionDisplay )
	{
		var sb = new StringBuilder();
		sb.AppendLine( "# s&box API surface - LIVE reflection dump from the running editor" );
		sb.Append( "# build: " ).AppendLine( versionDisplay );
		sb.Append( "# versionKey: " ).AppendLine( versionKey );
		sb.AppendLine( "# format: TYPE <kind> <fullname>[ : base] then indented members (P property, M method, F field, E event)" );

		var assemblies = AppDomain.CurrentDomain.GetAssemblies()
			.Where( a => !a.IsDynamic )
			.Where( a =>
			{
				var n = a.GetName().Name ?? "";
				return n.StartsWith( "Sandbox", StringComparison.Ordinal )
					|| n.StartsWith( "package.", StringComparison.Ordinal )
					|| n.StartsWith( "Facepunch", StringComparison.Ordinal );
			} );

		var typeCount = 0;
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		foreach ( var type in assemblies.SelectMany( SafeTypes ).OrderBy( t => t.FullName, StringComparer.Ordinal ) )
		{
			if ( namespaceFilter is not null && !(type.Namespace?.StartsWith( namespaceFilter, StringComparison.OrdinalIgnoreCase ) ?? false) )
				continue;

			var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
			var baseStr = type.BaseType is not null && type.BaseType != typeof( object ) && !type.IsEnum
				? $" : {type.BaseType.FullName}" : "";
			sb.Append( "TYPE " ).Append( kind ).Append( ' ' ).Append( type.FullName ).AppendLine( baseStr );
			typeCount++;

			foreach ( var p in type.GetProperties( flags ).Where( p => p.GetIndexParameters().Length == 0 ) )
				sb.Append( "  P " ).Append( FriendlyType( p.PropertyType ) ).Append( ' ' ).Append( p.Name )
					.Append( ' ' ).Append( p.CanRead ? "get;" : "" ).AppendLine( p.CanWrite ? "set;" : "" );

			foreach ( var m in type.GetMethods( flags ).Where( m => !m.IsSpecialName ) )
				sb.Append( "  M " ).Append( FriendlyType( m.ReturnType ) ).Append( ' ' ).Append( m.Name )
					.Append( '(' ).Append( string.Join( ", ", m.GetParameters().Select( x => $"{FriendlyType( x.ParameterType )} {x.Name}" ) ) ).AppendLine( ")" );

			foreach ( var f in type.GetFields( flags ).Where( f => !f.IsSpecialName ) )
				sb.Append( "  F " ).Append( FriendlyType( f.FieldType ) ).Append( ' ' ).AppendLine( f.Name );

			foreach ( var e in type.GetEvents( flags ) )
				sb.Append( "  E " ).Append( FriendlyType( e.EventHandlerType ) ).Append( ' ' ).AppendLine( e.Name );
		}

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) );
		File.WriteAllText( absolutePath, sb.ToString() );

		return (typeCount, sb.Length);
	}

	static IEnumerable<Type> SafeTypes( Assembly assembly )
	{
		try { return assembly.GetExportedTypes(); }
		catch { return Array.Empty<Type>(); }
	}

	static string FriendlyType( Type t )
	{
		if ( t is null ) return "?";
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
