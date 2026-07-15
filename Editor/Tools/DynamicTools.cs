using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;
using SboxMcp.Integration;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// The dynamic layer: call ANY method / read or write ANY property on the live
/// engine, resolved by reflection from the current build. This turns the whole
/// API surface into callable tools without hardcoding wrappers - discover with
/// api_search / api_get_type, then invoke here.
/// </summary>
public static class DynamicTools
{
	const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
	const BindingFlags Static = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

	[McpTool( "invoke_static", "Calls any public static method on any type (e.g. Sound.Play, Game.ActiveScene helpers). Args are JSON, matched to parameters by position; GameObject/Component params accept an id string. If the method returns a Task/Task<T> it is awaited and its result returned (not the Task object).", ToolCategory.Editor, Writes = true )]
	public static async Task<object> InvokeStatic(
		[Desc( "Type name or full name, e.g. 'Sandbox.Sound'" )] string typeName,
		[Desc( "Static method name" )] string method,
		[Desc( "Positional argument values as a JSON array, e.g. [\"sounds/x.sound\"]" )] JsonElement args = default )
	{
		var type = ResolveType( typeName );
		var candidates = type.GetMethods( Static )
			.Where( m => m.Name == method && !m.IsGenericMethodDefinition )
			.ToArray();

		return await InvokeBest( candidates, null, NormalizeArgs( args ), $"{typeName}.{method}" );
	}

	[McpTool( "invoke_component_method", "Calls any public method on a component instance (e.g. Rigidbody.ApplyForce, PlayerController.Jump). Args are JSON positional; GameObject/Component params accept an id string. If the method returns a Task/Task<T> it is awaited and its result returned.", ToolCategory.Component, Writes = true )]
	public static async Task<object> InvokeComponentMethod(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Method name" )] string method,
		[Desc( "Positional argument values as a JSON array" )] JsonElement args = default )
	{
		var component = FindComponent( FindGameObject( gameObject ), type );
		var candidates = component.GetType().GetMethods( Instance )
			.Where( m => m.Name == method && !m.IsGenericMethodDefinition )
			.ToArray();

		return await InvokeBest( candidates, component, NormalizeArgs( args ), $"{type}.{method}" );
	}

	[McpTool( "invoke_and_await_log", "Calls a public static method, then blocks until a console log line matches `logPattern` (regex) or it times out - collapses the invoke + sleep + editor_get_logs-poll pattern into one call, returning the matching line(s) and stack. NOTE: game-side Log.* during play may not reach the editor log stream; to observe live play-scene state prefer property_watch / get_component_property (which now read the live play scene).", ToolCategory.Editor, Writes = true )]
	public static async Task<object> InvokeAndAwaitLog(
		[Desc( "Type name or full name, e.g. 'MyGame.DebugHelpers'" )] string typeName,
		[Desc( "Static method name" )] string method,
		[Desc( "Regex to wait for in the console log, e.g. 'spawned \\d+ enemies'" )] string logPattern,
		[Desc( "Positional argument values as a JSON array" )] JsonElement args = default,
		[Desc( "Max seconds to wait for a matching log line" )] int timeoutSeconds = 10 )
	{
		if ( string.IsNullOrWhiteSpace( logPattern ) )
			throw new ArgumentException( "logPattern is required - it's the regex this tool waits for. For a plain invoke without waiting, use invoke_static." );

		Regex regex;
		try { regex = new Regex( logPattern, RegexOptions.IgnoreCase ); }
		catch ( Exception e ) { throw new ArgumentException( $"logPattern is not a valid regex: {e.Message}" ); }

		// snapshot the log cursor BEFORE firing so we only match new lines
		var sinceSeq = LogCapture.LatestSeq;

		var type = ResolveType( typeName );
		var candidates = type.GetMethods( Static )
			.Where( m => m.Name == method && !m.IsGenericMethodDefinition )
			.ToArray();
		var invokeResult = await InvokeBest( candidates, null, NormalizeArgs( args ), $"{typeName}.{method}" );

		var deadline = DateTime.Now.AddSeconds( Math.Clamp( timeoutSeconds, 1, 120 ) );
		while ( DateTime.Now < deadline )
		{
			var hit = LogCapture.Recent( 500, null, sinceSeq: sinceSeq )
				.Where( l => l.Message is not null && regex.IsMatch( l.Message ) )
				.Select( l => new { seq = l.Seq, time = l.Time.ToString( "HH:mm:ss" ), level = l.Level, message = l.Message, stack = l.Stack } )
				.FirstOrDefault();

			if ( hit is not null )
				return new { invoked = invokeResult, matched = true, log = hit };

			await Task.Delay( 150 );
		}

		return new
		{
			invoked = invokeResult,
			matched = false,
			note = $"No console line matched /{logPattern}/ within {timeoutSeconds}s. If this was a game-side Log.* emitted during play, the editor log stream may not capture it - read the resulting state with get_component_property / property_watch instead."
		};
	}

