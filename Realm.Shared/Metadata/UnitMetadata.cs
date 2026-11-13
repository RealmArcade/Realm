using System.Diagnostics.CodeAnalysis;
using Realm.Shared.Animation;
using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One unit or building template: its combat stats, its price, what it can build, cast and carry, and how it moves, dies and sounds.")]
public class UnitMetadata : PlaceableMetadata
{
	public UnitMetadata()
	{
		IgnorePlayerColor = false;
	}

	[SchemaDescription("Health the unit starts with and can never exceed before upgrades.")]
	public float MaxHp { get; set; }

	[SchemaDescription("Damage one attack deals before the target's armour, resistances and the map's combat tuning are applied.")]
	public float Damage { get; set; }

	[SchemaDescription("How far the unit can reach with its weapon, measured from its edge to the target's edge.")]
	[SchemaUnit("m")]
	public float Range { get; set; }

	[SchemaDescription("Flat damage reduction applied to every physical hit the unit takes.")]
	public float Armor { get; set; }

	[SchemaDescription("How fast the unit crosses the ground under its own orders.")]
	[SchemaUnit("m/s")]
	public float Speed { get; set; }

	[SchemaDescription("How long the unit waits between attacks.")]
	[SchemaUnit("s")]
	public float AttackCooldown { get; set; }

	[SchemaDescription("How far from its post the unit looks for something to attack on its own.")]
	[SchemaUnit("m")]
	public float ScanRadius { get; set; }

	[SchemaDescription("Gold the unit costs to train.")]
	public float CostGold { get; set; }

	[SchemaDescription("Wood the unit costs to train.")]
	public float CostWood { get; set; }

	[SchemaDescription("Stone the unit costs to train.")]
	public float CostStone { get; set; }

	[SchemaDescription("How much of its owner's population the unit occupies while it is alive.")]
	public int PopCost { get; set; }

	[SchemaDescription("How long the producing building takes to train the unit.")]
	[SchemaUnit("s")]
	public float ProductionTime { get; set; }

	[SchemaDescription("The unit's strength attribute, which the map's combat tuning turns into attack and armour.")]
	public float Strength { get; set; }

	[SchemaDescription("The unit's agility attribute, which the map's combat tuning turns into attack and movement speed.")]
	public float Agility { get; set; }

	[SchemaDescription("The unit's vitality attribute, which the map's combat tuning turns into health and mana.")]
	public float Vitality { get; set; }

	[SchemaDescription("The unit's intelligence attribute, which the map's combat tuning turns into spell power and mana regeneration.")]
	public float Intelligence { get; set; }

	[SchemaDescription("The unit's wisdom attribute, which the map's combat tuning turns into magic penetration and healing.")]
	public float Wisdom { get; set; }

	[SchemaDescription("The unit's fortune attribute, which the map's combat tuning turns into crit chance, evasion and bounty.")]
	public float Fortune { get; set; }

	[SchemaDescription("How much each attribute and the bounty multiplier gain for every level the unit reaches.")]
	public LevelGrowthMetadata? LevelGrowth { get; set; }

	[SchemaDescription("Health the unit regains per second while it is out of combat.")]
	public float HpRegen { get; set; }

	[SchemaDescription("How long the unit has to stay out of combat before health regeneration resumes.")]
	[SchemaUnit("s")]
	public float HpRegenCombatDelay { get; set; }

	[SchemaDescription("Mana the unit starts with and can never exceed before upgrades.")]
	public float MaxMana { get; set; }

	[SchemaDescription("Mana the unit regains per second.")]
	public float ManaRegen { get; set; }

	[SchemaDescription("Flat reduction applied to every magical hit the unit takes.")]
	public float MagicResistance { get; set; }

	[SchemaDescription("Armour points the unit's attacks ignore outright.")]
	public float FlatArmorPenetration { get; set; }

	[SchemaDescription("The share of the target's armour the unit's attacks ignore.")]
	public float PercentArmorPenetration { get; set; }

	[SchemaDescription("Magic resistance points the unit's spells ignore outright.")]
	public float FlatMagicPenetration { get; set; }

	[SchemaDescription("How far either side of the listed damage an attack may land, so the same unit does not hit for an identical number every time.")]
	public float DamageVariance { get; set; }

	[SchemaDescription("Whether the unit's damage is mitigated as physical or as magical.")]
	public string DamageType { get; set; } = "physical";

	[SchemaDescription("The chance an attack crits, as a fraction of one.")]
	public float CritChance { get; set; }

	[SchemaDescription("What a crit multiplies the attack's damage by.")]
	public float CritMultiplier { get; set; } = 1.0f;

	[SchemaDescription("How the unit's attacks spread over several targets, or None when every attack hits one target only.")]
	public string SplashType { get; set; } = "None";

	[SchemaDescription("The radius inside which a splashing attack deals its full inner share of damage.")]
	[SchemaUnit("m")]
	public float SplashInnerRadius { get; set; }

	[SchemaDescription("The radius inside which a splashing attack deals its medium share of damage.")]
	[SchemaUnit("m")]
	public float SplashMediumRadius { get; set; }

	[SchemaDescription("The radius outside which a splashing attack deals no damage at all.")]
	[SchemaUnit("m")]
	public float SplashOuterRadius { get; set; }

	[SchemaDescription("The share of the attack's damage dealt to targets inside the inner splash radius.")]
	public float SplashInnerRatio { get; set; } = 1.0f;

