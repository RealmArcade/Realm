using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;
using Realm.Client.Utils;
using Environment = System.Environment;
using Realm.Client.Services;
namespace Realm.Client.Tests;

/// <summary>capturing the ux screens into a temp directory.</summary>
public partial class UxSceneTests
	{
	[TestCase]
	public async Task CaptureUxScreenshots()
	{
		ISceneRunner runner = ISceneRunner.Load("res://GameHost.tscn");
		await runner.AwaitMillis(1000);

		string tempDir = Path.Combine(Path.GetTempPath(), "Realm_UX_Screenshots");
		Directory.CreateDirectory(tempDir);

		GameScreen[] screens = new GameScreen[]
		{
			GameScreen.MainMenu,
			GameScreen.LobbyBrowser,
			GameScreen.LobbyCreate,
			GameScreen.LobbyRoom,
			GameScreen.Settings,
			GameScreen.MapDiscovery,
			GameScreen.MapDetails,
			GameScreen.MapEditorHUD,
			GameScreen.ReplayList,
			GameScreen.GameOver
		};

		foreach (GameScreen screen in screens)
		{
			if (screen == GameScreen.MapDetails)
			{
				MapData[] dummy = GetDummyMaps();
				MapData mapData = (dummy != null && dummy.Length > 0) ? dummy[0] : new MapData { Title = "Test Map" };
				GameContext.UserInterface?.TransitionToMapDetails(mapData);
			}
			else
			{
				GameContext.UserInterface?.TransitionTo(screen);
			}

			await runner.AwaitMillis(1000);

			Image image = SceneRunnerCapture.CaptureViewportImage(runner);
			string fileName = $"UX_Screen_{screen}.png";
			string filePath = Path.Combine(tempDir, fileName);
			image.SavePng(filePath);
		}
	}
}
