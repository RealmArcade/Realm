using System;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     The charges a unit holds and regains over time, such as the soldiers a barracks keeps ready to field.
/// </summary>
/// <remarks>
///     The engine regains a charge on its own, so a map only states how many charges a unit keeps and how long one takes
///     to come back, and spends a charge when it fields what the charge was holding. Reading it from the world means every
///     screen states the same count without the map publishing it.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct UnitCharges
{
	/// <summary>Gets or sets the number of charges the unit holds right now.</summary>
	public int Charges;

	/// <summary>Gets or sets the greatest number of charges the unit may hold.</summary>
	public int MaximumCharges;

	/// <summary>Gets or sets the seconds the unit takes to regain one charge.</summary>
	public float RechargeSeconds;

	/// <summary>Gets or sets the seconds accumulated toward the charge being regained.</summary>
	public float RechargeProgress;

	public UnitCharges(int charges, int maximumCharges, float rechargeSeconds)
	{
		Charges = charges;
		MaximumCharges = maximumCharges;
		RechargeSeconds = rechargeSeconds;
		RechargeProgress = 0f;
	}

	/// <summary>
	///     Answers whether the unit still has a charge it may spend, which is what a map asks before fielding what the
	///     charge holds.
	/// </summary>
	/// <param name="charges">The charges the unit holds before the spend.</param>
	/// <returns>True when the count may be lowered by one; otherwise false.</returns>
	public static bool CanSpend(in UnitCharges charges)
	{
		return charges.MaximumCharges > 0 && charges.Charges > 0;
	}

	/// <summary>
	///     Lowers the count by one without going below zero, which spends the charge the unit held.
	/// </summary>
	public void Spend()
	{
		if (Charges > 0) Charges--;
	}

	/// <summary>
	///     Advances the recharge by the elapsed time, regaining whole charges as they become due. A unit that already
	///     holds its greatest number keeps no partial charge, because the time it waits at full is not time served
	///     toward the next one.
	/// </summary>
	/// <param name="deltaSeconds">The seconds this tick covers.</param>
	public void Advance(float deltaSeconds)
	{
		if (deltaSeconds <= 0f || MaximumCharges <= 0) return;

		if (Charges >= MaximumCharges)
		{
			RechargeProgress = 0f;
			return;
		}

		if (RechargeSeconds <= 0f)
		{
			Charges = MaximumCharges;
			RechargeProgress = 0f;
			return;
		}

		RechargeProgress += deltaSeconds;

		while (RechargeProgress >= RechargeSeconds && Charges < MaximumCharges)
		{
			RechargeProgress -= RechargeSeconds;
			Charges++;
		}

		if (Charges >= MaximumCharges) RechargeProgress = 0f;
	}

	/// <summary>
	///     States how many seconds are left until the next charge arrives, or zero when the unit already holds all it may.
	/// </summary>
	/// <returns>The remaining seconds, never negative.</returns>
	public float RemainingSecondsToNextCharge()
	{
		if (Charges >= MaximumCharges || RechargeSeconds <= 0f) return 0f;

		return MathF.Max(0f, RechargeSeconds - RechargeProgress);
	}
}
