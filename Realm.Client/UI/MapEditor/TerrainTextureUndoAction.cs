using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public class TerrainTextureUndoAction : IEditorAction
{
	private readonly string _textureFileName;
	private readonly TerrainTextureSnapshot _before;
	private readonly TerrainTextureSnapshot _after;

	public TerrainTextureUndoAction(string textureFileName, TerrainTextureSnapshot before, TerrainTextureSnapshot after)
	{
		_textureFileName = textureFileName;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(TerrainTextureSnapshot snapshot)
	{
		if (string.IsNullOrEmpty(_textureFileName)) return;

		string tintHex = $"#{snapshot.Tint.ToHtml(false)}";
		UpdateGroundTerrain(snapshot, tintHex);
		UpdateMetadata(snapshot, tintHex);
	}

	private void UpdateGroundTerrain(TerrainTextureSnapshot snapshot, string tintHex)
	{
		if (Realm.Client.Core.GameHost.Instance == null || Realm.Client.Core.GameHost.Instance.GroundTerrain == null) return;
		
		Realm.Client.Core.GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(
			_textureFileName,
			snapshot.TileMode,
			snapshot.UvScale,
			snapshot.StochasticTileSize,
			snapshot.CrossFade,
			snapshot.Brightness,
			tintHex,
			snapshot.HeightScale,
			snapshot.HeightOffset,
			snapshot.CrevicePower,
			snapshot.NormalScale,
			snapshot.RoughnessScale
		);
	}

	private void UpdateMetadata(TerrainTextureSnapshot snapshot, string tintHex)
	{
		try
		{
			string wsPath = Services.MapWorkspaceService.GetActiveWorkspacePath();
			string metaPath = System.IO.Path.Combine(wsPath, "metadata.json");
			
			if (!System.IO.File.Exists(metaPath)) return;
			if (!MetadataService.Instance.TryLoadMetadata(wsPath, out var metadataRoot) || metadataRoot == null) return;
			
			var existing = metadataRoot.GetTerrainTexture(_textureFileName);
			if (existing != null)
			{
				existing.Brightness = snapshot.Brightness;
				existing.Tint = tintHex;
				existing.RoughnessScale = snapshot.RoughnessScale;
				existing.NormalScale = snapshot.NormalScale;
				existing.HeightScale = snapshot.HeightScale;
				existing.HeightOffset = snapshot.HeightOffset;
				existing.CrevicePower = snapshot.CrevicePower;
				existing.TileMode = snapshot.TileMode;
				existing.UvScale = snapshot.UvScale;
				existing.StochasticTileSize = snapshot.StochasticTileSize;
				existing.CrossFade = snapshot.CrossFade;
				existing.DefaultPathingCode = snapshot.DefaultPathingCode;
				existing.DecalBombingRules = snapshot.DecalBombingRules != null ? new System.Collections.Generic.List<ProceduralBombingDecalRule>(snapshot.DecalBombingRules) : new();
				existing.VfxBombingRules = snapshot.VfxBombingRules != null ? new System.Collections.Generic.List<ProceduralBombingVfxRule>(snapshot.VfxBombingRules) : new();
			}
			MetadataService.Instance.SaveMetadata(metaPath, metadataRoot);
			MapEditorHUD.Instance?.UpdateLastMetadataSyncTime(metaPath);
		}
		catch { }
	}
}