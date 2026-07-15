using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;

namespace SboxMcp.Tools;

internal static class ToolHelpers
{
	/// <summary>Ambient scene target: "editor", "play", or null (= active/focused).
	/// Lets tools plant and modify objects in the PERSISTENT editor scene while play
	/// mode runs (so they survive Stop and restarts) instead of the throwaway play
	/// clone - the biggest single time-sink reported. Set via the scene_target tool.</summary>
	public static string SceneTargetMode;

	static SceneEditorSession PlaySession() =>
		SceneEditorSession.All?.FirstOrDefault( s => s is not null && s.IsPlaying );

	static SceneEditorSession EditorSession() =>
		SceneEditorSession.All?.FirstOrDefault( s => s is not null && !s.IsPlaying );

	static InvalidOperationException NoScene() =>
		new( "No scene is open in the editor - open or create a scene first" );

	public static SceneEditorSession RequireSession()
	{
		var play = PlaySession();
		var editor = EditorSession();

		if ( SceneTargetMode == "editor" )
			return editor ?? SceneEditorSession.Active ?? throw NoScene();

		if ( SceneTargetMode == "play" )
			return play ?? throw new InvalidOperationException(
				"scene_target is set to 'play' but play mode isn't running - editor_play first, or scene_target active." );

		// default: follow the LIVE game while it's playing so reads/writes hit the
		// ticking play-scene clone. Play clones reuse the editor object GUIDs, so a
		// by-guid lookup on the dormant editor scene silently returns a copy that
		// never ticks (the reported get_component_property/get_transform bug).
		return play ?? editor ?? SceneEditorSession.Active ?? throw NoScene();
	}

	public static Scene RequireScene()
	{
		var session = RequireSession();

		// Game.ActiveScene is the exact scene the engine ticks during play; prefer
		// it over the session's Scene so live values are read, unless the caller
		// explicitly targeted the editor scene
		if ( session.IsPlaying && SceneTargetMode != "editor" && Game.ActiveScene is not null )
			return Game.ActiveScene;

		return session.Scene
			?? throw new InvalidOperationException( "The active editor session has no scene" );
	}

	/// <summary>Normalizes a tool's JSON <c>args</c> element into a positional
	/// argument list. Accepts a real JSON array, a STRING that encodes a JSON array
	/// (MCP clients frequently stringify it), null/undefined (= no args), or a lone
	/// scalar/object (= a single argument). This is why multi-arg invoke calls used
	/// to fail with "no overload taking 0 arguments".</summary>
	public static JsonElement[] NormalizeArgs( JsonElement args )
	{
		switch ( args.ValueKind )
		{
			case JsonValueKind.Undefined:
			case JsonValueKind.Null:
				return Array.Empty<JsonElement>();

			case JsonValueKind.Array:
				return args.EnumerateArray().Select( e => e.Clone() ).ToArray();

			case JsonValueKind.String:
				var s = args.GetString();
				var trimmed = s?.TrimStart();
				if ( trimmed is not null && (trimmed.StartsWith( '[' ) || trimmed.StartsWith( '{' )) )
				{
					try
					{
						using var doc = JsonDocument.Parse( s );
						return doc.RootElement.ValueKind == JsonValueKind.Array
							? doc.RootElement.EnumerateArray().Select( e => e.Clone() ).ToArray()
							: new[] { doc.RootElement.Clone() };
					}
					catch { /* not actually JSON - treat as one literal string arg */ }
				}
				return new[] { args.Clone() };

			default:
				// a lone scalar (number/bool) or object → a single positional argument
				return new[] { args.Clone() };
		}
	}

	/// <summary>If the value is a Task/Task&lt;T&gt;, awaits it (with a timeout) and
	/// returns its result (or a completion marker for a non-generic Task); otherwise
	/// returns the value unchanged. Fixes async engine/game methods whose returned
	/// Task was previously handed back unawaited (you got "System.Threading.Tasks.Task`1[...]"
	/// instead of the actual result).</summary>
	public static async Task<object> AwaitIfTask( object value, int timeoutMs = 25000 )
	{
		if ( value is not Task task )
			return value;

		var finished = await Task.WhenAny( task, Task.Delay( timeoutMs ) );
		if ( finished != task )
			throw new InvalidOperationException(
				$"The method returned a Task that didn't complete within {timeoutMs / 1000}s - it may be long-running or awaiting something that never completes." );

		await task; // surface any exception thrown inside the task

		var tt = task.GetType();
		if ( tt.IsGenericType && tt.GetGenericTypeDefinition() == typeof( Task<> ) )
		{
			var inner = tt.GetProperty( "Result" )?.GetValue( task );
			// Task (non-generic) is compiled as Task<VoidTaskResult> - no real value
			return inner is null || inner.GetType().Name == "VoidTaskResult" ? "(task completed)" : inner;
		}

		return "(task completed)";
	}

