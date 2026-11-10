using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using Environment = System.Environment;
using HttpClient = System.Net.Http.HttpClient;
using Key = NSec.Cryptography.Key;
using static Godot.Control;
using Realm.Client.Utils;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public void SaveUnitObjectAttachment(string unitId, HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
		{
			string wsPath = GameContext.EditorService._editingDirectory;
			GameContext.RegistryService.SaveUnitObjectAttachment(unitId, hand, attachmentId, orientation, wsPath);
		}

		public void SaveUnitObjectAttachment(string unitId, string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				GameContext.RegistryService.SaveUnitObjectAttachment(unitId, socket, attachmentId, orientation, wsPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveUnitObjectAttachment error: {ex.Message}");
			}
		}

		private (string regKey, bool isBuildingMeta) ResolveRegistryKeyAndType(string unitId)
		{
			return GameContext.RegistryService.ResolveRegistryKeyAndType(unitId);
		}

		private (string regKey, bool isBuildingMeta) ResolveRegistryKeyByIterating(string unitId, string cleanId)
		{
			return GameContext.RegistryService.ResolveRegistryKeyByIterating(unitId, cleanId);
		}

		private void UpdateLocalRegistry(string regKey, bool isBuildingMeta, string socket, string attachmentId, HandAttachmentOrientation orientation)
		{
			GameContext.RegistryService.UpdateLocalRegistry(regKey, isBuildingMeta, socket, attachmentId, orientation);
		}

		private void UpdateMetadataFile(string unitId, string regKey, string socket, string attachmentId, HandAttachmentOrientation orientation, string? wsPath = null)
		{
			if (wsPath == null)
			{
				wsPath = GameContext.EditorService._editingDirectory;
			}

			GameContext.RegistryService.UpdateMetadataFile(unitId, regKey, socket, attachmentId, orientation, wsPath);
		}

		private void ApplyAttachmentsToGameInstances(string unitId, string regKey)
		{
			GameContext.RegistryService.ApplyAttachmentsToGameInstances(unitId, regKey);
		}

		private bool IsMatchingUnit(string instanceUnitId, string unitId, string regKey)
		{
			return GameContext.RegistryService.IsMatchingUnit(instanceUnitId, unitId, regKey);
		}

		public void SaveAllUnitObjectAttachments(string targetId, UnitObjectAttachments? attachments)
		{
			GameContext.RegistryService.SaveAllUnitObjectAttachments(targetId, attachments, (t, a) => RestoreUnitObjectAttachments(t, a));
		}

		public void SaveUnitPseudoSockets(string targetId, List<PseudoSocketAnchor>? pseudoSockets)
		{
			GameContext.RegistryService.SaveUnitPseudoSockets(targetId, pseudoSockets, (t, a) => RestoreUnitPseudoSockets(t, a));
		}

		public void SaveCustomWeaponToMetadata(string weaponId, WeaponMetadata weapon)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				GameContext.RegistryService.SaveCustomWeaponToMetadata(weaponId, weapon, wsPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveCustomWeaponToMetadata error: {ex.Message}");
			}
		}

		public void SaveCustomVfxToMetadata(string vfxId, VfxAttachmentConfig config)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				GameContext.RegistryService.SaveCustomVfxToMetadata(vfxId, config, wsPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveCustomVfxToMetadata error: {ex.Message}");
			}
		}

		public void RemoveCustomVfxFromMetadata(string vfxId)
		{
			try
			{
				string wsPath = GameContext.EditorService._editingDirectory;
				GameContext.RegistryService.RemoveCustomVfxFromMetadata(vfxId, wsPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] RemoveCustomVfxFromMetadata error: {ex.Message}");
			}
		}

		public void SaveCustomUnitAnimations(string unitId, Dictionary<string, List<UnitAnimationEntry>> animations)
		{
			try
			{
				GameContext.EditorService.SaveCustomUnitAnimations(unitId, animations);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveCustomUnitAnimations error: {ex.Message}");
			}
		}

		public void SaveCustomUnitAnimations(string unitId, Dictionary<string, string[]> animations)
		{
			var converted = new Dictionary<string, List<UnitAnimationEntry>>(StringComparer.OrdinalIgnoreCase);
			if (animations != null)
			{
				foreach (var kvp in animations)
				{
					converted[kvp.Key] = (kvp.Value ?? Array.Empty<string>())
						.Select(s => new UnitAnimationEntry { Animation = s })
						.ToList();
				}
			}

			SaveCustomUnitAnimations(unitId, converted);
		}

		public void SaveEntityModelPathToMetadata(string entityId, string fieldName, string domain, string newModelPath)
		{
			try
			{
				GameContext.EditorService.SaveEntityModelPathToMetadata(entityId, fieldName, domain, newModelPath);

				GameContext.Host?.LoadUnitMetadata(GameContext.EditorService.GetWorkspacePath());
				if (fieldName != "PortraitModelPath")
				{
					GameContext.Host?.RefreshAllPlacedObjectModels(entityId);
				}

				_entityPaletteController?.SelectCategory(_entityPaletteController.CurrentCategory, triggerAddObject: false);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveEntityModelPathToMetadata error: {ex.Message}");
			}
		}


		public void SaveCustomAbilityVfxToMetadata(string abilityId, string visualEffect, string castSound, string iconPath, float aoeRadius)
		{
			try
			{
				GameContext.EditorService.SaveCustomAbilityVfxToMetadata(abilityId, visualEffect, castSound, iconPath, aoeRadius);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[MapEditorHUD] SaveCustomAbilityVfxToMetadata error: {ex.Message}");
			}
		}
	}
}
