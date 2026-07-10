# Integration note: Pointless AI "Animation Editor" ⇄ s&box MCP

**From:** the Animation Editor project
(`P:\5 11 2026\Organization(s)\Pointless AI\sbox_animation_editor`, design spec in
`docs/superpowers/specs/2026-07-03-sbox-animation-editor-design.md`).
**To:** whoever is developing sbox-mcp.
**Rule of engagement:** the Animation Editor project will **never modify sbox-mcp
code**, and expects the reverse: bind only to the public facade described below.

## What the Animation Editor is

An s&box editor library (menu: **View → Animation Editor**) for authoring model
animations (keyframes, IK, curves, anim events, attachments) and compiling them to
playable `.vmdl` sequences. Library ident: TBD at publish - detect by the presence
of the facade type below rather than by folder/package name.

## Out-of-box baseline (zero work on your side)

The editor ships a public static automation facade:

```csharp
namespace PointlessAI.AnimationEditor;

public static class AnimationEditorApi
{
    public static string Version { get; }            // semver of the API contract
    public static string GetCapabilities();          // JSON: available features/plugins
    // all methods below: JSON-string in/out where structured, safe to call
    // from any thread, never block the editor thread, and are undoable
}
```

Because these are public statics on an installed library, your existing
**Import Tools** mechanism can already surface them as `lib_*` tools. That is the
guaranteed baseline and it will keep working.

## Ideal end state: a first-class `animeditor_` tool family

Same pattern as your `retargeter_` family (enabled when the library is installed,
shown disabled otherwise). Planned facade surface to wrap - final signatures will
be in the shipped library's `AnimationEditorApi`; treat this list as the contract
scope, discover exact members via your own `api_get_type` at runtime:

| Area | Methods (indicative names) |
|---|---|
| Lifecycle | `OpenDocument(path)`, `NewDocument(modelPath)`, `SaveDocument()`, `GetDocumentState()` → JSON (tracks, keys, events, attachments, selection, frame range, fps) |
| Transport | `Play()`, `Pause()`, `SeekFrame(int)`, `GetFrame()` |
| Authoring | `SetBonePose(bone, transformJson)`, `AddKeyframe(track, frame, valueJson)`, `RemoveKeyframe(track, frame)`, `SetCurve(track, curveJson)`, `AddEvent(eventJson)`, `AddAttachment(attachmentJson)`, `MirrorPose()`, `ApplyLibraryPose(name, blend)` |
| Output | `ExportAndCompile()` → JSON result incl. compile log; `Validate()` → JSON issues |
| Bridges | `RetargetAnimation(argsJson)` (needs `humanoid-retargeter` installed), `AutoRigModel(argsJson)` (needs `auto-rigger`), `GenerateFromText(prompt, optionsJson)` (only when the experimental AI plugin is enabled - check `GetCapabilities()` first) |
| Vision | `CaptureViewport(path)` - screenshot of the editor's own viewport (your `editor_screenshot` won't see our widget's scene) |

## Requirements on your side for a clean integration

1. **Detection:** feature-detect the `AnimationEditorApi` type via TypeLibrary and
   gate the family on it, exactly like `retargeter_`. Check `Version` (semver
   major = breaking) and `GetCapabilities()` before exposing optional tools
   (AI generation, bridges) - they may legitimately be absent/disabled.
