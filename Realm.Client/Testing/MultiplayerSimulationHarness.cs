using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Maps;

namespace Realm.Client.Testing;

[GlobalClass]
public partial class MultiplayerSimulationHarness : Node
{
	private const int PlayableSlotCount = 12;
	private const string HostPlayerName = "HarnessHost";
	private const int HarnessMatchSeed = 12345;

	public string Role
	{
		set
		{
			_role = value;
			_roleLocked = true;
		}
	}

	private string _role = "host";
	private bool _roleLocked;
	private string _hostAddress = "127.0.0.1";
	private int _port = 8999;
	private string _mapFolder = string.Empty;
	private string _outputDirectory = string.Empty;
	private string _listeningFile = string.Empty;
	private string _readyFile = string.Empty;
	private string _doneFile = string.Empty;
	private string _sequenceFile = string.Empty;
	private string _clientBuildId = string.Empty;
	private int _drainMilliseconds = 0;
	private int _finalSequence = -1;
	private float _durationSeconds = 15f;
	private string _networkProfile = "Latency";
	private int _peerCount = 2;
	private int _peerIndex = 1;
	private int _botCount;
	private int _splitUnitsBeforeCommand = -1;
	private float _dropAtSeconds;
	private float _pauseAtSeconds;
	private float _discardAtSeconds;
	private bool _startAuthority;
	private bool _editorFirst;
	private bool _realFlow;
	private bool _probeOwnsProcess;
	private bool _probesEnabled;
	private string _waitForPeerReadyFile = string.Empty;
	private int _hardeningFailureCount;
	private int _recordedClientPeerId = -1;
	private ClientServerSession? _session;

	public override void _Ready()
	{
		_ = RunAsync();
	}

	private async Task RunAsync()
	{
		int exitCode = 1;
		try
		{
			ParseArguments();
			ShadowClientVerifier.IsEnabled = true;
			ShadowClientVerifier.Reset();
			exitCode = _role switch
			{
				"host" => await RunHostAsync(),
				"testmap" => await RunTestMapAsync(),
				"probe" => await RunProbeAsync(),
				"editorreturn" => await RunEditorReturnLauncherAsync(),
				"editorreturnwork" => await RunEditorReturnWorkAsync(),
				_ => await RunClientAsync()
			};
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MP_HARNESS] Unhandled failure: {exception}");
		}
		finally
		{
			TryWriteDoneFile();
			if (!_probeOwnsProcess) GetTree().Quit(exitCode);
		}
	}

	private void ParseArguments()
	{
		foreach (string argument in OS.GetCmdlineUserArgs())
		{
			int separatorIndex = argument.IndexOf('=');
			if (separatorIndex <= 0) continue;

			string key = argument.Substring(0, separatorIndex);
			string value = argument.Substring(separatorIndex + 1);
			switch (key)
			{
				case "--role":
					if (!_roleLocked) _role = value;
					break;
				case "--host":
					_hostAddress = value;
					break;
				case "--port":
					_port = int.Parse(value);
					break;
				case "--map":
					_mapFolder = value;
					break;
				case "--output":
					_outputDirectory = value;
					break;
				case "--listening-file":
					_listeningFile = value;
					break;
				case "--ready-file":
					_readyFile = value;
					break;
				case "--done-file":
					_doneFile = value;
					break;
				case "--sequence-file":
					_sequenceFile = value;
					break;
				case "--client-build":
					_clientBuildId = value;
					break;
				case "--drain-ms":
					_drainMilliseconds = int.Parse(value);
					break;
				case "--duration":
					_durationSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--profile":
					_networkProfile = value;
					break;
				case "--peers":
					_peerCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--peer-index":
					_peerIndex = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--bots":
					_botCount = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--wait-for-peer-ready":
					_waitForPeerReadyFile = value;
					break;
				case "--drop-at":
					_dropAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--pause-at":
					_pauseAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--discard-at":
					_discardAtSeconds = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
					break;
				case "--start-authority":
					_startAuthority = value == "1";
					break;
				case "--editor-first":
					_editorFirst = value == "1";
					break;
				case "--real-flow":
					_realFlow = value == "1";
					break;
				case "--probes":
					_probesEnabled = value == "1";
					break;
			}
		}

		if (string.IsNullOrEmpty(_outputDirectory)) throw new InvalidOperationException("Missing --output argument.");
	}

	public override void _PhysicsProcess(double delta)
	{
		_session?.Pump();
		RecordClientPeerId(_session?.LocalPeerId ?? 0);
	}

	private void RecordClientPeerId(int peerId)
	{
		if (_role != "client" || peerId <= 0 || peerId == _recordedClientPeerId) return;

		_recordedClientPeerId = peerId;
		try
		{
			File.AppendAllText(Path.Combine(_outputDirectory, "client_peer_id.txt"), peerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
		}
		catch (IOException)
		{
		}
	}

	private void TryWriteReadyFile()
	{
		if (string.IsNullOrEmpty(_readyFile)) return;
		File.WriteAllText(_readyFile, DateTime.UtcNow.ToString("O"));
	}


	private static void SetPrivateProperty(object target, string propertyName, object value)
	{
		PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (property == null) throw new InvalidOperationException($"Property {propertyName} was not found on {target.GetType().Name}.");
		property.SetValue(target, value);
	}
}