	[McpTool( "get_static_property", "Reads any public static property or field (e.g. Time.Now, Game.IsPlaying).", ToolCategory.Editor )]
	public static object GetStaticProperty(
		[Desc( "Type name or full name" )] string typeName,
		[Desc( "Property or field name" )] string member )
	{
		var type = ResolveType( typeName );

		if ( type.GetProperty( member, Static ) is PropertyInfo p && p.CanRead )
			return new { type = typeName, member, value = Present( p.GetValue( null ) ) };

		if ( type.GetField( member, Static ) is FieldInfo f )
			return new { type = typeName, member, value = Present( f.GetValue( null ) ) };

		throw new InvalidOperationException( $"'{typeName}' has no readable static property/field '{member}' - use api_get_type to list its members" );
	}

	[McpTool( "set_static_property", "Writes any public static property or field. Value is JSON.", ToolCategory.Editor, Writes = true )]
	public static object SetStaticProperty(
		[Desc( "Type name or full name" )] string typeName,
		[Desc( "Property or field name" )] string member,
		[Desc( "New value as JSON" )] JsonElement value )
	{
		var type = ResolveType( typeName );

		if ( type.GetProperty( member, Static ) is PropertyInfo p && p.CanWrite )
		{
			p.SetValue( null, Convert( value, p.PropertyType, member ) );
			return new { type = typeName, member, set = Present( p.GetValue( null ) ) };
		}

		if ( type.GetField( member, Static ) is FieldInfo f && !f.IsInitOnly && !f.IsLiteral )
		{
			f.SetValue( null, Convert( value, f.FieldType, member ) );
			return new { type = typeName, member, set = Present( f.GetValue( null ) ) };
		}

		throw new InvalidOperationException( $"'{typeName}' has no writable static property/field '{member}'" );
	}

