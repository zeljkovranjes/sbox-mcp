using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;
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

	[McpTool( "invoke_static", "Calls any public static method on any type (e.g. Sound.Play, Game.ActiveScene helpers). Args are JSON, matched to parameters by position; GameObject/Component params accept an id string.", ToolCategory.Editor, Writes = true )]
	public static object InvokeStatic(
		[Desc( "Type name or full name, e.g. 'Sandbox.Sound'" )] string typeName,
		[Desc( "Static method name" )] string method,
		[Desc( "Positional argument values as a JSON array, e.g. [\"sounds/x.sound\"]" )] JsonElement args = default )
	{
		var type = ResolveType( typeName );
		var candidates = type.GetMethods( Static )
			.Where( m => m.Name == method && !m.IsGenericMethodDefinition )
			.ToArray();

		return InvokeBest( candidates, null, args, $"{typeName}.{method}" );
	}

	[McpTool( "invoke_component_method", "Calls any public method on a component instance (e.g. Rigidbody.ApplyForce, PlayerController.Jump). Args are JSON positional; GameObject/Component params accept an id string.", ToolCategory.Component, Writes = true )]
	public static object InvokeComponentMethod(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Method name" )] string method,
		[Desc( "Positional argument values as a JSON array" )] JsonElement args = default )
	{
		var component = FindComponent( FindGameObject( gameObject ), type );
		var candidates = component.GetType().GetMethods( Instance )
			.Where( m => m.Name == method && !m.IsGenericMethodDefinition )
			.ToArray();

		return InvokeBest( candidates, component, args, $"{type}.{method}" );
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

	static object InvokeBest( MethodInfo[] candidates, object instance, JsonElement args, string label )
	{
		if ( candidates.Length == 0 )
			throw new InvalidOperationException( $"No method '{label}' - use api_get_type to list methods and their signatures" );

		var argCount = args.ValueKind == JsonValueKind.Array ? args.GetArrayLength() : 0;

		// try every count-compatible overload, binding args; use the first that
		// binds cleanly (so Play(string) is tried even if Play(SoundEvent) came first)
		var compatible = candidates
			.Where( m => m.GetParameters().Count( p => !p.IsOptional ) <= argCount && m.GetParameters().Length >= argCount )
			.ToArray();

		if ( compatible.Length == 0 )
			throw new InvalidOperationException(
				$"'{label}' has no overload taking {argCount} argument(s). Overloads: "
				+ string.Join( " | ", candidates.Select( m => $"({string.Join( ", ", m.GetParameters().Select( p => p.ParameterType.Name ) )})" ) ) );

		ArgumentException lastBindError = null;
		foreach ( var method in compatible )
		{
			var parameters = method.GetParameters();
			var bound = new object[parameters.Length];
			try
			{
				for ( var i = 0; i < parameters.Length; i++ )
				{
					bound[i] = i < argCount
						? Convert( args[i], parameters[i].ParameterType, parameters[i].Name )
						: parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Default( parameters[i].ParameterType );
				}
			}
			catch ( ArgumentException e )
			{
				lastBindError = e;
				continue; // this overload's args don't fit - try the next
			}

			try
			{
				var result = method.Invoke( instance, bound );
				return new { invoked = label, returned = method.ReturnType == typeof( void ) ? "void" : Present( result ) };
			}
			catch ( TargetInvocationException e ) when ( e.InnerException is not null )
			{
				throw e.InnerException;
			}
		}

		throw lastBindError ?? new ArgumentException( $"Arguments did not match any overload of '{label}' - use api_get_type to check signatures" );
	}

	/// <summary>Converts a JSON value to the target CLR type, resolving
	/// GameObject/Component parameters from an id string.</summary>
	static object Convert( JsonElement value, Type target, string paramName )
	{
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
