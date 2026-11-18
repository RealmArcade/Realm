using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.UI.InGame;
using Realm.Client.Services;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

public partial class LobbyRoom
{
	private int ParsePingValue(string latency)
	{
		if (string.IsNullOrEmpty(latency) || latency == "--" || latency == "measuring...")
		{
			return -1;
		}

		string clean = latency.Replace("ms", "").Trim();
		if (int.TryParse(clean, out int val))
		{
			return val;
		}

		return -1;
	}

	private int ParseJitterValue(string jitter)
	{
		if (string.IsNullOrEmpty(jitter) || jitter == "--" || jitter == "")
		{
			return -1;
		}

		string clean = jitter.Replace("ms", "").Trim();
		if (int.TryParse(clean, out int val))
		{
			return val;
		}

		return -1;
	}

	private float ParseLossValue(string loss)
	{
		if (string.IsNullOrEmpty(loss) || loss == "--" || loss == "")
		{
			return -1f;
		}

		int pctIdx = loss.IndexOf('%');
		if (pctIdx >= 0)
		{
			string clean = loss.Substring(0, pctIdx).Trim();
			if (float.TryParse(clean, out float val))
			{
				return val;
			}
		}

		return -1f;
	}

	/// <summary>
	///     Reads the worst of a seat's three link measurements as one colour, so a single glyph can say what the numbers on
	///     the row used to spell out. A seat whose measurements have not arrived yet reads neutral rather than healthy.
	/// </summary>
	/// <param name="player">The seat whose link is read.</param>
	/// <returns>The colour the seat's link glyph is drawn in.</returns>
	private Color GetLinkQualityColor(PlayerInfo player)
	{
		bool measured = false;
		bool degraded = false;
		bool failing = false;

		int ping = ParsePingValue(player.Latency);
		if (ping >= 0)
		{
			measured = true;
			if (ping >= 200) failing = true;
			else if (ping >= 100) degraded = true;
		}

		int jitter = ParseJitterValue(player.Jitter);
		if (jitter >= 0)
		{
			measured = true;
			if (jitter >= 50) failing = true;
			else if (jitter >= 20) degraded = true;
		}

		float loss = ParseLossValue(player.PacketLoss);
		if (loss >= 0f)
		{
			measured = true;
			if (loss >= 5.0f) failing = true;
			else if (loss >= 1.0f) degraded = true;
		}

		if (failing) return new Color(0.95f, 0.34f, 0.34f);
		if (degraded) return new Color(0.95f, 0.8f, 0.25f);
		return measured ? new Color(0.35f, 0.9f, 0.45f) : new Color(0.62f, 0.64f, 0.7f);
	}

	private bool IsAnyMetricRed()
	{
		foreach (var player in Lobby.PlayerList)
		{
			if (player.PeerId < 0)
			{
				continue;
			}

			int ping = ParsePingValue(player.Latency);
			if (ping >= 200)
			{
				return true;
			}

			int jitter = ParseJitterValue(player.Jitter);
			if (jitter >= 50)
			{
				return true;
			}

			float loss = ParseLossValue(player.PacketLoss);
			if (loss >= 5.0f)
			{
				return true;
			}
		}

		return false;
	}

	private void UpdateHostStabilityLabel(string stability)
	{
		if (GameContext.Lobby == null || Lobby.IsHost || _hostStabilityLabel == null)
		{
			if (_hostStabilityLabel != null)
			{
				_hostStabilityLabel.Visible = false;
			}

			return;
		}

		_hostStabilityLabel.Visible = true;
		if (stability == "Excellent")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Good");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.4f));
		}
		else if (stability == "Average")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Average");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.1f));
		}
		else if (stability == "Poor")
		{
			_hostStabilityLabel.Text = Tr("Host Stability: Poor");
			_hostStabilityLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.25f, 0.25f));
		}
		else
		{
			_hostStabilityLabel.Visible = false;
		}
	}
}
