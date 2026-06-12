using System;
using System.Collections.Generic;
using System.Linq;
using SboxMcp.Registry;

namespace SboxMcp.Integration;

public sealed class ActivityRecord
{
	public DateTime Time { get; init; } = DateTime.Now;
	public string ToolName { get; init; }
	public ToolCategory Category { get; init; }
	public string ArgsDigest { get; init; }
	public bool Ok { get; init; }
	public string Error { get; init; }
	public long DurationMs { get; init; }
}

/// <summary>
/// Ring buffer of recent tool calls; the dock's activity feed renders it.
/// </summary>
public static class ActivityLog
{
	const int Capacity = 500;

	static readonly LinkedList<ActivityRecord> _records = new();

	/// <summary>Raised on the editor main thread after a record is added.</summary>
	public static event Action<ActivityRecord> Added;
	public static event Action Cleared;

	public static IReadOnlyList<ActivityRecord> Records
	{
		get { lock ( _records ) return _records.ToArray(); }
	}

	public static int TotalCalls { get; private set; }

	internal static void Record( ActivityRecord record )
	{
		lock ( _records )
		{
			_records.AddFirst( record );
			while ( _records.Count > Capacity )
				_records.RemoveLast();
			TotalCalls++;
		}

		Added?.Invoke( record );
	}

	public static void Clear()
	{
		lock ( _records ) _records.Clear();
		Cleared?.Invoke();
	}
}
