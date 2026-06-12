using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SboxMcp.Server;

namespace SboxMcp.Registry;

/// <summary>
/// A discovered [McpTool] method, with its generated descriptor and an
/// argument-binding invoker.
/// </summary>
public sealed class RegisteredTool
{
	public McpToolAttribute Meta { get; }
	public MethodInfo Method { get; }
	public McpToolDescriptor Descriptor { get; }

	internal RegisteredTool( McpToolAttribute meta, MethodInfo method )
	{
		Meta = meta;
		Method = method;
		Descriptor = new McpToolDescriptor( meta.Name, BuildDescription( meta ), SchemaGenerator.ForMethod( method ) );
	}

	static string BuildDescription( McpToolAttribute meta ) =>
		meta.Writes ? $"{meta.Description} (modifies project state)" : meta.Description;

	/// <summary>
	/// Binds JSON arguments to the method's parameters by name and invokes it.
	/// Throws ToolArgumentException on missing/unbindable arguments.
	/// </summary>
	public object Invoke( JsonElement? args )
	{
		var parameters = Method.GetParameters();
		var bound = new object[parameters.Length];

		for ( var i = 0; i < parameters.Length; i++ )
		{
			var p = parameters[i];

			if ( args is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty( p.Name, out var value )
				&& value.ValueKind != JsonValueKind.Null )
			{
				try
				{
					bound[i] = p.ParameterType == typeof( JsonElement )
						? value.Clone()
						: value.Deserialize( p.ParameterType, ToolRegistry.BindOptions );
				}
				catch ( Exception e ) when ( e is JsonException or NotSupportedException )
				{
					throw new ToolArgumentException(
						$"Argument '{p.Name}' could not be read as {p.ParameterType.Name}: {e.Message}", e );
				}
			}
			else if ( p.HasDefaultValue )
			{
				bound[i] = p.DefaultValue;
			}
			else
			{
				throw new ToolArgumentException( $"Missing required argument '{p.Name}'" );
			}
		}

		try
		{
			return Method.Invoke( null, bound );
		}
		catch ( TargetInvocationException e ) when ( e.InnerException is not null )
		{
			throw e.InnerException;
		}
	}
}

/// <summary>
/// Discovers [McpTool] static methods and serves them to the MCP server.
/// </summary>
public sealed class ToolRegistry
{
	internal static readonly JsonSerializerOptions BindOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter() }
	};

	static readonly JsonSerializerOptions ResultOptions = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	readonly List<RegisteredTool> _tools = new();
	readonly Dictionary<string, RegisteredTool> _byName = new( StringComparer.Ordinal );

	public IReadOnlyList<RegisteredTool> Tools => _tools;

	public void AddAssembly( Assembly assembly )
	{
		var methods = assembly.GetTypes()
			.Where( t => t.IsClass )
			.SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
			.Select( m => (Method: m, Meta: m.GetCustomAttribute<McpToolAttribute>()) )
			.Where( x => x.Meta is not null )
			.OrderBy( x => x.Meta.Name, StringComparer.Ordinal );

		foreach ( var (method, meta) in methods )
		{
			if ( _byName.ContainsKey( meta.Name ) )
				continue;

			var tool = new RegisteredTool( meta, method );
			_tools.Add( tool );
			_byName[meta.Name] = tool;
		}
	}

	public RegisteredTool Find( string name ) => _byName.GetValueOrDefault( name );

	/// <summary>
	/// Converts a tool's return value to the text sent back to the client.
	/// </summary>
	public static string FormatResult( object result ) => result switch
	{
		null => """{ "ok": true }""",
		string s => s,
		_ => JsonSerializer.Serialize( result, ResultOptions )
	};
}
