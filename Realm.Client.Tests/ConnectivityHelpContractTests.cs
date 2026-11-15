using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GdUnit4;
using Realm.Shared.Reachability;

namespace Realm.Client.Tests;

/// <summary>
///     Holds the connectivity help to the shapes its two audiences and its own copy depend on: the causes each side lists,
///     the translation call behind every sentence, and the cause a measured failure opens on. All three fail silently -
///     a missing key reads as English in every language, and a cause the game just ruled out is still a readable page - so
///     they are checked here rather than discovered by a player.
/// </summary>
[TestSuite]
public class ConnectivityHelpContractTests
{
	private const int GamePort = 8999;

	private static readonly ConnectivityIssue[] HostingCauses =
	{
		ConnectivityIssue.RouterBlocksInboundPath,
		ConnectivityIssue.OutsidePortChangesPerPlayer,
		ConnectivityIssue.FirewallOrSecuritySoftware,
		ConnectivityIssue.DoubleNat,
		ConnectivityIssue.CarrierGradeNat,
		ConnectivityIssue.VpnOrVirtualAdapter,
		ConnectivityIssue.RestrictiveNetwork,
		ConnectivityIssue.LocalSeatBroken,
		ConnectivityIssue.LobbyServerUnreachable
	};

	private static readonly ConnectivityIssue[] JoiningCauses =
	{
		ConnectivityIssue.RestrictiveNetwork,
		ConnectivityIssue.VpnOrVirtualAdapter,
		ConnectivityIssue.CarrierGradeNat,
		ConnectivityIssue.DoubleNat,
		ConnectivityIssue.FirewallOrSecuritySoftware,
		ConnectivityIssue.RouterBlocksInboundPath,
		ConnectivityIssue.LobbyServerUnreachable
	};

	[TestCase]
	public void BothSidesListTheCausesThisBuildMeasures()
	{
		Expect(ConnectivityIssueCatalog.For(ConnectivityAudience.Hosting).Select(entry => entry.Issue).SequenceEqual(HostingCauses),
			"The hosting issue list changed. Every cause it lists is a failure path the game measures; a new one needs copy for both sides and a check.");

		Expect(ConnectivityIssueCatalog.For(ConnectivityAudience.Joining).Select(entry => entry.Issue).SequenceEqual(JoiningCauses),
			"The joining issue list changed. A joiner fails at its own network and at the host's, and the list is what says which.");

		foreach (ConnectivityAudience audience in Enum.GetValues<ConnectivityAudience>())
		{
			foreach (ConnectivityIssueContent entry in ConnectivityIssueCatalog.For(audience))
			{
				Expect(entry.TitleKey.Length > 0 && entry.SummaryKey.Length > 0 && entry.SearchPhraseKey.Length > 0 && entry.NextStepKey.Length > 0,
					$"{audience}/{entry.Issue} is missing one of its four sentences.");
				Expect(SentenceCount(entry.SummaryKey) <= 3, $"{audience}/{entry.Issue} explains itself in more than three sentences.");

				int links = 0;
				foreach (ConnectivityLink link in new[] { entry.PrimaryLink, entry.SecondaryLink })
				{
					if (link.Url.Length == 0) continue;
					links++;
					var uri = new Uri(link.Url);
					Expect(link.Url.StartsWith("https://", StringComparison.Ordinal), $"{audience}/{entry.Issue} links over plain http.");
					Expect(uri.Host is "en.wikipedia.org" or "learn.microsoft.com", $"{audience}/{entry.Issue} links to the untrusted host '{uri.Host}'.");
				}

				Expect(links <= 2, $"{audience}/{entry.Issue} offers more than two links.");
			}
		}
	}

	[TestCase]
	public void EverySentenceTheHelpCanDrawIsATranslationKey()
	{
		HashSet<string> english = EnglishCatalog();
		var missing = Sentences()
			.Where(sentence => !english.Contains(sentence))
			.OrderBy(sentence => sentence, StringComparer.Ordinal)
			.ToList();

		Expect(missing.Count == 0,
			"These sentences have no TranslationServer.Translate call site in Realm.Client, so the localization build prunes them and every language reads them in English:\n"
			+ string.Join("\n", missing));
	}

	[TestCase]
	public void EveryMeasuredFailureOpensOnACauseThatWasNotRuledOut()
	{
		foreach (HostReachabilityFailure failure in Enum.GetValues<HostReachabilityFailure>())
		{
			if (failure == HostReachabilityFailure.None) continue;

			ConnectivityFacts facts = new()
			{
				Audience = ConnectivityAudience.Hosting,
				HostFailure = failure,
				ListedPort = GamePort,
				PreferredPort = GamePort,
				FirewallGrant = WindowsFirewallGrant.Granted,
				FirewallRule = WindowsFirewallRuleState.Allowed,
				LobbyServerProbed = true,
				LobbyServerAnswered = true,
				OffersIpv4 = true
			};

			ConnectivityIssue selected = ConnectivityCheck.InitialIssue(facts);
			Expect(HostingCauses.Contains(selected), $"{failure} opens on '{selected}', which the hosting list does not carry.");

			ConnectivityDiagnosis diagnosis = ConnectivityCheck.Evaluate(facts, selected);
			Expect(diagnosis.Status != ConnectivityDiagnosisStatus.RuledOut,
				$"{failure} opens on '{selected}' and then rules it out with: {diagnosis.DetailKey}");

			ConnectivityDiagnosis everyAnswer = ConnectivityCheck.EvaluateAll(facts).Single(answer => answer.Issue == selected);
			Expect(everyAnswer.Status == diagnosis.Status, $"{failure} answers '{selected}' two different ways in one run.");
		}
	}

	private static IEnumerable<string> Sentences()
	{
		return typeof(ConnectivitySentences)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Where(field => !field.Name.StartsWith("Url", StringComparison.Ordinal))
			.Select(field => (string)field.GetRawConstantValue()!);
	}

	private static HashSet<string> EnglishCatalog()
	{
		string? path = Find("Realm.Client/locale/en.json");
		if (path == null) throw new Exception("Realm.Client/locale/en.json could not be found from the test directory.");

		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
		var catalog = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty property in document.RootElement.EnumerateObject()) catalog.Add(property.Name);
		return catalog;
	}

	private static string? Find(string relativePath)
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory != null)
		{
			string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(candidate)) return candidate;
			directory = directory.Parent;
		}

		return null;
	}

	private static int SentenceCount(string text)
	{
		return Regex.Matches(text.Trim(), "[.!?](?=\\s+[\"'\\p{Lu}\\p{N}])", RegexOptions.CultureInvariant).Count + 1;
	}

	private static void Expect(bool condition, string message)
	{
		if (!condition) throw new Exception(message);
	}
}
