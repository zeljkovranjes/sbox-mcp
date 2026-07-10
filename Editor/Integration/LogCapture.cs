using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sandbox;

namespace SboxMcp.Integration;

public sealed class CapturedLog
{
	public long Seq { get; init; }
	public DateTime Time { get; init; } = DateTime.Now;
	public string Level { get; init; }
	public string Logger { get; init; }
	public string Message { get; init; }
	public string Stack { get; init; }
	public bool IsDiagnostic { get; init; }
}

/// <summary>
/// Subscribes to the engine log stream so tools can read recent console
/// output (including compile diagnostics, which the editor logs). Each entry
/// gets a monotonic sequence number so callers can poll incrementally with a
/// "since" cursor instead of re-reading old entries.
/// </summary>
public static class LogCapture
{
	const int Capacity = 4000;

	static readonly LinkedList<CapturedLog> _logs = new();
	static long _nextSeq;
	static bool _hooked;

	public static void Start()
	{
		if ( _hooked )
			return;

		_hooked = true;
		Editor.EditorUtility.AddLogger( OnMessage );
	}

	public static void Stop()
	{
		if ( !_hooked )
			return;

		_hooked = false;
		Editor.EditorUtility.RemoveLogger( OnMessage );
	}

	/// <summary>The sequence number of the newest captured entry (0 if none).
	/// Pass it back as `sinceSeq` next call to get only what's new.</summary>
	public static long LatestSeq
	{
		get { lock ( _logs ) return _nextSeq; }
	}

	static void OnMessage( LogEvent ev )
	{
		lock ( _logs )
		{
			var entry = new CapturedLog
			{
				Seq = ++_nextSeq,
				Level = ev.Level.ToString(),
				Logger = ev.Logger,
				Message = ev.Message,
				Stack = ev.Stack,
				IsDiagnostic = ev.IsDiagnostic
			};

			_logs.AddFirst( entry );
			while ( _logs.Count > Capacity )
				_logs.RemoveLast();
		}
	}

	/// <summary>Newest-first recent entries, optionally only those newer than
	/// <paramref name="sinceSeq"/> (the incremental cursor).</summary>
	public static IReadOnlyList<CapturedLog> Recent( int count, string minLevel = null, bool diagnosticsOnly = false, long sinceSeq = 0 )
	{
		var threshold = Rank( minLevel );

		lock ( _logs )
		{
			return _logs
				.Where( l => l.Seq > sinceSeq )
				.Where( l => Rank( l.Level ) >= threshold )
				.Where( l => !diagnosticsOnly || l.IsDiagnostic )
				.Take( count )
				.ToArray();
		}
	}

	/// <summary>Regex/severity/time-filtered search over the buffer.</summary>
	public static IReadOnlyList<CapturedLog> Search( string pattern, string minLevel, int max, DateTime? since )
	{
		var threshold = Rank( minLevel );
		Regex rx = string.IsNullOrEmpty( pattern ) ? null : new Regex( pattern, RegexOptions.IgnoreCase );

		lock ( _logs )
		{
			return _logs
				.Where( l => Rank( l.Level ) >= threshold )
				.Where( l => since is null || l.Time >= since.Value )
				.Where( l => rx is null || (l.Message is not null && rx.IsMatch( l.Message )) )
				.Take( max )
				.ToArray();
		}
	}

	public static void Clear()
	{
		lock ( _logs ) _logs.Clear();
	}

	static int Rank( string level ) => level?.ToLowerInvariant() switch
	{
		"error" => 4,
		"warn" or "warning" => 3,
		"info" => 2,
		"debug" or "trace" => 1,
		_ => 0
	};
}
