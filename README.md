# s&box MCP

**An MCP server that runs inside the s&box editor.** Connect Claude Code, Claude Desktop, Cursor or VS Code and let AI work the editor for you — scenes, GameObjects, components, prefabs, assets, ModelDoc, AnimGraph, ShaderGraph, ActionGraph, code files, console diagnostics, play mode and screenshots. Around 50 tools, a colorful in-editor dock, and a setup that is exactly two steps.

## Setup

1. **Install the library** — in the s&box editor open the Library Manager and install *s&box MCP* (or clone this repo into your project's `Libraries/` folder).
2. **Copy the config** — open the dock via **View → MCP**, find your AI client's card on the Overview tab and click the snippet to copy it. Paste it into your client's MCP config. Done.

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

## The dock

- **Overview** — server status with a live pulse, connected clients, one-click copy config cards.
- **Activity** — every tool call the AI makes, live: category chip, arguments, duration, result. Approval cards appear here when a write needs your OK.
- **Tools** — searchable browser of every tool, filterable by category. This is the documentation.
- **Settings** — port, autostart, and the permission mode.

## Permission modes

| Mode | Behavior |
|---|---|
| **Full access** | Every tool runs without asking. |
| **Approve writes** *(default)* | Tools that modify your project pop an Approve/Deny card in the dock (60s timeout = deny). Read-only tools always run. |
| **Read-only** | Write tools are rejected with a message the AI understands. |

Every scene mutation runs inside an editor undo scope — anything the AI does, you can Ctrl+Z.

## Tool families

| Prefix | What it covers |
|---|---|
| `scene_` | Status, hierarchy, save, undo/redo |
| `gameobject_` | Create, delete, rename, transform, reparent, duplicate, find, details, select |
| `component_` | Type search, add/remove, get/set properties (any `[Property]`, resources by path) |
| `prefab_` | Instantiate, break instance, re-sync from prefab |
| `asset_` | Search, info, compile, create resource, raw read/write of any text asset |
| `modeldoc_` | Create .vmdl from FBX/OBJ, read as JSON, write KV3, auto-generate collision |
| `animgraph_` | Read as JSON, write KV3, list parameters |
| `shadergraph_` / `actiongraph_` | Read/write the JSON formats, list nodes |
| `code_` | List/read/write C# files (hot-reload is automatic), compile errors |
| `editor_` | Console logs, screenshots, play/stop, console commands, project info, selection |

Notes on graph authoring: `.vmdl` and `.vanmgrph` are KV3 text, ShaderGraph/ActionGraph are JSON. The write tools accept the full on-disk format and compile immediately, so format mistakes surface as compile errors the AI can read and fix. Reading an existing asset first (`*_get` with `raw=true`) is the recommended way for the AI to learn the current schema.

## Security

- The server binds to `127.0.0.1` only and rejects non-localhost browser origins.
- File tools are jailed to the project root.
- No auth — anything on your machine that can reach localhost can connect. That is the same trust model as most local MCP servers; use Read-only or Approve-writes mode if it concerns you.

## Troubleshooting

- **Port already in use** — the Overview tab shows the error with a one-click "try another port"; snippets update automatically.
- **Claude Desktop won't connect** — it needs Node.js installed (`npx` runs the `mcp-remote` shim).
- **Client connects but tools error with "No scene is open"** — open a scene in the editor first.
- **`editor_screenshot` fails** — the scene needs an enabled `CameraComponent` (the AI can add one).
- **Changed code in `Editor/` of this library** — the editor hot-reloads it; the server stops its old listener and restarts automatically.

## First-run smoke checklist

1. Open a project → console shows `s&box MCP loaded (... tools)` and `MCP server listening on http://127.0.0.1:9090/sbox-mcp`.
2. View → MCP → status pill pulses green.
3. `claude mcp add --transport http sbox http://127.0.0.1:9090/sbox-mcp`, then in Claude Code: *"what's in my scene?"* → watch the Activity tab.
4. Ask it to create a GameObject → approval card appears (default mode) → Approve → Ctrl+Z undoes it.

## Development

`dev/SboxMcp.Dev.csproj` compiles all `Editor/` sources against your installed s&box assemblies (`dotnet build dev/SboxMcp.Dev.csproj`, override the path with `-p:SboxManaged=...`). `dev/SboxMcp.Tests` covers the protocol, registry, server and path jail layers (`dotnet test`). The protocol/registry layers are Sandbox-free by design so they stay testable.

## License

MIT — see [LICENSE](LICENSE).
