using System;
using System.IO;
using System.Linq;
using SboxMcp.Integration;
using SboxMcp.Registry;
using static SboxMcp.Tools.AssetTools;

namespace SboxMcp.Tools;

public static class CodeTools
{
	static readonly string[] SkippedDirs = { "\\obj\\", "\\bin\\", "\\.git\\", "/obj/", "/bin/", "/.git/" };

	[McpTool( "code_list_files", "Lists C# source files in the project (Code/ and Editor/ folders). Saving a file hot-reloads automatically.", ToolCategory.Code )]
	public static object ListFiles(
		[Desc( "Subdirectory filter relative to project root, e.g. 'Code/Player'" )] string subdir = null,
		[Desc( "Include files from installed Libraries" )] bool includeLibraries = false )
	{
		var root = ProjectRoot;
		var searchRoot = subdir is null ? root : ResolveInProject( subdir );

		if ( !Directory.Exists( searchRoot ) )
			throw new InvalidOperationException( $"No directory '{subdir}' in the project" );

		var files = Directory.EnumerateFiles( searchRoot, "*.cs", SearchOption.AllDirectories )
			.Where( f => !SkippedDirs.Any( s => f.Contains( s, StringComparison.OrdinalIgnoreCase ) ) )
			.Where( f => includeLibraries || !f.Contains( Path.DirectorySeparatorChar + "Libraries" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			.Select( f => Path.GetRelativePath( root, f ).Replace( '\\', '/' ) )
			.OrderBy( f => f )
			.ToArray();

		return new { count = files.Length, files };
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
