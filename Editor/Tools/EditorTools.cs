using System;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using SboxMcp.Integration;
using SboxMcp.Registry;
using SboxMcp.Server;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class EditorTools
{
	[McpTool( "editor_get_logs", "Reads recent editor console output (newest first) - compile diagnostics, editor warnings/errors. NOTE: game-side Log.* emitted while play mode is running may not all appear here; to inspect play-mode state, read component values with component_get_property / get_component_property (they reflect the live play scene).", ToolCategory.Editor )]
	public static object GetLogs(
		int count = 100,
		[Desc( "Minimum severity: trace, info, warning or error" )] string minSeverity = null,
		[Desc( "Only entries newer than this cursor (pass back the 'cursor' from the previous call to poll incrementally instead of re-reading old lines)" )] long sinceSeq = 0 )
	{
		var logs = LogCapture.Recent( count, minSeverity, sinceSeq: sinceSeq )
			.Select( l => new { seq = l.Seq, time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, logger = l.Logger, message = l.Message } )
			.ToArray();

		// cursor = newest sequence number; pass it as sinceSeq next call for a
		// clean "only what's new" tail
		return new { count = logs.Length, cursor = LogCapture.LatestSeq, logs };
	}

	[McpTool( "logs_search", "Searches the captured console log by regex, minimum severity, and time window - returns matches WITH their stack traces (invaluable for errors/exceptions). Cleaner than paging editor_get_logs when hunting a specific message.", ToolCategory.Editor )]
	public static object LogsSearch(
		[Desc( "Regex to match in the message; omit to match everything" )] string pattern = null,
		[Desc( "Minimum severity: trace, info, warning or error" )] string minSeverity = null,
		[Desc( "Only entries from the last N seconds; omit for the whole buffer" )] int withinSeconds = 0,
		int max = 50 )
	{
		var since = withinSeconds > 0 ? System.DateTime.Now.AddSeconds( -withinSeconds ) : (System.DateTime?)null;

		var results = LogCapture.Search( pattern, minSeverity, max, since )
			.Select( l => new { seq = l.Seq, time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, logger = l.Logger, message = l.Message, stack = l.Stack } )
			.ToArray();

		return new { count = results.Length, cursor = LogCapture.LatestSeq, results };
	}

	[McpTool( "editor_clear_logs", "Clears the captured console log buffer.", ToolCategory.Editor )]
	public static object ClearLogs()
	{
		LogCapture.Clear();
		return new { cleared = true };
	}

	[McpTool( "editor_screenshot", "Captures what the game camera sees, as an image. DURING PLAY this is the player's live point of view (renders Game.ActiveScene through its active CameraComponent) - use it to see what the player sees. In edit mode it renders the edit scene's camera. For an arbitrary angle instead, use editor_screenshot_from. Needs an enabled CameraComponent.", ToolCategory.Editor )]
	public static object Screenshot(
		[Desc( "Image width in pixels" )] int width = 1280,
		[Desc( "Image height in pixels" )] int height = 720 )
	{
		var session = RequireSession();
		var scene = session.IsPlaying && Game.ActiveScene is not null ? Game.ActiveScene : session.Scene;

		if ( scene.Camera is null )
			throw new InvalidOperationException(
				"The scene has no enabled CameraComponent to render from - add one with component_add" );

		width = Math.Clamp( width, 64, 4096 );
		height = Math.Clamp( height, 64, 4096 );

		var pixmap = new Pixmap( width, height );

		if ( !scene.RenderToPixmap( pixmap ) )
			throw new InvalidOperationException( "Rendering failed - check editor_get_logs; ensure a valid camera, or try editor_screenshot_from" );

		var png = pixmap.GetPng();
		return new RawMcpResult( McpResults.ImageContent(
			Convert.ToBase64String( png ),
			$"{(session.IsPlaying ? "game" : "scene")} camera view, {width}x{height}" ) );
	}

	[McpTool( "editor_screenshot_from", "Renders the scene from an arbitrary viewpoint (no camera component needed) - use it to inspect what you built from any angle.", ToolCategory.Editor )]
	public static object ScreenshotFrom(
		[Desc( "Camera world position [x, y, z]" )] float[] position,
		[Desc( "Camera rotation [pitch, yaw, roll]; ignored when lookAt is set" )] float[] rotation = null,
		[Desc( "GameObject id/name to aim the camera at" )] string lookAt = null,
		int width = 1280,
		int height = 720 )
	{
		var session = RequireSession();
		var scene = session.Scene;

		width = Math.Clamp( width, 64, 4096 );
		height = Math.Clamp( height, 64, 4096 );

		// temporary camera, intentionally outside any undo scope
		var go = scene.CreateObject();
		try
		{
			go.Name = "__mcp_temp_camera";
			go.WorldPosition = ToVector3( position, "position" );

			if ( lookAt is not null )
			{
				var target = FindGameObject( lookAt );
				go.WorldRotation = Rotation.LookAt( target.WorldPosition - go.WorldPosition );
			}
			else if ( rotation is not null )
			{
				if ( rotation.Length != 3 )
					throw new ArgumentException( "'rotation' must be [pitch, yaw, roll]" );

				go.WorldRotation = Rotation.From( rotation[0], rotation[1], rotation[2] );
			}

			var camera = go.Components.Create<CameraComponent>();
			var pixmap = new Pixmap( width, height );

			if ( !camera.RenderToPixmap( pixmap ) )
				throw new InvalidOperationException( "Rendering failed" );

			return new RawMcpResult( McpResults.ImageContent(
				Convert.ToBase64String( pixmap.GetPng() ),
				$"view from [{string.Join( ", ", position )}], {width}x{height}" ) );
		}
		finally
		{
			go.Destroy();
		}
	}

	[McpTool( "editor_frame_object", "Points the editor viewport camera at a GameObject so the user can see it.", ToolCategory.Editor )]
	public static object FrameObject( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		session.FrameTo( go.GetBounds() );
		return new { framed = go.Name };
	}

	[McpTool( "editor_play", "Enters play mode with the current scene.", ToolCategory.Editor, Writes = true )]
	public static object Play()
	{
		var session = RequireSession();

		if ( session.IsPlaying )
			return new { playing = true, note = "already in play mode" };

		EditorScene.Play();
		return new { playing = SceneEditorSession.Active?.IsPlaying ?? false };
	}

	[McpTool( "editor_stop", "Exits play mode.", ToolCategory.Editor, Writes = true )]
	public static object Stop()
	{
		var session = RequireSession();

		if ( !session.IsPlaying )
			return new { playing = false, note = "was not in play mode" };

		EditorScene.Stop();
		return new { playing = false };
	}

	[McpTool( "editor_is_playing", "Whether the editor is currently in play mode.", ToolCategory.Editor )]
	public static object IsPlaying()
	{
		return new { playing = SceneEditorSession.Active?.IsPlaying ?? false };
	}

	[McpTool( "session_info", "Play-session identity and timing - use it to tell restarts apart (play clones reuse the editor's GUIDs, so 'did the scene restart?' is otherwise a guess): whether play mode is running, when the current play session started, a play-session counter, when code last hot-reloaded, and when the MCP server started.", ToolCategory.Editor )]
	public static object SessionInfo()
	{
		string Stamp( System.DateTime? t ) => t?.ToString( "yyyy-MM-dd HH:mm:ss" );

		return new
		{
			playing = SboxMcp.Integration.SessionTracker.IsPlaying,
			playSessionCount = SboxMcp.Integration.SessionTracker.PlaySessionCount,
			playStartedAt = Stamp( SboxMcp.Integration.SessionTracker.PlayStartedAt ),
			lastHotloadAt = Stamp( SboxMcp.Integration.SessionTracker.LastHotloadAt ),
			serverStartedAt = Stamp( SboxMcp.Integration.SessionTracker.ServerStartedAt )
		};
	}

	[McpTool( "perf_get_stats", "Measures the frame rate over a short window (by sampling the editor frame counter) and reports FPS + average frame time - use it to quantitatively confirm a perf fix (e.g. removing debug-draw overdraw) instead of eyeballing sphere counts. During play this reflects the running game's tick loop.", ToolCategory.Editor )]
	public static async Task<object> PerfGetStats(
		[Desc( "Measurement window in seconds (0.2-10)" )] double seconds = 1.0 )
	{
		seconds = Math.Clamp( seconds, 0.2, 10 );

		var startFrames = SessionTracker.FrameCount;
		var startTime = DateTime.Now;
		await Task.Delay( (int)(seconds * 1000) );
		var elapsed = (DateTime.Now - startTime).TotalSeconds;
		var frames = SessionTracker.FrameCount - startFrames;
		var fps = elapsed > 0 ? frames / elapsed : 0;

		return (object)new
		{
			fps = Math.Round( fps, 1 ),
			frameTimeMs = fps > 0 ? (object)Math.Round( 1000.0 / fps, 2 ) : null,
			frames,
			windowSeconds = Math.Round( elapsed, 2 ),
			playing = SessionTracker.IsPlaying,
			note = "FPS is the editor frame loop (which is the game tick loop during play). GPU draw-call counters aren't exposed by the editor API. Measure before and after a change to compare."
		};
	}

	[McpTool( "editor_run_console_command", "Runs an editor console command (e.g. 'clear', convars).", ToolCategory.Editor, Writes = true )]
	public static object RunConsoleCommand( [Desc( "The console command line to run" )] string command )
	{
		Editor.ConsoleSystem.Run( command );
		return new { ran = command, note = "check editor_get_logs for output" };
	}

	[McpTool( "convar_get", "Reads a console variable's value (game/engine settings).", ToolCategory.Editor )]
	public static object ConVarGet( [Desc( "ConVar name, e.g. 'sv_gravity'" )] string name )
	{
		var value = Sandbox.ConsoleSystem.GetValue( name, null );
		if ( value is null )
			throw new InvalidOperationException( $"No console variable '{name}' - check the exact name with editor_run_console_command 'find {name}'" );

		return new { name, value };
	}

	[McpTool( "convar_set", "Sets a console variable's value.", ToolCategory.Editor, Writes = true )]
	public static object ConVarSet(
		[Desc( "ConVar name" )] string name,
		[Desc( "New value (string)" )] string value )
	{
		Sandbox.ConsoleSystem.SetValue( name, value );
		return new { name, value = Sandbox.ConsoleSystem.GetValue( name, value ) };
	}

	[McpTool( "editor_get_project_info", "Gets the current project: title, ident, type, paths.", ToolCategory.Editor )]
	public static object GetProjectInfo()
	{
		var project = Project.Current
			?? throw new InvalidOperationException( "No project is loaded" );

		return new
		{
			title = project.Config?.Title,
			ident = project.Config?.Ident,
			org = project.Config?.Org,
			type = project.Config?.Type,
			rootPath = project.GetRootPath(),
			hasCode = project.HasCodePath(),
			hasEditorCode = project.HasEditorPath()
		};
	}

	[McpTool( "editor_get_selection", "Gets the GameObjects currently selected in the editor.", ToolCategory.Editor )]
	public static object GetSelection()
	{
		var session = RequireSession();
		var selected = session.Selection.OfType<GameObject>()
			.Select( o => new { id = o.Id, name = o.Name } )
			.ToArray();

		return new { count = selected.Length, selected };
	}
}
