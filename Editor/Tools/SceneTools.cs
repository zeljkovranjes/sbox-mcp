using System;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class SceneTools
{
	[McpTool( "scene_get_status", "Gets the active scene: name, play state, unsaved changes, object count.", ToolCategory.Scene )]
	public static object GetStatus()
	{
		var session = RequireSession();
		var scene = session.Scene;

		return new
		{
			name = scene.Name,
			isPlaying = session.IsPlaying,
			sceneTarget = ToolHelpers.SceneTargetMode ?? "active",
			hasUnsavedChanges = session.HasUnsavedChanges,
			objectCount = scene.GetAllObjects( false ).Count( o => o is not Sandbox.Scene ),
			selection = session.Selection.OfType<Sandbox.GameObject>().Select( o => new { id = o.Id, name = o.Name } ).ToArray()
		};
	}

	[McpTool( "scene_setup_basic", "Bootstraps a usable scene in the current scene: a ground plane (with a collider), a directional light, and a camera - so you can start building and playing immediately.", ToolCategory.Scene, Writes = true )]
	public static object SetupBasic(
		[Desc( "Ground size multiplier (scales a dev box)" )] float groundScale = 10f )
	{
		var session = RequireSession();
		var box = Model.Load( "models/dev/box.vmdl" );

		using var undo = session.UndoScope( "MCP: setup basic scene" ).WithGameObjectCreations().Push();

		var ground = session.Scene.CreateObject();
		ground.Name = "Ground";
		ground.LocalScale = new Vector3( groundScale, groundScale, 1f );
		ground.Components.Create<ModelRenderer>().Model = box;
		// A BoxCollider (primitive), NOT a ModelCollider: the dev box model has
		// no collision mesh, so a ModelCollider would leave the ground non-solid
		// and objects would fall straight through it.
		ground.Components.Create<BoxCollider>();

		var sun = session.Scene.CreateObject();
		sun.Name = "Sun";
		sun.WorldRotation = Rotation.From( 60, 45, 0 );
		sun.Components.Create<DirectionalLight>();

		var cam = session.Scene.CreateObject();
		cam.Name = "Camera";
		cam.WorldPosition = new Vector3( -350, 0, 200 );
		cam.WorldRotation = Rotation.From( 25, 0, 0 );
		cam.Components.Create<CameraComponent>().FieldOfView = 70f;

		return new { created = new[] { "Ground", "Sun", "Camera" }, note = "ground has a collider; a directional light and camera are set - ready to build and play" };
	}

	[McpTool( "scene_diff", "Compares the in-memory editor scene to its saved .scene file on disk: reports unsaved changes and which top-level GameObjects were added or removed since the last save. Review it before scene_save to catch an accidental overwrite (e.g. saving over the wrong scene) and to make deliberate saves reviewable.", ToolCategory.Scene )]
	public static object SceneDiff()
	{
		var session = RequireSession();
		var scene = session.Scene;

		var memObjects = scene.Children.Where( o => o is not Sandbox.Scene ).Select( o => o.Name ).ToArray();

		string scenePath = null;
		string[] diskObjects = null;
		string diskNote = null;

		try
		{
			scenePath = scene.Source?.ResourcePath;
			var file = string.IsNullOrEmpty( scenePath ) ? null : AssetSystem.FindByPath( scenePath )?.GetSourceFile( true );

			if ( !string.IsNullOrEmpty( file ) && File.Exists( file ) )
			{
				using var doc = System.Text.Json.JsonDocument.Parse( File.ReadAllText( file ) );
				if ( doc.RootElement.TryGetProperty( "GameObjects", out var arr ) && arr.ValueKind == System.Text.Json.JsonValueKind.Array )
				{
					diskObjects = arr.EnumerateArray()
						.Select( e => e.TryGetProperty( "Name", out var n ) ? n.GetString() : null )
						.Where( n => n is not null )
						.ToArray();
				}
			}
			else
			{
				diskNote = "scene has not been saved to disk yet (or its source file was not found)";
			}
		}
		catch ( Exception e )
		{
			diskNote = "could not read/parse the disk scene: " + e.Message;
		}

		var added = diskObjects is null ? null : memObjects.Except( diskObjects ).ToArray();
		var removed = diskObjects is null ? null : diskObjects.Except( memObjects ).ToArray();

		return new
		{
			scene = scene.Name,
			scenePath,
			hasUnsavedChanges = session.HasUnsavedChanges,
			inMemoryObjects = memObjects.Length,
			onDiskObjects = diskObjects?.Length,
			addedSinceSave = added,
			removedSinceSave = removed,
			note = diskNote ?? (session.HasUnsavedChanges
				? "In-memory scene differs from disk - scene_save to persist (or you may lose these changes on restart)."
				: "In-memory scene matches the last save.")
		};
	}

	[McpTool( "scene_target", "Chooses which scene the object/component tools act on while PLAY mode is running: 'editor' = the persistent edit scene (plant a toggle/route/prop that survives Stop and restarts - the fix for losing objects to restarts), 'play' = the live throwaway play clone, 'active' (default) = whatever is focused. Set 'editor' before planting persistent objects during play, then reset to 'active'. No effect when not playing.", ToolCategory.Scene, Writes = true )]
	public static object SetSceneTarget( [Desc( "'editor', 'play', or 'active'" )] string target = "active" )
	{
		var t = (target ?? "active").ToLowerInvariant();
		if ( t is not ("editor" or "play" or "active") )
			throw new ArgumentException( "target must be 'editor', 'play', or 'active'" );

		ToolHelpers.SceneTargetMode = t == "active" ? null : t;

		var resolved = RequireSession();
		return new
		{
			target = t,
			resolvedScene = resolved.Scene?.Name,
			resolvedIsPlaying = resolved.IsPlaying,
			note = "Applies to subsequent object/component tools until changed. Reset to 'active' when done."
		};
	}

	[McpTool( "scene_load_map", "Imports a map into the scene by creating a GameObject with a MapInstance component - loads Hammer/Source2 .vmap geometry as a level. Set mapName to a map asset path like 'maps/mylevel.vmap' (find them with asset_search assetType 'vmap').", ToolCategory.Scene, Writes = true )]
	public static object LoadMap(
		[Desc( "Map asset name/path, e.g. 'maps/mylevel.vmap'" )] string mapName,
		[Desc( "Name for the map GameObject" )] string objectName = "Map",
		[Desc( "World origin [x, y, z] for the map" )] float[] position = null )
	{
		if ( string.IsNullOrWhiteSpace( mapName ) )
			throw new ArgumentException( "mapName is required (e.g. 'maps/mylevel.vmap')" );

		var session = RequireSession();

		using var undo = session.UndoScope( "MCP: load map" ).WithGameObjectCreations().Push();

		var go = session.Scene.CreateObject();
		go.Name = string.IsNullOrWhiteSpace( objectName ) ? "Map" : objectName;

		if ( position is not null )
			go.WorldPosition = ToVector3( position, "position" );

		var map = go.Components.Create<MapInstance>();
		map.MapName = mapName;

		return new { loaded = mapName, gameObject = go.Name, id = go.Id, isLoaded = map.IsLoaded };
	}

	[McpTool( "scene_add_asset", "Adds any asset to the scene, dispatching by type: a model (.vmdl) -> GameObject with a ModelRenderer; a prefab (.prefab) -> instantiated; a map (.vmap) -> GameObject with a MapInstance. The one-call 'put this asset in the scene'. For materials/textures/sounds (which aren't scene objects), apply them to a component instead.", ToolCategory.Asset, Writes = true )]
	public static object AddAsset(
		[Desc( "Asset path, e.g. 'models/x.vmdl', 'prefabs/y.prefab', 'maps/z.vmap'" )] string path,
		[Desc( "Object name; defaults to the asset's file name" )] string name = null,
		[Desc( "World position [x, y, z]" )] float[] position = null )
	{
		if ( AssetSystem.FindByPath( path ) is null )
			throw new InvalidOperationException( $"No asset at '{path}' - use asset_search to find it" );

		var session = RequireSession();
		var pos = position is null ? Vector3.Zero : ToVector3( position, "position" );
		var displayName = string.IsNullOrWhiteSpace( name ) ? Path.GetFileNameWithoutExtension( path ) : name;
		var ext = Path.GetExtension( path ).ToLowerInvariant();

		using var undo = session.UndoScope( "MCP: add asset" ).WithGameObjectCreations().Push();

		switch ( ext )
		{
			case ".vmdl":
			{
				var go = session.Scene.CreateObject();
				go.Name = displayName;
				go.WorldPosition = pos;
				go.Components.Create<ModelRenderer>().Model = Model.Load( path );
				return new { added = "model", gameObject = go.Name, id = go.Id };
			}
			case ".prefab":
			{
				var prefabFile = ResourceLibrary.Get<PrefabFile>( path )
					?? throw new InvalidOperationException( $"Prefab '{path}' could not be loaded" );
				var prefabScene = SceneUtility.GetPrefabScene( prefabFile )
					?? throw new InvalidOperationException( $"Prefab '{path}' could not be loaded" );
				var instance = prefabScene.Clone( new Transform( pos ) );
				return new { added = "prefab", gameObject = instance.Name, id = instance.Id };
			}
			case ".vmap":
			{
				var go = session.Scene.CreateObject();
				go.Name = displayName;
				go.WorldPosition = pos;
				go.Components.Create<MapInstance>().MapName = path;
				return new { added = "map", gameObject = go.Name, id = go.Id };
			}
			default:
				throw new InvalidOperationException(
					$"Don't know how to add a '{ext}' asset as a scene object. Supported: .vmdl (model), .prefab, .vmap (map). Materials/textures/sounds are applied to components (material_create, component_set_property, sound_play), not added as objects." );
		}
	}

	[McpTool( "navmesh_generate", "Enables and bakes the scene's NavMesh from its static/ground colliders so NPCs and enemies can pathfind. Set the agent size to match your characters. Run after the level geometry exists.", ToolCategory.Scene, Writes = true )]
	public static object NavMeshGenerate(
		[Desc( "Agent radius (character half-width)" )] float agentRadius = 16f,
		[Desc( "Agent height" )] float agentHeight = 72f,
		[Desc( "Max step height the agent can climb" )] float agentStepSize = 18f )
	{
		var scene = RequireScene();
		var nav = scene.NavMesh
			?? throw new InvalidOperationException( "This scene has no NavMesh object" );

		nav.IsEnabled = true;
		nav.AgentRadius = agentRadius;
		nav.AgentHeight = agentHeight;
		nav.AgentStepSize = agentStepSize;
		nav.Generate( scene.PhysicsWorld );

		return new
		{
			enabled = true,
			agentRadius,
			agentHeight,
			isGenerating = nav.IsGenerating,
			note = "generation may finish asynchronously; query paths with navmesh_find_path"
		};
	}

	[McpTool( "navmesh_find_path", "Finds a navigation path between two world points on the scene's NavMesh (for NPC/enemy movement) - returns the waypoints. Requires navmesh_generate first.", ToolCategory.Scene )]
	public static object NavMeshFindPath(
		[Desc( "Start point [x, y, z]" )] float[] from,
		[Desc( "Destination point [x, y, z]" )] float[] to )
	{
		var scene = RequireScene();
		var nav = scene.NavMesh;
		if ( nav is null || !nav.IsEnabled )
			throw new InvalidOperationException( "The scene's NavMesh is not enabled - call navmesh_generate first" );

		var target = ToVector3( to, "to" );
		var path = nav.CalculatePath( new Sandbox.Navigation.CalculatePathRequest
		{
			Start = ToVector3( from, "from" ),
			Target = target
		} );

		var points = path.Points is null ? Array.Empty<float[]>() : path.Points.Select( p => V( p.Position ) ).ToArray();
		var reaches = path.Status == Sandbox.Navigation.NavMeshPathStatus.Complete;

		// a Partial path's LAST waypoint is the closest reachable point, which the
		// engine leaves short of the target - callers must gate on `reaches`, never
		// on distance-to-last-point, or they'll treat unreachable targets as reached
		var lastPos = points.Length > 0 ? path.Points.Last().Position : (Vector3?)null;
		var endsAt = lastPos.HasValue ? V( lastPos.Value ) : null;
		var gap = lastPos.HasValue ? Vector3.DistanceBetween( lastPos.Value, target ) : (float?)null;

		return new
		{
			reaches,                 // TRUE only when the target is actually reachable
			found = reaches,         // kept for back-compat
			status = path.Status.ToString(),
			waypoints = points.Length,
			endsAt,                  // real endpoint of the path (may be short of the target)
			requestedEnd = V( target ),
			endpointGap = gap.HasValue ? (object)Math.Round( gap.Value, 2 ) : null,
			points,
			note = reaches
				? null
				: "PARTIAL/failed path: the target is NOT reachable. 'endsAt' is the closest reachable point (endpointGap units short) - do not treat it as the destination. Gate movement/AI logic on 'reaches'."
		};
	}

	static Sandbox.Navigation.NavMesh RequireNav()
	{
		var nav = RequireScene().NavMesh;
		if ( nav is null || !nav.IsEnabled )
			throw new InvalidOperationException( "The scene's NavMesh is not enabled - call navmesh_generate first" );

		return nav;
	}

	[McpTool( "navmesh_random_point", "Returns a random reachable point on the scene's NavMesh - for AI wander targets. Optionally sampled near a position within a radius. Requires navmesh_generate first.", ToolCategory.Scene )]
	public static object NavMeshRandomPoint(
		[Desc( "Center to sample near [x, y, z]; omit for anywhere on the navmesh" )] float[] near = null,
		[Desc( "Sample radius around 'near'" )] float radius = 500f )
	{
		var nav = RequireNav();
		var point = near is not null ? nav.GetRandomPoint( ToVector3( near, "near" ), radius ) : nav.GetRandomPoint();

		return point is null
			? new { found = false, point = (float[])null }
			: new { found = true, point = V( point.Value ) };
	}

	[McpTool( "navmesh_closest_point", "Snaps a world point to the nearest point on the scene's NavMesh within a radius (clamp a spawn/target onto walkable ground). Requires navmesh_generate first.", ToolCategory.Scene )]
	public static object NavMeshClosestPoint(
		[Desc( "World point [x, y, z]" )] float[] position,
		[Desc( "Search radius" )] float radius = 200f )
	{
		var nav = RequireNav();
		var point = nav.GetClosestPoint( ToVector3( position, "position" ), radius );

		return point is null
			? new { found = false, point = (float[])null }
			: new { found = true, point = V( point.Value ) };
	}

	[McpTool( "scene_get_hierarchy", "Gets the scene's GameObject tree with ids, names and component types.", ToolCategory.Scene )]
	public static object GetHierarchy(
		[Desc( "How many levels deep to expand" )] int maxDepth = 4,
		[Desc( "Id of a GameObject to use as the root; omit for the whole scene" )] string rootId = null )
	{
		if ( rootId is not null )
			return DescribeTree( FindGameObject( rootId ), maxDepth );

		var scene = RequireScene();
		return new
		{
			scene = scene.Name,
			objects = scene.Children.Select( c => DescribeTree( c, maxDepth - 1 ) ).ToArray()
		};
	}

	[McpTool( "scene_create", "Creates a new scene (with a camera and a light) and makes it active. Save it with scene_save_as.", ToolCategory.Scene, Writes = true )]
	public static object Create()
	{
		var session = SceneEditorSession.CreateDefault();
		session.MakeActive();
		return new { created = session.Scene.Name, note = "unsaved - use scene_save_as to write it to disk" };
	}

	[McpTool( "scene_open", "Opens a scene (or prefab) from disk in the editor and makes it active.", ToolCategory.Scene )]
	public static object Open( [Desc( "Scene asset path, e.g. 'scenes/minimal.scene'" )] string scenePath )
	{
		var session = SceneEditorSession.CreateFromPath( scenePath )
			?? throw new InvalidOperationException( $"No scene at '{scenePath}' - use scene_list" );

		session.MakeActive();
		return new { opened = session.Scene.Name };
	}

	[McpTool( "scene_list", "Lists all scene assets in the project.", ToolCategory.Scene )]
	public static object List()
	{
		var scenes = AssetSystem.All
			.Where( a => string.Equals( a.AssetType?.FileExtension, "scene", StringComparison.OrdinalIgnoreCase ) )
			.Select( a => a.Path )
			.OrderBy( p => p )
			.ToArray();

		return new { count = scenes.Length, scenes };
	}

	[McpTool( "scene_save", "Saves the active scene to disk. Fails for never-saved scenes - use scene_save_as for those.", ToolCategory.Scene, Writes = true )]
	public static object Save()
	{
		var session = RequireSession();

		if ( session.IsPlaying )
			throw new InvalidOperationException( "Cannot save while playing - editor_stop first (play-mode changes are discarded by design)" );

		if ( session.Scene.Source is null )
			throw new InvalidOperationException( "This scene has never been saved - use scene_save_as with a path" );

		session.Save( false );
		return new { saved = true, scene = session.Scene.Name };
	}

	[McpTool( "scene_save_as", "Saves the active scene to a new path under Assets/ (works for never-saved scenes).", ToolCategory.Scene, Writes = true )]
	public static object SaveAs( [Desc( "Assets-relative path ending in .scene, e.g. 'scenes/level1.scene'" )] string scenePath )
	{
		var session = RequireSession();
		var scene = session.Scene;

		if ( session.IsPlaying )
			throw new InvalidOperationException( "Cannot save while playing - editor_stop first (play-mode changes are discarded by design)" );

		if ( scene is PrefabScene )
			throw new InvalidOperationException( "The active session is a prefab - prefabs save with scene_save, or use prefab_create_from_gameobject" );

		if ( !scenePath.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "scenePath must end in .scene" );

		var absolute = AssetTools.ResolveNewAssetPath( scenePath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolute ) );

		var asset = AssetSystem.CreateResource( "scene", absolute )
			?? throw new InvalidOperationException( $"Could not create a scene resource at '{scenePath}' - is the path inside the project?" );

		// mirror of SceneEditorSession.Save: Scene.CreateSceneFile() is internal,
		// so reach it via reflection (same flow the editor's own Ctrl+S runs)
		var createSceneFile = typeof( Scene ).GetMethod( "CreateSceneFile",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )
			?? throw new InvalidOperationException( "Scene.CreateSceneFile not found - the engine changed; report this" );

		var resource = (Sandbox.GameResource)createSceneFile.Invoke( scene, null );
		asset.SaveToDisk( resource );

		// Scene.Source's setter is internal - reflection again, matching the editor's save flow
		typeof( Scene ).GetProperty( "Source" )?.SetValue( scene, resource );
		scene.Name = System.IO.Path.GetFileNameWithoutExtension( absolute );
		session.HasUnsavedChanges = false;

		return new { saved = asset.Path };
	}

	[McpTool( "scene_undo", "Undoes the last editor action.", ToolCategory.Scene, Writes = true )]
	public static object Undo()
	{
		var ok = RequireSession().UndoSystem.Undo();
		return new { undone = ok };
	}

	[McpTool( "scene_redo", "Redoes the last undone editor action.", ToolCategory.Scene, Writes = true )]
	public static object Redo()
	{
		var ok = RequireSession().UndoSystem.Redo();
		return new { redone = ok };
	}
}
