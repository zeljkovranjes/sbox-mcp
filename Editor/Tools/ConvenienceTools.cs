using System;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using SboxMcp.Server;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// Convenience tools for common multi-step actions.
/// </summary>
public static class ConvenienceTools
{
	[McpTool( "component_find_all", "Finds every GameObject in the scene that has a given component type (e.g. all lights, all colliders).", ToolCategory.Component )]
	public static object FindAll( [Desc( "Component type name, e.g. 'PointLight'" )] string type, int max = 200 )
	{
		var scene = RequireScene();
		var wanted = FindComponentType( type ).TargetType;

		var hits = scene.GetAllObjects( false )
			.Where( o => o is not Scene )
			.SelectMany( o => o.Components.GetAll<Component>( FindMode.EverythingInSelf )
				.Where( c => wanted.IsInstanceOfType( c ) )
				.Select( c => new { id = o.Id, name = o.Name } ) )
			.Take( max )
			.ToArray();

		return new { type = wanted.Name, count = hits.Length, objects = hits };
	}

	[McpTool( "gameobject_move_by", "Moves a GameObject by a relative offset (adds to its current position).", ToolCategory.GameObject, Writes = true )]
	public static object MoveBy(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Offset [x, y, z] in world space" )] float[] offset )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: move" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.WorldPosition += ToVector3( offset, "offset" );

		return new { gameObject = go.Name, position = V( go.WorldPosition ) };
	}

	[McpTool( "gameobject_rotate_by", "Rotates a GameObject by relative angles (adds to its current rotation).", ToolCategory.GameObject, Writes = true )]
	public static object RotateBy(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Delta angles [pitch, yaw, roll] in degrees" )] float[] angles )
	{
		if ( angles is null || angles.Length != 3 )
			throw new ArgumentException( "angles must be [pitch, yaw, roll]" );

		var session = RequireSession();
		var go = FindGameObject( gameObject );

		using var undo = session.UndoScope( "MCP: rotate" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push();
		go.WorldRotation *= Rotation.From( angles[0], angles[1], angles[2] );

		return new { gameObject = go.Name, rotation = A( go.WorldRotation ) };
	}

	[McpTool( "gameobject_group", "Groups GameObjects under a new empty parent (keeps their world positions).", ToolCategory.GameObject, Writes = true )]
	public static object Group(
		[Desc( "Name for the new group object" )] string groupName,
		[Desc( "GameObject ids or unique names to put under it" )] string[] gameObjects )
	{
		if ( gameObjects is null || gameObjects.Length == 0 )
			throw new ArgumentException( "Pass at least one GameObject" );

		var session = RequireSession();
		var members = gameObjects.Select( FindGameObject ).ToList();

		using var undo = session.UndoScope( $"MCP: group {groupName}" ).WithGameObjectCreations()
			.WithGameObjectChanges( members, GameObjectUndoFlags.All ).Push();

		var group = session.Scene.CreateObject();
		group.Name = string.IsNullOrWhiteSpace( groupName ) ? "Group" : groupName;
		group.WorldPosition = members.Aggregate( Vector3.Zero, ( acc, m ) => acc + m.WorldPosition ) / members.Count;

		foreach ( var m in members )
			m.SetParent( group, keepWorldPosition: true );

		return new { group = group.Name, id = group.Id, members = members.Select( m => m.Name ).ToArray() };
	}

	[McpTool( "asset_delete", "Deletes an asset file from the project. This is not undoable - be sure.", ToolCategory.Asset, Writes = true )]
	public static object DeleteAsset( [Desc( "Asset path, e.g. 'materials/old.vmat'" )] string path )
	{
		var asset = AssetSystem.FindByPath( path )
			?? throw new InvalidOperationException( $"No asset at '{path}' - use asset_search" );

		var name = asset.Path;
		asset.Delete();
		return new { deleted = name, note = "Asset deletion is permanent (not on the undo stack)." };
	}

	[McpTool( "screenshot_to_file", "Renders the scene from a viewpoint and SAVES it as a png in the project (instead of returning the image). Good for building reference images.", ToolCategory.Editor, Writes = true )]
	public static object ScreenshotToFile(
		[Desc( "Output path ending in .png, e.g. 'screenshots/level.png'" )] string path,
		[Desc( "Camera world position [x, y, z]" )] float[] position,
		[Desc( "GameObject id/name to aim at; or use rotation" )] string lookAt = null,
		[Desc( "Rotation [pitch, yaw, roll] if not using lookAt" )] float[] rotation = null,
		int width = 1280,
		int height = 720 )
	{
		if ( !path.EndsWith( ".png", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "path must end in .png" );

		var session = RequireSession();
		var scene = session.Scene;

		width = Math.Clamp( width, 64, 4096 );
		height = Math.Clamp( height, 64, 4096 );

		var go = scene.CreateObject();
		try
		{
			go.Name = "__mcp_temp_camera";
			go.WorldPosition = ToVector3( position, "position" );

			if ( lookAt is not null )
			{
				var target = FindGameObject( lookAt ).WorldPosition;
				go.WorldRotation = Rotation.LookAt( (target - go.WorldPosition).Normal );
			}
			else if ( rotation is not null && rotation.Length == 3 )
			{
				go.WorldRotation = Rotation.From( rotation[0], rotation[1], rotation[2] );
			}

			var camera = go.Components.Create<CameraComponent>();
			var pixmap = new Pixmap( width, height );
			if ( !camera.RenderToPixmap( pixmap ) )
				throw new InvalidOperationException( "Rendering failed - check editor_get_logs and verify the position is valid" );

			var absolute = AssetTools.ResolveNewAssetPath( path );
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolute ) );
			pixmap.SavePng( absolute );

			return new { saved = path, width, height };
		}
		finally
		{
			go.Destroy();
		}
	}
}