	[McpTool( "get_component_property", "Reads ANY public property or field on a component instance by reflection - including plain C# members that aren't editor-serialized [Property] fields (component_get_properties only shows those).", ToolCategory.Component )]
	public static object GetComponentProperty(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property or field name" )] string property )
	{
		var component = FindComponent( FindGameObject( gameObject ), type );
		var t = component.GetType();

		if ( t.GetProperty( property, Instance ) is PropertyInfo p && p.CanRead )
			return new { type, property, value = Present( p.GetValue( component ) ) };

		if ( t.GetField( property, Instance ) is FieldInfo f )
			return new { type, property, value = Present( f.GetValue( component ) ) };

		throw new InvalidOperationException( $"'{type}' has no readable property/field '{property}' - use api_get_type to list its members" );
	}

	[McpTool( "set_component_property", "Writes ANY public property or field on a component instance by reflection. Use for plain C# members that AREN'T editor-serialized [Property] fields (e.g. SkinnedModelRenderer.PlayAnimationsInEditorScene) - for serialized fields prefer component_set_property, which is undoable and resolves resources/references. Value is JSON.", ToolCategory.Component, Writes = true )]
	public static object SetComponentProperty(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property or field name" )] string property,
		[Desc( "New value as JSON" )] JsonElement value )
	{
		var component = FindComponent( FindGameObject( gameObject ), type );
		var t = component.GetType();

		if ( t.GetProperty( property, Instance ) is PropertyInfo p && p.CanWrite )
		{
			p.SetValue( component, Convert( value, p.PropertyType, property ) );
			return new { type, property, set = Present( p.GetValue( component ) ) };
		}

		if ( t.GetField( property, Instance ) is FieldInfo f && !f.IsInitOnly && !f.IsLiteral )
		{
			f.SetValue( component, Convert( value, f.FieldType, property ) );
			return new { type, property, set = Present( f.GetValue( component ) ) };
		}

		throw new InvalidOperationException( $"'{type}' has no writable property/field '{property}' - use api_get_type to check (it may be read-only)" );
	}

	[McpTool( "property_watch", "Records a component property over time (a flight recorder): samples it at `hz` for `seconds` and returns the time series. The generic tool for diagnosing freezes, drift, velocities, or animgraph params - it polls from the tool side each sample, so it works during play as the values change AND avoids the pitfall that hot-loaded GameObjectSystem recorders never instantiate into an already-running scene.", ToolCategory.Component )]
	public static async Task<object> PropertyWatch(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property or field name (e.g. 'Velocity', 'WorldRotation')" )] string property,
		[Desc( "Samples per second (1-60)" )] int hz = 10,
		[Desc( "Duration in seconds (0.1-30)" )] double seconds = 3 )
	{
		hz = Math.Clamp( hz, 1, 60 );
		seconds = Math.Clamp( seconds, 0.1, 30 );
		var intervalMs = Math.Max( 1, (int)(1000.0 / hz) );

		var start = DateTime.Now;
		var deadline = start.AddSeconds( seconds );
		var samples = new List<object>();

		while ( DateTime.Now < deadline )
		{
			// read on the main thread each tick (the async continuation may resume
			// off it); the property changes live as the game runs
			var reading = await MainThreadDispatcher.Run( () =>
			{
				var component = FindComponent( FindGameObject( gameObject ), type );
				var t = component.GetType();

				if ( t.GetProperty( property, Instance ) is PropertyInfo p && p.CanRead )
					return Present( p.GetValue( component ) );
				if ( t.GetField( property, Instance ) is FieldInfo f )
					return Present( f.GetValue( component ) );

				throw new InvalidOperationException( $"'{type}' has no readable property/field '{property}'" );
			} );

			samples.Add( new { tMs = Math.Round( (DateTime.Now - start).TotalMilliseconds ), value = reading } );
			await Task.Delay( intervalMs );
		}

		return (object)new { property = $"{type}.{property}", hz, seconds, sampleCount = samples.Count, samples };
	}

	// ---- internals -------------------------------------------------------

	static Type ResolveType( string typeName )
	{
		// type library first (has friendly names for game/editor types)
		var desc = EditorTypeLibrary.GetTypes()
			.FirstOrDefault( t => t.TargetType is not null
				&& (string.Equals( t.TargetType.FullName, typeName, StringComparison.OrdinalIgnoreCase )
					|| string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase )) );
		if ( desc?.TargetType is not null )
			return desc.TargetType;

		// fall back to raw assembly reflection so low-level engine types
		// (RealTime, Time, ...) that api_search/api_dump surface are usable too
		var fromAssemblies = AppDomain.CurrentDomain.GetAssemblies()
			.Where( a => !a.IsDynamic )
			.SelectMany( a => { try { return a.GetExportedTypes(); } catch { return Array.Empty<Type>(); } } )
			.FirstOrDefault( t => string.Equals( t.FullName, typeName, StringComparison.OrdinalIgnoreCase )
				|| string.Equals( t.Name, typeName, StringComparison.OrdinalIgnoreCase ) );

		return fromAssemblies
			?? throw new InvalidOperationException( $"No type '{typeName}' - use api_search to find it (try the full name like 'Sandbox.{typeName}')" );
	}

