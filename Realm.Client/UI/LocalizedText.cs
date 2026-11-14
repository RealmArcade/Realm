using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.UI;

/// <summary>
///     Text a control derives from a translation key when it draws it, instead of text that was translated once and kept.
///     Every control registered here is asked for its text again whenever the language changes, so a string built from a
///     key and the values around it - a count, a name, a caret, an uppercase title - is redrawn in the new language
///     rather than left in the one it was built in. A control registered once never has to be revisited by the screen
///     that owns it, which is what keeps a language change working for controls that do not exist yet.
/// </summary>
public static class LocalizedText
{
	private enum TextSlot
	{
		Text,
		Tooltip,
		OptionItemText
	}

	private readonly record struct Registration(ulong InstanceId, TextSlot Slot, int ItemIndex);

	private class Binding
	{
		public Binding(Node target, TextSlot slot, int itemIndex, string key, object?[] arguments, bool uppercase, string suffix, Func<string?>? renderer)
		{
			Target = new WeakReference<Node>(target);
			Slot = slot;
			ItemIndex = itemIndex;
			Key = key;
			Arguments = arguments;
			Uppercase = uppercase;
			Suffix = suffix;
			Renderer = renderer;
		}

		public WeakReference<Node> Target { get; }
		public TextSlot Slot { get; }
		public int ItemIndex { get; }
		public string Key { get; }
		public object?[] Arguments { get; }
		public bool Uppercase { get; }
		public string Suffix { get; }
		public Func<string?>? Renderer { get; }
	}

	private static readonly Dictionary<Registration, Binding> Bindings = new();
	private static bool _listening;

	private static void Listen()
	{
		if (_listening) return;
		_listening = true;
		LocalizationManager.LanguageChanged += _ => RefreshAll();
	}

	public static void Set(Control control, string key)
	{
		Set(control, key, Array.Empty<object?>());
	}

	public static void Set(Control control, string key, params object?[] arguments)
	{
		Register(control, TextSlot.Text, 0, key, arguments, false, "", null);
	}

	public static void SetUppercase(Control control, string key, string suffix)
	{
		Register(control, TextSlot.Text, 0, key, Array.Empty<object?>(), true, suffix, null);
	}

	public static void SetTooltip(Control control, string key, params object?[] arguments)
	{
		Register(control, TextSlot.Tooltip, 0, key, arguments, false, "", null);
	}

	public static void Bind(Control control, Func<string?> renderer)
	{
		Register(control, TextSlot.Text, 0, "", Array.Empty<object?>(), false, "", renderer);
	}

	public static void BindTooltip(Control control, Func<string?> renderer)
	{
		Register(control, TextSlot.Tooltip, 0, "", Array.Empty<object?>(), false, "", renderer);
	}

	public static void BindOptionItem(OptionButton option, int itemIndex, Func<string?> renderer)
	{
		Register(option, TextSlot.OptionItemText, itemIndex, "", Array.Empty<object?>(), false, "", renderer);
	}

	public static void RefreshAll()
	{
		if (Bindings.Count == 0) return;

		List<Registration>? abandoned = null;
		foreach (KeyValuePair<Registration, Binding> entry in Bindings)
		{
			Binding binding = entry.Value;
			if (!binding.Target.TryGetTarget(out Node? target) || !GodotObject.IsInstanceValid(target))
			{
				abandoned ??= new List<Registration>();
				abandoned.Add(entry.Key);
				continue;
			}

			string? text = Render(binding.Key, binding.Arguments, binding.Uppercase, binding.Suffix, binding.Renderer);
			if (text != null) Apply(target, binding.Slot, binding.ItemIndex, text);
		}

		if (abandoned == null) return;
		for (int index = 0; index < abandoned.Count; index++)
		{
			Bindings.Remove(abandoned[index]);
		}
	}

	private static void Register(Node target, TextSlot slot, int itemIndex, string key, object?[] arguments, bool uppercase, string suffix, Func<string?>? renderer)
	{
		Listen();

		string? text = Render(key, arguments, uppercase, suffix, renderer);
		Bindings[new Registration(target.GetInstanceId(), slot, itemIndex)] = new Binding(target, slot, itemIndex, key, arguments, uppercase, suffix, renderer);
		if (text != null) Apply(target, slot, itemIndex, text);
	}

	private static string? Render(string key, object?[] arguments, bool uppercase, string suffix, Func<string?>? renderer)
	{
		string? text;
		if (renderer != null)
		{
			text = renderer();
			if (text == null) return null;
		}
		else
		{
			string template = TranslationServer.Translate(key);
			text = arguments.Length == 0 ? template : string.Format(template, arguments);
		}

		if (uppercase) text = text.ToUpperInvariant();
		return string.IsNullOrEmpty(suffix) ? text : text + suffix;
	}

	private static void Apply(Node target, TextSlot slot, int itemIndex, string text)
	{
		switch (slot)
		{
			case TextSlot.Text:
				ApplyText(target, text);
				break;
			case TextSlot.Tooltip:
				if (target is Control tooltipOwner) tooltipOwner.TooltipText = text;
				break;
			case TextSlot.OptionItemText:
				if (target is OptionButton option && itemIndex < option.ItemCount) option.SetItemText(itemIndex, text);
				break;
		}
	}

	private static void ApplyText(Node target, string text)
	{
		switch (target)
		{
			case Label label:
				label.Text = text;
				break;
			case RichTextLabel richTextLabel:
				richTextLabel.Text = text;
				break;
			case Button button:
				button.Text = text;
				break;
			case Window window:
				window.Title = text;
				break;
		}
	}
}
