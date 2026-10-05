using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Realm.Shared.Metadata;
using Realm.Shared.Serialization;

namespace Realm.Shared.Terrain;

public struct Vector3Data
{
	[JsonPropertyName("X")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
	public float Y { get; set; }

	[JsonPropertyName("Z")]
	public float Z { get; set; }

	public Vector3Data(float x, float y, float z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public static implicit operator Vector3(Vector3Data v) => new(v.X, v.Y, v.Z);
	public static implicit operator Vector3Data(Vector3 v) => new(v.X, v.Y, v.Z);

	public static Vector3Data Zero => new(0f, 0f, 0f);
	public static Vector3Data One => new(1f, 1f, 1f);
}

public struct Vector2Data
{
	[JsonPropertyName("X")]
	public float X { get; set; }

	[JsonPropertyName("Y")]
	public float Y { get; set; }

	public Vector2Data(float x, float y)
	{
		X = x;
		Y = y;
	}

	public static implicit operator Vector2(Vector2Data v) => new(v.X, v.Y);
	public static implicit operator Vector2Data(Vector2 v) => new(v.X, v.Y);

	public static Vector2Data Zero => new(0f, 0f);
	public static Vector2Data One => new(1f, 1f);
}

public class CoordinateSaveData
{
	public string Name { get; set; } = string.Empty;
	public float MinX { get; set; }
	public float MinZ { get; set; }
	public float MaxX { get; set; }
	public float MaxZ { get; set; }
}

public class UnitSaveData
{
	public string UnitId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; } = 1.0f;
	public bool IsEnemy { get; set; }
	public int Player { get; set; }
}

public class PropSaveData
{
	public string PropId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationY { get; set; }
	public float Scale { get; set; } = 1.0f;
}

public class DecalSaveData
{
	public string DecalId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationX { get; set; }
	public float RotationY { get; set; }
	public float RotationZ { get; set; }
	public float Scale { get; set; } = 1.0f;
}

public class VfxSaveData
{
	public string VfxId { get; set; } = string.Empty;
	public float PosX { get; set; }
	public float PosY { get; set; }
	public float PosZ { get; set; }
	public float RotationX { get; set; }
	public float RotationY { get; set; }
	public float RotationZ { get; set; }
	public float ScaleX { get; set; } = 1.0f;
	public float ScaleY { get; set; } = 1.0f;
	public float ScaleZ { get; set; } = 1.0f;
	public float NormalOffset { get; set; }
	public VfxAttachmentConfig? Config { get; set; }
}

public class MapSaveData
{
	public int Width { get; set; }
	public int Depth { get; set; }

	public List<UnitSaveData>? Units { get; set; }
	public List<PropSaveData>? Props { get; set; }
	public List<DecalSaveData>? Decals { get; set; }
	public List<VfxSaveData>? Vfx { get; set; }
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
	public string? SkyboxPath { get; set; }
	public List<CoordinateSaveData>? Coordinates { get; set; }

	public static string GenerateJsonSchema()
	{
		return RealmJsonSchemaExporter.GenerateJsonSchema(typeof(MapSaveData));
	}
}
