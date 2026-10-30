using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
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
		if (GameContext.Host == null || GameContext.Host.GroundTerrain == null) return;

		GameContext.Host.GroundTerrain.UpdateTextureParamDirect(
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
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			string metaPath = Path.Combine(wsPath, "metadata.json");

			if (!File.Exists(metaPath)) return;
			if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metadataRoot) || metadataRoot == null) return;

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
				existing.DecalBombingRules = snapshot.DecalBombingRules != null ? new List<ProceduralBombingDecalRule>(snapshot.DecalBombingRules) : new();
				existing.VfxBombingRules = snapshot.VfxBombingRules != null ? new List<ProceduralBombingVfxRule>(snapshot.VfxBombingRules) : new();
			}

			GameContext.MetadataService.SaveMetadata(metaPath, metadataRoot);
			GameContext.MapEditorHud?.UpdateLastMetadataSyncTime(metaPath);
		}
		catch
		{
		}
	}
}