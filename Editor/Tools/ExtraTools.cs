using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// High-value concrete tools on top of the universal mechanisms: tags,
/// bounds, orientation, bulk creation, component copy.
/// </summary>
public static class ExtraTools
{
	[McpTool( "gameobject_add_tag", "Adds a tag to a GameObject (tags drive collision filtering, queries and gameplay logic).", ToolCategory.GameObject, Writes = true )]
	public static object AddTag( [Desc( "GameObject id or unique name" )] string gameObject, string tag )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( $"MCP: add tag {tag}" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.Tags.Add( tag );

		return new { gameObject = go.Name, tags = go.Tags.TryGetAll().ToArray() };
	}

	[McpTool( "gameobject_remove_tag", "Removes a tag from a GameObject.", ToolCategory.GameObject, Writes = true )]
	public static object RemoveTag( [Desc( "GameObject id or unique name" )] string gameObject, string tag )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( $"MCP: remove tag {tag}" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.Tags.Remove( tag );

		return new { gameObject = go.Name, tags = go.Tags.TryGetAll().ToArray() };
	}

	[McpTool( "gameobject_get_bounds", "Gets a GameObject's world-space bounding box (renderers + children).", ToolCategory.GameObject )]
	public static object GetBounds( [Desc( "GameObject id or unique name" )] string gameObject )
	{
		var go = FindGameObject( gameObject );
		var b = go.GetBounds();

		return new
		{
			gameObject = go.Name,
			center = V( b.Center ),
			size = V( b.Size ),
			mins = V( b.Mins ),
			maxs = V( b.Maxs )
		};
	}

	[McpTool( "gameobject_look_at", "Rotates a GameObject to face a target position or another GameObject.", ToolCategory.GameObject, Writes = true )]
	public static object LookAt(
		[Desc( "GameObject id or unique name to rotate" )] string gameObject,
		[Desc( "Target world position [x, y, z]; ignored when targetObject is set" )] float[] position = null,
		[Desc( "Target GameObject id/name to face" )] string targetObject = null )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		var target = targetObject is not null
			? FindGameObject( targetObject ).WorldPosition
			: position is not null ? ToVector3( position, "position" )
			: throw new ArgumentException( "Pass either position or targetObject" );

		using var undo = session.UndoScope( "MCP: look at" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.WorldRotation = Rotation.LookAt( (target - go.WorldPosition).Normal );

		return new { gameObject = go.Name, rotation = A( go.WorldRotation ) };
	}

	[McpTool( "gameobject_create_many", "Creates several GameObjects at once (e.g. a grid or row). Returns their ids.", ToolCategory.GameObject, Writes = true )]
	public static object CreateMany(
		[Desc( "Base name; each gets a numeric suffix" )] string name,
		[Desc( "How many to create" )] int count,
		[Desc( "Position of the first [x, y, z]" )] float[] startPosition = null,
		[Desc( "Offset added per object [x, y, z]" )] float[] step = null,
		[Desc( "Parent id; omit for scene root" )] string parentId = null )
	{
		if ( count is < 1 or > 512 )
			throw new ArgumentException( "count must be 1..512" );

		var session = RequireSession();
		var parent = parentId is null ? null : FindGameObject( parentId );
		var start = startPosition is null ? Vector3.Zero : ToVector3( startPosition, "startPosition" );
		var delta = step is null ? new Vector3( 60, 0, 0 ) : ToVector3( step, "step" );

		using var undo = session.UndoScope( $"MCP: create {count} objects" ).WithGameObjectCreations().Push();

		var created = new object[count];
		for ( var i = 0; i < count; i++ )
		{
			var go = session.Scene.CreateObject();
			go.Name = $"{name} {i + 1}";
			if ( parent is not null ) go.Parent = parent;
			go.WorldPosition = start + delta * i;
			created[i] = new { id = go.Id, name = go.Name };
		}

		return new { count, created };
	}

	[McpTool( "component_copy", "Copies all property values from one component to another GameObject's component of the same type (e.g. clone a configured renderer's settings). Creates the component on the target if it doesn't have one yet.", ToolCategory.Component, Writes = true )]
	public static object CopyComponent(
		[Desc( "Source GameObject id or unique name" )] string fromGameObject,
		[Desc( "Target GameObject id or unique name" )] string toGameObject,
		[Desc( "Component type name" )] string type )
	{
		var session = RequireSession();
		var source = FindComponent( FindGameObject( fromGameObject ), type );
		var toGo = FindGameObject( toGameObject );

		var existing = toGo.Components.GetAll<Component>( FindMode.EverythingInSelf )
			.FirstOrDefault( c => c.GetType() == source.GetType() );

		using var undo = session.UndoScope( $"MCP: copy {type}" )
			.WithComponentCreations()
			.WithComponentChanges( existing is not null ? new[] { existing } : Array.Empty<Component>() )
			.Push();

		// create a matching component on the target if it has none yet
		var target = existing ?? toGo.Components.Create( FindComponentType( type ) )
			?? throw new InvalidOperationException( $"Could not create a {type} on '{toGameObject}'" );

		if ( source.Serialize() is System.Text.Json.Nodes.JsonObject node )
		{
			// keep the target's own identity; copy only the values
			node.Remove( "__guid" );
			target.DeserializeImmediately( node );
		}

		return new { copied = type, from = fromGameObject, to = toGameObject, createdTarget = existing is null };
	}
}
