using System;

namespace SboxMcp.Server;

/// <summary>
/// One connected MCP client, identified by the Mcp-Session-Id header.
/// </summary>
public sealed class McpSession
{
	public string Id { get; } = Guid.NewGuid().ToString( "N" );
	public string ClientName { get; internal set; } = "unknown";
	public DateTime ConnectedAt { get; } = DateTime.Now;
	public DateTime LastSeen { get; internal set; } = DateTime.Now;
	public int CallCount { get; internal set; }

	internal void Touch()
	{
		LastSeen = DateTime.Now;
		CallCount++;
	}
}
