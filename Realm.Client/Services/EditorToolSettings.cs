using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Services;

/// <summary>
/// The editor's live tool settings: which tool is active, the brush, placement, clump, grid and polar
/// values its sliders drive, the paste options, and the selection, pending and drag state those tools keep
/// between clicks. It is a plain state object with no behaviour beyond the paste-reflection cycle, so the
/// editor can hold one and hand out the values without carrying them as its own fields.
/// </summary>
public sealed class EditorToolSettings
{
	public const float MinBrushRadius = 1.0f;
	public const float MaxBrushRadius = 20.0f;
	public const float MinBrushStrength = 0.0f;
	public const float MaxBrushStrength = 10.0f;
	public const float MinPlacementScale = 0.25f;
	public const float MaxPlacementScale = 5.0f;
	public const float MinClumpCount = 1.0f;
	public const float MaxClumpCount = 20.0f;
	public const float MinClumpScale = 0.0f;
	public const float MaxClumpScale = 1.0f;
	public const float MaxClumpDensity = MaxClumpCount;

	public EditorTool ActiveTool { get; set; } = EditorTool.None;

	public float BrushRadius { get; set; } = 2.0f;
	public float BrushStrength { get; set; } = 3.0f;
	public int PaintTextureIndex { get; set; } = 3;
	public int CliffPaintTextureIndex { get; set; } = 1;

	public float WaterHeight { get; set; } = 0.9f;
	public bool IsWaterRemove { get; set; } = false;
	public float ExactHeight { get; set; } = 0.0f;

	public string PreviewType { get; set; } = "";
	public float PlacementRotation { get; set; } = 0.0f;
	public float PlacementScale { get; set; } = 1.0f;
	public int PlaceUnitPlayerIndex { get; set; } = 0;
	public bool PlaceUnitIsEnemy => PlaceUnitPlayerIndex != 0;
	public Godot.Vector3? TapeMeasureStart { get; set; }

	public float ClumpCount { get; set; } = 5.0f;
	public float ClumpScale { get; set; } = 0.3f;
	public bool ClumpMode { get; set; } = false;
	public bool RandomRotation { get; set; } = false;
	public bool RandomScale { get; set; } = false;

	public GridOverlayMode GridMode { get; set; } = GridOverlayMode.Off;
	public bool CameraBoundsVisible { get; set; } = false;
	public bool DisableShadows { get; set; } = false;
	public int PolarSpokeFolds { get; set; } = 4;
	public bool PolarOverlayVisible { get; set; } = false;
	public float PolarRingSpacing { get; set; } = 8.0f;
	public float PolarRadialStep { get; set; } = 90.0f;

	public bool PasteTextures { get; set; } = true;
	public bool PasteHeights { get; set; } = true;
	public bool PasteEntities { get; set; } = true;
	public bool PastePathing { get; set; } = true;
	public float PasteRotation { get; set; } = 0.0f;
	public PasteReflection PasteReflectionMode { get; set; } = PasteReflection.None;

	public int LastSelectionMinX { get; set; } = -1;
	public int LastSelectionMinZ { get; set; } = -1;
	public int LastSelectionMaxX { get; set; } = -1;
	public int LastSelectionMaxZ { get; set; } = -1;
	public bool LastSelectionBrushIsSquare { get; set; } = true;

	public int PendingCoordinateMinX { get; set; }
	public int PendingCoordinateMinZ { get; set; }
	public int PendingCoordinateMaxX { get; set; }
	public int PendingCoordinateMaxZ { get; set; }

	public Godot.Vector3 DragObjectStartHitPos { get; set; }
	public Vector2 DragStartMousePos { get; set; }
	public Godot.Vector3 DragStartGroundPos { get; set; }
	public bool DragObjectHasMoved { get; set; }

	public Rect2I? TerrainFlushRegion { get; set; }
	public float TerrainMeshRebuildPeriodMs { get; set; } = 33.3f;

	public int ScaleDialogTargetWidth { get; set; }

	public Godot.Vector3 LastRaycastPos { get; set; } = new(float.MinValue, float.MinValue, float.MinValue);

	/// <summary>Steps the paste reflection through none, horizontal, vertical and back.</summary>
	public PasteReflection CyclePasteReflection()
	{
		var next = PasteReflectionMode switch
		{
			PasteReflection.None => PasteReflection.Horizontal,
			PasteReflection.Horizontal => PasteReflection.Vertical,
			PasteReflection.Vertical => PasteReflection.None,
			_ => PasteReflection.None
		};
		PasteReflectionMode = next;
		return next;
	}
}
