using System.Linq;
using Editor;
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
			hasUnsavedChanges = session.HasUnsavedChanges,
			objectCount = scene.GetAllObjects( false ).Count(),
			selection = session.Selection.OfType<Sandbox.GameObject>().Select( o => new { id = o.Id, name = o.Name } ).ToArray()
		};
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

	[McpTool( "scene_save", "Saves the active scene to disk.", ToolCategory.Scene, Writes = true )]
	public static object Save()
	{
		var session = RequireSession();
		session.Save( false );
		return new { saved = true, scene = session.Scene.Name };
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
