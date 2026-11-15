using System;
using System.Runtime.InteropServices;
namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Tracks the construction progress of a building entity, from placement to completion.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ConstructionState
{
	public float TotalBuildTime;
	public float Progress;

	public float CompletionCeilingFraction;

	public ConstructionState(float totalBuildTime)
	{
		TotalBuildTime = totalBuildTime;
		Progress = 0f;
		CompletionCeilingFraction = 1f;
	}

	public readonly float ProgressCeiling => TotalBuildTime * Math.Clamp(CompletionCeilingFraction, 0f, 1f);

	public readonly bool IsComplete => CompletionCeilingFraction >= 1f && Progress >= TotalBuildTime;
}
