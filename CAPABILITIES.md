# What s&box MCP can edit

The goal is total reach: **every part of the editor an AI could reasonably drive.**
Most of that is covered by dedicated tools; the rest is reachable through four
universal mechanisms, so "there's no tool for X" almost never means "X can't be
done."

## The four universal mechanisms

These give coverage far beyond the named tools:

1. **Any component property** — `component_set_property` sets *any* `[Property]`
   on *any* component (built-in or one you wrote), resolved live through the type
   library. No per-component tools needed.
2. **Any text asset** — `asset_write_raw` writes *any* s&box resource. Every
   resource format is KV3 or JSON text (`.vmdl`, `.vmat`, `.sound`, `.vanmgrph`,
   `.shdrgrph`, `.action`, `.prefab`, `.scene`, custom `GameResource`s…), so the
   full asset system is authorable.
3. **Any C#** — `code_write_file` writes *any* source (components, systems,
   editor tools, custom resources, UI). The editor hot-reloads it; the AI reads
   compile errors and iterates.
4. **Any type** — `api_search` / `api_get_type` reflect the *entire* s&box +
   project API, so the AI discovers what exists and how to use it before acting.

## Coverage by editor subsystem

| Subsystem | How it's edited | Dedicated tools |
|---|---|---|
| **Scenes** | Full lifecycle | `scene_create`, `scene_open`, `scene_list`, `scene_save`, `scene_save_as`, `scene_get_status`, `scene_get_hierarchy`, `scene_undo`, `scene_redo` |
| **GameObjects** | Full CRUD + transforms + hierarchy | `gameobject_create/delete/rename/duplicate/find/get_details/select`, `gameobject_get_transform/set_transform`, `gameobject_set_parent/set_enabled` |
| **Components** | Add/remove + **any property on any type** | `component_list_types`, `component_add/remove`, `component_get_properties`, `component_set_property`, `component_set_enabled` |
| **Prefabs** | Author + use | `prefab_create_from_gameobject`, `prefab_instantiate`, `prefab_break_instance`, `prefab_update_from_prefab` |
| **Assets (all formats)** | Search/compile/create + **raw read/write of any** | `asset_search/get_info/compile/create_resource/read_raw/write_raw` |
| **Materials** | `.vmat` author | `material_create` (+ `asset_write_raw` for full control) |
| **Textures** | Import image bytes | `texture_write` |
| **Sounds** | `.sound` events | `soundevent_create` |
| **Models / ModelDoc** | `.vmdl` from mesh, read/write, collision | `modeldoc_create_from_mesh/get/set/add_physics` |
| **AnimGraph** | `.vanmgrph` read/write, params | `animgraph_get/set/list_parameters` |
| **ShaderGraph** | `.shdrgrph` read/write, nodes | `shadergraph_get/set/list_nodes` |
| **ActionGraph** | `.action` read/write | `actiongraph_get/set` |
| **Input** | Project input actions | `input_list_actions/add_action/remove_action` |
| **Project settings** | Startup scene + `.sbproj`/`ProjectSettings/*` | `project_set_startup_scene` (+ `asset_write_raw`/`code_write_file`) |
| **Code** | C#/Razor/SCSS/shaders, scaffold, run | `code_list_files/read_file/write_file`, `code_create_component`, `code_run_static_method`, `code_get_compile_errors` |
| **Cloud content** | Search + install from sbox.game | `cloud_search`, `cloud_install` (opt-in) |
| **API discovery** | Reflect the whole type surface | `api_search`, `api_get_type` |
| **Play / debug** | Enter/exit play, console, commands | `editor_play/stop/is_playing`, `editor_run_console_command`, `editor_get_logs/clear_logs`, `editor_get_project_info/get_selection` |
| **Viewport / vision** | Screenshots + framing | `editor_screenshot`, `editor_screenshot_from`, `editor_frame_object` |
| **Extensibility** | Drive other libraries | `retargeter_*`, imported `lib_*` tools |

## Worked example — "add a player"

Nothing player-specific is hardcoded; it falls out of the universal mechanisms:

1. `api_search "player"` → finds `PlayerController` (a built-in component).
2. `api_get_type "PlayerController"` → its real properties (`WalkSpeed`,
   `EyeHeight`, …).
3. `gameobject_create "Player"` → `component_add "PlayerController"` →
   `component_set_property` for each field.
4. For custom behavior: `code_create_component "PlayerAbilities"` → hot-reload →
   `component_add`.

The same path builds *any* actor: enemies, cameras, triggers, vehicles.

## Known limits (honest)

- **Hammer / maps (`.vmap`)** — the map editor is engine-native with no managed
  authoring API. Scene-based games are unaffected (s&box favors scenes over maps).
- **Binary-only assets** — assets with no text source (some imported binaries)
  can't be hand-authored; reference or `cloud_install` them instead.
- **Live gameplay input simulation** — not simulated; use `code_run_static_method`
  to exercise game logic programmatically instead.

Everything else in the editor is reachable. If a workflow hits a wall, it's a
candidate for a new dedicated tool on top of the mechanisms above — the tool
registry makes adding one a single annotated method.
