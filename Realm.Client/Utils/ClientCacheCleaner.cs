using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.InGame;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;

namespace Realm.Client.Utils;

public static class ClientCacheCleaner
{
	public static void ClearAllCaches()
	{
		UIStyle.ClearCache();
		MinimapOverlay.ClearCache();
		CommandPanel.ClearCache();
		InventoryPanel.ClearCache();
		ReplayViewerPanel.ClearCache();
		RuntimeTerrain.ClearCache();
		AssetThumbnailProvider.ClearCache();
		RtexIconLoader.ClearCache();
		ModelCache.Clear();
		ModelShaderManager.ClearCache();
		VfxShaderManager.ClearCache();
		ProceduralVfxMeshGenerator.ClearCache();
		VisualProjectile3D.ClearCache();
		SpawnDeathShaderManager.ClearCache();
		Prop3D.ClearCache();
		Unit3D.ClearCache();
		ProceduralVfxInstance3D.ClearCache();
		FXService.ClearCache();
		GameHost.ClearCache();
		GameContext.EditorService?.ClearDecalCaches();
	}
}
