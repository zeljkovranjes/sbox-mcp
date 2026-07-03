using System;
using System.Collections.Generic;
using System.Linq;
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

	[McpTool( "scene_trace_box", "Casts a box (volume) through the physics scene and reports the first hit - like scene_trace_ray but with thickness, good for 'would this fit / what's along this corridor'.", ToolCategory.Scene )]
	public static object TraceBox(
		[Desc( "Box half-extents [x, y, z]" )] float[] extents,
		[Desc( "Start point [x, y, z]" )] float[] from,
		[Desc( "End point [x, y, z]" )] float[] to )
	{
		var scene = RequireScene();
		var result = scene.Trace.Box( ToVector3( extents, "extents" ), ToVector3( from, "from" ), ToVector3( to, "to" ) ).Run();

		return new
		{
			hit = result.Hit,
			hitObject = result.GameObject is null ? null : new { id = result.GameObject.Id, name = result.GameObject.Name },
			point = V( result.EndPosition ),
			normal = V( result.Normal ),
			distance = result.Distance
		};
	}

	[McpTool( "scene_overlap_sphere", "Finds every object whose collider overlaps a sphere at a point - a proximity / area-of-effect query (explosion damage, AI perception, 'what is near here'). Returns the GameObjects found, nearest first.", ToolCategory.Scene )]
	public static object OverlapSphere(
		[Desc( "Sphere center [x, y, z]" )] float[] center,
		[Desc( "Sphere radius" )] float radius )
	{
		if ( radius <= 0 )
			throw new ArgumentException( "radius must be positive" );

		var scene = RequireScene();
		var c = ToVector3( center, "center" );
		var results = scene.Trace.Sphere( radius, c, c ).RunAll();

		var seen = new HashSet<Guid>();
		var objects = results
			.Where( r => r.GameObject is not null && seen.Add( r.GameObject.Id ) )
			.Select( r => new { id = r.GameObject.Id, name = r.GameObject.Name, distance = Vector3.DistanceBetween( c, r.GameObject.WorldPosition ) } )
			.OrderBy( o => o.distance )
			.ToArray();

		return new { center = V( c ), radius, count = objects.Length, objects };
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
