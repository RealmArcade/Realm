namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents the active upgrades purchased by a player entity.
	/// </summary>
	public struct PlayerUpgrades
	{
		public bool WeaponsUpgrade;
		public bool ShieldsUpgrade;
		public bool HarvestingUpgrade;

		public PlayerUpgrades(bool weaponsUpgrade, bool shieldsUpgrade, bool harvestingUpgrade)
		{
			WeaponsUpgrade = weaponsUpgrade;
			ShieldsUpgrade = shieldsUpgrade;
			HarvestingUpgrade = harvestingUpgrade;
		}
	}
}
