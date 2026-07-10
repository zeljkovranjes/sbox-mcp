using System;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using SboxMcp.Integration;
using SboxMcp.Registry;
using static SboxMcp.Tools.ToolHelpers;

namespace SboxMcp.Tools;

/// <summary>
/// sbox.game cloud asset access - search, install, and import cloud content
/// (models, maps, ...) into the project and scene.
/// </summary>
public static class CloudTools
{
	[McpTool( "cloud_search", "Searches sbox.game for cloud assets (models, maps, materials, sounds...) to use in the project. Add 'type:map' to find maps.", ToolCategory.Cloud )]
	public static async Task<object> Search(
		[Desc( "Search text, e.g. 'wooden crate' - add 'type:model' to filter" )] string query,
		int max = 20 )
	{
		var found = await Package.FindAsync( query, max );

		var packages = (found?.Packages ?? Array.Empty<Package>())
			.Select( p => new
			{
				ident = p.FullIdent,
				title = p.Title,
				type = p.TypeName,
				summary = p.Summary
			} )
			.ToArray();

		return (object)new { count = packages.Length, packages };
	}

	[McpTool( "cloud_install", "Downloads and installs a cloud asset into the project so it can be referenced by path.", ToolCategory.Cloud, Writes = true )]
	public static async Task<object> Install(
		[Desc( "Package ident from cloud_search, e.g. 'facepunch.wooden_crate'" )] string packageIdent )
	{
		// fetch first so an unknown ident gives a clear error before we install
		var package = await Package.FetchAsync( packageIdent, false )
			?? throw new InvalidOperationException( $"No cloud package '{packageIdent}' - check the ident with cloud_search" );

		var asset = await AssetSystem.InstallAsync( packageIdent );

		// InstallAsync returns null even on SUCCESS when the package has no single
		// primary asset (model packs, material collections) - don't report failure
		if ( asset is not null )
			return (object)new { installed = asset.Path, package = package.FullIdent, note = "reference it by this path, e.g. in component_set_property" };

		return (object)new
		{
			installed = package.FullIdent,
			primaryAsset = (string)null,
			note = "Installed. This package has no single primary asset (e.g. a pack) - use asset_search to find the individual files it added."
		};
	}

	[McpTool( "cloud_load_map", "Downloads a map from sbox.game and imports it into the current scene (creates a GameObject with a MapInstance loading the map geometry). Use a map package ident from cloud_search (add 'type:map').", ToolCategory.Cloud, Writes = true )]
	public static async Task<object> LoadMap(
		[Desc( "Map package ident, e.g. 'facepunch.datacore'" )] string packageIdent,
		[Desc( "Name for the map GameObject" )] string objectName = "Map" )
	{
		var package = await Package.FetchAsync( packageIdent, false )
			?? throw new InvalidOperationException( $"No cloud package '{packageIdent}' - check the ident with cloud_search (add 'type:map')" );

		// download + install the map's files
		var asset = await AssetSystem.InstallAsync( packageIdent );
		var mapName = asset?.Path ?? packageIdent;

		// the network awaits above dropped the active-scene scope InvokeTool set
		// up, so hop back onto the main thread (with the scene scope) to build the
		// map object - MainThreadDispatcher.Run does the main-thread + scope hop
		return await MainThreadDispatcher.Run( () =>
		{
			var session = RequireSession();

			using var undo = session.UndoScope( "MCP: load cloud map" ).WithGameObjectCreations().Push();

			var go = session.Scene.CreateObject();
			go.Name = string.IsNullOrWhiteSpace( objectName ) ? "Map" : objectName;

			var map = go.Components.Create<MapInstance>();
			map.MapName = mapName;

			return (object)new { downloaded = package.FullIdent, loaded = mapName, gameObject = go.Name, id = go.Id };
		} );
	}
}
