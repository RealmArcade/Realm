namespace Realm.MapAPI;

/// <summary>
/// Configuration settings for an autonomous AI bot profile including decision cadence and personality attributes.
/// </summary>
public readonly struct BotProfileConfig
{
	/// <summary>
	/// Gets the cadence in seconds between decision evaluation cycles.
	/// </summary>
	public float DecisionIntervalSeconds { get; }

	/// <summary>
	/// Gets the multiplier applied to aggressive/offensive tactical affordances.
	/// </summary>
	public float AggressionMultiplier { get; }

	/// <summary>
	/// Gets the softmax temperature parameter controlling exploration and decision randomness.
	/// </summary>
	public float ActionTemperature { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="BotProfileConfig"/> struct.
	/// </summary>
	/// <param name="decisionIntervalSeconds">The time in seconds between AI decision ticks.</param>
	/// <param name="aggressionMultiplier">The multiplier applied to aggressive utility calculations.</param>
	/// <param name="actionTemperature">The temperature factor for softmax action selection.</param>
	public BotProfileConfig(float decisionIntervalSeconds, float aggressionMultiplier, float actionTemperature)
	{
		DecisionIntervalSeconds = decisionIntervalSeconds;
		AggressionMultiplier = aggressionMultiplier;
		ActionTemperature = actionTemperature;
	}

	/// <summary>
	/// Gets the default AI bot profile configuration.
	/// </summary>
	public static BotProfileConfig Default => new(1.0f, 1.0f, 0.05f);
}