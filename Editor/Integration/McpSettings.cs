using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Integration;

/// <summary>
/// Persisted settings. EditorCookie is not thread-safe and must only be
/// touched on the editor main thread, so values are cached in fields:
/// getters are safe from any thread, setters are UI (main thread) only.
/// </summary>
public static class McpSettings
{
	public const int DefaultPort = 9090;

	static int _port = DefaultPort;
	static bool _autoStart = true;
	static PermissionMode _mode = PermissionMode.ApproveWrites;

	/// <summary>Called once from the editor main thread before anything reads settings.</summary>
	internal static void LoadFromCookies()
	{
		_port = EditorCookie.Get( "SboxMcp.Port", DefaultPort );
		_autoStart = EditorCookie.Get( "SboxMcp.AutoStart", true );
		_mode = EditorCookie.Get( "SboxMcp.PermissionMode", PermissionMode.ApproveWrites );
	}

	public static int Port
	{
		get => _port;
		set { _port = value; EditorCookie.Set( "SboxMcp.Port", value ); }
	}

	public static bool AutoStart
	{
		get => _autoStart;
		set { _autoStart = value; EditorCookie.Set( "SboxMcp.AutoStart", value ); }
	}

	public static PermissionMode Mode
	{
		get => _mode;
		set { _mode = value; EditorCookie.Set( "SboxMcp.PermissionMode", value ); }
	}
}
