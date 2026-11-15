using Godot;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The one place the connectivity help turns a sentence into the language the player reads. Every sentence the help can
///     draw is named here beside the translation call that resolves it, because a sentence reached only through a table is a
///     sentence the localization build cannot see, and a translation key with no call site is pruned from the shipped
///     language file. A sentence this switch does not name is drawn in English, which is the behaviour every other string in
///     the game already has.
/// </summary>
public static class ConnectivityHelpText
{
	/// <summary>
	///     The sentence for one key, in the language the game is running in. The key is the English sentence itself, which is
	///     what makes a key the build never collected still readable rather than blank.
	/// </summary>
	/// <param name="sentenceKey">The sentence to draw, as the shared vocabulary names it.</param>
	/// <returns>The sentence in the current language.</returns>
	public static string Text(string sentenceKey)
	{
		return sentenceKey switch
		{
		ConnectivitySentences.HostingTitle => TranslationServer.Translate(ConnectivitySentences.HostingTitle),
		ConnectivitySentences.HostingTitleLocalSeat => TranslationServer.Translate(ConnectivitySentences.HostingTitleLocalSeat),
		ConnectivitySentences.JoiningTitle => TranslationServer.Translate(ConnectivitySentences.JoiningTitle),
		ConnectivitySentences.LikelyCauseCaption => TranslationServer.Translate(ConnectivitySentences.LikelyCauseCaption),
		ConnectivitySentences.PlayerJoinFailureCaption => TranslationServer.Translate(ConnectivitySentences.PlayerJoinFailureCaption),
		ConnectivitySentences.CausesCaption => TranslationServer.Translate(ConnectivitySentences.CausesCaption),
		ConnectivitySentences.DiagnoseButton => TranslationServer.Translate(ConnectivitySentences.DiagnoseButton),
		ConnectivitySentences.RunAllButton => TranslationServer.Translate(ConnectivitySentences.RunAllButton),
		ConnectivitySentences.OpenLinkButton => TranslationServer.Translate(ConnectivitySentences.OpenLinkButton),
		ConnectivitySentences.SearchButton => TranslationServer.Translate(ConnectivitySentences.SearchButton),
		ConnectivitySentences.SearchCaption => TranslationServer.Translate(ConnectivitySentences.SearchCaption),
		ConnectivitySentences.NextStepCaption => TranslationServer.Translate(ConnectivitySentences.NextStepCaption),
		ConnectivitySentences.VerdictConfirmed => TranslationServer.Translate(ConnectivitySentences.VerdictConfirmed),
		ConnectivitySentences.VerdictInconclusive => TranslationServer.Translate(ConnectivitySentences.VerdictInconclusive),
		ConnectivitySentences.VerdictRuledOut => TranslationServer.Translate(ConnectivitySentences.VerdictRuledOut),
		ConnectivitySentences.VerdictUnchecked => TranslationServer.Translate(ConnectivitySentences.VerdictUnchecked),
		ConnectivitySentences.VerdictNeedsResearch => TranslationServer.Translate(ConnectivitySentences.VerdictNeedsResearch),
		ConnectivitySentences.RankedFoundCaption => TranslationServer.Translate(ConnectivitySentences.RankedFoundCaption),
		ConnectivitySentences.RankedNothingFound => TranslationServer.Translate(ConnectivitySentences.RankedNothingFound),
		ConnectivitySentences.DirectConnectCaption => TranslationServer.Translate(ConnectivitySentences.DirectConnectCaption),
		ConnectivitySentences.NextStepAddressFallback => TranslationServer.Translate(ConnectivitySentences.NextStepAddressFallback),
		ConnectivitySentences.DirectConnectNote => TranslationServer.Translate(ConnectivitySentences.DirectConnectNote),
		ConnectivitySentences.DirectConnectWithheldNote => TranslationServer.Translate(ConnectivitySentences.DirectConnectWithheldNote),
		ConnectivitySentences.CopyDiagnosticsButton => TranslationServer.Translate(ConnectivitySentences.CopyDiagnosticsButton),
		ConnectivitySentences.TryAgainButton => TranslationServer.Translate(ConnectivitySentences.TryAgainButton),
		ConnectivitySentences.HostDirectButton => TranslationServer.Translate(ConnectivitySentences.HostDirectButton),
		ConnectivitySentences.RetryJoinButton => TranslationServer.Translate(ConnectivitySentences.RetryJoinButton),
		ConnectivitySentences.LikelyHostProbeUnanswered => TranslationServer.Translate(ConnectivitySentences.LikelyHostProbeUnanswered),
		ConnectivitySentences.LikelyHostPerDestination => TranslationServer.Translate(ConnectivitySentences.LikelyHostPerDestination),
		ConnectivitySentences.LikelyHostMappingUnmeasured => TranslationServer.Translate(ConnectivitySentences.LikelyHostMappingUnmeasured),
		ConnectivitySentences.LikelyHostLoopback => TranslationServer.Translate(ConnectivitySentences.LikelyHostLoopback),
		ConnectivitySentences.LikelyHostRegistry => TranslationServer.Translate(ConnectivitySentences.LikelyHostRegistry),
		ConnectivitySentences.LikelyHostFamily => TranslationServer.Translate(ConnectivitySentences.LikelyHostFamily),
		ConnectivitySentences.LikelyHostOwnNetwork => TranslationServer.Translate(ConnectivitySentences.LikelyHostOwnNetwork),
		ConnectivitySentences.LikelyJoinRegistry => TranslationServer.Translate(ConnectivitySentences.LikelyJoinRegistry),
		ConnectivitySentences.LikelyJoinHostSide => TranslationServer.Translate(ConnectivitySentences.LikelyJoinHostSide),
		ConnectivitySentences.LikelyJoinOwnSide => TranslationServer.Translate(ConnectivitySentences.LikelyJoinOwnSide),
		ConnectivitySentences.LikelyJoinRefused => TranslationServer.Translate(ConnectivitySentences.LikelyJoinRefused),
		ConnectivitySentences.LinkPortForwarding => TranslationServer.Translate(ConnectivitySentences.LinkPortForwarding),
		ConnectivitySentences.LinkNetworkAddressTranslation => TranslationServer.Translate(ConnectivitySentences.LinkNetworkAddressTranslation),
		ConnectivitySentences.LinkCarrierGradeNat => TranslationServer.Translate(ConnectivitySentences.LinkCarrierGradeNat),
		ConnectivitySentences.LinkWindowsFirewall => TranslationServer.Translate(ConnectivitySentences.LinkWindowsFirewall),
		ConnectivitySentences.LinkVirtualPrivateNetwork => TranslationServer.Translate(ConnectivitySentences.LinkVirtualPrivateNetwork),
		ConnectivitySentences.LinkHolePunching => TranslationServer.Translate(ConnectivitySentences.LinkHolePunching),
		ConnectivitySentences.HostRouterTitle => TranslationServer.Translate(ConnectivitySentences.HostRouterTitle),
		ConnectivitySentences.HostRouterSummary => TranslationServer.Translate(ConnectivitySentences.HostRouterSummary),
		ConnectivitySentences.HostRouterSearch => TranslationServer.Translate(ConnectivitySentences.HostRouterSearch),
		ConnectivitySentences.HostRouterNextStep => TranslationServer.Translate(ConnectivitySentences.HostRouterNextStep),
		ConnectivitySentences.HostPerDestinationTitle => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationTitle),
		ConnectivitySentences.HostPerDestinationSummary => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationSummary),
		ConnectivitySentences.HostPerDestinationSearch => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationSearch),
		ConnectivitySentences.HostPerDestinationNextStep => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationNextStep),
		ConnectivitySentences.HostFirewallTitle => TranslationServer.Translate(ConnectivitySentences.HostFirewallTitle),
		ConnectivitySentences.HostFirewallSummary => TranslationServer.Translate(ConnectivitySentences.HostFirewallSummary),
		ConnectivitySentences.HostFirewallSearch => TranslationServer.Translate(ConnectivitySentences.HostFirewallSearch),
		ConnectivitySentences.HostFirewallNextStep => TranslationServer.Translate(ConnectivitySentences.HostFirewallNextStep),
		ConnectivitySentences.HostDoubleNatTitle => TranslationServer.Translate(ConnectivitySentences.HostDoubleNatTitle),
		ConnectivitySentences.HostDoubleNatSummary => TranslationServer.Translate(ConnectivitySentences.HostDoubleNatSummary),
		ConnectivitySentences.HostDoubleNatSearch => TranslationServer.Translate(ConnectivitySentences.HostDoubleNatSearch),
		ConnectivitySentences.HostDoubleNatNextStep => TranslationServer.Translate(ConnectivitySentences.HostDoubleNatNextStep),
		ConnectivitySentences.HostCarrierGradeNatTitle => TranslationServer.Translate(ConnectivitySentences.HostCarrierGradeNatTitle),
		ConnectivitySentences.HostCarrierGradeNatSummary => TranslationServer.Translate(ConnectivitySentences.HostCarrierGradeNatSummary),
		ConnectivitySentences.HostCarrierGradeNatSearch => TranslationServer.Translate(ConnectivitySentences.HostCarrierGradeNatSearch),
		ConnectivitySentences.HostCarrierGradeNatNextStep => TranslationServer.Translate(ConnectivitySentences.HostCarrierGradeNatNextStep),
		ConnectivitySentences.HostVpnTitle => TranslationServer.Translate(ConnectivitySentences.HostVpnTitle),
		ConnectivitySentences.HostVpnSummary => TranslationServer.Translate(ConnectivitySentences.HostVpnSummary),
		ConnectivitySentences.HostVpnSearch => TranslationServer.Translate(ConnectivitySentences.HostVpnSearch),
		ConnectivitySentences.HostVpnNextStep => TranslationServer.Translate(ConnectivitySentences.HostVpnNextStep),
		ConnectivitySentences.HostRestrictiveTitle => TranslationServer.Translate(ConnectivitySentences.HostRestrictiveTitle),
		ConnectivitySentences.HostRestrictiveSummary => TranslationServer.Translate(ConnectivitySentences.HostRestrictiveSummary),
		ConnectivitySentences.HostRestrictiveSearch => TranslationServer.Translate(ConnectivitySentences.HostRestrictiveSearch),
		ConnectivitySentences.HostRestrictiveNextStep => TranslationServer.Translate(ConnectivitySentences.HostRestrictiveNextStep),
		ConnectivitySentences.HostLocalSeatTitle => TranslationServer.Translate(ConnectivitySentences.HostLocalSeatTitle),
		ConnectivitySentences.HostLocalSeatSummary => TranslationServer.Translate(ConnectivitySentences.HostLocalSeatSummary),
		ConnectivitySentences.HostLocalSeatSearch => TranslationServer.Translate(ConnectivitySentences.HostLocalSeatSearch),
		ConnectivitySentences.HostLocalSeatNextStep => TranslationServer.Translate(ConnectivitySentences.HostLocalSeatNextStep),
		ConnectivitySentences.HostLobbyServerTitle => TranslationServer.Translate(ConnectivitySentences.HostLobbyServerTitle),
		ConnectivitySentences.HostLobbyServerSummary => TranslationServer.Translate(ConnectivitySentences.HostLobbyServerSummary),
		ConnectivitySentences.HostLobbyServerSearch => TranslationServer.Translate(ConnectivitySentences.HostLobbyServerSearch),
		ConnectivitySentences.HostLobbyServerNextStep => TranslationServer.Translate(ConnectivitySentences.HostLobbyServerNextStep),
		ConnectivitySentences.JoinRestrictiveSummary => TranslationServer.Translate(ConnectivitySentences.JoinRestrictiveSummary),
		ConnectivitySentences.JoinRestrictiveNextStep => TranslationServer.Translate(ConnectivitySentences.JoinRestrictiveNextStep),
		ConnectivitySentences.JoinVpnSummary => TranslationServer.Translate(ConnectivitySentences.JoinVpnSummary),
		ConnectivitySentences.JoinVpnNextStep => TranslationServer.Translate(ConnectivitySentences.JoinVpnNextStep),
		ConnectivitySentences.JoinCarrierGradeNatTitle => TranslationServer.Translate(ConnectivitySentences.JoinCarrierGradeNatTitle),
		ConnectivitySentences.JoinCarrierGradeNatSummary => TranslationServer.Translate(ConnectivitySentences.JoinCarrierGradeNatSummary),
		ConnectivitySentences.JoinCarrierGradeNatNextStep => TranslationServer.Translate(ConnectivitySentences.JoinCarrierGradeNatNextStep),
		ConnectivitySentences.JoinDoubleNatSummary => TranslationServer.Translate(ConnectivitySentences.JoinDoubleNatSummary),
		ConnectivitySentences.JoinDoubleNatNextStep => TranslationServer.Translate(ConnectivitySentences.JoinDoubleNatNextStep),
		ConnectivitySentences.JoinFirewallTitle => TranslationServer.Translate(ConnectivitySentences.JoinFirewallTitle),
		ConnectivitySentences.JoinFirewallSummary => TranslationServer.Translate(ConnectivitySentences.JoinFirewallSummary),
		ConnectivitySentences.JoinFirewallNextStep => TranslationServer.Translate(ConnectivitySentences.JoinFirewallNextStep),
		ConnectivitySentences.JoinHostRouterTitle => TranslationServer.Translate(ConnectivitySentences.JoinHostRouterTitle),
		ConnectivitySentences.JoinHostRouterSummary => TranslationServer.Translate(ConnectivitySentences.JoinHostRouterSummary),
		ConnectivitySentences.JoinHostRouterSearch => TranslationServer.Translate(ConnectivitySentences.JoinHostRouterSearch),
		ConnectivitySentences.JoinHostRouterNextStep => TranslationServer.Translate(ConnectivitySentences.JoinHostRouterNextStep),
		ConnectivitySentences.JoinLobbyServerTitle => TranslationServer.Translate(ConnectivitySentences.JoinLobbyServerTitle),
		ConnectivitySentences.JoinLobbyServerSummary => TranslationServer.Translate(ConnectivitySentences.JoinLobbyServerSummary),
		ConnectivitySentences.JoinLobbyServerSearch => TranslationServer.Translate(ConnectivitySentences.JoinLobbyServerSearch),
		ConnectivitySentences.JoinLobbyServerNextStep => TranslationServer.Translate(ConnectivitySentences.JoinLobbyServerNextStep),
		ConnectivitySentences.HostRouterConfirmed => TranslationServer.Translate(ConnectivitySentences.HostRouterConfirmed),
		ConnectivitySentences.HostRouterInconclusive => TranslationServer.Translate(ConnectivitySentences.HostRouterInconclusive),
		ConnectivitySentences.HostRouterRuledOut => TranslationServer.Translate(ConnectivitySentences.HostRouterRuledOut),
		ConnectivitySentences.HostPerDestinationConfirmed => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationConfirmed),
		ConnectivitySentences.HostPerDestinationInconclusive => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationInconclusive),
		ConnectivitySentences.HostPerDestinationRuledOut => TranslationServer.Translate(ConnectivitySentences.HostPerDestinationRuledOut),
		ConnectivitySentences.HostFirewallDeclined => TranslationServer.Translate(ConnectivitySentences.HostFirewallDeclined),
		ConnectivitySentences.HostFirewallRuleBlocks => TranslationServer.Translate(ConnectivitySentences.HostFirewallRuleBlocks),
		ConnectivitySentences.HostFirewallRuleDisabled => TranslationServer.Translate(ConnectivitySentences.HostFirewallRuleDisabled),
		ConnectivitySentences.HostFirewallUnreadable => TranslationServer.Translate(ConnectivitySentences.HostFirewallUnreadable),
		ConnectivitySentences.HostFirewallRuledOut => TranslationServer.Translate(ConnectivitySentences.HostFirewallRuledOut),
		ConnectivitySentences.HostFirewallSecuritySoftwareUnknown => TranslationServer.Translate(ConnectivitySentences.HostFirewallSecuritySoftwareUnknown),
		ConnectivitySentences.HostFirewallNoWindows => TranslationServer.Translate(ConnectivitySentences.HostFirewallNoWindows),
		ConnectivitySentences.DoubleNatConfirmed => TranslationServer.Translate(ConnectivitySentences.DoubleNatConfirmed),
		ConnectivitySentences.DoubleNatRuledOut => TranslationServer.Translate(ConnectivitySentences.DoubleNatRuledOut),
		ConnectivitySentences.OutsideAddressUnmeasured => TranslationServer.Translate(ConnectivitySentences.OutsideAddressUnmeasured),
		ConnectivitySentences.DoubleNatProviderShared => TranslationServer.Translate(ConnectivitySentences.DoubleNatProviderShared),
		ConnectivitySentences.CarrierGradeNatConfirmed => TranslationServer.Translate(ConnectivitySentences.CarrierGradeNatConfirmed),
		ConnectivitySentences.CarrierGradeNatRuledOut => TranslationServer.Translate(ConnectivitySentences.CarrierGradeNatRuledOut),
		ConnectivitySentences.VpnConfirmed => TranslationServer.Translate(ConnectivitySentences.VpnConfirmed),
		ConnectivitySentences.VpnInconclusive => TranslationServer.Translate(ConnectivitySentences.VpnInconclusive),
		ConnectivitySentences.VpnRuledOut => TranslationServer.Translate(ConnectivitySentences.VpnRuledOut),
		ConnectivitySentences.RestrictiveConfirmed => TranslationServer.Translate(ConnectivitySentences.RestrictiveConfirmed),
		ConnectivitySentences.RestrictiveRuledOut => TranslationServer.Translate(ConnectivitySentences.RestrictiveRuledOut),
		ConnectivitySentences.RestrictiveUnprobed => TranslationServer.Translate(ConnectivitySentences.RestrictiveUnprobed),
		ConnectivitySentences.LocalSeatLoopbackConfirmed => TranslationServer.Translate(ConnectivitySentences.LocalSeatLoopbackConfirmed),
		ConnectivitySentences.LocalSeatPortHeld => TranslationServer.Translate(ConnectivitySentences.LocalSeatPortHeld),
		ConnectivitySentences.LocalSeatRuledOut => TranslationServer.Translate(ConnectivitySentences.LocalSeatRuledOut),
		ConnectivitySentences.LocalSeatUnmeasured => TranslationServer.Translate(ConnectivitySentences.LocalSeatUnmeasured),
		ConnectivitySentences.LobbyServerRegistryConfirmed => TranslationServer.Translate(ConnectivitySentences.LobbyServerRegistryConfirmed),
		ConnectivitySentences.LobbyServerOwnNetworkConfirmed => TranslationServer.Translate(ConnectivitySentences.LobbyServerOwnNetworkConfirmed),
		ConnectivitySentences.LobbyServerFamilyConfirmed => TranslationServer.Translate(ConnectivitySentences.LobbyServerFamilyConfirmed),
		ConnectivitySentences.LobbyServerRuledOut => TranslationServer.Translate(ConnectivitySentences.LobbyServerRuledOut),
		ConnectivitySentences.LobbyServerUnprobed => TranslationServer.Translate(ConnectivitySentences.LobbyServerUnprobed),
		ConnectivitySentences.JoinRestrictiveConfirmed => TranslationServer.Translate(ConnectivitySentences.JoinRestrictiveConfirmed),
		ConnectivitySentences.JoinRestrictiveHostSide => TranslationServer.Translate(ConnectivitySentences.JoinRestrictiveHostSide),
		ConnectivitySentences.JoinFirewallConfirmed => TranslationServer.Translate(ConnectivitySentences.JoinFirewallConfirmed),
		ConnectivitySentences.JoinFirewallInconclusive => TranslationServer.Translate(ConnectivitySentences.JoinFirewallInconclusive),
		ConnectivitySentences.JoinHostSideConfirmed => TranslationServer.Translate(ConnectivitySentences.JoinHostSideConfirmed),
		ConnectivitySentences.JoinHostSideInconclusive => TranslationServer.Translate(ConnectivitySentences.JoinHostSideInconclusive),
			_ => sentenceKey
		};
	}

	/// <summary>The sentence one issue's entry is drawn with, which is the sentence the shared content names.</summary>
	/// <param name="content">The entry to draw.</param>
	/// <returns>The translated title.</returns>
	public static string Title(ConnectivityIssueContent content) => Text(content.TitleKey);

	/// <summary>The explanation one issue's entry carries.</summary>
	/// <param name="content">The entry to draw.</param>
	/// <returns>The translated explanation.</returns>
	public static string Summary(ConnectivityIssueContent content) => Text(content.SummaryKey);

	/// <summary>The phrase a reader can search for their own hardware.</summary>
	/// <param name="content">The entry to draw.</param>
	/// <returns>The translated phrase.</returns>
	public static string SearchPhrase(ConnectivityIssueContent content) => Text(content.SearchPhraseKey);

	/// <summary>What to do next in this game, with the value the sentence states filled in.</summary>
	/// <param name="content">The entry to draw.</param>
	/// <param name="address">The address players on this network join at, or the sentence that says where it is.</param>
	/// <returns>The translated next step.</returns>
	public static string NextStep(ConnectivityIssueContent content, string address)
	{
		string sentence = Text(content.NextStepKey);
		return content.NextStepStatesAddress ? string.Format(sentence, address) : sentence;
	}

	/// <summary>The title of a reference page.</summary>
	/// <param name="link">The page.</param>
	/// <returns>The translated title.</returns>
	public static string LinkTitle(ConnectivityLink link) => Text(link.TitleKey);

	/// <summary>What one check found, as the one sentence it is shown with.</summary>
	/// <param name="diagnosis">The answer to draw.</param>
	/// <returns>The translated sentence, with the port or address it states filled in.</returns>
	public static string DiagnosisSentence(ConnectivityDiagnosis diagnosis)
	{
		string sentence = Text(diagnosis.DetailKey);
		return diagnosis.DetailValue == null ? sentence : string.Format(sentence, diagnosis.DetailValue);
	}

	/// <summary>What the game measured, as the one line the dialog opens with.</summary>
	/// <param name="cause">The sentence and the value it states.</param>
	/// <returns>The translated sentence.</returns>
	public static string LikelyCause(ConnectivityLikelyCause cause)
	{
		string sentence = Text(cause.Key);
		return cause.Value == null ? sentence : string.Format(sentence, cause.Value);
	}

	/// <summary>The word one verdict is shown with.</summary>
	/// <param name="status">What the check found.</param>
	/// <returns>The translated word.</returns>
	public static string Verdict(ConnectivityDiagnosisStatus status)
	{
		return status switch
		{
			ConnectivityDiagnosisStatus.Confirmed => Text(ConnectivitySentences.VerdictConfirmed),
			ConnectivityDiagnosisStatus.Inconclusive => Text(ConnectivitySentences.VerdictInconclusive),
			_ => Text(ConnectivitySentences.VerdictRuledOut)
		};
	}
}
