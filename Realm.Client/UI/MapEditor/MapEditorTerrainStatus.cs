using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The status line under the cursor: the terrain point under the mouse, and the tool/place description for it. Runs every
/// frame, so it keeps the last rounded position, tool and place and only rebuilds the string when one of them moves.
/// </summary>
public sealed class MapEditorTerrainStatus
{
	private readonly Node _owner;
	private readonly Action<string> _setStatusText;

	private float _lastX = float.NaN;
	private float _lastY = float.NaN;
	private float _lastZ = float.NaN;
	private EditorTool? _lastTool;
	private string? _lastPlaceId;

	public MapEditorTerrainStatus(Node owner, Action<string> setStatusText)
	{
		_owner = owner;
		_setStatusText = setStatusText;
	}

	public void Update()
	{
		if (GameContext.Host is not { } host) return;

		var mousePos = _owner.GetViewport().GetMousePosition();
		if (!host.TryRaycastTerrainFromMousePosition(mousePos, out Vector3 pos)) return;

		float roundX = (float)Math.Round(pos.X, 1);
		float roundY = (float)Math.Round(pos.Y, 1);
		float roundZ = (float)Math.Round(pos.Z, 1);
		EditorTool currentTool = host.ActiveEditorTool;
		string currentPlaceId = host.ActivePlaceId;

		if (roundX == _lastX && roundY == _lastY && roundZ == _lastZ
			&& currentTool == _lastTool && currentPlaceId == _lastPlaceId)
		{
			return;
		}

		_lastX = roundX;
		_lastY = roundY;
		_lastZ = roundZ;
		_lastTool = currentTool;
		_lastPlaceId = currentPlaceId;

		GameContext.EditorService.CheckAndUpdateRaycast(pos);
		_setStatusText(host.GetTerrainStatusString(pos));
	}
}
