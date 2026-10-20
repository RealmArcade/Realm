using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;

namespace Realm.Client.Tests;

[TestSuite]
[RequireGodotRuntime]
public class UxSceneTests
{
	public static MapData[] GetDummyMaps()
	{
		var maps = new MapData[]
		{
			new MapData
			{
				MapId = "castle_td",
				Title = "CASTLE TD",
				Version = "1.0.0",
				Creator = "Realm Builder",
				ThumbnailPath = "res://Assets/UI/moonlit_castle.webp",
				Description = "Defend the last fortress against waves of ancient evil! Includes the sintepres sucitoure unit. Features deep mountain passes, branching paths, unique hero units, and custom tower designs. Can you withstand the boss waves?",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_castle.webp",
					"res://Assets/UI/moonlit_forest.webp",
					"res://Assets/UI/forest_path.webp",
					"res://Assets/UI/snowy_forest_path.webp"
				},
				Features = new string[]
				{
					"Custom Units",
					"Boss Waves",
					"Achievements",
					"Hardcore Mode"
				},
				RatingStars = 4.7f,
				Votes5Star = "14,231 Votes",
				Votes3Star = "1,533 Votes",
				Votes1Star = "3,273 Votes",
				AvgRating = "4.7 / 5.0",
				AvgPlaytime = "42 min",
				PlayerCount = "1.2k Active (Last 24h)",
				CompletionRate = "74%",
				FileSize = "1.8 GB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "8 Players",
				Genre = "Campaign / TD",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.webp",
					"res://Assets/UI/victory_flag.webp",
					"res://Assets/UI/battle_shield.webp",
					"res://Assets/UI/battle_axe.webp"
				}
			}
		};

		foreach (var m in maps)
		{
			m.AvailableVersions = new List<MapData> { m };
		}

		return maps;
	}

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
				UIManager.Instance.TransitionToMapDetails(mapData);
			}
			else
			{
				UIManager.Instance.TransitionTo(screen);
			}

			await runner.AwaitMillis(1000);

			Image image = runner.Scene().GetViewport().GetTexture().GetImage();
			string fileName = $"UX_Screen_{screen}.png";
			string filePath = Path.Combine(tempDir, fileName);
			image.SavePng(filePath);
		}
	}
}