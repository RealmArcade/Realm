using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.Core;

public readonly record struct HotkeySpec(Key Key, KeyModifierMask Modifiers)
{
	public static readonly KeyModifierMask TrackedModifiers =
		KeyModifierMask.MaskShift | KeyModifierMask.MaskCtrl | KeyModifierMask.MaskAlt | KeyModifierMask.MaskMeta;

	public static HotkeySpec None => new(Key.None, (KeyModifierMask)0);

	public bool IsNone => Key == Key.None;

	public static implicit operator HotkeySpec(Key key)
	{
		return new HotkeySpec(key, (KeyModifierMask)0);
	}

	public static bool TryParse(string? specification, out HotkeySpec hotkey, out string errorMessage)
	{
		hotkey = None;
		errorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(specification))
		{
			errorMessage = "A hotkey needs a key name, for example 'Q' or 'Shift+Q'.";
			return false;
		}

		string[] parts = specification.Split('+', StringSplitOptions.TrimEntries);
		KeyModifierMask modifiers = (KeyModifierMask)0;

		for (int index = 0; index < parts.Length - 1; index++)
		{
			if (!TryParseModifier(parts[index], out KeyModifierMask modifier))
			{
				errorMessage = $"'{parts[index]}' is not a modifier. Use Shift, Ctrl, Alt or Meta, for example 'Shift+Q'.";
				return false;
			}

			modifiers |= modifier;
		}

		string keyToken = parts[^1];
		if (TryParseModifier(keyToken, out _))
		{
			errorMessage = $"'{specification}' names only modifiers. Add the key to press, for example '{keyToken}+Q'.";
			return false;
		}

		if (!TryParseKey(keyToken, out Key key))
		{
			errorMessage = $"'{keyToken}' is not a key name. Use a key name such as 'Q', 'F1' or 'Escape'.";
			return false;
		}

		hotkey = new HotkeySpec(key, modifiers);
		return true;
	}

	public static HotkeySpec ParseOrDefault(string? specification)
	{
		if (string.IsNullOrWhiteSpace(specification)) return None;
		return TryParse(specification, out HotkeySpec hotkey, out _) ? hotkey : None;
	}

	private static bool TryParseModifier(string token, out KeyModifierMask modifier)
	{
		switch (token.Trim().ToLowerInvariant())
		{
			case "shift":
				modifier = KeyModifierMask.MaskShift;
				return true;
			case "ctrl":
			case "control":
				modifier = KeyModifierMask.MaskCtrl;
				return true;
			case "alt":
				modifier = KeyModifierMask.MaskAlt;
				return true;
			case "meta":
			case "cmd":
			case "command":
			case "super":
				modifier = KeyModifierMask.MaskMeta;
				return true;
			default:
				modifier = (KeyModifierMask)0;
				return false;
		}
	}

	private static bool TryParseKey(string token, out Key key)
	{
		key = Key.None;

		string trimmed = token.Trim();
		if (trimmed.Length == 0) return false;
		if (trimmed.Contains(',') || trimmed.Contains('|') || trimmed.Contains(' ')) return false;
		if (int.TryParse(trimmed, out _)) return false;
		if (!Enum.TryParse(trimmed, true, out key)) return false;

		return key != Key.None;
	}

	public bool Matches(Key pressedKey, KeyModifierMask pressedModifiers)
	{
		if (IsNone) return false;
		if (Key != pressedKey) return false;
		return (pressedModifiers & TrackedModifiers) == (Modifiers & TrackedModifiers);
	}

	public bool MatchesIgnoringQueueModifier(Key pressedKey, KeyModifierMask pressedModifiers)
	{
		if (IsNone) return false;
		if ((Modifiers & KeyModifierMask.MaskShift) != 0) return false;
		if (Key != pressedKey) return false;
		KeyModifierMask remainingPressedModifiers = (pressedModifiers & TrackedModifiers) & ~KeyModifierMask.MaskShift;
		return remainingPressedModifiers == (Modifiers & TrackedModifiers);
	}

	public string DisplayName => IsNone ? string.Empty : $"{FormatModifiers()}{Key}";

	public override string ToString()
	{
		return DisplayName;
	}

	private string FormatModifiers()
	{
		var parts = new List<string>(4);
		if ((Modifiers & KeyModifierMask.MaskShift) != 0) parts.Add("Shift");
		if ((Modifiers & KeyModifierMask.MaskCtrl) != 0) parts.Add("Ctrl");
		if ((Modifiers & KeyModifierMask.MaskAlt) != 0) parts.Add("Alt");
		if ((Modifiers & KeyModifierMask.MaskMeta) != 0) parts.Add("Meta");

		return parts.Count == 0 ? string.Empty : $"{string.Join("+", parts)}+";
	}
}