	/// <summary>
	/// Resolves a GameObject by id (preferred) or by unique name.
	/// </summary>
	public static GameObject FindGameObject( string idOrName )
	{
		if ( string.IsNullOrWhiteSpace( idOrName ) )
			throw new ArgumentException( "GameObject id/name must not be empty" );

		var scene = RequireScene();

		if ( Guid.TryParse( idOrName, out var guid ) )
		{
			return scene.Directory.FindByGuid( guid )
				?? throw new InvalidOperationException( $"No GameObject with id '{idOrName}' - use gameobject_find to search" );
		}

		var matches = scene.GetAllObjects( false )
			.Where( o => o is not Scene )
			.Where( o => string.Equals( o.Name, idOrName, StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		return matches.Count switch
		{
			1 => matches[0],
			0 => throw new InvalidOperationException( $"No GameObject named '{idOrName}' - use gameobject_find to search" ),
			_ => throw new InvalidOperationException(
				$"{matches.Count} GameObjects are named '{idOrName}' - use an id instead: "
				+ string.Join( ", ", matches.Take( 5 ).Select( m => m.Id ) ) )
		};
	}

	public static Component FindComponent( GameObject go, string typeName )
	{
		var components = go.Components.GetAll<Component>( FindMode.EverythingInSelf ).ToList();

		var match = components.FirstOrDefault( c => string.Equals( c.GetType().FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? components.FirstOrDefault( c => string.Equals( c.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase ) );

		return match ?? throw new InvalidOperationException(
			$"'{go.Name}' has no component '{typeName}'. It has: "
			+ string.Join( ", ", components.Select( c => c.GetType().Name ) ) );
	}

	public static TypeDescription FindComponentType( string typeName )
	{
		var all = EditorTypeLibrary.GetTypes<Component>()
			.Where( t => !t.IsAbstract && !t.IsGenericType )
			.ToList();

		var match = all.FirstOrDefault( t => string.Equals( t.FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
			?? all.FirstOrDefault( t => string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) );

		if ( match is not null )
			return match;

		var close = all
			.Where( t => t.Name.Contains( typeName, StringComparison.OrdinalIgnoreCase ) )
			.Take( 8 )
			.Select( t => t.Name )
			.ToList();

		throw new InvalidOperationException( close.Count > 0
			? $"No component type '{typeName}'. Did you mean: {string.Join( ", ", close )}?"
			: $"No component type '{typeName}' - use component_list_types to search" );
	}

	public static Vector3 ToVector3( float[] v, string argName )
	{
		if ( v is null || v.Length != 3 )
			throw new ArgumentException( $"'{argName}' must be an array of 3 numbers [x, y, z]" );

		return new Vector3( v[0], v[1], v[2] );
	}

	public static float[] V( Vector3 v ) => new[] { v.x, v.y, v.z };

	public static float[] A( Rotation r )
	{
		var angles = r.Angles();
		return new[] { angles.pitch, angles.yaw, angles.roll };
	}

	public static object Describe( GameObject go ) => new
	{
		id = go.Id,
		name = go.Name,
		enabled = go.Enabled,
		position = V( go.WorldPosition ),
		components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.Select( c => c.GetType().Name ).ToArray(),
		childCount = go.Children.Count,
		isPrefabInstance = go.IsPrefabInstance
	};

	public static object DescribeTree( GameObject go, int depth )
	{
		var components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.Select( c => c.GetType().Name ).ToArray();

		return new
		{
			id = go.Id,
			name = go.Name,
			enabled = go.Enabled,
			components,
			children = depth <= 0
				? (object)$"{go.Children.Count} children (increase maxDepth to see them)"
				: go.Children.Select( c => DescribeTree( c, depth - 1 ) ).ToArray()
		};
	}
}
