using Godot;

namespace Realm.Client.Utils;

public static class MathUtils
{
    public static Vector3[] GetAabbCorners(Aabb mAabb)
    {
        Vector3 min = mAabb.Position;
        Vector3 max = mAabb.End;
        return new[]
        {
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z)
        };
    }

    public static void UpdateAabbWithMeshCorners(ref Aabb combinedAabb, ref bool hasAabb, MeshInstance3D meshInst, Transform3D relXform)
    {
        Aabb mAabb = meshInst.Mesh.GetAabb();
        Vector3[] corners = GetAabbCorners(mAabb);
        for (int i = 0; i < 8; i++)
        {
            Vector3 pt = relXform * corners[i];
            if (!hasAabb)
            {
                combinedAabb = new Aabb(pt, Vector3.Zero);
                hasAabb = true;
            }
            else
            {
                combinedAabb = combinedAabb.Expand(pt);
            }
        }
    }
}