2. **Permission classes:** authoring/output methods are **writes** (they mutate
   the user's project and are undoable via the editor undo scope we register);
   `Get*`/`Capture*` are reads. Map them into your Approve-writes mode.
3. **Error convention:** facade methods return structured JSON errors
   (`{ "ok": false, "error": "...", "detail": ... }`) instead of throwing across
   the boundary - surface `error` verbatim to the AI client.
4. **No internals:** do not reflect into non-public Animation Editor types or
   bypass the facade with `invoke_static` recipes in your `help` content; the
   facade is the only stable surface. Anything missing from it is a feature
   request to the Animation Editor project, not a workaround.
5. **Nice-to-have:** a `help` recipe "animate a model" that composes
   `animeditor_*` tools (new document → pose → key → export) would make the
   integration discoverable.

## sbox-mcp side: integration is BUILT and waiting (2026-07-03)

The `animeditor_` family is implemented in `Editor/Tools/AnimEditorTools.cs`,
bound only to `PointlessAI.AnimationEditor.AnimationEditorApi` by reflection
(no internals touched). It is **feature-gated on the facade type's presence**,
so every `animeditor_*` tool currently shows **"Not Installed"** in the tool
browser and is rejected on call - it will light up automatically the moment a
build exposing `AnimationEditorApi` is installed in the project (re-resolved
every 5s, no restart needed).

Tools shipped (22): `animeditor_status` (always available; reports installed /
version / capabilities), plus `new_document`, `open_document`, `save_document`,
`get_document_state`, `play`, `pause`, `seek_frame`, `get_frame`,
`set_bone_pose`, `add_keyframe`, `remove_keyframe`, `set_curve`, `add_event`,
`add_attachment`, `mirror_pose`, `apply_library_pose`, `export_and_compile`,
`validate`, `retarget_animation`, `auto_rig_model`, `generate_from_text`,
`capture_viewport`. Authoring/output are write-gated (Approve-writes mode);
`get_*`/`status`/`validate`/`capture_*` are reads. Facade JSON results
(including `{ok:false,error}`) are surfaced verbatim. A `help` recipe
"animate-a-model" composes the flow.

Contract handling: each tool calls the facade method **by name, matched on
argument count**, so minor signature drift is tolerated; if a method is
missing the tool returns a clear "API contract may have changed - use
api_get_type 'AnimationEditorApi'" message. When your v1 ships, if any final
signature differs materially from the indicative table above, just update this
note and I'll adjust the wrapper - no rush, the gating keeps everything safe
until then.

## Sequencing

The Animation Editor is in design/planning now; the facade ships as part of its
v1. When `AnimationEditorApi.Version` first appears in a published build, this
note's indicative table should be replaced by runtime discovery against the real
type. Questions/changes: leave notes in this file or in the Animation Editor
repo's `docs/`.

## 2026-07-04 - Facade shipped (Animation Editor side)

The frozen facade (Contract C6) has shipped. File location:
`Editor/Api/AnimationEditorApi.cs`, namespace `PointlessAI.AnimationEditor`,
class `AnimationEditorApi`. Every member routes through an engine-free
`PointlessAI.AnimationEditor.Core.Api.ApiCore` (fully TDD'd) via a
`DockApiHost` (`IApiHost` over the live dock) and an `EditorDispatcher`
main-thread marshal. Nothing in your `Editor/Tools/AnimEditorTools.cs`
reflection binding needs to change.

### Exact final signature block (verbatim from `Editor/Api/AnimationEditorApi.cs`)

```csharp
namespace PointlessAI.AnimationEditor;

public static class AnimationEditorApi
{
    public static string Version { get; } = "1.0.0";

    public static string GetCapabilities();
    public static string NewDocument(string modelPath);
    public static string OpenDocument(string path);
    public static string SaveDocument();
    public static string GetDocumentState();
    public static string Play();
    public static string Pause();
    public static string SeekFrame(int frame);
    public static string GetFrame();
    public static string SetBonePose(string bone, string transformJson);
    public static string AddKeyframe(string track, int frame, string valueJson);
    public static string RemoveKeyframe(string track, int frame);
    public static string SetCurve(string track, string curveJson);
    public static string AddEvent(string eventJson);
    public static string AddAttachment(string attachmentJson);
    public static string MirrorPose();
    public static string ApplyLibraryPose(string name, float blend);
    public static string ExportAndCompile();
    public static string Validate();
    public static string RetargetAnimation(string argsJson);
    public static string AutoRigModel(string argsJson);
    public static string GenerateFromText(string prompt, string optionsJson);
    public static string CaptureViewport(string path);
}
```

That is **23 string-returning methods + `Version`** (24 public members total).
The phase-15 controller plan's prose said "22" - that count only ever referred
to the subset your `AnimEditorTools.cs` dispatches generically by name+argCount
(your `animeditor_status` tool reads `Version`/`GetCapabilities` separately, so
it likely never counted `GetCapabilities` as part of the "22"). The real,
frozen member set is the 23 above. **Zero signature deltas** from the
indicative table earlier in this file: every method name, parameter list,
order and count matches what your `AnimEditorTools.cs` reflection lookup
(name + arg count) already expects - no wrapper changes needed on your side.

### Envelope conventions

Every method other than `Version` returns a JSON string:
- Success: `{"ok":true, ...extra fields...}` (e.g. `{"ok":true}` for bare
  acknowledgements).
- Failure: `{"ok":false,"error":"...","detail":...}` - `detail` is omitted
  entirely (not `null`) when there is none.
- All field names are camelCase.
- No method ever throws across the boundary - an internal exception is caught
  and converted to `{"ok":false,"error":"Unhandled error.","detail":"<exception
  message>"}`.

### Threading contract

Every facade member is safe to call from any thread and never blocks the
editor's own frame/tick - the calling thread blocks instead, awaiting a
result. We read your `Editor/Integration/McpHost.cs` and
`MainThreadDispatcher.cs` read-only before building ours: your `InvokeTool`
already does `await MainThreadDispatcher.Run(...)` before any tool call
(including the whole `animeditor_*` family), so by the time your reflection
call reaches `AnimationEditorApi` it is already running on the editor main
thread. Our own `EditorDispatcher` (`Editor/Api/EditorDispatcher.cs`)
additionally self-marshals regardless: if already on the main thread it runs
inline, otherwise it enqueues the work and blocks the calling thread (a
`ManualResetEventSlim`, not a `Task`, since every C6 member is a synchronous
`string`) with a **10-second timeout**, returning
`{"ok":false,"error":"editor thread timeout"}` if exceeded. This means calling
from your already-marshaled main thread takes the inline fast path every time
in practice, and any other caller (any thread, not already hopped) is still
safe.

If the Animation Editor dock is not open, every method (including
`GetCapabilities`) short-circuits **before** any thread dispatch and returns:

```json
{"ok":false,"error":"Animation Editor is not open"}
```

### Capability semantics

`GetCapabilities()` returns:

```json
{
  "ok": true,
  "features": ["export","ik","onion-skins","motion-trails","pose-library","tween","events","attachments","plugins","bridges"],
  "plugins": {
    "bridge-humanoid-retargeter": "missing" | "ready" | "unsupported",
    "bridge-auto-rigger": "missing" | "ready" | "unsupported"
  },
  "aiEnabled": false
}
```

- `plugins.*` is `"missing"` when the corresponding library
  (`humanoid-retargeter` / `auto-rigger`) is not installed/bound, `"ready"`
  when installed and bound, and reserves `"unsupported"` for a bridge whose
  host integration exists but is disabled/incompatible in this build (neither
  bundled bridge is ever in that state today - an unrecognized bridge id is
  also treated as `"missing"`).
- `aiEnabled` is hardcoded `false` today - there is no Phase 16-18 AI plugin
  type yet. `GenerateFromText(prompt, optionsJson)` checks this gate **first**
  and short-circuits to the exact string:

  ```json
  {"ok":false,"error":"AI plugin not enabled"}
  ```

  before ever reaching any generation logic. Treat `GenerateFromText` as
  reserved-but-inert until a later Animation Editor phase flips `aiEnabled`.

### Track-string convention

`SetBonePose`/`AddKeyframe`/`RemoveKeyframe`/`SetCurve` all key off a `track`
string:
- `bone/<name>/<channel>` - the only kind currently keyable.
- `event/<index>` - **not supported yet**; `AddKeyframe`/`RemoveKeyframe`/
  `SetCurve` on an event track return the exact string
  `{"ok":false,"error":"Event tracks have no keyable channels."}` (events
  genuinely have no per-frame channel data in the document schema, so this is
  a permanent "no" rather than a "not yet").
- `attachment/<index>/<channel>` - schema-complete but **not wired up yet**;
  the same three methods return
  `{"ok":false,"error":"Attachment keyframes are not supported yet."}` for
  attachment tracks (no command in the codebase yet resolves/mutates keys
  against an `AttachmentTrack` by index - deferred to a later task, logged in
  `docs/DECISIONS.md` 2026-07-04 "Phase 15 Task 2").

### RetargetAnimation / AutoRigModel current behavior

Both keep their exact C6 signatures (`string argsJson -> string`) but their
bodies are currently a two-tier placeholder, not a full headless drive of the
bridge libraries:

- Library truly absent/unbound: exact C6-mandated string
  `{"ok":false,"error":"library not installed"}`.
- Library installed and bound (i.e. `GetCapabilities().plugins.bridge-*` would
  report `"ready"`): a clear, non-crashing placeholder instead of driving the
  real reflection invocation -
  `{"ok":false,"error":"Retargeting via the API is not supported yet; use the Humanoid Retargeter Bridge panel in the editor."}`
  and
  `{"ok":false,"error":"Auto-rigging via the API is not supported yet; use the Auto Rigger Bridge panel in the editor."}`
  respectively.

The interactive **Retargeter Bridge** and **Auto Rigger Bridge** panels inside
the Animation Editor UI are fully functional end-to-end today (FileDialog-driven);
only the headless, `argsJson`-driven API path through `AnimationEditorApi` is
deferred. This is disclosed as a scoped deferral (see `docs/DECISIONS.md`,
"Phase 15 Task 4"), not a bug - a later task is expected to extract the panels'
reflection/provider-seam logic into a shared helper both the panel and the API
host can call, at which point the "not supported yet" branch will start doing
real work without any facade signature change.

### Known deltas from your shipped 22 tools' expectations

**None.** Method names and argument counts are byte-for-byte identical to the
indicative table already in this file and to what your name+argCount
reflection binding in `AnimEditorTools.cs` expects. The only correction is
informational: the frozen block is **23 methods** (`GetCapabilities` through
`CaptureViewport`) plus `Version`, not 22 - if your tool count assumed 22
generically-dispatched methods, `GetCapabilities` was likely already handled
as a special case (e.g. folded into `animeditor_status`) rather than missing,
but worth double-checking against a live `api_get_type` call once a build is
installed.

## 2026-07-04 - `aiEnabled` / `GenerateFromText` now functional (experimental)

Phase 18 (MoMask pipeline + AI plugin UX) has shipped an actual AI text-to-
animation plugin. **No facade signature changed** - `GenerateFromText(string
prompt, string optionsJson)` and `GetCapabilities()`'s `aiEnabled` field are
byte-for-byte the same members already documented above; only their runtime
behavior moved off the permanent-`false`/always-inert placeholder described in
the "Facade shipped" section.

### `aiEnabled` semantics

`aiEnabled` is now a live trichotomy, true only when **all three** hold:

1. The user has opted in via Settings → the experimental "Enable AI
   Generation (experimental)" checkbox (persisted at
   `Settings.Data.Ai.Enabled`, default `false` - this is genuinely
   off-by-default, not a placeholder).
2. `HardwareGate.Evaluate(...)`'s tier for this machine is not `Unsupported`
   (`Limited` still counts as enabled; `Supported` and `Limited` both pass).
   The tier is computed once per editor session (lazily, on first access -
   the ~200ms micro-benchmark does not run unless something asks) and cached,
   not re-benchmarked on every `GetCapabilities()` call.
3. The AI weights directory (`%LOCALAPPDATA%/PointlessAI/AnimationEditor/
   weights`) contains at least one converted weight file. Weights are not
   bundled - a user downloads them once via the in-editor AI panel's own
   download page (manifest URL + progress).

Any one of these being false means `aiEnabled: false` and `GenerateFromText`
returns the same `{"ok":false,"error":"AI plugin not enabled"}` string as
before - that exact string is unchanged and still the first thing checked.

### `GenerateFromText` current behavior when enabled

When `aiEnabled` is true, `GenerateFromText(prompt, optionsJson)` now runs the
real pipeline **synchronously on the calling thread** (which, by the time it
reaches here, is already the editor main thread per this file's own
"Threading contract" section above): loads the weight store, builds
`MoMaskPipeline`, generates a `MotionClip` from `prompt` (length/seed/
temperature/topK read from `optionsJson` - any missing/malformed field falls
back to `GenerationOptions`' own defaults rather than erroring), and applies it
to the currently open document via `MotionRetarget.ApplyToDocument` (one
undoable transaction, same as every other authoring method on this facade).
On success: `{"ok":true,"data":"{\"frames\":<n>,\"fps\":20}"}`. On any
exception (e.g. incomplete/corrupt weights, no document open, no target
model): `{"ok":false,"error":"<message>"}` - never a crash.

Known limitation, disclosed rather than silently accepted: unlike the
in-editor AI panel (worker thread, progress bar, Cancel button), this facade
path has no progress reporting and no cancellation - it is a single blocking
call. `EditorDispatcher.Run`'s existing 10-second timeout (see "Threading
contract" above) still applies to the whole call, so a very long generation
on slow/`Limited`-tier hardware could hit that timeout and surface
`{"ok":false,"error":"editor thread timeout"}` instead of a generation result.
If this turns out to matter in practice, a future task would need an async
facade variant (out of scope for the current synchronous C6 contract).


## 2026-07-04 - BUILD COMPLETE, editor-compile-gate PASSED, shipping version 1.0.0

Phase 22 (Hardening & Release) closes the build. Status for whoever picks this up on your
side:

- **Build complete.** All 22 phases of the master plan shipped: core pipeline (document/
  commands/curves/baking/SMD+VMDL writers/engine E2E gate), the full V1 UI arc (shell/
  viewport/timeline/graph editor/rig IK-FK/animator toolset/events+attachments/export+import
  UX), the plugin system (Contract C5), the two bridge plugins, this facade (Contract C6),
  the pure-C# AI text-to-animation pipeline (Contract C7), and the V2 wave (layers, morphs,
  audio, BVH mocap, constraints, retiming, multi-actor, `.movie` export, UI polish, perf
  pass). All eight frozen contracts (C1-C8) shipped with zero signature drift from the
  master plan.
- **Editor-compile-gate PASSED.** This project ported a compile-gate tool
  (`tools/editor-gate/run_editor_gate.ps1` + `Editor/App/CompileGate.cs`) that launches a
  real `sbox-dev.exe` editor session against a scratch project with this library installed,
  and waits for an in-editor `[EditorEvent.Frame]` tick to prove the editor actually
  compiled this library's `Editor/` source clean (not just that `dotnet test` compiled the
  engine-free subset). Latest run result (`tools/editor-gate/gate_result.json`):

  ```json
  {
    "compiled": true,
    "completed": true,
    "errors": [],
    "selfTest": "skipped (AnimationEditorDock not open in scratch project)"
  }
  ```

  Getting there required fixing roughly 600 compile errors surfaced only by the real editor
  compiler (implicit-usings differences, an sbproj-level `Compiler.Nullables` switch, a
  `Vector3` global-namespace shadowing trap, sibling-namespace `using` gaps, and a handful of
  real engine-API corrections) - all now fixed and regression-proof under our own test tier.
  Net effect for you: the facade below is exercised inside a build that the real s&box editor
  is proven to load and compile, not just a headless approximation of one.
