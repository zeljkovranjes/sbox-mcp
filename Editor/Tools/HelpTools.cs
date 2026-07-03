using System;
using System.Collections.Generic;
using System.Linq;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Workflow recipes: teaches connected AI clients how to COMPOSE the tools,
/// not just what each one does. `help` lists topics; `help(topic)` returns
/// the step-by-step recipe.
/// </summary>
public static class HelpTools
{
	static readonly Dictionary<string, (string Title, string Recipe)> Topics = new( StringComparer.OrdinalIgnoreCase )
	{
		["getting-started"] = ("Getting started",
			"1. scene_get_status - see what's open. No scene? scene_create or scene_open.\n"
			+ "2. scene_get_hierarchy - learn the scene's structure before changing it.\n"
			+ "3. Prefer ids over names when acting on objects; get them from the hierarchy.\n"
			+ "4. Every write is undoable; the user can revert any action from the Activity feed.\n"
			+ "5. When unsure a type or property exists: api_search / api_get_type. Never guess."),

		["make-a-player"] = ("Make a playable character",
			"1. gameobject_create name='Player' (position a little above the ground).\n"
			+ "2. component_add type='PlayerController' - it auto-creates the Rigidbody, collider and a body renderer.\n"
			+ "3. api_get_type 'PlayerController' to see all ~70 knobs; component_set_property for WalkSpeed / RunSpeed / JumpSpeed / ThirdPerson etc.\n"
			+ "4. The scene needs ground with a collider under the player, or they fall forever.\n"
			+ "5. editor_play to test; editor_get_logs for errors; editor_stop when done.\n"
			+ "Custom behavior: code_create_component (e.g. 'PlayerAbilities'), wait for hot-reload, component_add it."),

		["custom-component"] = ("Write a custom component (script)",
			"1. code_create_component className='MyThing' properties=['float Speed','GameObject Target'] - scaffolds Code/MyThing.cs.\n"
			+ "2. code_write_file to flesh out OnUpdate/OnStart logic (read the file first with code_read_file).\n"
			+ "3. Wait a moment for hot-reload, then code_get_compile_errors - fix until clean.\n"
			+ "4. component_add it to a GameObject; component_set_property to configure the [Property] fields.\n"
			+ "5. To verify logic without playing: add a public static test method and code_run_static_method it."),

		["materials-and-textures"] = ("Materials and textures",
			"1. Get a texture: texture_write (base64 image bytes -> png in the project) or cloud_search/cloud_install.\n"
			+ "2. material_create path='materials/thing.vmat' colorTexture='textures/thing_color.png' (+normal/roughness/metalness).\n"
			+ "3. Assign it: component_set_property on the renderer, property 'MaterialOverride', value the .vmat path.\n"
			+ "4. Full control over the format: asset_read_raw / asset_write_raw on the .vmat."),

		["sounds"] = ("Sound events",
			"1. Sound files (wav/mp3/ogg) must exist in the project - asset_search assetType='wav' or cloud_install some.\n"
			+ "2. soundevent_create path='sounds/jump.sound' soundFiles=['sounds/jump1.wav'] volume/pitch/ui.\n"
			+ "3. Play from code (Sound.Play(\"sounds/jump.sound\")) or a SoundPointComponent (component_add)."),

		["input-actions"] = ("Input actions",
			"1. input_list_actions - see what exists (Forward, Jump, attack1...).\n"
			+ "2. input_add_action name='Dash' keyboardCode='shift' group='Movement'.\n"
			+ "3. In code: Input.Pressed(\"Dash\") / Input.Down(\"Dash\") - applies on next play-mode entry.\n"
			+ "4. Actions live in ProjectSettings/Input.config; input_remove_action deletes."),

		["import-3d-models"] = ("Import and use 3D models",
			"1. Get a mesh: cloud_search 'crate' + cloud_install (installs ready .vmdl) - or an .fbx already in the project.\n"
			+ "2. For raw meshes: modeldoc_create_from_mesh meshAssetPath='models/thing.fbx' collision='Hull'.\n"
			+ "3. Use it: component_add 'ModelRenderer' + component_set_property 'Model' = the .vmdl path.\n"
			+ "4. Solid? Also component_add 'ModelCollider' with the same Model.\n"
			+ "5. Inspect/edit the model definition: modeldoc_get / modeldoc_set / modeldoc_add_physics."),

		["animation"] = ("Animation and AnimGraph",
			"1. Characters animate via SkinnedModelRenderer + an animgraph (.vanmgrph).\n"
			+ "2. animgraph_list_parameters path='models/x.vanmgrph' - the knobs code can drive.\n"
			+ "3. From code: renderer.Set(\"move_speed\", value) etc.\n"
			+ "4. Author/patch graphs: animgraph_get (json or raw kv3) then animgraph_set with full KV3; compile errors come back to you.\n"
			+ "5. Retarget mocap/animations from fbx/bvh: retargeter_add_files (needs the Humanoid Retargeter library)."),

		["scenes-and-prefabs"] = ("Scenes and prefabs",
			"1. scene_create (fresh, has camera+light) / scene_open / scene_save_as 'scenes/level1.scene'.\n"
			+ "2. Build a thing once, then prefab_create_from_gameobject -> reuse with prefab_instantiate.\n"
			+ "3. project_set_startup_scene decides what the game boots into.\n"
			+ "4. Multiple scenes = levels/menus; scene_list shows them all."),

		["testing-your-game"] = ("Test what you built",
			"1. editor_play - enters play mode (the scene runs for real).\n"
			+ "2. editor_get_logs minSeverity='warning' - runtime errors show here.\n"
			+ "3. editor_screenshot (through the game camera while playing) or editor_screenshot_from any angle in edit mode.\n"
			+ "4. code_run_static_method for programmatic assertions without playing.\n"
			+ "5. editor_stop. Play-mode changes are discarded on stop - edit in edit mode."),

		["discover-the-api"] = ("Discover any engine/project API",
			"1. api_search query='rigidbody' (componentsOnly=true to filter) - finds ANY type, engine or project.\n"
			+ "2. api_get_type 'Rigidbody' - real properties and method signatures.\n"
			+ "3. Components -> component_add / component_set_property. Static APIs / methods -> use them from code (code_write_file).\n"
			+ "4. This works for the project's own types too - search your game's classes the same way."),

		["safety-and-revert"] = ("Safety and reverting",
			"1. Every scene mutation runs in an undo scope - the user can Ctrl+Z or right-click the action in the Activity feed to revert it.\n"
			+ "2. Modes: the user may set approve-writes (writes wait for a click) or read-only (writes rejected).\n"
			+ "3. File writes are jailed to the project; describe destructive intents before doing them.\n"
			+ "4. Prefer small, verifiable steps: change -> screenshot/logs -> continue."),
	};

	[McpTool( "help", "How to use this server well: recipes for composing tools into game-building workflows. Call without arguments for the topic list; call with a topic for the step-by-step recipe. Read 'getting-started' first.", ToolCategory.Editor )]
	public static object Help(
		[Desc( "Topic name from the list; omit to list all topics" )] string topic = null )
	{
		if ( topic is null )
		{
			return new
			{
				note = "Call help(topic) for a step-by-step recipe.",
				topics = Topics.Select( t => new { name = t.Key, title = t.Value.Title } ).ToArray()
			};
		}

		if ( !Topics.TryGetValue( topic, out var entry ) )
			throw new ArgumentException( $"No topic '{topic}'. Topics: {string.Join( ", ", Topics.Keys )}" );

		return new { topic, title = entry.Title, steps = entry.Recipe };
	}
}
