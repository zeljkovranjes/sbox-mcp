using System;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// Physics queries - lets the AI understand scene geometry (where the ground
/// is, what's in front of something, line-of-sight).
/// </summary>
public static class PhysicsTools
{
	[McpTool( "scene_trace_ray", "Casts a ray through the scene's physics and reports the first thing it hits (object, point, surface normal, distance). Use it to find the ground under a point, what an object is looking at, etc.", ToolCategory.Scene )]
	public static object TraceRay(
		[Desc( "Start point [x, y, z]" )] float[] from,
		[Desc( "End point [x, y, z]" )] float[] to,
		[Desc( "Sphere-cast radius; 0 = a thin ray" )] float radius = 0f )
	{
		var scene = RequireScene();
		var a = ToVector3( from, "from" );
		var b = ToVector3( to, "to" );

		var builder = radius > 0
			? scene.Trace.Sphere( radius, a, b )
			: scene.Trace.Ray( a, b );

		var result = builder.Run();

		return new
		{
			hit = result.Hit,
			hitObject = result.GameObject is null ? null : new { id = result.GameObject.Id, name = result.GameObject.Name },
			hitComponent = result.Component?.GetType().Name,
			point = V( result.EndPosition ),
			normal = V( result.Normal ),
			distance = result.Distance
		};
	}

	[McpTool( "gameobject_drop_to_ground", "Moves a GameObject straight down onto the first solid surface below it (like Blender's drop-to-floor).", ToolCategory.GameObject, Writes = true )]
	public static object DropToGround(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Start the downward trace this far above the object (to clear its own body)" )] float startOffset = 200f )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		var start = go.WorldPosition + Vector3.Up * startOffset;
		var result = session.Scene.Trace.Ray( start, start + Vector3.Down * 100000f )
			.IgnoreGameObjectHierarchy( go )
			.Run();

		if ( !result.Hit )
			return new { gameObject = go.Name, dropped = false, note = "nothing solid below - is there ground with a collider?" };

		using var undo = session.UndoScope( "MCP: drop to ground" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.WorldPosition = result.EndPosition;

		return new { gameObject = go.Name, dropped = true, position = V( go.WorldPosition ), groundObject = result.GameObject?.Name };
	}

	[McpTool( "scene_trace_down", "Finds the ground (or whatever solid surface) directly below a point by tracing downward. Handy for dropping objects onto the floor.", ToolCategory.Scene )]
	public static object TraceDown(
		[Desc( "Point to trace down from [x, y, z]" )] float[] from,
		[Desc( "How far down to look" )] float maxDistance = 10000f )
	{
		var scene = RequireScene();
		var a = ToVector3( from, "from" );
		var b = a + Vector3.Down * maxDistance;

		var result = scene.Trace.Ray( a, b ).Run();

		return new
		{
			hit = result.Hit,
			groundObject = result.GameObject is null ? null : new { id = result.GameObject.Id, name = result.GameObject.Name },
			groundPoint = result.Hit ? V( result.EndPosition ) : null,
			groundHeight = result.Hit ? result.EndPosition.z : (float?)null,
			normal = V( result.Normal )
		};
	}
}
