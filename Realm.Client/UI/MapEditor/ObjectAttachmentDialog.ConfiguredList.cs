// The configured-attachments list: one card per (socket, attachment) pair, the sockets dropdown and
// picker sync, and the lookup that maps a card back to its entry.

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
	private void RebuildConfiguredAttachmentsUI()
	{
		if (_configuredAttachmentsContainer == null) return;

		foreach (Node child in _configuredAttachmentsContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configured = GetConfiguredAttachments();
		if (configured.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No attachments configured on this object."),
				AutowrapMode = TextServer.AutowrapMode.Word,
				CustomMinimumSize = new Vector2(530, 0),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_configuredAttachmentsContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			var card = new PanelContainer();
			card.Name = "Card_PanelContainer";
			card.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var row = new HBoxContainer();
			row.Name = "Row_HBoxContainer";
			row.AddThemeConstantOverride("separation", 8);

			string? parentAttId = entry.Orientation.ParentAttachmentId;
			bool isChild = !string.IsNullOrEmpty(parentAttId);

			string badgeText = isChild
				? $"[{entry.SocketId} ➔ {Path.GetFileNameWithoutExtension(parentAttId)}]"
				: $"[{entry.SocketId}]";

			var badge = new Label
			{
				Text = badgeText,
				CustomMinimumSize = new Vector2(isChild ? 130 : 85, 0),
				ClipText = true
			};
			badge.AddThemeColorOverride("font_color", isChild ? new Color(0.9f, 0.6f, 1.0f) : UIStyle.ColorCyanGlow);
			badge.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(badge);

			string displayName = FormatAttachmentDisplayName(entry.AttachmentId);

			var nameLbl = new Label
			{
				Text = displayName,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClipText = true
			};
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);

			var summaryLbl = new Label
			{
				Text = $"Pos: ({entry.Orientation.PositionX:F2},{entry.Orientation.PositionY:F2},{entry.Orientation.PositionZ:F2})",
				CustomMinimumSize = new Vector2(130, 0),
				ClipText = true
			};
			summaryLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
			summaryLbl.AddThemeFontSizeOverride("font_size", 10);
			row.AddChild(summaryLbl);

			string capturedSocket = entry.SocketId;
			string capturedAtt = entry.AttachmentId;
			string? capturedParent = entry.Orientation.ParentAttachmentId;
			int capturedIndex = entry.Index;

			var btnEdit = new Button();
			btnEdit.Name = "BtnEdit_Button";
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️ " + TranslationServer.Translate("Edit");
			btnEdit.AddThemeFontSizeOverride("font_size", 11);
			btnEdit.CustomMinimumSize = new Vector2(55, 22);
			btnEdit.FocusMode = FocusModeEnum.None;
			btnEdit.TooltipText = TranslationServer.Translate("Select and tune transform in sliders");
			btnEdit.Pressed += () => SelectConfiguredAttachment(capturedSocket, capturedAtt, capturedIndex);
			row.AddChild(btnEdit);

			var btnRemove = UIStyle.CreateBareButton("BtnRemove_Button");
			btnRemove.Text = $"{UnicodeIcons.TRASH} " + TranslationServer.Translate("Remove");
			btnRemove.AddThemeFontSizeOverride("font_size", 11);
			btnRemove.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
			btnRemove.CustomMinimumSize = new Vector2(65, 22);
			btnRemove.FocusMode = FocusModeEnum.None;
			btnRemove.TooltipText = TranslationServer.Translate("Detach and remove this attachment");
			btnRemove.Pressed += () => RemoveAttachment(capturedSocket, capturedAtt, capturedParent);
			row.AddChild(btnRemove);

			card.AddChild(row);
			_configuredAttachmentsContainer.AddChild(card);
		}
	}
}
