namespace Realm.Godot.Tests;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

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
				ThumbnailPath = "res://Assets/UI/moonlit_castle.png",
				Description = "Defend the last fortress against waves of ancient evil! Includes the sintepres sucitoure unit. Features deep mountain passes, branching paths, unique hero units, and custom tower designs. Can you withstand the boss waves?",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_castle.png",
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/snowy_forest_path.png"
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
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/victory_flag.png",
					"res://Assets/UI/battle_shield.png",
					"res://Assets/UI/battle_axe.png"
				}
			},
			new MapData
			{
				MapId = "moonlit_valley",
				Title = "MOONLIT VALLEY",
				Version = "1.0.0",
				Creator = "Elven Scout",
				ThumbnailPath = "res://Assets/UI/moonlit_forest.png",
				Description = "An elven sanctuary under attack by the undead horde. Harvest timber, fortify ancient trees, and command the forest spirits to push back the darkness in this beautiful, defense-focused melee map.",
				Screenshots = new string[]
				{
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_castle.png"
				},
				Features = new string[]
				{
					"Elven Alliance",
					"Timber Economy",
					"Hero Units",
					"Dynamic Weather"
				},
				RatingStars = 4.3f,
				Votes5Star = "8,409 Votes",
				Votes3Star = "720 Votes",
				Votes1Star = "950 Votes",
				AvgRating = "4.3 / 5.0",
				AvgPlaytime = "35 min",
				PlayerCount = "850 Active",
				CompletionRate = "81%",
				FileSize = "950 MB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "4 Players",
				Genre = "Melee / Defense",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/battle_shield.png"
				}
			},
			new MapData
			{
				MapId = "frostbite_pass",
				Title = "FROSTBITE PASS",
				Version = "1.0.0",
				Creator = "Ice Mage",
				ThumbnailPath = "res://Assets/UI/snowy_forest_path.png",
				Description = "Brace yourself for freezing winds and relentless siege attacks. Frostbite Pass is a highly tactical map where snow slows unit movement and avalanches can shift the battlefield layout.",
				Screenshots = new string[]
				{
					"res://Assets/UI/snowy_forest_path.png",
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_castle.png"
				},
				Features = new string[]
				{
					"Snow Hazards",
					"Avalanches",
					"Custom Towers",
					"Achievements"
				},
				RatingStars = 3.9f,
				Votes5Star = "4,213 Votes",
				Votes3Star = "1,980 Votes",
				Votes1Star = "1,112 Votes",
				AvgRating = "3.9 / 5.0",
				AvgPlaytime = "50 min",
				PlayerCount = "420 Active",
				CompletionRate = "52%",
				FileSize = "1.2 GB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "6 Players",
				Genre = "Coop / Survival",
				Awards = new string[]
				{
					"res://Assets/UI/battle_axe.png",
					"res://Assets/UI/victory_flag.png"
				}
			},
			new MapData
			{
				MapId = "forest_trails",
				Title = "FOREST TRAILS",
				Version = "1.0.0",
				Creator = "Pathfinder",
				ThumbnailPath = "res://Assets/UI/forest_path.png",
				Description = "A simple yet beautiful beginner-friendly map featuring lush forests, wide lanes, and rich resource spots. Perfect for testing new strategies or practicing build orders.",
				Screenshots = new string[]
				{
					"res://Assets/UI/forest_path.png",
					"res://Assets/UI/moonlit_forest.png",
					"res://Assets/UI/snowy_forest_path.png"
				},
				Features = new string[]
				{
					"Rich Resources",
					"Beginner Friendly",
					"Simple Paths"
				},
				RatingStars = 4.5f,
				Votes5Star = "12,987 Votes",
				Votes3Star = "1,102 Votes",
				Votes1Star = "640 Votes",
				AvgRating = "4.5 / 5.0",
				AvgPlaytime = "25 min",
				PlayerCount = "2.1k Active",
				CompletionRate = "92%",
				FileSize = "620 MB",
				EngineVersion = "Godot Realm Engine v1.0",
				MaxPlayers = "2 Players",
				Genre = "Tutorial / Skirmish",
				Awards = new string[]
				{
					"res://Assets/UI/gold_coin.png",
					"res://Assets/UI/victory_flag.png"
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
        ISceneRunner runner = ISceneRunner.Load("res://Main.tscn");
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

            global::Godot.Image image = runner.Scene().GetViewport().GetTexture().GetImage();
            string fileName = $"UX_Screen_{screen}.png";
            string filePath = Path.Combine(tempDir, fileName);
            image.SavePng(filePath);
        }
    }
}
