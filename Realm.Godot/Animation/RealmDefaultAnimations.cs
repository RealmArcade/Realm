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
			string animsDir = Path.Combine(baseAssetsDirectory, "animations");
			if (!Directory.Exists(animsDir))
			{
				animsDir = Path.Combine(baseAssetsDirectory, "Assets", "animations");
			}
			if (!Directory.Exists(animsDir))
			{
				Directory.CreateDirectory(animsDir);
			}

			Idle = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "idle.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/idle.ranim"));
			Walk = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "walk.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/walk.ranim"));
			Attack = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "attack.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/attack.ranim"));
			Death = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "death.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/death.ranim"));
			Labor = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "labor.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/labor.ranim"));
			Spell_Cast = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "spell_cast.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/spell_cast.ranim"));
			Dance = AnimationRetargetingService.GetOrLoadRanimData(Path.Combine(animsDir, "dance.ranim"))
				?? AnimationRetargetingService.GetOrLoadRanimData(ProjectSettings.GlobalizePath("res://Assets/animations/dance.ranim"));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[RealmDefaultAnimations] Error ensuring default animations: {ex.Message}");
		}
	}
}
