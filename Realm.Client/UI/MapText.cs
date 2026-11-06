using System;
using System.Text;
using Godot;

namespace Realm.Client.UI;

/// <summary>
///     Translates the text a map writes in its metadata.json - unit, ability, item and upgrade names, descriptions and
///     tooltips - for the language the local player picked in the settings menu. The lookup runs on the client, so every
///     player in a match reads the same map in their own language.
/// </summary>
/// <remarks>
///     <para>
///         The catalog is the map's own <c>locale/{language}.json</c>, which
///         <see cref="LocalizationManager.SetupTranslations"/> merges into the TranslationServer next to the game's own
///         catalogs whenever a map loads. An entry's key is the English text exactly as metadata.json writes it, so a map
///         needs no key naming scheme: an ability description is translated by an entry whose key is that description.
///     </para>
///     <para>
///         Nothing here parses or formats the text, so it cannot damage what a metadata string carries. A <c>{{token}}</c>
///         is looked up and returned untouched along with the rest of the sentence, and is only replaced afterwards by
///         <see cref="TooltipTemplate.Resolve"/>, so a translation is free to move its placeholders around. BBCode is
///         left alone for the same reason: <see cref="TranslateName"/> only ever rewrites the text between tags, which
///         keeps colour markup out of the keys a translator has to copy.
///     </para>
/// </remarks>
public static class MapText
{
	// Anything that makes a leading "[...]" something other than a plain role word: BBCode markup, a path, or a phrase.
	private static readonly char[] s_roleTagRejected = { '[', ']', '=', '/', ' ' };

	/// <summary>
	///     Translates a whole metadata string, such as an ability description or an item blurb. The string is used as the
	///     catalog key, placeholders and markup included, and is returned unchanged when the map does not translate it.
	/// </summary>
	public static string Translate(string? text)
	{
		if (string.IsNullOrEmpty(text)) return string.Empty;

		TryLookup(text, out string translated);
		return translated;
	}

	/// <summary>
	///     Translates a tooltip template, which additionally understands a leading role tag: a map can tag an ability with
	///     a role and the client renders "[Damage] Deals 5 damage". Translating the role and the sentence as two entries
	///     keeps the tag out of the key a translator copies; a whole-text entry for the rendered tooltip still wins.
	/// </summary>
	public static string TranslateTemplate(string? template)
	{
		if (string.IsNullOrEmpty(template)) return string.Empty;

		if (TryLookup(template, out string translated)) return translated;

		return TranslateRolePrefix(template);
	}

	/// <summary>
	///     Translates a short name that may carry BBCode, such as a template name a map colours by element. Tags are
	///     copied verbatim and only the visible runs are looked up, so the entry a translator writes is the plain name.
	/// </summary>
	public static string TranslateName(string? name)
	{
		if (string.IsNullOrEmpty(name)) return string.Empty;

		int open = name.IndexOf('[');
		if (open < 0)
		{
			TryLookup(name, out string whole);
			return whole;
		}

		var builder = new StringBuilder(name.Length);
		int index = 0;

		while (index < name.Length)
		{
			int nextTag = name.IndexOf('[', index);
			if (nextTag < 0)
			{
				AppendTranslatedRun(builder, name, index, name.Length);
				break;
			}

			if (nextTag > index) AppendTranslatedRun(builder, name, index, nextTag);

			int close = name.IndexOf(']', nextTag + 1);
			if (close < 0)
			{
				// An unclosed '[' is part of the name, not markup, so the rest is looked up as one run.
				AppendTranslatedRun(builder, name, nextTag, name.Length);
				break;
			}

			builder.Append(name, nextTag, close - nextTag + 1);
			index = close + 1;
		}

		return builder.ToString();
	}

	private static void AppendTranslatedRun(StringBuilder builder, string text, int start, int end)
	{
		string run = text[start..end];
		if (string.IsNullOrWhiteSpace(run))
		{
			builder.Append(run);
			return;
		}

		TryLookup(run, out string translated);
		builder.Append(translated);
	}

	private static string TranslateRolePrefix(string template)
	{
		if (template[0] != '[') return template;

		int close = template.IndexOf(']', 1);
		if (close < 2 || close + 1 >= template.Length || template[close + 1] != ' ') return template;

		string role = template[1..close];
		if (role.IndexOfAny(s_roleTagRejected) >= 0) return template;

		string body = template[(close + 2)..];

		TryLookup(role, out string translatedRole);
		TryLookup(body, out string translatedBody);

		if (string.Equals(translatedRole, role, StringComparison.Ordinal) &&
			string.Equals(translatedBody, body, StringComparison.Ordinal))
		{
			return template;
		}

		return $"[{translatedRole}] {translatedBody}";
	}

	/// <summary>
	///     Looks one string up in the loaded catalogs. A missing key comes back as itself, which is how an untranslated
	///     map reads in English and how the caller tells that nothing was found.
	/// </summary>
	private static bool TryLookup(string text, out string translated)
	{
		translated = TranslationServer.Translate(text);
		if (string.IsNullOrEmpty(translated))
		{
			translated = text;
			return false;
		}

		return !string.Equals(translated, text, StringComparison.Ordinal);
	}
}
