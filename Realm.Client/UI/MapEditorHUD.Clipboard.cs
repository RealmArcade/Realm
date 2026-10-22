using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;

namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		public void ClearPasteTelemetry()
		{
			if (_lblPasteTelemetry != null)
			{
				_lblPasteTelemetry.Text = "";
			}
		}

		public void UpdatePasteReflectionExternal(PasteReflection reflection)
		{
			GameContext.EditorService.UpdatePasteReflectionExternalDomain(reflection);
			UpdatePasteReflectionButtonText();
		}

		private void CyclePasteAnchor()
		{
			var editorService = GameContext.EditorService;
			if (editorService == null || !editorService.HasCopiedArea)
			{
				ShowFeedback(TranslationServer.Translate("No area currently in clipboard to set anchor for."));
				return;
			}

			editorService.CyclePasteAnchorDomain();
			UpdatePasteAnchorButtonText();
			ShowFeedback(string.Format(TranslationServer.Translate("Paste Anchor: {0}"), TranslationServer.Translate(EditorService.PasteAnchorNames[editorService.CurrentPasteAnchorIndex])));
		}

		public void UpdatePasteAnchorButtonText()
		{
			if (_btnPasteAnchor != null)
			{
				_btnPasteAnchor.Text = $"{UnicodeIcons.MOUSE_POINTER} " + string.Format(TranslationServer.Translate("ANCHOR: {0}"), TranslationServer.Translate(EditorService.PasteAnchorNames[GameContext.EditorService.CurrentPasteAnchorIndex]));
			}
		}

		private void CyclePasteReflection()
		{
			if (GameContext.Host == null) return;
			var next = GameContext.EditorService.CyclePasteReflectionDomain();
			UpdatePasteReflectionButtonText();
			ShowFeedback(string.Format(TranslationServer.Translate("Paste Reflection: {0}"), TranslationServer.Translate(next.ToString().ToUpperInvariant())));
		}

		public void UpdatePasteReflectionButtonText()
		{
			if (_btnPasteReflection != null && GameContext.Host != null)
			{
				_btnPasteReflection.Text = $"{UnicodeIcons.ARROWS_H} " + string.Format(TranslationServer.Translate("REFLECT: {0}"), TranslationServer.Translate(GameContext.EditorService.EditorPasteReflection.ToString().ToUpperInvariant()));
			}
		}
	}
}