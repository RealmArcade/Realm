using System;
using Godot;
using Realm.Client.Utils;
using Realm.Ecs.Components.Core;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// The mirrored/symmetry dropdown that appears in the brush, placement and clipboard cards. All three are filled
/// with the same five entries and refreshed the same way, and none of them needs anything but the editor's current
/// mode and fold count, so the population and refresh live here instead of being repeated per panel.
/// </summary>
internal static class SymmetryDropdowns
{
	private static string RotationalText(int folds) =>
		$"{UnicodeIcons.ROTATE} " + string.Format(TranslationServer.Translate("ROTATIONAL ({0}-FOLD)"), folds);

	/// <summary>
	/// Fills an empty dropdown with the mirror modes and wires its selection to <paramref name="onSelected"/>.
	/// </summary>
	public static void Populate(OptionButton opt, int folds, Action<MirrorMode> onSelected, Font? font,
		string tooltip = "")
	{
		if (opt == null) return;
		opt.Clear();
		opt.Set("icon_max_width", 0);
		opt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		opt.ClipText = true;

		opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.BAN} SYMMETRY: NONE"), (int)MirrorMode.None);
		opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_V} MIRROR: VERTICAL"), (int)MirrorMode.Vertical);
		opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.ARROWS_H} MIRROR: HORIZONTAL"), (int)MirrorMode.Horizontal);
		opt.AddItem(TranslationServer.Translate($"{UnicodeIcons.GRID_QUAD} MIRROR: QUAD"), (int)MirrorMode.Both);
		opt.AddItem(RotationalText(folds), (int)MirrorMode.Rotational);

		if (!string.IsNullOrEmpty(tooltip))
		{
			opt.TooltipText = tooltip;
		}

		MapEditorStyle.OptionButtonPopup(opt, font);

		opt.ItemSelected += (long idx) =>
		{
			int modeId = opt.GetItemId((int)idx);
			onSelected((MirrorMode)modeId);
		};
	}

	/// <summary>
	/// Re-labels the rotational entry (the fold count changes as symmetry spokes do) and selects the given mode.
	/// Selection is written without signalling so the handler above does not re-enter the editor's setter.
	/// </summary>
	public static void Refresh(OptionButton? opt, MirrorMode mode, int folds)
	{
		if (opt == null) return;

		string rotText = RotationalText(folds);
		for (int i = 0; i < opt.ItemCount; i++)
		{
			if (opt.GetItemId(i) == (int)MirrorMode.Rotational)
			{
				opt.SetItemText(i, rotText);
				break;
			}
		}

		for (int i = 0; i < opt.ItemCount; i++)
		{
			if (opt.GetItemId(i) == (int)mode)
			{
				if (opt.Selected != i)
				{
					opt.Selected = i;
				}

				break;
			}
		}
	}
}
