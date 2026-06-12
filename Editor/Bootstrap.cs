using Editor;
using Sandbox.Diagnostics;

namespace SboxMcp;

/// <summary>
/// Entry point for the MCP library. The editor invokes <see cref="OnFrame"/> every
/// tool frame; the first call performs one-time initialization.
/// </summary>
public static class McpBootstrap
{
	internal static readonly Logger Log = new( "MCP" );

	static bool _initialized;

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		if ( !_initialized )
		{
			_initialized = true;
			Log.Info( "s&box MCP library loaded" );
		}
	}
}
