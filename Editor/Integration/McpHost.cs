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
	public static ToolRegistry Registry { get; private set; }
	public static string LastError { get; private set; }

	/// <summary>Raised when server state or LastError changes.</summary>
	public static event Action Changed;

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		if ( !_initialized )
		{
			_initialized = true;

			try
			{
				Initialize();
			}
			catch ( Exception e )
			{
				LastError = $"MCP failed to initialize: {e.Message}";
				Log.Warning( e, LastError );
			}
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

		McpSettings.LoadFromCookies();

		ToolRegistry.RequirementResolver = key => key switch
		{
			Tools.RetargeterTools.Requirement => Tools.RetargeterTools.IsInstalled ? null : "Not Installed",
			_ => null
		};

		ToolRegistry.DisabledResolver = tool =>
			McpSettings.GetToolDisabledOverride( tool.Meta.Name ) ?? tool.Meta.DisabledByDefault;

		var registry = new ToolRegistry();
		registry.AddAssembly( typeof( McpHost ).Assembly );
		Registry = registry;

		ToolImporter.RegisterSaved( registry );

		Server = new McpServer( registry, InvokeTool );
		Server.StateChanged += () => Changed?.Invoke();

		LogCapture.Start();
		AppDomain.CurrentDomain.SetData( StopSlot, (Action)Shutdown );

		Log.Info( $"s&box MCP loaded ({registry.Tools.Count} tools)" );

		if ( McpSettings.AutoStart )
			Start();
	}

	static void Shutdown()
	{
		Server?.Stop();
		LogCapture.Stop();
	}

	public static void Start()
	{
		if ( Server is null )
		{
			LastError ??= "MCP server is not initialized";
			Changed?.Invoke();
			return;
		}

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
			var result = await MainThreadDispatcher.Run( () =>
			{
				// editor operations expect the edited scene to be the active
				// scene scope (the engine's own cut/paste/clone paths do this)
				var scene = SceneEditorSession.Active?.Scene;
				using var sceneScope = scene?.Push();

				return tool.Invoke( args );
			} );

			// async tools (cloud downloads etc.) return a Task from the main
			// thread hop; await its completion here
			if ( result is Task pending )
			{
				await pending;
				var resultProperty = pending.GetType().GetProperty( "Result" );
				result = resultProperty?.PropertyType.Name == "VoidTaskResult"
					? null
					: resultProperty?.GetValue( pending );
			}

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
