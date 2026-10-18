using Godot;

namespace Realm.Client;

public class MeshSubInfo
{
	public Mesh Mesh;
	public Transform3D RelativeTransform;
	public Material[] SurfaceMaterials;
	public Material MaterialOverride;
	public GeometryInstance3D.ShadowCastingSetting CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
	public float VisibilityRangeBegin = 0f;
	public float VisibilityRangeEnd = 0f;
	public float VisibilityRangeBeginMargin = 2.0f;
	public float VisibilityRangeEndMargin = 2.0f;
	public GeometryInstance3D.VisibilityRangeFadeModeEnum VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
}