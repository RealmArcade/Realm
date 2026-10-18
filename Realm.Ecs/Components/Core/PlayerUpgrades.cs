using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Represents the active upgrades purchased by a player entity.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct PlayerUpgrades
	{
		public byte WeaponsUpgradeByte;

		public bool WeaponsUpgrade
		{
			get => WeaponsUpgradeByte != 0;
			set => WeaponsUpgradeByte = value ? (byte)1 : (byte)0;
		}

		public byte ShieldsUpgradeByte;

		public bool ShieldsUpgrade
		{
			get => ShieldsUpgradeByte != 0;
			set => ShieldsUpgradeByte = value ? (byte)1 : (byte)0;
		}

		public byte HarvestingUpgradeByte;

		public bool HarvestingUpgrade
		{
			get => HarvestingUpgradeByte != 0;
			set => HarvestingUpgradeByte = value ? (byte)1 : (byte)0;
		}

		public PlayerUpgrades(bool weaponsUpgrade, bool shieldsUpgrade, bool harvestingUpgrade)
		{
			WeaponsUpgradeByte = weaponsUpgrade ? (byte)1 : (byte)0;
			ShieldsUpgradeByte = shieldsUpgrade ? (byte)1 : (byte)0;
			HarvestingUpgradeByte = harvestingUpgrade ? (byte)1 : (byte)0;
		}
	}
}