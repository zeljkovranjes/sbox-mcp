using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Editor;
using Sandbox;
using static Sandbox.Internal.GlobalToolsNamespace;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

public static class ComponentTools
{
	[McpTool( "component_list_types", "Searches available component types (ModelRenderer, Rigidbody, custom components...).", ToolCategory.Component )]
	public static object ListTypes(
		[Desc( "Name filter (case-insensitive substring); omit for all" )] string query = null,
		int max = 50 )
	{
		var types = EditorTypeLibrary.GetTypes<Component>()
			.Where( t => !t.IsAbstract )
			.Where( t => query is null
				|| t.Name.Contains( query, StringComparison.OrdinalIgnoreCase )
				|| (t.FullName?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false) )
			.OrderBy( t => t.Name )
			.Take( max )
			.Select( t => new { name = t.Name, fullName = t.FullName, title = t.Title, group = t.Group } )
			.ToArray();

		return new { count = types.Length, types };
	}

	[McpTool( "component_add", "Adds a component to a GameObject.", ToolCategory.Component, Writes = true )]
	public static object Add(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name, e.g. 'ModelRenderer'" )] string type )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var typeDesc = FindComponentType( type );

		using var undo = session.UndoScope( $"MCP: add {typeDesc.Name}" ).WithComponentCreations().Push();

		var component = go.Components.Create( typeDesc )
			?? throw new InvalidOperationException( $"'{typeDesc.Name}' could not be instantiated as a component" );

		return new { added = component.GetType().Name, to = go.Name, properties = component.Serialize() };
	}

	[McpTool( "component_remove", "Removes a component from a GameObject.", ToolCategory.Component, Writes = true )]
	public static object Remove(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );
		var name = component.GetType().Name;

		using var undo = session.UndoScope( $"MCP: remove {name}" )
			.WithComponentDestructions( new[] { component } ).Push();

