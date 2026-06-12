using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using Sandbox.Diagnostics;
using SboxMcp.Registry;
using SboxMcp.Server;

namespace SboxMcp.Integration;

/// <summary>
/// Owns the MCP server inside the editor: builds the tool registry, runs the
/// permission -> main-thread -> activity pipeline, and survives hotloads.
/// </summary>
public static class McpHost
{
	internal static readonly Logger Log = new( "MCP" );

	/// <summary>AppDomain slot holding a stop delegate, so a hotloaded assembly can stop its predecessor's listener.</summary>
	const string StopSlot = "sbox-mcp.stop";

	static bool _initialized;

	public static McpServer Server { get; private set; }
	public static string LastError { get; private set; }

	/// <summary>Raised when server state or LastError changes.</summary>
	public static event Action Changed;

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		if ( !_initialized )
		{
			_initialized = true;
			Initialize();
		}

		MainThreadDispatcher.Pump();
	}

	static void Initialize()
	{
		// a previous hotloaded version of this assembly may still hold the port
		if ( AppDomain.CurrentDomain.GetData( StopSlot ) is Action oldStop )
		{
			try { oldStop(); }
			catch ( Exception e ) { Log.Warning( $"Failed to stop previous MCP server: {e.Message}" ); }
		}

		var registry = new ToolRegistry();
		registry.AddAssembly( typeof( McpHost ).Assembly );

		Server = new McpServer( registry, InvokeTool );
		Server.StateChanged += () => Changed?.Invoke();

		AppDomain.CurrentDomain.SetData( StopSlot, (Action)StopListener );

		Log.Info( $"s&box MCP loaded ({registry.Tools.Count} tools)" );

		if ( McpSettings.AutoStart )
			Start();
	}

	static void StopListener() => Server?.Stop();

	public static void Start()
	{
		try
		{
			Server.Start( McpSettings.Port );
			LastError = null;
			Log.Info( $"MCP server listening on {Server.Url}" );
		}
		catch ( Exception e )
		{
			LastError = $"Could not start on port {McpSettings.Port}: {e.Message}";
			Log.Warning( LastError );
		}

		Changed?.Invoke();
	}

	public static void Stop()
	{
		Server?.Stop();
		Log.Info( "MCP server stopped" );
		Changed?.Invoke();
	}

	public static void Restart()
	{
		Stop();
		Start();
	}

	static async Task<object> InvokeTool( RegisteredTool tool, JsonElement? args )
	{
		var allowed = await PermissionGate.RequestAsync( tool, args );
		if ( !allowed )
		{
			var reason = McpSettings.Mode == PermissionMode.ReadOnly
				? "the MCP server is in read-only mode"
				: "the user denied this action (or did not approve within 60s)";

			var denial = new ActivityRecord
			{
				ToolName = tool.Meta.Name,
				Category = tool.Meta.Category,
				ArgsDigest = PermissionGate.Summarize( args ),
				Ok = false,
				Error = "denied"
			};
			ActivityLog.Record( denial );

			throw new UnauthorizedAccessException(
				$"'{tool.Meta.Name}' was not executed: {reason}. Read-only tools still work." );
		}

		var sw = Stopwatch.StartNew();
		try
		{
			var result = await MainThreadDispatcher.Run( () => tool.Invoke( args ) );

			ActivityLog.Record( new ActivityRecord
			{
				ToolName = tool.Meta.Name,
				Category = tool.Meta.Category,
				ArgsDigest = PermissionGate.Summarize( args ),
				Ok = true,
				DurationMs = sw.ElapsedMilliseconds
			} );

			return result;
		}
		catch ( Exception e )
		{
			ActivityLog.Record( new ActivityRecord
			{
				ToolName = tool.Meta.Name,
				Category = tool.Meta.Category,
				ArgsDigest = PermissionGate.Summarize( args ),
				Ok = false,
				Error = e.Message,
				DurationMs = sw.ElapsedMilliseconds
			} );

			throw;
		}
	}
}
