using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Integration;

/// <summary>
/// Persisted settings, stored in editor cookies so they survive restarts.
/// </summary>
public static class McpSettings
{
	public const int DefaultPort = 9090;

	public static int Port
	{
		get => EditorCookie.Get( "SboxMcp.Port", DefaultPort );
		set => EditorCookie.Set( "SboxMcp.Port", value );
	}

	public static bool AutoStart
	{
		get => EditorCookie.Get( "SboxMcp.AutoStart", true );
		set => EditorCookie.Set( "SboxMcp.AutoStart", value );
	}

	public static PermissionMode Mode
	{
		get => EditorCookie.Get( "SboxMcp.PermissionMode", PermissionMode.ApproveWrites );
		set => EditorCookie.Set( "SboxMcp.PermissionMode", value );
	}
}
