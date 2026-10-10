using System;

namespace Realm.Client.Services
{
	public class RandomService
	{
		public Random Rng { get; } = new();
	}
}
