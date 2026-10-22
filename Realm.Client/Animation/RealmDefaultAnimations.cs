using System;
using System.IO;
using Godot;

namespace Realm.Client.Animation;

public static class RealmDefaultAnimations
{
	private static RealmAnimationData? _idle;
	private static RealmAnimationData? _walk;
	private static RealmAnimationData? _attack;
	private static RealmAnimationData? _death;
	private static RealmAnimationData? _labor;
	private static RealmAnimationData? _spellCast;
	private static RealmAnimationData? _dance;

	public static RealmAnimationData? Idle
	{
		get => _idle ??= LoadDefaultAnimation("idle.ranim");
		private set => _idle = value;
	}

	public static RealmAnimationData? Walk
	{
		get => _walk ??= LoadDefaultAnimation("walk.ranim");
		private set => _walk = value;
	}

	public static RealmAnimationData? Attack
	{
		get => _attack ??= LoadDefaultAnimation("attack.ranim");
		private set => _attack = value;
	}

	public static RealmAnimationData? Death
	{
		get => _death ??= LoadDefaultAnimation("death.ranim");
		private set => _death = value;
	}

	public static RealmAnimationData? Labor
	{
		get => _labor ??= LoadDefaultAnimation("labor.ranim");
		private set => _labor = value;
	}

	public static RealmAnimationData? Spell_Cast
	{
		get => _spellCast ??= LoadDefaultAnimation("spell_cast.ranim");
		private set => _spellCast = value;
	}

	public static RealmAnimationData? Dance
	{
		get => _dance ??= LoadDefaultAnimation("dance.ranim");
		private set => _dance = value;
	}

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

	private static RealmAnimationData? LoadAnimation(string animsDir, string fileName)
	{
		string localPath = Path.Combine(animsDir, fileName);
		RealmAnimationData? data = AnimationRetargetingService.GetOrLoadRanimData(localPath);
		if (data != null)
		{
			return data;
		}

		return LoadDefaultAnimation(fileName);
	}

	private static RealmAnimationData? LoadDefaultAnimation(string fileName)
	{
		string resPath = $"res://Assets/animations/{fileName}";
		var data = AnimationRetargetingService.GetOrLoadRanimData(resPath);
		if (data != null)
		{
			return data;
		}

		string defaultPath = ProjectSettings.GlobalizePath(resPath);
		return AnimationRetargetingService.GetOrLoadRanimData(defaultPath);
	}
}