	[SchemaDescription("The share of the attack's damage dealt to targets between the inner and medium splash radii.")]
	public float SplashMediumRatio { get; set; } = 0.5f;

	[SchemaDescription("The share of the attack's damage dealt to targets between the medium and outer splash radii.")]
	public float SplashOuterRatio { get; set; } = 0.25f;

	[SchemaDescription("Whether the unit's splash damages its own side as well as the enemy's.")]
	public bool FriendlyFire { get; set; }

	[SchemaDescription("Which unit gives way when two of them push each other, higher values winning.")]
	public int PushPriority { get; set; }

	[SchemaDescription("Whether the unit walks, flies or is fixed in place, which decides which terrain it may cross.")]
	public string MovementType { get; set; } = "Ground";

	[SchemaDescription("How far the unit sees, which is the range its fog of war is lifted over.")]
	[SchemaUnit("m")]
	public float SightRange { get; set; } = 15.0f;

	[SchemaDescription("How far away the unit notices an enemy and decides to engage it without being ordered to.")]
	[SchemaUnit("m")]
	public float AcquisitionRange { get; set; } = 15.0f;

	[SchemaDescription("Gold the killer's owner is paid for destroying the unit.")]
	public float GoldBounty { get; set; }

	[SchemaDescription("How long the unit stays down before it revives at its owner's start location. Zero means it stays dead.")]
	[SchemaUnit("s")]
	public float RespawnSeconds { get; set; }

	[SchemaDescription("The buildings a worker may raise, as a selection of unit and building templates.")]
	[SchemaReference("Units", "Buildings")]
	public string[]? BuildOptions { get; set; }

	[SchemaDescription("Whether the unit is a hero: it levels up, revives and is shown separately in the interface.")]
	public bool IsHero { get; set; }

	[SchemaDescription("Whether the unit joins the selection a player gets by dragging a box over their army.")]
	public bool IncludeInDefaultHotkeySelection { get; set; } = true;

	[SchemaDescription("The abilities that appear on the unit's command card.")]
	[SchemaReference("Abilities")]
	public string[]? Abilities { get; set; }

	[SchemaDescription("Experience the killer's owner is awarded for destroying the unit.")]
	public float XpBounty { get; set; }

	[SchemaDescription("Experience the unit needs to reach its next level, before any per-level growth is applied.")]
	public float ExperiencePerLevel { get; set; }

	[SchemaDescription("The highest level the unit may reach. One means it never levels.")]
	public int MaxLevel { get; set; }

	[SchemaDescription("The level the unit starts the match at.")]
	public int Level { get; set; } = 1;

	[SchemaDescription("The kinds of terrain the unit may cross, which narrows what its movement type allows.")]
	public string[]? PathingCapabilities { get; set; }

	[SchemaDescription("The kinds of target the unit may attack, such as ground units, air units or structures.")]
	public string[]? Targets { get; set; }

	[SchemaDescription("The weapons the unit attacks with. Several weapons are fired in the order they are listed.")]
	[SchemaReference("Weapons")]
	public string[]? Weapons { get; set; }

	[SchemaDescription("Path to the model flown by the unit's projectile when its weapon has no model of its own.")]
	[SchemaAsset(new[] { "rmesh" }, new[] { "Assets/models/items" })]
	public string? ProjectileModelPath { get; set; }

	[SchemaDescription("The model animations the unit plays, keyed by the state that asks for them such as idle, walk or attack.")]
	public Dictionary<string, List<UnitAnimationEntry>>? Animations { get; set; }

	[SchemaDescription("Which attachments sit on which hand or named socket of the unit's model.")]
	public UnitObjectAttachments? ObjectAttachments { get; set; }

	[SchemaDescription("The voice lines and sound effects the unit plays in response to orders, wounds, death and spell casts.")]
	public UnitSoundsMetadata? Sounds { get; set; }

	[SchemaDescription("The items the unit is carrying when it is trained.")]
	[SchemaReference("Items", "Consumables")]
	public string[]? StartingItems { get; set; }

	[SchemaDescription("The items the unit offers for sale when it is selected.")]
	[SchemaReference("Items", "Consumables")]
	public string[]? ShopItems { get; set; }

	[SchemaDescription("The upgrades the unit offers for research when it is selected.")]
	[SchemaReference("Upgrades")]
	public string[]? Upgrades { get; set; }

	[SchemaDescription("Status effects the unit carries from the moment it is trained.")]
	public string[]? StatusEffects { get; set; }

	[SchemaDescription("The sound events the map's script raises on this unit, which its sound lists answer by name.")]
	public string[]? SoundEvents { get; set; }

	public const float DefaultConstructionRange = 4f;

	[SchemaDescription("How close a worker has to stand to a site before it may start or continue building there.")]
	[SchemaUnit("m")]
	public float ConstructionRange { get; set; } = DefaultConstructionRange;

	public bool TryGetObjectAttachment(HumanoidBone hand, string attachmentId, [NotNullWhen(true)] out HandAttachmentOrientation? orientation)
	{
		if (ObjectAttachments != null)
		{
			return ObjectAttachments.TryGetOrientation(hand, attachmentId, out orientation);
		}

		orientation = default;
		return false;
	}

	public bool TryGetObjectAttachment(string socket, string attachmentId, [NotNullWhen(true)] out HandAttachmentOrientation? orientation)
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
