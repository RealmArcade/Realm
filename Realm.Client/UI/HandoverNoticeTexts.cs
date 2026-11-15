using Godot;
using Realm.Shared.Network;

namespace Realm.Client.UI;

/// <summary>
///     The sentences a lobby's host handover can state, written out as the keys their screens translate. Every key is named
///     at a real call site so the localization generator finds it, and a sentence this build does not know falls back to the
///     key itself, which is the sentence the authority stated.
/// </summary>
public static class HandoverNoticeTexts
{
	/// <summary>Translates one sentence of a host handover for the language the screen is running in.</summary>
	/// <param name="noticeKey">The sentence the lobby stated, which is the English text and its key.</param>
	/// <returns>The sentence a player reads.</returns>
	public static string Translate(string noticeKey)
	{
		if (string.IsNullOrEmpty(noticeKey)) return string.Empty;

		return noticeKey switch
		{
			LobbyHandoverNotice.OfferedToHost => TranslationServer.Translate(LobbyHandoverNotice.OfferedToHost),
			LobbyHandoverNotice.OfferedByHost => TranslationServer.Translate(LobbyHandoverNotice.OfferedByHost),
			LobbyHandoverNotice.HostRefused => TranslationServer.Translate(LobbyHandoverNotice.HostRefused),
			LobbyHandoverNotice.CandidateRefused => TranslationServer.Translate(LobbyHandoverNotice.CandidateRefused),
			LobbyHandoverNotice.Withdrawn => TranslationServer.Translate(LobbyHandoverNotice.Withdrawn),
			LobbyHandoverNotice.Expired => TranslationServer.Translate(LobbyHandoverNotice.Expired),
			LobbyHandoverNotice.RehearsalStarted => TranslationServer.Translate(LobbyHandoverNotice.RehearsalStarted),
			LobbyHandoverNotice.RehearsalCancelled => TranslationServer.Translate(LobbyHandoverNotice.RehearsalCancelled),
			LobbyHandoverNotice.RehearsalTimedOut => TranslationServer.Translate(LobbyHandoverNotice.RehearsalTimedOut),
			LobbyHandoverNotice.VerdictRefused => TranslationServer.Translate(LobbyHandoverNotice.VerdictRefused),
			LobbyHandoverNotice.AwaitingConfirmation => TranslationServer.Translate(LobbyHandoverNotice.AwaitingConfirmation),
			LobbyHandoverNotice.Confirmed => TranslationServer.Translate(LobbyHandoverNotice.Confirmed),
			LobbyHandoverNotice.LobbyChanged => TranslationServer.Translate(LobbyHandoverNotice.LobbyChanged),
			LobbyHandoverNotice.CountdownRunning => TranslationServer.Translate(LobbyHandoverNotice.CountdownRunning),
			LobbyHandoverNotice.OneAtATime => TranslationServer.Translate(LobbyHandoverNotice.OneAtATime),
			LobbyHandoverNotice.UnknownSeat => TranslationServer.Translate(LobbyHandoverNotice.UnknownSeat),
			LobbyHandoverNotice.ComputerCannotHost => TranslationServer.Translate(LobbyHandoverNotice.ComputerCannotHost),
			LobbyHandoverNotice.NotTheCandidate => TranslationServer.Translate(LobbyHandoverNotice.NotTheCandidate),
			LobbyHandoverNotice.RolledBack => TranslationServer.Translate(LobbyHandoverNotice.RolledBack),
			LobbyHandoverNotice.AlreadyHost => TranslationServer.Translate(LobbyHandoverNotice.AlreadyHost),
			LobbyHandoverNotice.RehearsalIncomplete => TranslationServer.Translate(LobbyHandoverNotice.RehearsalIncomplete),
			LobbyHandoverNotice.Settling => TranslationServer.Translate(LobbyHandoverNotice.Settling),
			LobbyHandoverNotice.NoProbeRelay => TranslationServer.Translate(LobbyHandoverNotice.NoProbeRelay),
			_ => LocalizationManager.TranslateKey(noticeKey)
		};
	}
}
