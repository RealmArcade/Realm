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
using HttpClient = System.Net.Http.HttpClient;
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

	private string GetPingColorCode(string latency)
	{
		int val = ParsePingValue(latency);
		if (val < 0) return "#a0a0a0";
		if (val < 100) return "#4ce66a";
		if (val < 200) return "#ffd700";
		return "#ff5555";
	}

	private string GetJitterColorCode(string jitter)
	{
		int val = ParseJitterValue(jitter);
		if (val < 0) return "#a0a0a0";
		if (val < 20) return "#4ce66a";
		if (val < 50) return "#ffd700";
		return "#ff5555";
	}

	private string GetLossColorCode(string loss)
	{
		float val = ParseLossValue(loss);
		if (val < 0f) return "#a0a0a0";
		if (val < 1.0f) return "#4ce66a";
		if (val < 5.0f) return "#ffd700";
		return "#ff5555";
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
