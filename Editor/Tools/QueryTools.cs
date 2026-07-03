using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// Lightweight read queries and simple transform helpers.
/// </summary>
public static class QueryTools
{
	[McpTool( "gameobject_get_children", "Lists a GameObject's direct children.", ToolCategory.GameObject )]
	public static object GetChildren( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );
		return new
		{
			gameObject = go.Name,
			count = go.Children.Count,
			children = go.Children.Select( c => new { id = c.Id, name = c.Name, enabled = c.Enabled } ).ToArray()
		};
	}

	[McpTool( "gameobject_get_parent", "Gets a GameObject's parent (null if it's at the scene root).", ToolCategory.GameObject )]
	public static object GetParent( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );
		var parent = go.Parent is Scene or null ? null : go.Parent;
		return new { gameObject = go.Name, parent = parent is null ? null : new { id = parent.Id, name = parent.Name } };
	}

	[McpTool( "component_list_on", "Lists the components on a GameObject with their type and enabled state.", ToolCategory.Component )]
	public static object ListOn( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );
		var components = go.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.Select( c => new { type = c.GetType().Name, fullName = c.GetType().FullName, enabled = c.Enabled } )
			.ToArray();

		return new { gameObject = go.Name, count = components.Length, components };
	}

	[McpTool( "scene_get_stats", "Summarizes the scene: total objects and a count of each component type in use.", ToolCategory.Scene )]
	public static object GetStats()
	{
		var scene = RequireScene();
		var objects = scene.GetAllObjects( false ).Where( o => o is not Scene ).ToList();

		var componentCounts = objects
			.SelectMany( o => o.Components.GetAll<Component>( FindMode.EverythingInSelf ) )
			.GroupBy( c => c.GetType().Name )
			.OrderByDescending( g => g.Count() )
			.ToDictionary( g => g.Key, g => g.Count() );

		return new
		{
			scene = scene.Name,
			objectCount = objects.Count,
			componentTypes = componentCounts.Count,
			components = componentCounts
		};
	}

	[McpTool( "gameobject_scale_by", "Multiplies a GameObject's local scale by a factor (uniform, or per-axis).", ToolCategory.GameObject, Writes = true )]
	public static object ScaleBy(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Uniform factor; ignored when perAxis is set" )] float factor = 1f,
		[Desc( "Per-axis factors [x, y, z]" )] float[] perAxis = null )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: scale" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.LocalScale *= perAxis is not null ? ToVector3( perAxis, "perAxis" ) : new Vector3( factor );

		return new { gameObject = go.Name, scale = V( go.LocalScale ) };
	}

	[McpTool( "gameobject_reset_transform", "Resets a GameObject's local transform to identity (origin, no rotation, scale 1).", ToolCategory.GameObject, Writes = true )]
	public static object ResetTransform( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: reset transform" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.LocalTransform = global::Transform.Zero;

		return new { gameObject = go.Name, reset = true };
	}

	[McpTool( "editor_select_all", "Selects every GameObject in the scene.", ToolCategory.Editor )]
	public static object SelectAll()
	{
		var session = RequireSession();
		session.Selection.Clear();
		foreach ( var go in session.Scene.GetAllObjects( false ).Where( o => o is not Scene ) )
			session.Selection.Add( go );

		return new { selected = session.Selection.OfType<GameObject>().Count() };
	}

	[McpTool( "editor_clear_selection", "Clears the editor selection.", ToolCategory.Editor )]
	public static object ClearSelection()
	{
		RequireSession().Selection.Clear();
		return new { cleared = true };
	}

	[McpTool( "gameobject_set_tags", "Replaces all of a GameObject's tags with the given set.", ToolCategory.GameObject, Writes = true )]
	public static object SetTags(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "The complete tag list (replaces existing)" )] string[] tags )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: set tags" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();

		go.Tags.RemoveAll();
		foreach ( var tag in tags ?? Array.Empty<string>() )
			go.Tags.Add( tag );

		return new { gameObject = go.Name, tags = go.Tags.TryGetAll().ToArray() };
	}

	[McpTool( "gameobject_distance", "Measures the distance between two GameObjects.", ToolCategory.GameObject )]
	public static object Distance(
		[Desc( "First GameObject id or unique name" )] string a,
		[Desc( "Second GameObject id or unique name" )] string b )
	{
		var ga = FindGameObject( a );
		var gb = FindGameObject( b );
		var d = Vector3.DistanceBetween( ga.WorldPosition, gb.WorldPosition );
		return new { from = ga.Name, to = gb.Name, distance = d, delta = V( gb.WorldPosition - ga.WorldPosition ) };
	}

	[McpTool( "gameobject_align", "Aligns GameObjects to a target position on the chosen axes (e.g. line them up on X). Unset axes keep each object's own value.", ToolCategory.GameObject, Writes = true )]
	public static object Align(
		[Desc( "GameObject ids or unique names to move" )] string[] gameObjects,
		[Desc( "Target position [x, y, z]" )] float[] target,
		[Desc( "Apply the target's X" )] bool x = false,
		[Desc( "Apply the target's Y" )] bool y = false,
		[Desc( "Apply the target's Z" )] bool z = false )
	{
		if ( gameObjects is null || gameObjects.Length == 0 )
			throw new ArgumentException( "Pass at least one GameObject" );
		if ( !x && !y && !z )
			throw new ArgumentException( "Set at least one of x/y/z to align on" );

		var session = RequireSession();
		var t = ToVector3( target, "target" );
		var objs = gameObjects.Select( FindGameObject ).ToList();

		using var undo = session.UndoScope( "MCP: align" )
			.WithGameObjectChanges( objs, GameObjectUndoFlags.Properties ).Push();

		foreach ( var go in objs )
		{
			var p = go.WorldPosition;
			go.WorldPosition = new Vector3( x ? t.x : p.x, y ? t.y : p.y, z ? t.z : p.z );
		}

		return new { aligned = objs.Select( o => o.Name ).ToArray(), axes = $"{(x?"x":"")}{(y?"y":"")}{(z?"z":"")}" };
	}
}
