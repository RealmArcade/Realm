using Godot;
using NSec.Cryptography;
using Realm.EditorAPI;
using Realm.Client.Services;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using PasteReflection = Realm.Ecs.Components.Core.PasteReflection;
using TerrainCell = Realm.Ecs.Components.Terrain.TerrainCell;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;

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
            if (_lastPasteReflectionExternal == reflection) return;
            _lastPasteReflectionExternal = reflection;
            UpdatePasteReflectionButtonText();
        }

        private int GetPasteAnchorX(int index, int width)
        {
            return index switch
            {
                0 => width / 2,
                1 => 0,
                2 => Math.Max(0, width - 1),
                3 => Math.Max(0, width - 1),
                4 => 0,
                _ => width / 2
            };
        }

        private int GetPasteAnchorZ(int index, int depth)
        {
            return index switch
            {
                0 => depth / 2,
                1 => 0,
                2 => 0,
                3 => Math.Max(0, depth - 1),
                4 => Math.Max(0, depth - 1),
                _ => depth / 2
            };
        }

        private void CyclePasteAnchor()
        {
            if (_editorService == null || !_editorService.HasCopiedArea)
            {
                ShowFeedback(TranslationServer.Translate("No area currently in clipboard to set anchor for."));
                return;
            }
            _currentPasteAnchorIndex = (_currentPasteAnchorIndex + 1) % _pasteAnchorNames.Length;
            int w = _editorService.CopiedAreaWidth;
            int d = _editorService.CopiedAreaDepth;
            int ax = GetPasteAnchorX(_currentPasteAnchorIndex, w);
            int az = GetPasteAnchorZ(_currentPasteAnchorIndex, d);
            _editorService.SetCopiedAreaAnchor(ax, az);
            UpdatePasteAnchorButtonText();
            ShowFeedback(string.Format(TranslationServer.Translate("Paste Anchor: {0}"), TranslationServer.Translate(_pasteAnchorNames[_currentPasteAnchorIndex])));
        }

        public void UpdatePasteAnchorButtonText()
        {
            if (_btnPasteAnchor != null)
            {
                _btnPasteAnchor.Text = string.Format(TranslationServer.Translate($"{UnicodeIcons.MOUSE_POINTER} ANCHOR: {0}"), TranslationServer.Translate(_pasteAnchorNames[_currentPasteAnchorIndex]));
            }
        }

        private void CyclePasteReflection()
        {
            if (Realm.Client.Core.GameHost.Instance == null) return;
            var current = Realm.Client.Core.GameHost.Instance.EditorPasteReflection;
            var next = current switch
            {
                PasteReflection.None => PasteReflection.Horizontal,
                PasteReflection.Horizontal => PasteReflection.Vertical,
                PasteReflection.Vertical => PasteReflection.None,
                _ => PasteReflection.None
            };
            Realm.Client.Core.GameHost.Instance.EditorPasteReflection = next;
            UpdatePasteReflectionButtonText();
            ShowFeedback(string.Format(TranslationServer.Translate("Paste Reflection: {0}"), TranslationServer.Translate(next.ToString().ToUpperInvariant())));
        }

        public void UpdatePasteReflectionButtonText()
        {
            if (_btnPasteReflection != null && Realm.Client.Core.GameHost.Instance != null)
            {
                _btnPasteReflection.Text = string.Format(TranslationServer.Translate($"{UnicodeIcons.ARROWS_H} REFLECT: {0}"), TranslationServer.Translate(Realm.Client.Core.GameHost.Instance.EditorPasteReflection.ToString().ToUpperInvariant()));
            }
        }
    }
}
