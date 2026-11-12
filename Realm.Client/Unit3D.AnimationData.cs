using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Animation;

namespace Realm.Client;
public partial class Unit3D
{
	public void UpdateHandAttachmentsForAnimation(string animName, string resolvedName)
	{
		if (string.IsNullOrEmpty(UnitId) || !GameHost.UnitRegistry.TryGetValue(UnitId, out var uMeta) || uMeta.Animations == null || uMeta.Animations.Count == 0)
		{
			return;
		}

		ParseAnimationName(animName, out string actionType, out int variantIndex);

		UnitAnimationEntry? matchedEntry = FindExactAnimationEntry(uMeta, actionType, variantIndex);

		if (!matchedEntry.HasValue)
		{
			matchedEntry = FindFallbackAnimationEntry(uMeta, animName, resolvedName);
		}

		if (matchedEntry.HasValue)
		{
			if (!string.IsNullOrEmpty(matchedEntry.Value.RightHandAttachment)) SetHandAttachment(HumanoidBone.RightHand, matchedEntry.Value.RightHandAttachment);
			if (!string.IsNullOrEmpty(matchedEntry.Value.LeftHandAttachment)) SetHandAttachment(HumanoidBone.LeftHand, matchedEntry.Value.LeftHandAttachment);
		}
	}

	private void ParseAnimationName(string animName, out string actionType, out int variantIndex)
	{
		actionType = animName;
		variantIndex = 0;
		int underscoreIdx = animName.LastIndexOf('_');
		if (underscoreIdx > 0 && int.TryParse(animName.Substring(underscoreIdx + 1), out int parsedIdx))
		{
			actionType = animName.Substring(0, underscoreIdx);
			variantIndex = parsedIdx;
		}
	}

	private UnitAnimationEntry? FindExactAnimationEntry(UnitMetadata uMeta, string actionType, int variantIndex)
	{
		if (uMeta.Animations is not { } animations) return null;

		foreach (var kvp in animations)
		{
			if (kvp.Key.Equals(actionType, StringComparison.OrdinalIgnoreCase))
			{
				if (kvp.Value != null && kvp.Value.Count > 0)
				{
					int clampedIndex = Math.Clamp(variantIndex, 0, kvp.Value.Count - 1);
					return kvp.Value[clampedIndex];
				}

				break;
			}
		}

		return null;
	}

	private UnitAnimationEntry? FindFallbackAnimationEntry(UnitMetadata uMeta, string animName, string resolvedName)
	{
		if (uMeta.Animations is not { } animations) return null;

		foreach (var kvp in animations)
		{
			if (kvp.Value == null) continue;

			for (int i = 0; i < kvp.Value.Count; i++)
			{
				var entry = kvp.Value[i];
				if (!string.IsNullOrEmpty(entry.Animation) && (entry.Animation.Equals(animName, StringComparison.OrdinalIgnoreCase) || entry.Animation.Equals(resolvedName, StringComparison.OrdinalIgnoreCase)))
				{
					return entry;
				}
			}
		}

		return null;
	}


	private StringName? ResolveRandomAnimationVariant(string animName, string[] animations)
	{
		var variants = new List<StringName>();
		string prefix = $"{animName}_";
		foreach (var name in animations)
		{
			string nameStr = name.ToString();
			if (nameStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				variants.Add(name);
			}
		}

		if (variants.Count > 0)
		{
			int randIdx = Random.Shared.Next(variants.Count);
			return variants[randIdx];
		}

		return null;
	}

	private StringName? TryRetargetAnimation(string animName, StringName direct)
	{
		string? resolvedPath = AnimationRetargetingService.ResolveAnimationFilePath(animName, UnitId);
		if (!string.IsNullOrEmpty(resolvedPath))
		{
			if (TryBindRetargetedAnimation(resolvedPath, animName)) return direct;
		}

		var fallbackAnim = GetFallbackAnimationData(animName);
		if (fallbackAnim != null && _modelNode != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(fallbackAnim, _modelNode, animName, out _))
			{
				return direct;
			}
		}

		return null;
	}

	private bool TryBindRetargetedAnimation(string resolvedPath, string animName)
	{
		var animData = AnimationRetargetingService.GetOrLoadRanimData(resolvedPath);
		if (animData != null && _modelNode != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(animData, _modelNode, animName, out _))
			{
				return true;
			}
		}

		return false;
	}

	private RealmAnimationData? GetFallbackAnimationData(RealmAnimationType animType)
	{
		return AnimationRetargetingService.GetFallbackAnimationData(animType, UnitId);
	}

	private RealmAnimationData? GetFallbackAnimationData(string animName)
	{
		return AnimationRetargetingService.GetFallbackAnimationData(animName, UnitId);
	}


	private StringName? ResolveAnimationName(string animName)
	{
		if (_animationPlayer == null) return null;
		if (_resolvedAnimCache.TryGetValue(animName, out var cached)) return cached;

		var animations = _animationPlayer.GetAnimationList();
		StringName? result = null;

		if (!animName.Contains('_'))
		{
			var randomVariant = ResolveRandomAnimationVariant(animName, animations);
			if (randomVariant != null) result = randomVariant;
		}

		if (result == null)
		{
			StringName direct = new StringName(animName);
			if (_animationPlayer.HasAnimation(direct))
			{
				result = direct;
			}
			else
			{
				foreach (var name in animations)
				{
					if (name.ToString().Equals(animName, StringComparison.OrdinalIgnoreCase))
					{
						result = name;
						break;
					}
				}

				if (result == null)
				{
					var retargeted = TryRetargetAnimation(animName, direct);
					if (retargeted != null) result = retargeted;
					else if (animations.Length > 0) result = animations[0];
				}
			}
		}

		_resolvedAnimCache[animName] = result;
		return result;
	}

	public void ApplyModelTint(Color color)
	{
		SetPlayerColor(color);
	}


	protected Color GetSelectionRingColor()
	{
		return new Color(0.22f, 0.54f, 0.26f);
	}

}
