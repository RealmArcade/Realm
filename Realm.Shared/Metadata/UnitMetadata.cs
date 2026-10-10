using Realm.Shared.Animation;

namespace Realm.Shared.Metadata;

public class UnitMetadata
{
	public string TemplateID { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public float MaxHp { get; set; }
	public float Damage { get; set; }
	public float Range { get; set; }
	public float Armor { get; set; }
	public float Speed { get; set; }
	public float AttackCooldown { get; set; }
	public float ScanRadius { get; set; }
	public float CostGold { get; set; }
	public float CostWood { get; set; }
	public float CostStone { get; set; }
	public int PopCost { get; set; }
	public float ProductionTime { get; set; }
	public string? AttackType { get; set; }
	public string? ArmorType { get; set; }
	public float Strength { get; set; }
	public float Agility { get; set; }
	public float Vitality { get; set; }
	public float Intelligence { get; set; }
	public float Wisdom { get; set; }
	public float Fortune { get; set; }
	public float HpRegen { get; set; }
	public float HpRegenCombatDelay { get; set; }
	public float MaxMana { get; set; }
	public float ManaRegen { get; set; }
	public float RatedArmor { get; set; }
	public float FlatArmorPenetration { get; set; }
	public float PercentArmorPenetration { get; set; }
	public float DamageVariance { get; set; }
	public string DamageType { get; set; } = "normal";
	public float CritChance { get; set; }
	public float CritMultiplier { get; set; } = 1.0f;
	public string SplashType { get; set; } = "None";
	public float SplashInnerRadius { get; set; }
	public float SplashMediumRadius { get; set; }
	public float SplashOuterRadius { get; set; }
	public float SplashInnerRatio { get; set; } = 1.0f;
	public float SplashMediumRatio { get; set; } = 0.5f;
	public float SplashOuterRatio { get; set; } = 0.25f;
	public bool FriendlyFire { get; set; }
	public int PushPriority { get; set; }
	public string MovementType { get; set; } = "Ground";
	public float SightRange { get; set; } = 15.0f;
	public float AcquisitionRange { get; set; } = 15.0f;
	public float GoldBounty { get; set; }
	public string? ModelPath { get; set; }
	public string? PortraitModelPath { get; set; }
	public string VisualMode { get; set; } = "GroundPlane";
	public float Scale { get; set; } = 1.0f;
	public float YOffset { get; set; }
	public float CollisionCircle { get; set; }
	public float Brightness { get; set; } = 0.5f;
	public string? Tint { get; set; }
	public bool NormalizeLuminance { get; set; } = true;
	public bool IgnorePlayerColor { get; set; }
	public bool DespillPlayerColor { get; set; } = false;
	public string[]? BuildOptions { get; set; }
	public bool IsHero { get; set; }
	public string[]? Abilities { get; set; }
	public float XpBounty { get; set; }
	public string[]? PathingCapabilities { get; set; }
	public int PathingType { get; set; }
	public float? ObstacleRadius { get; set; }
	public string[]? Targets { get; set; }
	public string[]? Weapons { get; set; }
	public string? ProjectileModelPath { get; set; }
	public Dictionary<string, List<UnitAnimationEntry>>? Animations { get; set; }
	public UnitObjectAttachments? ObjectAttachments { get; set; }
	public UnitSoundsMetadata? Sounds { get; set; }
	public string[]? StartingItems { get; set; }
	public string[]? Upgrades { get; set; }
	public string[]? StatusEffects { get; set; }
	public string[]? SoundEvents { get; set; }
	public string? SpawnShader { get; set; }
	public string? DeathShader { get; set; }
	public string? DespawnShader
	{
		get => DeathShader;
		set => DeathShader = value;
	}

	public bool TryGetObjectAttachment(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.TryGetOrientation(hand, attachmentId, out orientation);
		}
		orientation = default;
		return false;
	}

	public bool TryGetObjectAttachment(string socket, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.TryGetSocketOrientation(socket, attachmentId, out orientation);
		}
		orientation = default;
		return false;
	}

	public void SetObjectAttachment(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
	{
		ObjectAttachments ??= new UnitObjectAttachments();
		ObjectAttachments.SetOrientation(hand, attachmentId, orientation);
	}

	public void SetObjectAttachment(string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		ObjectAttachments ??= new UnitObjectAttachments();
		ObjectAttachments.SetSocketOrientation(socket, attachmentId, orientation);
	}

	public bool RemoveObjectAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.RemoveSocketAttachment(socket, attachmentId, parentAttachmentId);
		}
		return false;
	}
}