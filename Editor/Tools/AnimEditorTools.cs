using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// Integration with the Pointless AI "Animation Editor" library, bound purely
/// by reflection to its public facade PointlessAI.AnimationEditor.AnimationEditorApi
/// (per its integration note). We never touch its source and only call the
/// facade - no reflecting into internals. Tools are gated on the facade type's
/// presence, exactly like the Humanoid Retargeter family; until the library
/// ships they show "Not Installed".
///
/// Facade convention (from the note): methods are public static, JSON-string
/// in/out where structured, thread-safe, undoable, and return structured JSON
/// errors { "ok": false, "error": "..." } instead of throwing. We surface the
/// facade's JSON result verbatim to the client.
/// </summary>
public static class AnimEditorTools
{
	public const string Requirement = "animation-editor";
	const string FacadeTypeName = "PointlessAI.AnimationEditor.AnimationEditorApi";

	static Type _facade;
	static DateTime _lastLookup = DateTime.MinValue;

	/// <summary>The facade type, re-resolved periodically so installing the
	/// library mid-session enables the tools.</summary>
	static Type Facade
	{
		get
		{
			if ( DateTime.Now - _lastLookup > TimeSpan.FromSeconds( 5 ) )
			{
				_lastLookup = DateTime.Now;
				_facade = AppDomain.CurrentDomain.GetAssemblies()
					.Where( a => !a.IsDynamic )
					.Select( a => { try { return a.GetType( FacadeTypeName ); } catch { return null; } } )
					.LastOrDefault( t => t is not null );
			}

			return _facade;
		}
	}

	public static bool IsInstalled => Facade is not null;

	// ---- status -----------------------------------------------------------

	[McpTool( "animeditor_status", "Whether the Pointless AI Animation Editor library is installed, plus its API version and capabilities (which optional features/bridges are available).", ToolCategory.AnimEditor )]
	public static object Status()
	{
		var facade = Facade;
		if ( facade is null )
			return new { installed = false, note = "Install the Animation Editor library to enable authoring tools (View -> Animation Editor)." };

		var version = ReadProperty( facade, "Version" );
		var capabilities = TryCall( facade, "GetCapabilities" );
		return new { installed = true, version, capabilities };
	}

	// ---- lifecycle (writes) ----------------------------------------------

