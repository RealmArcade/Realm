﻿using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.UI;

namespace Realm.Client.Services;

/// <summary>
///     The damage and heal numbers that rise off a unit: a small fixed pool of Label3D nodes, the live
///     entries with their start position, colour and elapsed time, and the pass that rises and fades them
///     before returning each label to the pool.
/// </summary>
internal sealed class FloatingCombatText
{
	private readonly Stack<Label3D> _floatingLabelPool = new();
	private readonly ActiveFloatingLabel[] _activeFloatingLabels = new ActiveFloatingLabel[MaxActiveFloatingLabels];
	private int _activeFloatingLabelsCount = 0;
	private const int MaxActiveFloatingLabels = 48;
	private const float FloatingLabelDuration = 1.0f;
	private const float FloatingLabelRiseDistance = 1.6f;

	private struct ActiveFloatingLabel
	{
		public Label3D Label;
		public Vector3 StartPosition;
		public Color BaseColor;
		public float Elapsed;
	}

	private Label3D RentFloatingLabel(Node3D parent)
	{
		Label3D? label = null;
		while (_floatingLabelPool.Count > 0)
		{
			var cand = _floatingLabelPool.Pop();
			if (cand != null && GodotObject.IsInstanceValid(cand))
			{
				label = cand;
				break;
			}
		}

		if (label == null)
		{
			label = new Label3D { Name = "FloatingCombatText" };
			label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		}

		label.Visible = true;
		if (label.GetParent() != parent)
		{
			if (label.GetParent() != null)
			{
				label.GetParent().RemoveChild(label);
			}
			parent.AddChild(label);
		}

		return label;
	}

	private void ReturnFloatingLabel(Label3D label)
	{
		if (label == null || !GodotObject.IsInstanceValid(label)) return;
		label.Visible = false;
		_floatingLabelPool.Push(label);
	}

	public void Update(float fDelta)
	{
		for (int i = _activeFloatingLabelsCount - 1; i >= 0; i--)
		{
			ref var entry = ref _activeFloatingLabels[i];
			if (entry.Label == null || !GodotObject.IsInstanceValid(entry.Label))
			{
				int lastIdx = --_activeFloatingLabelsCount;
				if (i < lastIdx)
				{
					_activeFloatingLabels[i] = _activeFloatingLabels[lastIdx];
				}
				_activeFloatingLabels[lastIdx] = default;
				continue;
			}

			entry.Elapsed += fDelta;
			if (entry.Elapsed >= FloatingLabelDuration)
			{
				ReturnFloatingLabel(entry.Label);
				int lastIdx = --_activeFloatingLabelsCount;
				if (i < lastIdx)
				{
					_activeFloatingLabels[i] = _activeFloatingLabels[lastIdx];
				}
				_activeFloatingLabels[lastIdx] = default;
				continue;
			}

			float t = entry.Elapsed / FloatingLabelDuration;
			float oneMinusT = 1.0f - t;
			float easedRise = 1.0f - (oneMinusT * oneMinusT * oneMinusT);
			entry.Label.Position = new Vector3(
				entry.StartPosition.X,
				entry.StartPosition.Y + FloatingLabelRiseDistance * easedRise,
				entry.StartPosition.Z);
			entry.Label.Modulate = new Color(
				entry.BaseColor.R,
				entry.BaseColor.G,
				entry.BaseColor.B,
				1.0f - t);
		}
	}

	public void SpawnDamage(Node3D parent, Vector3 worldPosition, float amount, bool isCritical = false)
	{
		if (!GameSettings.FloatingCombatText) return;
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeFloatingLabelsCount >= MaxActiveFloatingLabels) return;

		int amountInt = (int)Math.Round(amount);
		if (amountInt <= 0) return;

		bool isHeavyOrCrit = isCritical || amount >= 75f;
		Color baseColor = isHeavyOrCrit ? new Color(1.0f, 0.85f, 0.2f, 1.0f) : new Color(1.0f, 0.25f, 0.25f, 1.0f);

		var label = RentFloatingLabel(parent);
		label.Text = isHeavyOrCrit ? $"CRIT! -{amountInt}" : $"-{amountInt}";
		label.Modulate = baseColor;
		label.OutlineModulate = Colors.Black;
		label.OutlineSize = isHeavyOrCrit ? 14 : 10;
		label.FontSize = isHeavyOrCrit ? 52 : 38;

		float offsetX = (float)GD.RandRange(-0.25, 0.25);
		float offsetZ = (float)GD.RandRange(-0.25, 0.25);
		Vector3 startPosition = worldPosition + new Vector3(offsetX, 1.8f, offsetZ);
		label.Position = startPosition;

		_activeFloatingLabels[_activeFloatingLabelsCount++] = new ActiveFloatingLabel
		{
			Label = label,
			StartPosition = startPosition,
			BaseColor = baseColor,
			Elapsed = 0f
		};
	}

	public void SpawnHeal(Node3D parent, Vector3 worldPosition, float amount)
	{
		if (!GameSettings.FloatingCombatText) return;
		if (parent == null || !GodotObject.IsInstanceValid(parent)) return;
		if (_activeFloatingLabelsCount >= MaxActiveFloatingLabels) return;

		int amountInt = (int)Math.Round(amount);
		if (amountInt <= 0) return;

		Color baseColor = new Color(0.2f, 0.95f, 0.3f, 1.0f);
		var label = RentFloatingLabel(parent);
		label.Text = $"+{amountInt}";
		label.Modulate = baseColor;
		label.OutlineModulate = Colors.Black;
		label.OutlineSize = 10;
		label.FontSize = 38;

		float offsetX = (float)GD.RandRange(-0.25, 0.25);
		float offsetZ = (float)GD.RandRange(-0.25, 0.25);
		Vector3 startPosition = worldPosition + new Vector3(offsetX, 1.8f, offsetZ);
		label.Position = startPosition;

		_activeFloatingLabels[_activeFloatingLabelsCount++] = new ActiveFloatingLabel
		{
			Label = label,
			StartPosition = startPosition,
			BaseColor = baseColor,
			Elapsed = 0f
		};
	}
}
