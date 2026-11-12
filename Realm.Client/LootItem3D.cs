using Godot;
using Realm.Ecs;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;

namespace Realm.Client;

[GlobalClass]
public partial class LootItem3D : Node3D
{
	public int UniqueId { get; set; }
	public string ItemTypeId { get; set; } = string.Empty;
	public int Charges { get; set; } = 1;
	public float Lifetime { get; set; }
	public float RemainingLifetime { get; set; }
	public float PickupRadius { get; set; } = 2.0f;
	public bool AutoPickup { get; set; } = true;
	public int OwnerPlayerIndex { get; set; } = -1;
	public bool IsDespawned { get; set; }
	public Entity Entity { get; set; }

	private const float PedestalTopHeight = 0.08f;
	private const float ModelRestingHeight = 0.1f;

	private Node3D? _visualRoot;
	private float _hoverTimer;

	public void Initialize(int id, string itemTypeId, int charges, float lifetime, float pickupRadius, bool autoPickup, int ownerPlayerIndex)
	{
		UniqueId = id;
		ItemTypeId = itemTypeId;
		Charges = charges;
		Lifetime = lifetime;
		RemainingLifetime = lifetime;
		PickupRadius = pickupRadius;
		AutoPickup = autoPickup;
		OwnerPlayerIndex = ownerPlayerIndex;
		IsDespawned = false;

		CreateVisuals();
	}

	private void CreateVisuals()
	{
		_visualRoot = new Node3D();
		_visualRoot.Name = "VisualRoot_Node3D";
		AddChild(_visualRoot);

		var meshInstance = new MeshInstance3D();
		meshInstance.Name = "MeshInstance_MeshInstance3D";
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		var cylinderMesh = new CylinderMesh
		{
			TopRadius = 0.3f,
			BottomRadius = 0.4f,
			Height = 0.08f
		};
		meshInstance.Mesh = cylinderMesh;
		meshInstance.Position = new Vector3(0, PedestalTopHeight * 0.5f, 0);

		var pedestalMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.85f, 0.7f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(0.7f, 0.5f, 0.1f)
		};
		meshInstance.MaterialOverride = pedestalMat;
		_visualRoot.AddChild(meshInstance);

		ItemMetadata? itemMetadata = null;
		if (GameContext.RegistryService?.ItemRegistry != null)
		{
			GameContext.RegistryService.ItemRegistry.TryGetValue(ItemTypeId, out itemMetadata);
		}

		if (AddModelVisual(itemMetadata?.ModelPath)) return;

		string? iconPath = string.IsNullOrEmpty(itemMetadata?.IconPath) ? null : itemMetadata!.IconPath;

		if (!string.IsNullOrEmpty(iconPath) && ResourceLoader.Exists(iconPath))
		{
			var texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
			if (texture != null)
			{
				var sprite = new Sprite3D
				{
					Texture = texture,
					Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
					PixelSize = 0.012f,
					Position = new Vector3(0, 0.55f, 0),
					NoDepthTest = false
				};
				_visualRoot.AddChild(sprite);
				return;
			}
		}

		var gemInstance = new MeshInstance3D();
		gemInstance.Name = "GemInstance_MeshInstance3D";
		var boxMesh = new BoxMesh
		{
			Size = new Vector3(0.35f, 0.35f, 0.35f)
		};
		gemInstance.Mesh = boxMesh;
		gemInstance.Position = new Vector3(0, 0.45f, 0);
		gemInstance.RotationDegrees = new Vector3(45f, 45f, 0f);

		var gemMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.2f, 0.8f, 0.9f),
			EmissionEnabled = true,
			Emission = new Color(0.2f, 0.8f, 0.9f)
		};
		gemInstance.MaterialOverride = gemMat;
		_visualRoot.AddChild(gemInstance);
	}

	/// <summary>
	///     Puts the item's authored model on the pedestal, resting on it rather than sunk into the ground whatever the
	///     mesh's own origin is. The pedestal stays, so a dropped item still reads as a pickup marker on the ground.
	/// </summary>
	/// <param name="modelPath">The model the item's template states, or nothing when it states none.</param>
	/// <returns>True when a model was placed, which is what makes the icon and the gem the fallbacks.</returns>
	private bool AddModelVisual(string? modelPath)
	{
		if (string.IsNullOrEmpty(modelPath) || _visualRoot == null) return false;

		Node? model = ModelCache.GetModel(modelPath);
		if (model == null)
		{
			return false;
		}

		if (model is not Node3D modelNode)
		{
			model.QueueFree();
			return false;
		}

		(float _, float restingOffset) = ModelCache.CalculateModelBounds(modelPath);
		modelNode.Name = "LootModel_Node3D";
		modelNode.Position = new Vector3(0f, PedestalTopHeight + restingOffset + ModelRestingHeight, 0f);
		_visualRoot.AddChild(modelNode);
		return true;
	}

	public override void _Process(double delta)
	{
		if (_visualRoot == null || !IsInstanceValid(_visualRoot) || !Visible) return;

		var camera = GetViewport()?.GetCamera3D();
		if (camera != null && GodotObject.IsInstanceValid(camera) && !camera.IsPositionInFrustum(GlobalPosition))
		{
			return;
		}

		_hoverTimer += (float)delta;
		_visualRoot.RotationDegrees = new Vector3(0, _hoverTimer * 50f % 360f, 0);
	}
}
