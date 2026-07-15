using System;
using System.Linq;
using Editor;
using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Integration;

/// <summary>
/// Tracks play-session identity and hotload timing so tools (and the agents
/// driving them) can tell restarts apart - play clones reuse the editor GUIDs,
/// which otherwise makes "did the scene restart?" a forensic guess.
/// The play counter persists across hotloads via EditorCookie; the rest is
/// transient (main-thread only, like the frame hooks that maintain it).
/// </summary>
public static class SessionTracker
{
	static bool? _wasPlaying;

	public static DateTime ServerStartedAt { get; } = DateTime.Now;
	public static DateTime? PlayStartedAt { get; private set; }
	public static DateTime? LastHotloadAt { get; private set; }

	// check ALL sessions, not just Active: during HTTP-driven play the "active"
	// session can still be the editor one, so Active?.IsPlaying under-reports
	public static bool IsPlaying =>
		SceneEditorSession.All?.Any( s => s is not null && s.IsPlaying ) ?? false;

	/// <summary>Monotonic editor-frame counter (drives perf_get_stats' FPS calc).</summary>
	public static long FrameCount { get; private set; }

	/// <summary>How many times play mode has been entered this editor run
	/// (persisted so a hotload doesn't reset it mid-session).</summary>
	public static int PlaySessionCount
	{
		get => EditorCookie.Get( "SboxMcp.PlaySessionCount", 0 );
		private set => EditorCookie.Set( "SboxMcp.PlaySessionCount", value );
	}

	[EditorEvent.Frame]
	static void OnFrame()
	{
		FrameCount++;

		var playing = IsPlaying;

		// first frame after (re)load: sync without counting, so a hotload during
		// play doesn't register as a new session
		if ( _wasPlaying is null )
		{
			_wasPlaying = playing;
			return;
		}

		if ( playing && _wasPlaying == false )
		{
			PlaySessionCount++;
			PlayStartedAt = DateTime.Now;
		}
		else if ( !playing && _wasPlaying == true )
		{
			PlayStartedAt = null;
		}

		_wasPlaying = playing;
	}

	[EditorEvent.Hotload]
	static void OnHotload() => LastHotloadAt = DateTime.Now;
}
