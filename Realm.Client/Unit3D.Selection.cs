using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	private Node3D _rallyVisualsContainer = null!;
	private MeshInstance3D _rallyMarker = null!;
	private readonly List<MeshInstance3D> _rallyMarkersPool = new();
	private readonly List<MeshInstance3D> _rallyLinesPool = new();
	private readonly List<System.Numerics.Vector3> _rallyPointsBuffer = new();
	private Vector3 _lastRallyRenderPos = new Vector3(float.NaN, float.NaN, float.NaN);
	private readonly List<System.Numerics.Vector3> _lastRenderedRallyPoints = new();

	private MeshInstance3D _selectionRing = null!;
	private bool _isSelected = false;
	private MeshInstance3D _hoverRing = null!;
	private bool _isHovered = false;
	private MeshInstance3D _playerColorRing = null!;

	public bool IsPreview { get; set; } = false;

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (_selectionRing == null && _isSelected)
			{
				CreateSelectionRing();
			}

			if (_selectionRing != null)
			{
				if (_isSelected)
				{
					UpdateSelectionRingTransform();
				}

				_selectionRing.Visible = _isSelected;
			}

			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}

			if (IsBuilding && GameContext.PerspectiveFilter.CanSeeOwnedUi(Player))
			{
				UpdateRallyVisuals();
			}

			if (!value)
			{
				HidePathVisuals();
				if (_rallyVisualsContainer != null)
				{
					_rallyVisualsContainer.Visible = false;
				}
			}

		}
	}

	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			_isHovered = value;
			if (_hoverRing == null && _isHovered)
			{
				CreateHoverRing();
			}

			if (_hoverRing != null)
			{
				if (_isHovered)
				{
					UpdateSelectionRingTransform();
				}

				_hoverRing.Visible = _isHovered && !IsSelected;
			}
		}
	}

	public static void ClearCache()
	{
		Realm.Client.Utils.VisualSelectionHelper.ClearCache();
	}

	private void UpdateSelectionRingTransform()
	{
		Realm.Client.Utils.VisualSelectionHelper.UpdateSelectionRing(this, _selectionRing, _hoverRing, Scale, GetBaseObstacleRadius());
		UpdatePlayerColorRing();
	}

	private void UpdatePlayerColorRing()
	{
		if (IsPreview || IsBuilding)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		if (UI.GameSettings.QualityIdx == UI.GraphicsQuality.Low)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		float baseRadius = GetBaseObstacleRadius();
		if (baseRadius <= 0.001f)
		{
			if (_playerColorRing != null) _playerColorRing.Visible = false;
			return;
		}

		EnsurePlayerColorRing();

		Realm.Client.Utils.VisualSelectionHelper.CalculateSelectionRingParams(Scale, ModelPath, baseRadius, out float sx, out float ratio, out float worldRadius);

		float sy = Mathf.Abs(Scale.Y) > 0.001f ? Mathf.Abs(Scale.Y) : 1.0f;
		_playerColorRing.Mesh = Realm.Client.Utils.VisualSelectionHelper.GetOrCreatePlayerColorGlowMesh(worldRadius);
		_playerColorRing.Scale = new Vector3(1.0f / sx, 1.0f / sy, 1.0f / sx);
		_playerColorRing.Position = new Vector3(0f, 0.04f / sy, 0f);
		_playerColorRing.MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreatePlayerColorGlowMaterial(PlayerColor);
		_playerColorRing.Visible = !IsSelected && !IsHovered;
	}

	private void EnsurePlayerColorRing()
	{
		if (_playerColorRing != null) return;

		_playerColorRing = new MeshInstance3D
		{
			Name = "PlayerColorGlow_MeshInstance3D",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Visible = false
		};
		AddChild(_playerColorRing);
	}

	private void CreateHoverRing()
	{
		if (_hoverRing != null) return;
		_hoverRing = new MeshInstance3D
		{
			Name = "_hover_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreateHoverMaterial(),
			Visible = _isHovered && !IsSelected
		};
		AddChild(_hoverRing);
		UpdateSelectionRingTransform();
	}

	public void UpdateCollisionCircleScale(float ratio)
	{
		UpdateSelectionRingTransform();
		if (_selectionRing != null)
		{
			_selectionRing.Visible = _isSelected;
		}

		if (_hoverRing != null)
		{
			_hoverRing.Visible = _isHovered && !_isSelected;
		}
	}

	public void SetTemporarySelectionHighlight(bool highlight)
	{
		if (_selectionRing == null && highlight)
		{
			CreateSelectionRing();
		}

		if (_selectionRing != null)
		{
			if (highlight || _isSelected)
			{
				UpdateSelectionRingTransform();
			}

			_selectionRing.Visible = highlight || _isSelected;
		}
	}

	private void CreateSelectionRing()
	{
		if (_selectionRing != null) return;
		_selectionRing = new MeshInstance3D
		{
			Name = "_selection_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = Realm.Client.Utils.SharedMaterials.GetOrCreateSelectionMaterial(GetSelectionRingColor()),
			Visible = _isSelected
		};
		AddChild(_selectionRing);
		UpdateSelectionRingTransform();
	}

}
