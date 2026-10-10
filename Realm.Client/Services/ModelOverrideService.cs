using Godot;
using System;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class ModelOverrideService
{
    public readonly Dictionary<string, float> ModelScales = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, float> ModelCollisionCircleRatios = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, float> ModelObstacleRadii = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, float> ModelBrightness = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Color> ModelColorTint = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> ModelIgnorePlayerColor = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> ModelDespillPlayerColor = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> ModelNormalizeLuminance = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> ModelSpawnShaders = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> ModelDeathShaders = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, float> ModelYOffsets = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> ModelProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> ModelEnableProceduralAnimations = new(StringComparer.OrdinalIgnoreCase);

    public const float MaxSafeModelYOffset = 50f;
    public const float MinSafeModelCollisionRatio = 0.1f;
    public const float MaxSafeModelCollisionRatio = 10f;


    public void ClearAll()
    {
        ModelScales.Clear();
        ModelCollisionCircleRatios.Clear();
        ModelObstacleRadii.Clear();
        ModelBrightness.Clear();
        ModelColorTint.Clear();
        ModelDespillPlayerColor.Clear();
        ModelIgnorePlayerColor.Clear();
        ModelNormalizeLuminance.Clear();
        ModelSpawnShaders.Clear();
        ModelDeathShaders.Clear();
        ModelYOffsets.Clear();
        ModelProceduralAnimations.Clear();
        ModelEnableProceduralAnimations.Clear();
    }
}
