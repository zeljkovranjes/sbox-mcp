using System;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Sandbox;
using SboxMcp.Registry;

namespace SboxMcp.Tools;

/// <summary>
/// sbox.game cloud asset access. Disabled by default - downloading workshop
/// content is an external action the user opts into from the tool browser.
/// </summary>
public static class CloudTools
{
	[McpTool( "cloud_search", "Searches sbox.game for cloud assets (models, materials, sounds...) to use in the project.", ToolCategory.Cloud, DisabledByDefault = true )]
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

	[McpTool( "cloud_install", "Downloads and installs a cloud asset into the project so it can be referenced by path.", ToolCategory.Cloud, Writes = true, DisabledByDefault = true )]
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
}