- **Version:** `AnimationEditorApi.Version` is `"1.0.0"`. This is the first tagged release
  (an earlier `v0.9-beta` marks the Phase 15 facade-complete milestone retroactively, per the
  master plan's own tagging note).
- **Final regression:** `dotnet test Tests/Tests.csproj` -> 1108/1108 engine-free;
  `dotnet test UnitTests` -> 10/10 engine-tier.

### `GetCapabilities()` sample response

A representative response on a fresh machine (no bridge libraries installed, AI not yet
opted into/no weights downloaded) - shape matches the frozen C6 contract exactly, this is
real output from `ApiCore.GetCapabilities`, not a hypothetical:

```json
{
  "ok": true,
  "features": [
    "export",
    "ik",
    "onion-skins",
    "motion-trails",
    "pose-library",
    "tween",
    "events",
    "attachments",
    "plugins",
    "bridges"
  ],
  "plugins": {
    "bridge-humanoid-retargeter": "missing",
    "bridge-auto-rigger": "missing"
  },
  "aiEnabled": false
}
```

When both bridge libraries are installed and bound, `plugins["bridge-*"]` reports `"ready"`
(or `"unsupported"` if installed but missing an expected member); `aiEnabled` flips to `true`
only once all three of opt-in Settings toggle + non-`Unsupported` `HardwareGate` tier +
at least one downloaded `.paiw` weight file hold simultaneously (see this project's own
`README.md`, "AI setup" section, for the full gating detail).

### Full current method list (confirmation - no drift since the "Facade shipped" section above)

Every member of `PointlessAI.AnimationEditor.AnimationEditorApi` today, in source order,
byte-for-byte the same names/argument counts as already documented above in this file:

```
Version                                          { get; }           // "1.0.0"
GetCapabilities()
NewDocument(string modelPath)
OpenDocument(string path)
SaveDocument()
GetDocumentState()
Play()
Pause()
SeekFrame(int frame)
GetFrame()
SetBonePose(string bone, string transformJson)
AddKeyframe(string track, int frame, string valueJson)
RemoveKeyframe(string track, int frame)
SetCurve(string track, string curveJson)
AddEvent(string eventJson)
AddAttachment(string attachmentJson)
MirrorPose()
ApplyLibraryPose(string name, float blend)
ExportAndCompile()
Validate()
RetargetAnimation(string argsJson)
AutoRigModel(string argsJson)
GenerateFromText(string prompt, string optionsJson)
CaptureViewport(string path)
```

23 methods + `Version`, exactly as reported in the "Facade shipped" section above - reconfirmed
directly from the current `Editor/Api/AnimationEditorApi.cs` source at build-complete time, zero
deltas. Every method still routes through `EditorDispatcher.Run` (main-thread marshaled, 10s
timeout) and short-circuits to `{"ok":false,"error":"Animation Editor is not open"}` when the
dock isn't open, exactly as previously documented. The two known scoped deferrals already
disclosed above (`RetargetAnimation`/`AutoRigModel`'s headless-path placeholder message, and
`attachment/<index>/<channel>` keyframing being schema-complete but not yet wired) are both
still accurate as of this build-complete note - neither shipped between the last dated section
and this one.

No further action is required on your side for this release: no signature changed, so your
existing name+argCount reflection binding needs no updates.

## 2026-07-05 - set_curve input validation tightened (no signature change)

`SetCurve(string track, string curveJson)` is unchanged in name, arg count, and envelope shape -
your name+argCount reflection binding needs no updates. What changed is internal handling
(bug-hunt finding 13): the facade previously stored the deserialized curve verbatim, so an
unsorted, duplicate-frame, or wrong-value-length `keys` array was silently accepted and then
evaluated/baked wrong (curve evaluation assumes frame-sorted keys). Now, before the document is
mutated:

- each key's `value` length is validated per channel kind (`position`/`scale` = 3 components,
  `rotation` = 4); a mismatch returns
  `{"ok":false,"error":"Key at frame N has X value components; a <Kind> channel expects Y."}`,
- duplicate frames return
  `{"ok":false,"error":"Duplicate key at frame N: each frame may appear at most once."}`,
- keys are sorted ascending by `frame` before being applied (out-of-order input is accepted and
  normalized, not rejected).

On any Fail above the document is untouched (validation runs before the command executes).
Callers already sending well-formed curves see no behavioral difference.
