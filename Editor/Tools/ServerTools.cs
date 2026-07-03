using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SboxMcp.Integration;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Tools that operate on the MCP server itself: batch execution (many calls in
/// one request) and reading/adjusting the server's own configuration.
/// </summary>
public static class ServerTools
{
	[McpTool( "batch", "Runs several tool calls in one request, in order - big speedup for multi-step builds (create object, add component, set properties...). Each step is {name, arguments}. Stops on the first error unless continueOnError is true.", ToolCategory.Editor, Writes = true )]
	public static object Batch(
		[Desc( "JSON array of steps, e.g. [{\"name\":\"gameobject_create\",\"arguments\":{\"name\":\"X\"}}, ...]" )] JsonElement steps,
		[Desc( "Keep going after a step fails instead of stopping" )] bool continueOnError = false )
	{
		if ( steps.ValueKind != JsonValueKind.Array )
			throw new ArgumentException( "steps must be a JSON array of {name, arguments} objects" );

		var registry = McpHost.Registry
			?? throw new InvalidOperationException( "Server not initialized" );

		var results = new List<object>();
		var index = 0;

		foreach ( var step in steps.EnumerateArray() )
		{
			index++;

			if ( !step.TryGetProperty( "name", out var nameEl ) || nameEl.ValueKind != JsonValueKind.String )
			{
				results.Add( new { step = index, ok = false, error = "step is missing a string 'name'" } );
				if ( !continueOnError ) break; else continue;
			}

			var name = nameEl.GetString();
			var tool = registry.Find( name );

			if ( tool is null || !tool.IsAvailable )
			{
				results.Add( new { step = index, name, ok = false, error = tool is null ? "unknown tool" : $"unavailable: {tool.UnavailableReason}" } );
				if ( !continueOnError ) break; else continue;
			}

			JsonElement? args = step.TryGetProperty( "arguments", out var a ) && a.ValueKind == JsonValueKind.Object ? a : null;

			try
			{
				// already on the editor main thread (batch itself was dispatched there)
				var result = tool.Invoke( args );

				// async tools (cloud_*) return a Task - can't be awaited on the
				// main thread without freezing the editor, so reject clearly
				if ( result is System.Threading.Tasks.Task )
				{
					results.Add( new { step = index, name, ok = false, error = "this tool is async and cannot run inside a batch - call it on its own" } );
					if ( !continueOnError ) break; else continue;
				}

				results.Add( new { step = index, name, ok = true, result } );
			}
			catch ( Exception e )
			{
				results.Add( new { step = index, name, ok = false, error = e.Message } );
				if ( !continueOnError ) break;
			}
		}

		var ran = results.Count;
		var failed = results.Count( r => r.GetType().GetProperty( "ok" )?.GetValue( r ) is false );
		return new { requested = steps.GetArrayLength(), ran, failed, results };
	}

	[McpTool( "server_get_config", "Reads the MCP server's current configuration: port, permission mode, autostart, tool counts.", ToolCategory.Editor )]
	public static object GetConfig()
	{
		var registry = McpHost.Registry;
		var tools = registry?.Tools ?? (IReadOnlyList<RegisteredTool>)Array.Empty<RegisteredTool>();

		return new
		{
			url = McpHost.Server?.Url,
			running = McpHost.Server?.IsRunning ?? false,
			port = McpSettings.Port,
			autoStart = McpSettings.AutoStart,
			permissionMode = McpSettings.Mode.ToString(),
			toolCount = tools.Count,
			enabledTools = tools.Count( t => t.IsAvailable ),
			connectedClients = McpHost.Server?.Sessions.Count ?? 0,
			note = "Permission mode is set by the user in the dashboard and cannot be changed over MCP by design."
		};
	}

	[McpTool( "server_set_config", "Adjusts server settings the AI is allowed to change (port, autostart). Permission mode stays user-only. Changing the port restarts the listener.", ToolCategory.Editor, Writes = true )]
	public static object SetConfig(
		[Desc( "New port 1024-65535; omit to leave unchanged" )] int? port = null,
		[Desc( "Autostart on editor load; omit to leave unchanged" )] bool? autoStart = null )
	{
		var restarted = false;

		if ( autoStart is bool a )
			McpSettings.AutoStart = a;

		if ( port is int p )
		{
			if ( p is < 1024 or > 65535 )
				throw new ArgumentException( "port must be 1024..65535" );

			if ( p != McpSettings.Port )
			{
				McpSettings.Port = p;
				McpHost.Restart();
				restarted = true;
			}
		}

		return new { port = McpSettings.Port, autoStart = McpSettings.AutoStart, restarted, note = restarted ? "Listener restarted on the new port - reconnect your client." : "Updated." };
	}
}
