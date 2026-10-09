using Godot;
using System.Collections.Generic;
using System.Text.Json;

public static class HostStabilityTracker
{
    private const string HistoryPath = "user://stability_history.json";

    public static List<GameStabilitySummary> LoadHistory()
    {
        if (!FileAccess.FileExists(HistoryPath))
        {
            return new List<GameStabilitySummary>();
        }

        using var file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return new List<GameStabilitySummary>();
        }

        string json = file.GetAsText();
        try
        {
            var history = JsonSerializer.Deserialize<List<GameStabilitySummary>>(json);
            return history ?? new List<GameStabilitySummary>();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[HostStabilityTracker] Failed to load history: {ex.Message}");
            return new List<GameStabilitySummary>();
        }
    }

    public static void SaveHistory(List<GameStabilitySummary> history)
    {
        try
        {
            string json = JsonSerializer.Serialize(history);
            using var file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Write);
            if (file != null)
            {
                file.StoreString(json);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[HostStabilityTracker] Failed to save history: {ex.Message}");
        }
    }

    public static void AddGameSummary(GameStabilitySummary summary)
    {
        var history = LoadHistory();
        history.Add(summary);
        if (history.Count > 10)
        {
            history.RemoveAt(0);
        }
        SaveHistory(history);
    }

    public static string GetOverallStability()
    {
        var history = LoadHistory();
        if (history.Count == 0)
        {
            return "Excellent";
        }

        int poorCount = 0;
        int averageCount = 0;

        foreach (var game in history)
        {
            string rating = GradeGame(game);
            if (rating == "Poor")
            {
                poorCount++;
            }
            else if (rating == "Average")
            {
                averageCount++;
            }
        }

        if (poorCount >= 2)
        {
            return "Poor";
        }
        if (poorCount == 1 || averageCount >= 3)
        {
            return "Average";
        }
        return "Excellent";
    }

    private static string GradeGame(GameStabilitySummary game)
    {
        bool hasApi = HasApi(game);

        if (IsTickPoor(game) || IsApiPoor(game, hasApi))
        {
            return "Poor";
        }

        if (IsTickExcellent(game) && IsApiExcellent(game, hasApi))
        {
            return "Excellent";
        }

        return "Average";
    }

    private static bool HasApi(GameStabilitySummary game)
    {
        return game.AvgApiMs > 0 || game.MaxApiMs > 0;
    }

    private static bool IsTickPoor(GameStabilitySummary game)
    {
        return game.AvgTickMs >= 25.0f || game.MedianTickMs >= 25.0f || game.MaxTickMs >= 60.0f;
    }

    private static bool IsApiPoor(GameStabilitySummary game, bool hasApi)
    {
        return hasApi && (game.AvgApiMs >= 25.0f || game.MedianApiMs >= 25.0f || game.MaxApiMs >= 75.0f);
    }

    private static bool IsTickExcellent(GameStabilitySummary game)
    {
        return game.AvgTickMs < 12.0f && game.MedianTickMs < 12.0f && game.MaxTickMs < 33.33f;
    }

    private static bool IsApiExcellent(GameStabilitySummary game, bool hasApi)
    {
        return !hasApi || (game.AvgApiMs < 10.0f && game.MedianApiMs < 10.0f && game.MaxApiMs < 30.0f);
    }

    public static float CalculateAverage(List<float> list)
    {
        if (list == null || list.Count == 0)
        {
            return 0f;
        }
        float sum = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            sum += list[i];
        }
        return sum / list.Count;
    }

    public static float CalculateMedian(List<float> list)
    {
        if (list == null || list.Count == 0)
        {
            return 0f;
        }
        var sorted = new List<float>(list);
        sorted.Sort();
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 != 0)
        {
            return sorted[mid];
        }
        return (sorted[mid - 1] + sorted[mid]) / 2f;
    }

    public static float CalculateMax(List<float> list)
    {
        if (list == null || list.Count == 0)
        {
            return 0f;
        }
        float max = float.MinValue;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] > max)
            {
                max = list[i];
            }
        }
        return max;
    }
}