	static async Task<object> InvokeBest( MethodInfo[] candidates, object instance, JsonElement[] args, string label )
	{
		if ( candidates.Length == 0 )
			throw new InvalidOperationException( $"No method '{label}' - use api_get_type to list methods and their signatures" );

		var argCount = args.Length;

		// try every count-compatible overload, binding args; use the first that
		// binds cleanly (so Play(string) is tried even if Play(SoundEvent) came first)
		// the caller only supplies non-`out` params; `out` params (TryGet(...) etc.)
		// are filled by the method and returned in `outputs`
		static int CallerParamCount( MethodInfo m ) => m.GetParameters().Count( p => !p.IsOut );

		var compatible = candidates
			.Where( m => m.GetParameters().Count( p => !p.IsOptional && !p.IsOut ) <= argCount && CallerParamCount( m ) >= argCount )
			.ToArray();

		if ( compatible.Length == 0 )
			throw new InvalidOperationException(
				$"'{label}' has no overload taking {argCount} argument(s). Overloads: "
				+ string.Join( " | ", candidates.Select( m => $"({string.Join( ", ", m.GetParameters().Select( p => (p.IsOut ? "out " : "") + p.ParameterType.Name ) )})" ) ) );

		ArgumentException lastBindError = null;
		foreach ( var method in compatible )
		{
			var parameters = method.GetParameters();
			var bound = new object[parameters.Length];
			var callerArg = 0;
			try
			{
				for ( var i = 0; i < parameters.Length; i++ )
				{
					var pi = parameters[i];
					if ( pi.IsOut )
						bound[i] = Default( pi.ParameterType.GetElementType() ?? pi.ParameterType ); // filled by the method
					else if ( callerArg < argCount )
						bound[i] = Convert( args[callerArg++], pi.ParameterType, pi.Name );
					else
						bound[i] = pi.HasDefaultValue ? pi.DefaultValue : Default( pi.ParameterType );
				}
			}
			catch ( ArgumentException e )
			{
				lastBindError = e;
				continue; // this overload's args don't fit - try the next
			}

			object result;
			try
			{
				result = method.Invoke( instance, bound );
			}
			catch ( TargetInvocationException e ) when ( e.InnerException is not null )
			{
				throw e.InnerException;
			}

			// await a returned Task/Task<T> so callers get the real result, not the
			// Task object (the single biggest reported time-sink)
			var awaited = await AwaitIfTask( result );

			var outputs = parameters
				.Where( p => p.IsOut )
				.ToDictionary( p => p.Name, p => Present( bound[p.Position] ) );

			return new
			{
				invoked = label,
				returned = method.ReturnType == typeof( void ) ? "void" : Present( awaited ),
				outputs = outputs.Count > 0 ? outputs : null
			};
		}

		throw lastBindError ?? new ArgumentException( $"Arguments did not match any overload of '{label}' - use api_get_type to check signatures" );
	}

	/// <summary>Converts a JSON value to the target CLR type, resolving
	/// GameObject/Component parameters from an id string.</summary>
	static object Convert( JsonElement value, Type target, string paramName )
	{
		// by-ref params (Vector3&, common in s&box physics/math APIs) can't be
		// deserialized directly - unwrap to the element type; reflection Invoke
		// accepts the value positionally for a ref/in parameter
		if ( target.IsByRef )
			target = target.GetElementType();

		var underlying = Nullable.GetUnderlyingType( target ) ?? target;

		if ( typeof( GameObject ).IsAssignableFrom( underlying ) )
			return FindGameObject( value.GetString() );

		if ( typeof( Component ).IsAssignableFrom( underlying ) )
		{
			// "goId:ComponentType" - or just an id to take the first matching component
			var parts = (value.GetString() ?? "").Split( ':', 2 );
			var go = FindGameObject( parts[0] );
			return parts.Length == 2 ? FindComponent( go, parts[1] )
				: go.Components.GetAll<Component>( FindMode.EverythingInSelf ).FirstOrDefault( c => underlying.IsInstanceOfType( c ) )
				?? throw new InvalidOperationException( $"'{go.Name}' has no {underlying.Name} component - pass 'goId:ComponentType'" );
		}

		// resources (Model/Material/SoundEvent/...) accept an asset path string,
		// but only the engine's Json options resolve paths - not plain System.Text.Json
		if ( typeof( Resource ).IsAssignableFrom( underlying ) && value.ValueKind == JsonValueKind.String )
		{
			try { return Json.Deserialize( JsonSerializer.Serialize( value.GetString() ), underlying ); }
			catch ( Exception e ) { throw new ArgumentException( $"Argument '{paramName}' could not be resolved to a {underlying.Name}: {e.Message}" ); }
		}

		try
		{
			return value.Deserialize( target, ToolRegistry.BindOptions );
		}
		catch ( Exception e ) when ( e is JsonException or NotSupportedException )
		{
			throw new ArgumentException( $"Argument '{paramName}' could not be read as {underlying.Name}: {e.Message}. Note: a Rotation array is a quaternion [x,y,z,w]; for euler use gameobject_set_transform." );
		}
	}

	static object Default( Type t ) => t.IsValueType ? Activator.CreateInstance( t ) : null;

	/// <summary>Presents a return value in a client-friendly way.</summary>
	static object Present( object value ) => value switch
	{
		null => null,
		GameObject go => new { _type = "gameobject", id = go.Id, name = go.Name },
		Component c => new { _type = "component", type = c.GetType().Name, go = c.GameObject?.Id },
		string or bool or int or long or float or double => value,
		Enum e => e.ToString(),
		_ => value.ToString()
	};
}
