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
playable `.vmdl` sequences. Library ident: TBD at publish — detect by the presence
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
shown disabled otherwise). Planned facade surface to wrap — final signatures will
be in the shipped library's `AnimationEditorApi`; treat this list as the contract
scope, discover exact members via your own `api_get_type` at runtime:

| Area | Methods (indicative names) |
|---|---|
| Lifecycle | `OpenDocument(path)`, `NewDocument(modelPath)`, `SaveDocument()`, `GetDocumentState()` → JSON (tracks, keys, events, attachments, selection, frame range, fps) |
| Transport | `Play()`, `Pause()`, `SeekFrame(int)`, `GetFrame()` |
| Authoring | `SetBonePose(bone, transformJson)`, `AddKeyframe(track, frame, valueJson)`, `RemoveKeyframe(track, frame)`, `SetCurve(track, curveJson)`, `AddEvent(eventJson)`, `AddAttachment(attachmentJson)`, `MirrorPose()`, `ApplyLibraryPose(name, blend)` |
| Output | `ExportAndCompile()` → JSON result incl. compile log; `Validate()` → JSON issues |
| Bridges | `RetargetAnimation(argsJson)` (needs `humanoid-retargeter` installed), `AutoRigModel(argsJson)` (needs `auto-rigger`), `GenerateFromText(prompt, optionsJson)` (only when the experimental AI plugin is enabled — check `GetCapabilities()` first) |
| Vision | `CaptureViewport(path)` — screenshot of the editor's own viewport (your `editor_screenshot` won't see our widget's scene) |

## Requirements on your side for a clean integration

1. **Detection:** feature-detect the `AnimationEditorApi` type via TypeLibrary and
   gate the family on it, exactly like `retargeter_`. Check `Version` (semver
   major = breaking) and `GetCapabilities()` before exposing optional tools
   (AI generation, bridges) — they may legitimately be absent/disabled.
2. **Permission classes:** authoring/output methods are **writes** (they mutate
   the user's project and are undoable via the editor undo scope we register);
   `Get*`/`Capture*` are reads. Map them into your Approve-writes mode.
3. **Error convention:** facade methods return structured JSON errors
   (`{ "ok": false, "error": "...", "detail": ... }`) instead of throwing across
   the boundary — surface `error` verbatim to the AI client.
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
browser and is rejected on call — it will light up automatically the moment a
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
missing the tool returns a clear "API contract may have changed — use
api_get_type 'AnimationEditorApi'" message. When your v1 ships, if any final
signature differs materially from the indicative table above, just update this
note and I'll adjust the wrapper — no rush, the gating keeps everything safe
until then.

## Sequencing

The Animation Editor is in design/planning now; the facade ships as part of its
v1. When `AnimationEditorApi.Version` first appears in a published build, this
note's indicative table should be replaced by runtime discovery against the real
type. Questions/changes: leave notes in this file or in the Animation Editor
repo's `docs/`.