	[McpTool( "animeditor_new_document", "Starts a new animation document for a model.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object NewDocument( [Desc( "Model asset path, e.g. 'models/citizen/citizen.vmdl'" )] string modelPath )
		=> Call( "NewDocument", modelPath );

	[McpTool( "animeditor_open_document", "Opens an existing animation document.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object OpenDocument( [Desc( "Document asset path" )] string path )
		=> Call( "OpenDocument", path );

	[McpTool( "animeditor_save_document", "Saves the current animation document.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object SaveDocument() => Call( "SaveDocument" );

	[McpTool( "animeditor_get_document_state", "Gets the current document as JSON: tracks, keyframes, events, attachments, selection, frame range, fps.", ToolCategory.AnimEditor, Requires = Requirement )]
	public static object GetDocumentState() => Call( "GetDocumentState" );

	// ---- transport --------------------------------------------------------

	[McpTool( "animeditor_play", "Starts playback in the Animation Editor.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object Play() => Call( "Play" );

	[McpTool( "animeditor_pause", "Pauses playback.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object Pause() => Call( "Pause" );

	[McpTool( "animeditor_seek_frame", "Seeks to a frame.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object SeekFrame( int frame ) => Call( "SeekFrame", frame );

	[McpTool( "animeditor_get_frame", "Gets the current playback frame.", ToolCategory.AnimEditor, Requires = Requirement )]
	public static object GetFrame() => Call( "GetFrame" );

	// ---- authoring (writes) ----------------------------------------------

	[McpTool( "animeditor_set_bone_pose", "Sets a bone's pose. transform is JSON (position/rotation/scale).", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object SetBonePose(
		[Desc( "Bone name" )] string bone,
		[Desc( "Transform as JSON" )] JsonElement transform )
		=> Call( "SetBonePose", bone, transform.GetRawText() );

	[McpTool( "animeditor_add_keyframe", "Adds a keyframe on a track at a frame. value is JSON.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object AddKeyframe(
		[Desc( "Track name" )] string track,
		int frame,
		[Desc( "Key value as JSON" )] JsonElement value )
		=> Call( "AddKeyframe", track, frame, value.GetRawText() );

	[McpTool( "animeditor_remove_keyframe", "Removes a keyframe from a track at a frame.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object RemoveKeyframe( [Desc( "Track name" )] string track, int frame )
		=> Call( "RemoveKeyframe", track, frame );

	[McpTool( "animeditor_set_curve", "Sets a track's interpolation curve. curve is JSON.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object SetCurve( [Desc( "Track name" )] string track, [Desc( "Curve as JSON" )] JsonElement curve )
		=> Call( "SetCurve", track, curve.GetRawText() );

	[McpTool( "animeditor_add_event", "Adds an animation event. event is JSON (frame, name, data).", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object AddEvent( [Desc( "Event as JSON" )] JsonElement animEvent )
		=> Call( "AddEvent", animEvent.GetRawText() );

	[McpTool( "animeditor_add_attachment", "Adds an attachment point. attachment is JSON (name, bone, offset).", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object AddAttachment( [Desc( "Attachment as JSON" )] JsonElement attachment )
		=> Call( "AddAttachment", attachment.GetRawText() );

	[McpTool( "animeditor_mirror_pose", "Mirrors the current pose left/right.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object MirrorPose() => Call( "MirrorPose" );

	[McpTool( "animeditor_apply_library_pose", "Applies a named library pose, optionally blended in.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object ApplyLibraryPose( [Desc( "Pose name" )] string name, [Desc( "Blend 0-1" )] float blend = 1f )
		=> Call( "ApplyLibraryPose", name, blend );

	// ---- output -----------------------------------------------------------

	[McpTool( "animeditor_export_and_compile", "Exports the animation and compiles it into a playable .vmdl sequence. Returns a JSON result with the compile log.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object ExportAndCompile() => Call( "ExportAndCompile" );

	[McpTool( "animeditor_validate", "Validates the current document and returns any issues as JSON.", ToolCategory.AnimEditor, Requires = Requirement )]
	public static object Validate() => Call( "Validate" );

	// ---- bridges (optional; may be absent - check animeditor_status) ------

	[McpTool( "animeditor_retarget_animation", "Bridge: retargets an animation via the Humanoid Retargeter (must also be installed). args is JSON.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object RetargetAnimation( [Desc( "Arguments as JSON" )] JsonElement args )
		=> Call( "RetargetAnimation", args.GetRawText() );

	[McpTool( "animeditor_auto_rig_model", "Bridge: auto-rigs a model via the Auto Rigger (must also be installed). args is JSON.", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object AutoRigModel( [Desc( "Arguments as JSON" )] JsonElement args )
		=> Call( "AutoRigModel", args.GetRawText() );

	[McpTool( "animeditor_generate_from_text", "Bridge: generates an animation from a text prompt (only when the experimental AI plugin is enabled - check animeditor_status capabilities first).", ToolCategory.AnimEditor, Requires = Requirement, Writes = true )]
	public static object GenerateFromText(
		[Desc( "Text prompt describing the animation" )] string prompt,
		[Desc( "Options as JSON; omit for defaults" )] JsonElement options = default )
		=> Call( "GenerateFromText", prompt, options.ValueKind == JsonValueKind.Undefined ? "{}" : options.GetRawText() );

	// ---- vision -----------------------------------------------------------

	[McpTool( "animeditor_capture_viewport", "Screenshots the Animation Editor's own viewport to a file (editor_screenshot can't see its widget's scene).", ToolCategory.AnimEditor, Requires = Requirement )]
	public static object CaptureViewport( [Desc( "Output path ending in .png" )] string path )
		=> Call( "CaptureViewport", path );

	// ---- facade plumbing --------------------------------------------------

	/// <summary>
	/// Invokes a facade method by name (matched by argument count), passing args
	/// positionally, and returns its result. The facade returns JSON strings
	/// (surfaced verbatim) per the integration contract.
	/// </summary>
	static object Call( string method, params object[] args )
	{
		var facade = Facade
			?? throw new InvalidOperationException( "Animation Editor is not installed" );

		var candidates = facade.GetMethods( BindingFlags.Public | BindingFlags.Static )
			.Where( m => m.Name == method && m.GetParameters().Length == args.Length )
			.ToArray();

		if ( candidates.Length == 0 )
			throw new InvalidOperationException(
				$"The installed Animation Editor has no facade method '{method}' taking {args.Length} arg(s) - "
				+ "the API contract may have changed; use api_get_type 'AnimationEditorApi' to see the real surface." );

		object result;
		try
		{
			result = candidates[0].Invoke( null, args );
		}
		catch ( TargetInvocationException e ) when ( e.InnerException is not null )
		{
			throw e.InnerException;
		}

		// facade returns JSON strings - pass them straight through so the AI
		// sees the facade's exact result (including its { ok:false, error } form)
		return result is string s ? s : new { ok = true, result = result?.ToString() };
	}

	static object TryCall( Type facade, string method )
	{
		var mi = facade.GetMethod( method, BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes );
		try { return mi?.Invoke( null, null ); }
		catch { return null; }
	}

	static object ReadProperty( Type facade, string name )
	{
		try { return facade.GetProperty( name, BindingFlags.Public | BindingFlags.Static )?.GetValue( null ); }
		catch { return null; }
	}
}
