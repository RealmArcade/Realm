namespace Realm.MapAPI;

/// <summary>
/// Provides utility calculations for hero kill rewards and level progression.
/// </summary>
public static class HeroKillReward
{
	/// <summary>
	/// Calculates the resulting gold, experience, and level after an enemy kill based on the specified progression configuration.
	/// </summary>
	/// <param name="config">The hero progression configuration settings.</param>
	/// <param name="currentGold">The current amount of gold held prior to the kill.</param>
	/// <param name="currentXp">The current amount of experience accumulated prior to the kill.</param>
	/// <returns>A tuple containing the updated gold, updated experience, and calculated level.</returns>
	public static (float Gold, float Xp, int Level) AfterKill(
		HeroProgressionConfig config, float currentGold, float currentXp)
	{
		var gold = currentGold + config.KillGold;
		var xp = currentXp + config.KillXp;
		var level = 1 + (int)(xp / config.XpPerLevel);
		return (gold, xp, level);
	}
}