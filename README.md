<p align="center"><img src="Brain10.webp" width="140" alt="s&box MCP logo"></p>

# s&box MCP

**An MCP server that runs inside the s&box editor.** Connect Claude Code, Claude Desktop, Cursor or VS Code and let AI build *and playtest* games in the editor — scenes, GameObjects, components, prefabs, assets, materials, sounds, input actions, **imported maps and cloud content**, ModelDoc, AnimGraph, ShaderGraph, ActionGraph, code files (C#/Razor/SCSS), console diagnostics, play mode and screenshots.

**~160 tools** plus a dynamic invocation layer (any engine method is callable), live self-updating API discovery, a diagnostics/observability suite for driving play mode autonomously, built-in workflow recipes (`help`), an in-editor dashboard, and a two-step setup.

## Setup

1. **Install the library** — in the s&box editor open the Library Manager and install *s&box MCP* (or clone this repo into your project's `Libraries/` folder).
2. **Copy the config** — open the dashboard via the **MCP menu** in the menu bar, find your AI client's card on the Overview tab and click the snippet to copy it. Paste it into your client's MCP config. Done.

The server starts automatically with the editor and listens on `http://127.0.0.1:9090/sbox-mcp` (port configurable in Settings).

### Client configs at a glance

**Claude Code**
```bash
claude mcp add --transport http sbox http://127.0.0.1:9090/sbox-mcp
```

**Claude Desktop** (`claude_desktop_config.json`, needs Node.js)
```json
{ "mcpServers": { "sbox": { "command": "npx", "args": ["-y", "mcp-remote", "http://127.0.0.1:9090/sbox-mcp"] } } }
```

**Cursor** (`.cursor/mcp.json`)
```json
{ "mcpServers": { "sbox": { "url": "http://127.0.0.1:9090/sbox-mcp" } } }
```

**VS Code** (`.vscode/mcp.json`)
```json
{ "servers": { "sbox": { "type": "http", "url": "http://127.0.0.1:9090/sbox-mcp" } } }
```

## Importing maps, assets & cloud content

The AI can bring existing content into a scene, not just author it from scratch:

- **`scene_load_map`** — load a compiled `.vmap` into the scene (world geometry, props, lighting) via a `MapInstance`.
- **`scene_add_asset`** — add *any* asset by path; it dispatches on extension: `.vmdl` → a model renderer, `.prefab` → a cloned prefab instance, `.vmap` → a map instance. One call for "put this in the scene".
- **`cloud_load_map`** — download a map package from **sbox.game** and instantiate it, in one call.
- **`cloud_search` / `cloud_install`** — browse and install community content. **Cloud tools are enabled by default.**

## Building & driving play mode

Beyond authoring, the server is built to *run and observe* the game autonomously:

- **Live play-scene reads** — `get_component_property`, `component_get_property`, `gameobject_get_transform`, `property_watch` and friends resolve the **live, ticking play-scene clone** during play (not the dormant editor copy), so you read the real running state. Use **`scene_target`** to explicitly aim tools at the `editor`, `play`, or `active` scene.
- **`property_watch`** — a flight recorder: sample any property at N Hz for N seconds and get the time series (velocities, drift, animgraph params).
- **`session_info`** — play-session identity & timing (is it playing, when it started, a session counter, last hotload) so restarts are unambiguous.
- **`compile_await` / `build_info`** — wait for a compile to *settle* and confirm the running process actually hot-swapped your new code (assembly `buildId` MVID changes each recompile — no more throwaway `Log.Info` canaries).
- **`logs_search`** — regex + severity + time-window search of the console, returning matches **with stack traces**. `editor_get_logs` supports an incremental cursor.
- **`perf_get_stats`** — measures FPS / frame time so a perf fix can be confirmed quantitatively.
- **`scene_diff`** — compare the in-memory scene against the saved `.scene` on disk (what's added/removed since save) so you don't lose edits.
- **`inspector_describe`** — reports how a component's members render in the Inspector (group/tab, control kind, `[Range]` bounds, enum options) from the live build's attributes — verify `[Property]`/`[Group]`/`[Range]` registered correctly.

## The dashboard

- **Overview** — server status with a live pulse, connected clients, one-click copy config cards.
- **Activity** — every tool call the AI makes, live: category chip, arguments, duration, result. Approval cards appear here when a write needs your OK, each with **Revert this action**.
- **Tools** — searchable browser of every tool, filterable by category, with a **per-tool enable/disable toggle** (persisted). **Import Tools** exposes public static methods from your other installed libraries as `lib_*` MCP tools.
- **Settings** — port, autostart, and the permission mode dropdown.

## Permission modes

| Mode | Behavior |
|---|---|
| **Full access** *(default)* | Every tool runs without asking. |
| **Approve writes** | Tools that modify your project pop an Approve/Deny card in the dock (60s timeout = deny). Read-only tools always run. |
| **Read-only** | Write tools are rejected with a message the AI understands. |

Every scene mutation runs inside an editor undo scope — anything the AI does, you can Ctrl+Z.

## What can it edit?

Effectively **everything an AI could reasonably drive in the editor.** Beyond the named tools, four universal mechanisms give total reach:

- **Any component property** — `component_set_property` sets any `[Property]` (enums by name, resources by path, component/GameObject references); `set_component_property` reaches plain C# members too.
- **Any method or static** — `invoke_static`, `invoke_component_method`, `code_run_static_method`, `get_/set_static_property` call anything in the live engine via reflection. Positional JSON args are marshalled to the real parameter types, and a returned **`Task`/`Task<T>` is awaited** so you get the result, not the task object.
- **Any text asset** — `asset_write_raw` writes any resource; every s&box format is KV3 or JSON.
- **Any C#** — `code_write_file` writes components, systems, editor tools, UI; the editor hot-reloads.
- **Any type** — `api_search` / `api_get_type` reflect the whole API (writability reported accurately); `api_reference` exports the full current-build reference and auto-refreshes when you update s&box.

So "add a player" isn't a special tool — the AI finds `PlayerController` with `api_search`, reads its fields with `api_get_type`, and composes it.

## Tool families

| Prefix | What it covers |
|---|---|
| `scene_` | Create, open, list, save / save-as, status, hierarchy, undo/redo, traces, overlap, **load a `.vmap` map**, **add any asset**, **target editor/play scene**, **diff vs disk** |
| `navmesh_` | Bake the NavMesh and pathfind (`find_path` reports a `reaches` flag + real endpoint so partial paths can't be mistaken for arrivals), random/closest points |
| `gameobject_` | Create, delete, rename, transform, reparent, duplicate, find, details, select, spawn model/light/camera, align/group/drop-to-ground |
| `component_` | Type search, add/remove, get/set properties, **`inspector_describe`** how they render |
| `prefab_` | Create from GameObject, instantiate (incl. many), break instance, re-sync |
| `asset_` | Search, info, compile, create resource, duplicate/delete, raw read/write of any text asset |
| `material_` / `soundevent_` / `texture_` / `sound_` | Create materials, sound events, textures; play 2D/3D sound |
| `cloud_` | Search & install sbox.game content, **load a cloud map** (enabled by default) |
| `modeldoc_` / `animgraph_` / `shadergraph_` / `actiongraph_` | Read as JSON / write KV3, list parameters/nodes, auto-collision |
| `input_` / `project_` / `convar_` | Input actions, startup scene, console variables |
| `code_` | List/read/write/edit C#, Razor, SCSS, shaders (auto hot-reload), scaffold a component, `compile_await`, `build_info`, run a static method (args + Task-aware) |
| `api_` | Search/read the whole type surface; export & auto-refresh a full current-build reference |
| `invoke_` / `*_static_property` / `*_component_property` | Dynamic layer — call any method (Task-aware), read/write any static or instance member |
| `editor_` | Console logs (+ cursor), `logs_search`, screenshots (incl. from any angle & live play POV), play/stop, `session_info`, **`perf_get_stats`**, console commands, project info, selection |
| `batch` / `server_` / `help` | Batch calls, adjust server config over MCP, step-by-step recipes |
| `retargeter_` / `animeditor_` / `lib_` | Reflection-only integrations with other libraries + your imported tools |

Notes on graph authoring: `.vmdl` and `.vanmgrph` are KV3 text, ShaderGraph/ActionGraph are JSON. The write tools accept the full on-disk format and compile immediately, so format mistakes surface as compile errors the AI can read and fix. Reading an existing asset first (`*_get` with `raw=true`) is the recommended way for the AI to learn the current schema.

## Security

- The server binds to `127.0.0.1` only and rejects non-localhost browser origins.
- File tools are jailed to the project root.
- No auth — anything on your machine that can reach localhost can connect. That is the same trust model as most local MCP servers; use Read-only or Approve-writes mode if it concerns you.

## Troubleshooting

- **Port already in use** — the Overview tab shows the error with a one-click "try another port"; snippets update automatically.
- **Claude Desktop won't connect** — it needs Node.js installed (`npx` runs the `mcp-remote` shim).
- **Tools error with "No scene is open"** — open a scene in the editor first.
- **Reads look stale during play** — by default tools now resolve the live play scene while playing; if you deliberately want the persistent editor scene, `scene_target editor` (and `scene_target active` to reset).
- **New tools don't appear after editing this library** — make sure your project's `Libraries/sbox-mcp` junction points at this repo; a missing junction makes the editor compile a stale cached copy. `build_info`'s `buildId` changes on every recompile, so you can confirm the running build is current.
- **`editor_screenshot` fails** — the scene needs an enabled `CameraComponent` (the AI can add one).

## First-run smoke checklist

1. Open a project → console shows `s&box MCP loaded (... tools)` and `MCP server listening on http://127.0.0.1:9090/sbox-mcp`.
2. MCP menu → status pill pulses green.
3. `claude mcp add --transport http sbox http://127.0.0.1:9090/sbox-mcp`, then in Claude Code: *"what's in my scene?"* → watch the Activity tab.
4. Ask it to create a GameObject → (in Approve-writes mode) an approval card appears → Approve → Ctrl+Z undoes it.

## Development

The s&box editor compiles `Editor/` directly — just edit and let it hot-reload. The `Editor/Server` and `Editor/Registry` layers are deliberately Sandbox-free so they can be unit-tested outside the editor (`dev/SboxMcp.Dev.csproj`).

## License

MIT — see [LICENSE](LICENSE).
