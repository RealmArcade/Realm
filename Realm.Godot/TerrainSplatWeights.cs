using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct TerrainSplatWeights : IEquatable<TerrainSplatWeights>
{
    public int Index0;
    public int Index1;
    public int Index2;
    public int Index3;

    public float Weight0;
    public float Weight1;
    public float Weight2;
    public float Weight3;

    public int GetDominantIndex()
    {
        float maxW = Weight0;
        int idx = Index0;
        if (Weight1 > maxW)
        {
            maxW = Weight1;
            idx = Index1;
        }
        if (Weight2 > maxW)
        {
            maxW = Weight2;
            idx = Index2;
        }
        if (Weight3 > maxW)
        {
            maxW = Weight3;
            idx = Index3;
        }
        return idx;
    }

    public bool IsSolid(int textureIndex)
    {
        if (Index0 == textureIndex && Weight0 >= 0.999f) return true;
        if (Index1 == textureIndex && Weight1 >= 0.999f) return true;
        if (Index2 == textureIndex && Weight2 >= 0.999f) return true;
        if (Index3 == textureIndex && Weight3 >= 0.999f) return true;
        return false;
    }

    public static TerrainSplatWeights CreateSolid(int textureIndex)
    {
        return new TerrainSplatWeights
        {
            Index0 = textureIndex,
            Index1 = textureIndex,
            Index2 = textureIndex,
            Index3 = textureIndex,
            Weight0 = 1.0f,
            Weight1 = 0.0f,
            Weight2 = 0.0f,
            Weight3 = 0.0f
        };
    }

    public static TerrainSplatWeights PaintVertexWeighted(TerrainSplatWeights current, int targetTextureIndex, int intensityLevel)
    {
        float targetWeight = Math.Clamp(intensityLevel * 0.1f, 0.0f, 1.0f);

        int slotForTarget = GetSlotForTarget(current, targetTextureIndex);

        if (slotForTarget < 0)
        {
            if (targetWeight < .01f)
            {
                return current;
            }

            slotForTarget = GetLowestWeightSlot(current);
            current = SetTextureIndexForSlot(current, slotForTarget, targetTextureIndex);
        }

        float[] weights = new float[4] { current.Weight0, current.Weight1, current.Weight2, current.Weight3 };
        weights[slotForTarget] = targetWeight;

        float otherSum = GetOtherWeightsSum(weights, slotForTarget);
        float remainingWeight = 1.0f - targetWeight;

        if (otherSum > 0.0001f && remainingWeight > 0.0001f)
        {
            ScaleOtherWeights(weights, slotForTarget, remainingWeight / otherSum);
        }
        else
        {
            if (targetWeight < .01f)
            {
                return current;
            }
            ClearOtherWeights(weights, slotForTarget);
        }

        current.Weight0 = weights[0];
        current.Weight1 = weights[1];
        current.Weight2 = weights[2];
        current.Weight3 = weights[3];

        return current;
    }

    private static int GetSlotForTarget(TerrainSplatWeights current, int targetTextureIndex)
    {
        if (current.Index0 == targetTextureIndex) return 0;
        if (current.Index1 == targetTextureIndex) return 1;
        if (current.Index2 == targetTextureIndex) return 2;
        if (current.Index3 == targetTextureIndex) return 3;
        return -1;
    }

    private static int GetLowestWeightSlot(TerrainSplatWeights current)
    {
        int lowestSlot = 0;
        float minW = current.Weight0;
        if (current.Weight1 < minW) { minW = current.Weight1; lowestSlot = 1; }
        if (current.Weight2 < minW) { minW = current.Weight2; lowestSlot = 2; }
        if (current.Weight3 < minW) { lowestSlot = 3; }
        return lowestSlot;
    }

    private static TerrainSplatWeights SetTextureIndexForSlot(TerrainSplatWeights current, int slot, int textureIndex)
    {
        switch (slot)
        {
            case 0: current.Index0 = textureIndex; current.Weight0 = 0.0f; break;
            case 1: current.Index1 = textureIndex; current.Weight1 = 0.0f; break;
            case 2: current.Index2 = textureIndex; current.Weight2 = 0.0f; break;
            case 3: current.Index3 = textureIndex; current.Weight3 = 0.0f; break;
        }
        return current;
    }

    private static float GetOtherWeightsSum(float[] weights, int skipSlot)
    {
        float sum = 0.0f;
        for (int i = 0; i < 4; i++)
        {
            if (i != skipSlot) sum += weights[i];
        }
        return sum;
    }

    private static void ScaleOtherWeights(float[] weights, int skipSlot, float scale)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == skipSlot) continue;
            weights[i] *= scale;
            if (weights[i] < 0.01f) weights[i] = 0.0f;
        }
    }

    private static void ClearOtherWeights(float[] weights, int skipSlot)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i != skipSlot) weights[i] = 0.0f;
        }
    }

    public bool Equals(TerrainSplatWeights other)
    {
        return Index0 == other.Index0 && Index1 == other.Index1 &&
               Index2 == other.Index2 && Index3 == other.Index3 &&
               Weight0 == other.Weight0 && Weight1 == other.Weight1 &&
               Weight2 == other.Weight2 && Weight3 == other.Weight3;
    }

    public override bool Equals(object? obj) => obj is TerrainSplatWeights other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Index0, Index1, Index2, Index3);
    public static bool operator ==(TerrainSplatWeights a, TerrainSplatWeights b) => a.Equals(b);
    public static bool operator !=(TerrainSplatWeights a, TerrainSplatWeights b) => !a.Equals(b);

    public string Serialize()
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4}", Index0, Index1, Index2, Index3, Weight0, Weight1, Weight2, Weight3);
    }

    public static TerrainSplatWeights Deserialize(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return CreateSolid(3);

        TerrainSplatWeights result = new TerrainSplatWeights();
        int fieldIndex = 0;
        int start = 0;

        for (int i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] != ',') continue;

            ReadOnlySpan<char> field = span.Slice(start, i - start);
            start = i + 1;

            ParseField(field, fieldIndex, ref result);
            fieldIndex++;
        }

        if (fieldIndex < 8) return CreateSolid(3);

        return result;
    }

    private static void ParseField(ReadOnlySpan<char> field, int fieldIndex, ref TerrainSplatWeights weights)
    {
        switch (fieldIndex)
        {
            case 0: int.TryParse(field, out weights.Index0); break;
            case 1: int.TryParse(field, out weights.Index1); break;
            case 2: int.TryParse(field, out weights.Index2); break;
            case 3: int.TryParse(field, out weights.Index3); break;
            case 4: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out weights.Weight0); break;
            case 5: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out weights.Weight1); break;
            case 6: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out weights.Weight2); break;
            case 7: float.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out weights.Weight3); break;
        }
    }

    public static TerrainSplatWeights Deserialize(string? serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return CreateSolid(3);
        return Deserialize(serialized.AsSpan());
    }
}
