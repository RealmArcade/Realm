using System.Text.Json;

namespace Realm.Ecs.AI.Balancer;

public class BalanceGenome
{
	public float Unit0Cost { get; set; } = 100f;
	public float Unit0Health { get; set; } = 100f;
	public float Unit0Damage { get; set; } = 15f;
	public float Unit0Cooldown { get; set; } = 1.0f;

	public float[] ToVector()
	{
		return new float[] { Unit0Cost, Unit0Health, Unit0Damage, Unit0Cooldown };
	}

	public void FromVector(float[] vector)
	{
		if (vector == null || vector.Length < 4) return;
		Unit0Cost = MathF.Max(10f, vector[0]);
		Unit0Health = MathF.Max(10f, vector[1]);
		Unit0Damage = MathF.Max(1f, vector[2]);
		Unit0Cooldown = MathF.Max(0.1f, vector[3]);
	}

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
	}

	public static BalanceGenome FromJson(string json)
	{
		return JsonSerializer.Deserialize<BalanceGenome>(json) ?? new BalanceGenome();
	}
}