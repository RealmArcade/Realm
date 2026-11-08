using Godot;

namespace Realm.Client;

public class MeshSubInfo
{
	public Mesh Mesh = null!;
	public Transform3D RelativeTransform;
	public Material[] SurfaceMaterials = null!;
	public Material MaterialOverride = null!;
	public GeometryInstance3D.ShadowCastingSetting CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
	public float VisibilityRangeBegin = 0f;
	public float VisibilityRangeEnd = 0f;
	public float VisibilityRangeBeginMargin = 0f;
	public float VisibilityRangeEndMargin = 0f;
	public GeometryInstance3D.VisibilityRangeFadeModeEnum VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
	public bool HasProceduralAnimation;
	public Vector4 ProceduralParameters1;
	public Vector4 ProceduralParameters2;
	public Vector4 ProceduralParameters3;
	public Vector3 ProceduralRegionMin;
	public Vector3 ProceduralRegionMax;
}