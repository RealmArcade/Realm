using System;
using System.Globalization;

namespace Realm.Shared.Network;

/// <summary>Names the item a drop order puts down together with how many pieces of it leave the inventory.</summary>
public static class DropItemArgument
{
	private const char Separator = '|';

	public static string Compose(string itemId, int charges)
	{
		return $"{itemId}{Separator}{Math.Max(1, charges).ToString(CultureInfo.InvariantCulture)}";
	}

	public static void Parse(string? argument, out string itemId, out int charges)
	{
		itemId = argument ?? string.Empty;
		charges = 1;
		if (itemId.Length == 0) return;

		int separator = itemId.LastIndexOf(Separator);
		if (separator < 0) return;

		if (int.TryParse(itemId.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) charges = Math.Max(1, parsed);

		itemId = itemId.Substring(0, separator);
	}
}
