using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private void BuildStagesSection(VBoxContainer parent)
	{
		AddSectionHeader(parent, "🎬 " + TranslationServer.Translate("SEQUENCE STAGES"), new Color(0.55f, 0.9f, 0.75f));
		AddDescription(parent, TranslationServer.Translate("Stages replay other effects as a timed sequence: immediately, after a delay, on projectile impact, or when the target dies. Leave the duration at 0 to use the stage's own lifetime; set it to keep the sequence alive for procedural stages."));

		_stagesContainer = new VBoxContainer();
		_stagesContainer.Name = "StagesContainer_VBoxContainer";
		_stagesContainer.AddThemeConstantOverride("separation", 6);
		parent.AddChild(_stagesContainer);

		var addRow = new HBoxContainer();
		addRow.Name = "AddStageRow_HBoxContainer";
		var btnAddStage = AddButton(addRow, "+ " + TranslationServer.Translate("Add Stage"), AddStage, "Append a stage to this effect sequence");
		btnAddStage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		parent.AddChild(addRow);

		RebuildStageRows();
	}

	private void AddStage()
	{
		_currentConfig.Stages ??= new List<VfxStageMetadata>();
		_currentConfig.Stages.Add(new VfxStageMetadata { Trigger = VfxStageTrigger.Immediately, Scale = 1.0f });
		RebuildStageRows();
	}

	private void RemoveStage(VfxStageMetadata stage)
	{
		if (_currentConfig.Stages == null) return;
		_currentConfig.Stages.Remove(stage);
		RebuildStageRows();
	}

	private void RebuildStageRows()
	{
		if (_stagesContainer == null || !GodotObject.IsInstanceValid(_stagesContainer)) return;

		foreach (Node child in _stagesContainer.GetChildren())
		{
			_stagesContainer.RemoveChild(child);
			child.QueueFree();
		}

		var stages = _currentConfig.Stages;
		if (stages == null || stages.Count == 0)
		{
			AddDescription(_stagesContainer, TranslationServer.Translate("No stages yet. This effect plays as a single instance."));
			return;
		}

		for (int index = 0; index < stages.Count; index++)
		{
			BuildStageRow(stages[index], index);
		}
	}

	private void BuildStageRow(VfxStageMetadata stage, int index)
	{
		var panel = new PanelContainer();
		panel.Name = $"Stage{index}_PanelContainer";
		var box = new VBoxContainer();
		box.Name = $"Stage{index}_VBoxContainer";
		box.AddThemeConstantOverride("separation", 4);
		panel.AddChild(box);

		var header = new HBoxContainer();
		header.Name = $"Stage{index}Header_HBoxContainer";
		var title = new Label();
		title.Name = $"Stage{index}Title_Label";
		title.Text = string.Format(TranslationServer.Translate("Stage {0}"), index + 1);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.AddThemeFontSizeOverride("font_size", 11);
		header.AddChild(title);
		header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
		AddButton(header, "✕", () => RemoveStage(stage), "Remove this stage", 10, new Vector2(0, 22));
		box.AddChild(header);

		string[] triggerNames = Enum.GetNames<VfxStageTrigger>();
		AddOptionDropdown(box, TranslationServer.Translate("Trigger"), triggerNames, Array.IndexOf(triggerNames, stage.Trigger.ToString()), (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx < 0 || idx >= triggerNames.Length) return;
			stage.Trigger = (VfxStageTrigger)idx;
		}, 140f);

		string[] effectOptions = BuildStageEffectOptions(stage.EffectId);
		AddOptionDropdown(box, TranslationServer.Translate("Effect"), effectOptions, FindOptionIndex(effectOptions, stage.EffectId), (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx < 0 || idx >= effectOptions.Length) return;
			stage.EffectId = effectOptions[idx];
		}, 140f);

		AddNumberInput(box, TranslationServer.Translate("Delay (s)"), stage.DelaySeconds, (val) => stage.DelaySeconds = Math.Max(0f, val), 0.05f, "0", 140f);
		AddNumberInput(box, TranslationServer.Translate("Stage Duration (s)"), stage.DurationSeconds, (val) => stage.DurationSeconds = Math.Max(0f, val), 0.1f, "auto", 140f);
		AddNumberInput(box, TranslationServer.Translate("Stage Scale"), stage.Scale, (val) => stage.Scale = Math.Max(0.01f, val), 0.1f, "1", 140f);
		AddCheckBox(box, TranslationServer.Translate("Play as a one-shot particle burst"), stage.SingleInstance, (pressed) => stage.SingleInstance = pressed);

		_stagesContainer.AddChild(panel);
	}

	private static string[] BuildStageEffectOptions(string currentEffectId)
	{
		var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

		var vfxRegistry = GameContext.RegistryService?.VfxRegistry;
		if (vfxRegistry != null)
		{
			foreach (string vfxId in vfxRegistry.Keys) options.Add(vfxId);
		}

		foreach (string presetId in SpellParticleConfig.GetAllPresets().Keys) options.Add(presetId);

		if (!string.IsNullOrWhiteSpace(currentEffectId)) options.Add(currentEffectId);

		return options.ToArray();
	}

	private static int FindOptionIndex(string[] options, string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return -1;

		for (int index = 0; index < options.Length; index++)
		{
			if (string.Equals(options[index], value, StringComparison.OrdinalIgnoreCase)) return index;
		}

		return -1;
	}

}
