using System;

namespace Realm.Client.Services
{
	public class RandomService
	{
		public Random Rng { get; private set; } = new();

		public void SetSeed(int? seed)
		{
			if (seed.HasValue && seed.Value != 0)
			{
				Rng = new Random(seed.Value);
			}
			else
			{
				Rng = new Random();
			}
		}
	}
}