using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class ObjectAttachmentDialog
{
	private void UpdateSocketDropdown()
	{
		if (_optSocket == null) return;
		_isUpdatingUI = true;
		_optSocket.Clear();
		int selectedIdx = 0;
		for (int i = 0; i < _currentAvailableSockets.Count; i++)
		{
			var sock = _currentAvailableSockets[i];
			_optSocket.AddItem(TranslationServer.Translate(sock.DisplayName), i);
			if (sock.SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				selectedIdx = i;
			}
		}

		_optSocket.Selected = selectedIdx;
		if (_currentAvailableSockets.Count > 0)
		{
			_currentSocketId = _currentAvailableSockets[selectedIdx].SocketId;
		}

		_isUpdatingUI = false;
		UpdateParentDropdown();
	}

	private void UpdateParentDropdown()
	{
		if (_optParent == null) return;
		bool prevUpdating = _isUpdatingUI;
		_isUpdatingUI = true;
		_optParent.Clear();
		_availableParents.Clear();

		_optParent.AddItem(string.Format(TranslationServer.Translate("Socket: {0} (Direct)"), _currentSocketId), 0);
		_availableParents.Add(null);

		var configured = GetConfiguredAttachments();
		string normSocket = NormalizeSocketId(_currentSocketId);
		int selectedIdx = 0;

		foreach (var entry in configured)
		{
			if (!ShouldIncludeAsParent(entry, normSocket)) continue;

			string cleanId = Path.GetFileNameWithoutExtension(entry.AttachmentId);
			if (_availableParents.Contains(cleanId) || _availableParents.Contains(entry.AttachmentId)) continue;

			int itemIdx = _availableParents.Count;
			_availableParents.Add(entry.AttachmentId);

			AddParentOption(entry, cleanId, itemIdx);

			if (IsCurrentParent(entry.AttachmentId, cleanId))
			{
				selectedIdx = itemIdx;
			}
		}

		if (selectedIdx == 0)
		{
			_currentParentAttachmentId = null;
		}

		_optParent.Selected = selectedIdx;
		_isUpdatingUI = prevUpdating;
	}

	private bool ShouldIncludeAsParent(ConfiguredAttachmentEntry entry, string normSocket)
	{
		if (!entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase)) return false;
		if (string.Equals(entry.AttachmentId, _currentAttachmentId, StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId)) return false;
		return true;
	}

	private void AddParentOption(ConfiguredAttachmentEntry entry, string cleanId, int itemIdx)
	{
		string displayName = entry.AttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? $"✨ {entry.AttachmentId.Substring(4)}"
			: $"🗡️ {cleanId}";
		_optParent.AddItem(string.Format(TranslationServer.Translate("Mesh: {0}"), displayName), itemIdx);
	}

	private bool IsCurrentParent(string attachmentId, string cleanId)
	{
		if (string.IsNullOrEmpty(_currentParentAttachmentId)) return false;
		return string.Equals(_currentParentAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(_currentParentAttachmentId, cleanId, StringComparison.OrdinalIgnoreCase);
	}

	private void RefreshAttachmentList()
	{
		_availableAttachments = GetAvailableObjectAttachmentIds();
		if (_optAttachmentPicker == null) return;

		_isUpdatingUI = true;
		_optAttachmentPicker.Clear();

		int selectedIdx = 0;
		string cleanCurrent = GetCleanAttachmentId(_currentAttachmentId);

		for (int i = 0; i < _availableAttachments.Count; i++)
		{
			string item = _availableAttachments[i];
			AddAttachmentOption(item, i);

			if (!string.IsNullOrEmpty(cleanCurrent) && IsCurrentAttachmentMatch(item, cleanCurrent))
			{
				selectedIdx = i;
			}
		}

		if (_availableAttachments.Count > 0)
		{
			_optAttachmentPicker.Selected = selectedIdx;
			_currentAttachmentId = _availableAttachments[selectedIdx];
		}

		_isUpdatingUI = false;
	}

	private string GetCleanAttachmentId(string id)
	{
		if (string.IsNullOrEmpty(id)) return "";
		return id.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? id
			: Path.GetFileNameWithoutExtension(id);
	}

	private void AddAttachmentOption(string item, int index)
	{
		_optAttachmentPicker.AddItem(FormatAttachmentDisplayName(item), index);
	}

	private static string FormatAttachmentDisplayName(string attachmentId)
	{
		if (Unit3D.IsPlayerColorGlowAttachment(attachmentId))
		{
			return "✨ " + TranslationServer.Translate("Player Colour Glow");
		}

		return attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? $"✨ {attachmentId.Substring(4)}"
			: $"🗡️ {attachmentId}";
	}

	private bool IsCurrentAttachmentMatch(string item, string cleanCurrent)
	{
		string cleanItem = GetCleanAttachmentId(item);
		return item.Equals(_currentAttachmentId, StringComparison.OrdinalIgnoreCase) ||
		       cleanItem.Equals(cleanCurrent, StringComparison.OrdinalIgnoreCase);
	}
}
