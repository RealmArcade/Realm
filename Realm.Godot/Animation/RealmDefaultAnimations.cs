using Godot;
using System;
using System.IO;

namespace Realm.Godot.Animation;

public static class RealmDefaultAnimations
{
    public static RealmAnimationData Idle { get; private set; }
    public static RealmAnimationData Walk { get; private set; }
    public static RealmAnimationData Attack { get; private set; }
    public static RealmAnimationData Death { get; private set; }
    public static RealmAnimationData Labor { get; private set; }
    public static RealmAnimationData Spell_Cast { get; private set; }
    public static RealmAnimationData Dance { get; private set; }

    public static void EnsureDefaultTemplateAnimations(string baseAssetsDirectory)
    {
        try
        {
            string animsDir = GetOrCreateAnimationsDirectory(baseAssetsDirectory);
            LoadAllAnimations(animsDir);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealmDefaultAnimations] Error ensuring default animations: {ex.Message}");
        }
    }

    private static string GetOrCreateAnimationsDirectory(string baseAssetsDirectory)
    {
        string animsDir = Path.Combine(baseAssetsDirectory, "animations");
        if (Directory.Exists(animsDir))
        {
            return animsDir;
        }

        animsDir = Path.Combine(baseAssetsDirectory, "Assets", "animations");
        if (!Directory.Exists(animsDir))
        {
            Directory.CreateDirectory(animsDir);
        }

        return animsDir;
    }

    private static void LoadAllAnimations(string animsDir)
    {
        Idle = LoadAnimation(animsDir, "idle.ranim");
        Walk = LoadAnimation(animsDir, "walk.ranim");
        Attack = LoadAnimation(animsDir, "attack.ranim");
        Death = LoadAnimation(animsDir, "death.ranim");
        Labor = LoadAnimation(animsDir, "labor.ranim");
        Spell_Cast = LoadAnimation(animsDir, "spell_cast.ranim");
        Dance = LoadAnimation(animsDir, "dance.ranim");
    }

    private static RealmAnimationData LoadAnimation(string animsDir, string fileName)
    {
        string localPath = Path.Combine(animsDir, fileName);
        RealmAnimationData data = AnimationRetargetingService.GetOrLoadRanimData(localPath);
        if (data != null)
        {
            return data;
        }

        string defaultPath = ProjectSettings.GlobalizePath($"res://Assets/animations/{fileName}");
        return AnimationRetargetingService.GetOrLoadRanimData(defaultPath);
    }
}