		component.Destroy();
		return new { removed = name, from = go.Name };
	}

	[McpTool( "component_get_properties", "Gets all serialized properties of a component as JSON.", ToolCategory.Component )]
	public static object GetProperties(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type )
	{
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		return new
		{
			type = component.GetType().Name,
			gameObject = go.Name,
			enabled = component.Enabled,
			properties = component.Serialize()
		};
	}

	[McpTool( "component_set_property", "Sets one property on a component. Value is raw JSON. Resource properties accept an asset path string. Component/GameObject reference properties (e.g. a controller's Renderer/Target) accept a GameObject id, or 'goId:ComponentType' to pick a specific component.", ToolCategory.Component, Writes = true )]
	public static object SetProperty(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property name as shown by component_get_properties" )] string property,
		[Desc( "New value as JSON (or an id string for reference properties)" )] JsonElement value )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		if ( component.Serialize() is not JsonObject node )
			throw new InvalidOperationException( $"Component '{type}' did not serialize to an object" );

		var key = node.Select( kv => kv.Key )
			.FirstOrDefault( k => string.Equals( k, property, StringComparison.OrdinalIgnoreCase ) )
			?? throw new InvalidOperationException(
				$"Component '{component.GetType().Name}' has no property '{property}'. Available: "
				+ string.Join( ", ", node.Select( kv => kv.Key ).Where( k => !k.StartsWith( "__" ) ) ) );

		// resolve the real CLR property so reference types can be set directly
		var propInfo = component.GetType().GetProperty( key,
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase );

		using var undo = session.UndoScope( $"MCP: set {key}" )
			.WithComponentChanges( new[] { component } ).Push();

		ApplyProperty( component, node, key, propInfo, value );

		return new { set = key, on = component.GetType().Name, now = component.Serialize() };
	}

	[McpTool( "component_set_properties", "Sets several properties on one component in a single call. Values follow the same rules as component_set_property (asset paths for resources, ids for references).", ToolCategory.Component, Writes = true )]
	public static object SetProperties(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Object mapping property name -> value, e.g. {\"WalkSpeed\":200,\"ThirdPerson\":true}" )] JsonElement properties )
	{
		if ( properties.ValueKind != JsonValueKind.Object )
			throw new ArgumentException( "properties must be a JSON object of name -> value" );

		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		using var undo = session.UndoScope( $"MCP: set {type} properties" )
			.WithComponentChanges( new[] { component } ).Push();

		var applied = new List<string>();
		foreach ( var kv in properties.EnumerateObject() )
		{
			if ( component.Serialize() is not JsonObject node )
				throw new InvalidOperationException( $"Component '{type}' did not serialize to an object" );

			var key = node.Select( n => n.Key )
				.FirstOrDefault( k => string.Equals( k, kv.Name, StringComparison.OrdinalIgnoreCase ) );
			if ( key is null )
				throw new InvalidOperationException( $"Component '{component.GetType().Name}' has no property '{kv.Name}'" );

			var propInfo = component.GetType().GetProperty( key,
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase );

			ApplyProperty( component, node, key, propInfo, kv.Value );
			applied.Add( key );
		}

		return new { set = applied.ToArray(), on = component.GetType().Name, now = component.Serialize() };
	}

	/// <summary>Applies one value to a component property, resolving reference
	/// types directly and everything else via a minimal deserialize.</summary>
	static void ApplyProperty( Component component, JsonObject node, string key, System.Reflection.PropertyInfo propInfo, JsonElement value )
	{
		// Component / GameObject reference properties don't round-trip through
		// JSON reliably in the editor (ComponentReference.Resolve needs the
		// active scene) - resolve the target ourselves and set it directly.
		if ( propInfo is not null && propInfo.CanWrite && IsReferenceType( propInfo.PropertyType ) )
		{
			propInfo.SetValue( component, ResolveReference( value, propInfo.PropertyType ) );
			return;
		}

		// lists/arrays of references (waypoints, targets...) - resolve each element
		if ( propInfo is not null && propInfo.CanWrite && ReferenceElementType( propInfo.PropertyType ) is System.Type elementType )
		{
			propInfo.SetValue( component, ResolveReferenceCollection( value, propInfo.PropertyType, elementType ) );
			return;
		}

		// enums: accept the name (case-insensitive) OR the numeric ordinal and set
		// directly. The JSON serializer silently no-ops on some enum-name forms and
		// then writes a value that fails to deserialize on the next play-clone - a
		// silent failure plus delayed corruption. Setting via reflection (and hard-
		// erroring on an invalid name) avoids both.
		if ( propInfo is not null && propInfo.CanWrite
			&& (Nullable.GetUnderlyingType( propInfo.PropertyType ) ?? propInfo.PropertyType) is System.Type enumType && enumType.IsEnum )
		{
			object parsed;
			if ( value.ValueKind == JsonValueKind.String )
			{
				var name = value.GetString();
				if ( !Enum.TryParse( enumType, name, ignoreCase: true, out parsed ) )
					throw new InvalidOperationException(
						$"'{name}' is not a valid {enumType.Name} value for '{key}'. Options: {string.Join( ", ", Enum.GetNames( enumType ) )}" );
			}
			else if ( value.ValueKind == JsonValueKind.Number )
			{
				parsed = Enum.ToObject( enumType, value.GetInt64() );
			}
			else
			{
				throw new InvalidOperationException(
					$"'{key}' is a {enumType.Name} enum - pass its name (e.g. \"{Enum.GetNames( enumType ).FirstOrDefault()}\") or numeric value, not {value.ValueKind}." );
			}

			propInfo.SetValue( component, parsed );
			return;
		}

		// apply ONLY the target property: deserializing the full snapshot would
		// re-apply every other property too, and a resource that is still
		// loading serializes as null - the round trip would clobber it
		var minimal = new JsonObject();
		if ( node["__type"] is JsonNode typeNode ) minimal["__type"] = typeNode.DeepClone();
		if ( node["__guid"] is JsonNode guidNode ) minimal["__guid"] = guidNode.DeepClone();
		minimal[key] = value.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse( value.GetRawText() );

		component.DeserializeImmediately( minimal );
	}

	static bool IsReferenceType( System.Type t ) =>
		typeof( Component ).IsAssignableFrom( t ) || t == typeof( GameObject );

	/// <summary>Element type if <paramref name="t"/> is a List&lt;ref&gt; or ref[], else null.</summary>
	static System.Type ReferenceElementType( System.Type t )
	{
		if ( t.IsArray && IsReferenceType( t.GetElementType() ) )
			return t.GetElementType();

		if ( t.IsGenericType && t.GetGenericTypeDefinition() == typeof( List<> ) && IsReferenceType( t.GetGenericArguments()[0] ) )
			return t.GetGenericArguments()[0];

		return null;
	}

	static object ResolveReferenceCollection( JsonElement value, System.Type collectionType, System.Type elementType )
	{
		if ( value.ValueKind != JsonValueKind.Array )
			throw new ArgumentException( $"This property is a list of references - pass a JSON array of ids (or 'goId:ComponentType')" );

		var items = value.EnumerateArray().Select( e => ResolveReference( e, elementType ) ).ToList();

		if ( collectionType.IsArray )
		{
			var arr = Array.CreateInstance( elementType, items.Count );
			for ( var i = 0; i < items.Count; i++ ) arr.SetValue( items[i], i );
			return arr;
		}

		var list = (System.Collections.IList)Activator.CreateInstance( collectionType );
		foreach ( var item in items ) list.Add( item );
		return list;
	}

	/// <summary>
	/// Resolves a JSON value to a Component or GameObject. Accepts a plain id
	/// string, "goId:ComponentType", or a serialized reference object
	/// ({go, component_id, component_type}). Null/empty clears the reference.
	/// </summary>
	static object ResolveReference( JsonElement value, System.Type targetType )
	{
		string idOrSpec = null;

		if ( value.ValueKind == JsonValueKind.Null )
			return null;

		if ( value.ValueKind == JsonValueKind.String )
			idOrSpec = value.GetString();
		else if ( value.ValueKind == JsonValueKind.Object )
		{
			// serialized-reference form
			var compId = value.TryGetProperty( "component_id", out var c ) ? c.GetString() : null;
			var goId = value.TryGetProperty( "go", out var g ) ? g.GetString() : null;
			var compType = value.TryGetProperty( "component_type", out var t ) ? t.GetString() : null;

			if ( targetType == typeof( GameObject ) )
				idOrSpec = goId;
			else if ( goId is not null )
				idOrSpec = compType is not null ? $"{goId}:{compType}" : goId;
			else if ( compId is not null )
				idOrSpec = compId; // fall back to a component id
		}

		if ( string.IsNullOrWhiteSpace( idOrSpec ) )
			return null;

		if ( targetType == typeof( GameObject ) )
			return FindGameObject( idOrSpec );

		// component: "goId:ComponentType" or an id that identifies the owning object
		var parts = idOrSpec.Split( ':', 2 );
		var owner = FindGameObject( parts[0] );

		if ( parts.Length == 2 )
			return FindComponent( owner, parts[1] );

		return owner.Components.GetAll<Component>( FindMode.EverythingInSelf ).FirstOrDefault( x => targetType.IsInstanceOfType( x ) )
			?? throw new InvalidOperationException(
				$"'{owner.Name}' has no {targetType.Name} component - pass 'goId:ComponentType' to name a specific one" );
	}

	[McpTool( "component_get_property", "Reads a single property value from a component (cheaper than dumping all of them).", ToolCategory.Component )]
	public static object GetProperty(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		[Desc( "Property name" )] string property )
	{
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		if ( component.Serialize() is not JsonObject node )
			throw new InvalidOperationException( $"Component '{type}' did not serialize to an object" );

		var match = node.FirstOrDefault( kv => string.Equals( kv.Key, property, StringComparison.OrdinalIgnoreCase ) );
		if ( match.Key is null )
			throw new InvalidOperationException(
				$"Component '{component.GetType().Name}' has no property '{property}' - use component_get_properties to list them" );

		return new { on = component.GetType().Name, property = match.Key, value = match.Value };
	}

	[McpTool( "model_get_bone", "Gets a bone of a SkinnedModelRenderer as a GameObject you can parent things to (e.g. attach a weapon to a hand bone) plus its world transform. Omit boneName to list all the model's bone names first.", ToolCategory.Component, Writes = true )]
	public static object GetBone(
		[Desc( "GameObject id or unique name (must have a SkinnedModelRenderer)" )] string gameObject,
		[Desc( "Bone name; omit to list all bones on the model" )] string boneName = null )
	{
		var go = FindGameObject( gameObject );
		var renderer = go.Components.Get<SkinnedModelRenderer>()
			?? throw new InvalidOperationException( $"'{go.Name}' has no SkinnedModelRenderer" );

		var bones = renderer.Model?.Bones?.AllBones
			?? throw new InvalidOperationException( "The renderer has no model / bones loaded" );

		if ( string.IsNullOrEmpty( boneName ) )
			return new { boneCount = bones.Count, bones = bones.Select( b => b.Name ).ToArray() };

		// bone GameObjects only exist when CreateBoneObjects is enabled
		renderer.CreateBoneObjects = true;

		var boneGo = renderer.GetBoneObject( boneName )
			?? throw new InvalidOperationException(
				$"No bone '{boneName}'. Available: {string.Join( ", ", bones.Select( b => b.Name ).Take( 40 ) )}" );

		return new { bone = boneName, gameObjectId = boneGo.Id, worldPosition = V( boneGo.WorldPosition ), worldRotation = A( boneGo.WorldRotation ) };
	}

	[McpTool( "model_get_attachment", "Gets a model attachment point's world transform (attachments are named points authored on the model, e.g. 'hand', 'eyes', 'muzzle').", ToolCategory.Component )]
	public static object GetAttachment(
		[Desc( "GameObject id or unique name (must have a SkinnedModelRenderer)" )] string gameObject,
		[Desc( "Attachment name" )] string attachmentName )
	{
		var go = FindGameObject( gameObject );
		var renderer = go.Components.Get<SkinnedModelRenderer>()
			?? throw new InvalidOperationException( $"'{go.Name}' has no SkinnedModelRenderer" );

		var attachment = renderer.GetAttachment( attachmentName, true )
			?? throw new InvalidOperationException( $"No attachment '{attachmentName}' on this model - use modeldoc_get to see its attachments" );

		return new { attachment = attachmentName, worldPosition = V( attachment.Position ), worldRotation = A( attachment.Rotation ) };
	}

	[McpTool( "model_attach_to_bone", "Attaches (parents) a GameObject to a character's bone so it follows the animation - equip a weapon to 'hand_R', a hat to 'head'. Enables bone GameObjects automatically and snaps the item onto the bone by default.", ToolCategory.Component, Writes = true )]
	public static object AttachToBone(
		[Desc( "GameObject to attach (the weapon / item / accessory)" )] string gameObject,
		[Desc( "Character GameObject that has a SkinnedModelRenderer" )] string character,
		[Desc( "Bone name, e.g. 'hand_R' - use model_get_bone (omit boneName) to list them" )] string boneName,
		[Desc( "Keep the item's current world position instead of snapping it onto the bone" )] bool keepWorldPosition = false )
	{
		var session = RequireSession();
		var item = FindGameObject( gameObject );
		var charGo = FindGameObject( character );

		var renderer = charGo.Components.Get<SkinnedModelRenderer>()
			?? throw new InvalidOperationException( $"'{charGo.Name}' has no SkinnedModelRenderer" );

		renderer.CreateBoneObjects = true;
		var boneGo = renderer.GetBoneObject( boneName )
			?? throw new InvalidOperationException( $"No bone '{boneName}' - use model_get_bone (omit boneName) to list the model's bones" );

		using var undo = session.UndoScope( $"MCP: attach {item.Name} to {boneName}" )
			.WithGameObjectChanges( item, GameObjectUndoFlags.All ).Push();

		item.SetParent( boneGo, keepWorldPosition );

		return new { attached = item.Name, toBone = boneName, onCharacter = charGo.Name, snapped = !keepWorldPosition };
	}

	[McpTool( "physics_add_joint", "Connects two GameObjects with a physics joint - hinge (doors/levers), ball (ragdoll/chains), slider (pistons/elevators), fixed (weld together). Adds the joint to the first object and wires the second as the connected body. Both usually need a Rigidbody (or one static).", ToolCategory.Component, Writes = true )]
	public static object AddJoint(
		[Desc( "First GameObject - gets the joint component (usually has a Rigidbody)" )] string gameObject,
		[Desc( "Second GameObject to connect to (the other/anchor body)" )] string connectedTo,
		[Desc( "Joint type: 'hinge', 'ball', 'slider', or 'fixed'" )] string type = "fixed" )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var other = FindGameObject( connectedTo );

		var jointType = (type ?? "fixed").ToLowerInvariant() switch
		{
			"hinge" => "HingeJoint",
			"ball" => "BallJoint",
			"slider" => "SliderJoint",
			"fixed" or "weld" => "FixedJoint",
			_ => throw new ArgumentException( "type must be 'hinge', 'ball', 'slider' or 'fixed'" )
		};

		using var undo = session.UndoScope( $"MCP: add {jointType}" ).WithComponentCreations().Push();

		var joint = go.Components.Create( FindComponentType( jointType ) ) as Joint
			?? throw new InvalidOperationException( $"Could not create a {jointType}" );
		joint.Body = other;

		return new { added = jointType, on = go.Name, connectedTo = other.Name };
	}

	[McpTool( "component_set_enabled", "Enables or disables a component.", ToolCategory.Component, Writes = true )]
	public static object SetEnabled(
		[Desc( "GameObject id or unique name" )] string gameObject,
		[Desc( "Component type name" )] string type,
		bool enabled )
	{
		var session = RequireSession();
		var go = FindGameObject( gameObject );
		var component = FindComponent( go, type );

		using var undo = session.UndoScope( $"MCP: component enabled {enabled}" )
			.WithComponentChanges( new[] { component } ).Push();

		component.Enabled = enabled;
		return new { type = component.GetType().Name, enabled = component.Enabled };
	}
}
