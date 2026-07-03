using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox;
using SboxMcp.Integration;
using SboxMcp.Registry;
using static SboxMcp.Tools.AssetTools;

namespace SboxMcp.Tools;

public static class CodeTools
{
	static readonly string[] SkippedDirs = { "\\obj\\", "\\bin\\", "/obj/", "/bin/" };
	static readonly string[] SourceExtensions = { ".cs", ".razor", ".scss", ".shader", ".hlsl" };

	/// <summary>Skip build output and any dot-directory (.git, .sbox, .removed-libraries...).</summary>
	static bool IsSkipped( string fullPath )
	{
		if ( SkippedDirs.Any( s => fullPath.Contains( s, StringComparison.OrdinalIgnoreCase ) ) )
			return true;

		// any path segment starting with '.'
		return fullPath.Replace( '\\', '/' ).Split( '/' ).Any( seg => seg.StartsWith( '.' ) && seg.Length > 1 );
	}

	[McpTool( "code_list_files", "Lists source files in the project: C# (.cs), UI (.razor/.scss) and shaders. Saving a file hot-reloads automatically.", ToolCategory.Code )]
	public static object ListFiles(
		[Desc( "Subdirectory filter relative to project root, e.g. 'Code/Player'" )] string subdir = null,
		[Desc( "Include files from installed Libraries" )] bool includeLibraries = false )
	{
		var root = ProjectRoot;
		var searchRoot = subdir is null ? root : ResolveInProject( subdir );

		if ( !Directory.Exists( searchRoot ) )
			throw new InvalidOperationException( $"No directory '{subdir}' in the project" );

		var files = Directory.EnumerateFiles( searchRoot, "*.*", SearchOption.AllDirectories )
			.Where( f => SourceExtensions.Contains( Path.GetExtension( f ), StringComparer.OrdinalIgnoreCase ) )
			.Where( f => !IsSkipped( f ) )
			.Where( f => includeLibraries || !f.Contains( Path.DirectorySeparatorChar + "Libraries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			.Select( f => Path.GetRelativePath( root, f ).Replace( '\\', '/' ) )
			.OrderBy( f => f )
			.ToArray();

		return new { count = files.Length, files };
	}

	[McpTool( "code_search", "Searches project source files (C#/Razor/SCSS/shaders) for a substring or regex - find where a symbol is used, a class is defined, etc. Returns file:line matches.", ToolCategory.Code )]
	public static object Search(
		[Desc( "Text or regex to find" )] string pattern,
		[Desc( "Treat pattern as a regular expression" )] bool regex = false,
		[Desc( "Case-sensitive match" )] bool caseSensitive = false,
		[Desc( "Limit to a subdirectory relative to project root" )] string subdir = null,
		[Desc( "Also search installed library source under Libraries/" )] bool includeLibraries = false,
		int max = 100 )
	{
		if ( string.IsNullOrEmpty( pattern ) )
			throw new ArgumentException( "pattern must not be empty" );

		var root = ProjectRoot;
		var searchRoot = subdir is null ? root : ResolveInProject( subdir );
		if ( !Directory.Exists( searchRoot ) )
			throw new InvalidOperationException( $"No directory '{subdir}' - use code_list_files to see the layout" );

		var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
		System.Text.RegularExpressions.Regex rx = null;
		if ( regex )
			rx = new System.Text.RegularExpressions.Regex( pattern,
				caseSensitive ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase );

		var matches = new List<object>();

		foreach ( var file in Directory.EnumerateFiles( searchRoot, "*.*", SearchOption.AllDirectories ) )
		{
			if ( !SourceExtensions.Contains( Path.GetExtension( file ), StringComparer.OrdinalIgnoreCase ) )
				continue;
			if ( IsSkipped( file ) )
				continue;
			if ( !includeLibraries && file.Contains( Path.DirectorySeparatorChar + "Libraries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var rel = Path.GetRelativePath( root, file ).Replace( '\\', '/' );
			var lines = File.ReadAllLines( file );
			for ( var i = 0; i < lines.Length; i++ )
			{
				var hit = rx is not null ? rx.IsMatch( lines[i] ) : lines[i].Contains( pattern, comparison );
				if ( !hit ) continue;

				matches.Add( new { file = rel, line = i + 1, text = lines[i].Trim() } );
				if ( matches.Count >= max ) break;
			}
			if ( matches.Count >= max ) break;
		}

		return new { count = matches.Count, truncated = matches.Count >= max, matches };
	}

	[McpTool( "code_read_file", "Reads a project source file.", ToolCategory.Code )]
	public static object ReadFile( [Desc( "Path relative to project root, e.g. 'Code/Player.cs'" )] string path )
	{
		var absolute = ResolveInProject( path );

		if ( !File.Exists( absolute ) )
			throw new InvalidOperationException( $"No file at '{path}' - use code_list_files" );

		return new { path, content = File.ReadAllText( absolute ) };
	}

	[McpTool( "code_write_file", "Writes a project source file (creating it if missing). The editor hot-reloads changed code automatically; check editor_get_logs / code_get_compile_errors afterwards.", ToolCategory.Code, Writes = true )]
	public static object WriteFile(
		[Desc( "Path relative to project root, e.g. 'Code/Player.cs'" )] string path,
		[Desc( "Full new file content" )] string content )
	{
		var absolute = ResolveInProject( path );

		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );
		File.WriteAllText( absolute, content );

		return new { written = path, note = "hot-reload triggers automatically; verify with code_get_compile_errors" };
	}

	[McpTool( "code_edit_file", "Replaces an exact text snippet in a project source file - a targeted edit, versus code_write_file which rewrites the whole file. The old text must appear EXACTLY ONCE (include surrounding context to make it unique). The editor hot-reloads afterward.", ToolCategory.Code, Writes = true )]
	public static object EditFile(
		[Desc( "Path relative to project root, e.g. 'Code/Player.cs'" )] string path,
		[Desc( "Exact existing text to replace (must be unique in the file, whitespace included)" )] string oldText,
		[Desc( "Replacement text" )] string newText )
	{
		if ( string.IsNullOrEmpty( oldText ) )
			throw new ArgumentException( "oldText must not be empty - use code_write_file to create/overwrite a file" );

		var absolute = ResolveInProject( path );
		if ( !File.Exists( absolute ) )
			throw new InvalidOperationException( $"No file at '{path}' - use code_list_files" );

		var content = File.ReadAllText( absolute );

		var first = content.IndexOf( oldText, StringComparison.Ordinal );
		if ( first < 0 )
			throw new InvalidOperationException( $"The old text was not found in '{path}' - read it with code_read_file and match exactly (whitespace included)" );
		if ( content.IndexOf( oldText, first + 1, StringComparison.Ordinal ) >= 0 )
			throw new InvalidOperationException( $"The old text appears more than once in '{path}' - include more surrounding context to make it unique" );

		File.WriteAllText( absolute, content.Remove( first, oldText.Length ).Insert( first, newText ) );

		return new { edited = path, note = "hot-reload triggers automatically; verify with code_get_compile_errors" };
	}

	[McpTool( "code_create_component", "Scaffolds a new Component C# file (a script you can add to GameObjects) with the standard boilerplate and any [Property] fields. The editor hot-reloads it, then add it with component_add.", ToolCategory.Code, Writes = true )]
	public static object CreateComponent(
		[Desc( "Component class name, e.g. 'PlayerMovement'" )] string className,
		[Desc( "Namespace; omit for the project default" )] string @namespace = null,
		[Desc( "Property fields as 'Type Name' pairs, e.g. ['float Speed', 'GameObject Target']" )] string[] properties = null,
		[Desc( "Add an OnUpdate() method body" )] bool withUpdate = true )
	{
		if ( string.IsNullOrWhiteSpace( className ) || !char.IsLetter( className[0] ) )
			throw new ArgumentException( "className must start with a letter" );

		var ns = @namespace ?? DefaultNamespace();
		var sb = new System.Text.StringBuilder();
		sb.AppendLine( "using Sandbox;" ).AppendLine();
		sb.AppendLine( $"namespace {ns};" ).AppendLine();
		sb.AppendLine( $"public sealed class {className} : Component" );
		sb.AppendLine( "{" );

		foreach ( var p in properties ?? Array.Empty<string>() )
		{
			var parts = p.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			if ( parts.Length == 2 )
				sb.AppendLine( $"\t[Property] public {parts[0]} {parts[1]} {{ get; set; }}" ).AppendLine();
		}

		if ( withUpdate )
		{
			sb.AppendLine( "\tprotected override void OnUpdate()" );
			sb.AppendLine( "\t{" );
			sb.AppendLine( "\t\t// runs every frame while the component is enabled" );
			sb.AppendLine( "\t}" );
		}

		sb.AppendLine( "}" );

		var path = $"Code/{className}.cs";
		var absolute = ResolveInProject( path );
		if ( File.Exists( absolute ) )
			throw new InvalidOperationException( $"'{path}' already exists - edit it with code_write_file" );

		Directory.CreateDirectory( Path.GetDirectoryName( absolute ) );
		File.WriteAllText( absolute, sb.ToString() );

		return new { created = path, className, note = $"hot-reloading; then component_add(go, \"{className}\")" };
	}

	static string DefaultNamespace()
	{
		// RootNamespace lives in the .sbproj; read it from there rather than
		// guessing the config property name
		try
		{
			var sbproj = Directory.GetFiles( ProjectRoot, "*.sbproj" ).FirstOrDefault();
			if ( sbproj is not null
				&& System.Text.Json.Nodes.JsonNode.Parse( File.ReadAllText( sbproj ) ) is System.Text.Json.Nodes.JsonObject json )
			{
				// RootNamespace lives at Metadata.Compiler.RootNamespace; fall back to root
				var ns = json["Metadata"]?["Compiler"]?["RootNamespace"]?.GetValue<string>()
					?? json["RootNamespace"]?.GetValue<string>();
				if ( !string.IsNullOrWhiteSpace( ns ) )
					return ns;
			}
		}
		catch { /* fall through to default */ }

		return "Sandbox";
	}

	[McpTool( "code_run_static_method", "Invokes a public static parameterless method from project code - write a method with code_write_file, wait for hot-reload, then call it to test or inspect game state. Returns the method's ToString'd result.", ToolCategory.Code, Writes = true )]
	public static object RunStaticMethod(
		[Desc( "Type name, e.g. 'MyGame.DebugHelpers'" )] string typeName,
		[Desc( "Public static method with no parameters" )] string methodName )
	{
		var type = Sandbox.Internal.GlobalToolsNamespace.EditorTypeLibrary.GetType( typeName )
			?? throw new InvalidOperationException( $"No type '{typeName}' - is it compiled? Check code_get_compile_errors" );

		var method = type.Methods.FirstOrDefault( m => m.IsStatic && m.Name == methodName && m.Parameters.Length == 0 )
			?? throw new InvalidOperationException( $"'{typeName}' has no public static parameterless method '{methodName}'" );

		var result = method.InvokeWithReturn<object>( null, Array.Empty<object>() );
		return new { invoked = $"{typeName}.{methodName}", result = result?.ToString() ?? "null" };
	}

	[McpTool( "code_delete_file", "Deletes a project source file (e.g. remove a component you no longer need). Jailed to the project; not undoable.", ToolCategory.Code, Writes = true )]
	public static object DeleteFile( [Desc( "Path relative to project root, e.g. 'Code/OldThing.cs'" )] string path )
	{
		var absolute = ResolveInProject( path );
		if ( !File.Exists( absolute ) )
			throw new InvalidOperationException( $"No file at '{path}' - use code_list_files" );

		File.Delete( absolute );
		return new { deleted = path, note = "the editor will hot-reload; check code_get_compile_errors for references you may need to remove" };
	}

	[McpTool( "code_get_compile_errors", "Gets recent compiler errors and warnings from the editor console.", ToolCategory.Code )]
	public static object GetCompileErrors( int max = 50 )
	{
		var entries = LogCapture.Recent( max, "warning", diagnosticsOnly: true )
			.Select( l => new { time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, logger = l.Logger, message = l.Message } )
			.ToArray();

		// fall back to error-looking log lines if no tagged diagnostics are buffered
		if ( entries.Length == 0 )
		{
			entries = LogCapture.Recent( max, "error" )
				.Where( l => l.Message is not null )
				.Select( l => new { time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, logger = l.Logger, message = l.Message } )
				.ToArray();
		}

		return new
		{
			count = entries.Length,
			note = "Entries come from the editor console log stream. An empty list right after code_write_file may mean compilation has not finished - wait a moment and call again.",
			entries
		};
	}
}
