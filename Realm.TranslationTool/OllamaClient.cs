using System.Diagnostics;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Realm.TranslationTool
{
	public partial class OllamaClient
	{
		private readonly string _cacheDir;
		private readonly string _url;
		private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

		[GeneratedRegex(@"_+")]
		private static partial Regex MultipleUnderscoresRegex();

		[GeneratedRegex(@"\s+", RegexOptions.IgnoreCase)]
		private static partial Regex SpacesRegex();
		
		private Process _ollamaProc;
		public OllamaClient()
		{
			_cacheDir = "C:\\temp\\Realm.TranslationTool.cache";
			_url = "http://127.0.0.1:11434";			
		}

		private async Task<bool> IsOllamaRunning()
		{
			try
			{
				using var response = await _http.GetAsync(_url + "/api/tags");
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		public async Task EnsureOllama()
		{
			if (await IsOllamaRunning()) return;

			var startInfo = new ProcessStartInfo
			{
				FileName = "ollama",
				Arguments = "serve",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			_ollamaProc = Process.Start(startInfo);
			for (int i = 0; i < 30; i++)
			{
				if (await IsOllamaRunning()) return;
				await Task.Delay(1000);
			}
		}

		public async Task<string> GenerateText(string prompt, bool think = false, string modelOverride = null, string? task = null, string? step = null, string? category = null, string? promptName = null)
		{
			return await OllamaPromptAsync(prompt, think, modelOverride, task: task, step: step, category: category, promptName: promptName);
		}

		public async Task<string> OllamaPromptAsync(string prompt, bool think = false, string model = null, string[] images = null, string imagePathForHash = null, string? task = null, string? step = null, string? category = null, string? promptName = null)
		{
			model = "gemma4:12b-it-q4_K_M";
			string hashInput = model + prompt;
			if (images != null && imagePathForHash != null)
			{
				byte[] imgBytes = await File.ReadAllBytesAsync(imagePathForHash);
				hashInput += ComputeHash_Internal(imgBytes);
			}

			string cacheKey = ComputeHash(hashInput);
			string cacheFile = CacheContext.GetCacheFilePath(_cacheDir, $"{cacheKey}.bin", task, step, category, promptName);

			if (File.Exists(cacheFile))
			{
				var cacheResult = await File.ReadAllTextAsync(cacheFile);
				if (!string.IsNullOrWhiteSpace(cacheResult))
				{
					return cacheResult;
				}
			}

			var requestBody = new
			{
				model = model,
				think = think,
				prompt = prompt,
				stream = false,
				images = images,
				options = new { temperature = 1.0, top_p = 0.95, top_k = 64 }
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			using var response = await _http.PostAsync($"{_url}/api/generate", content);
			response.EnsureSuccessStatusCode();

			var jsonResponse = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			string result = jsonResponse.RootElement.GetProperty("response").GetString();

			if (!string.IsNullOrWhiteSpace(result))
			{
				string? dir = Path.GetDirectoryName(cacheFile);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				await File.WriteAllTextAsync(cacheFile, result);
			}

			return result;
		}

		private string ComputeHash(string input)
		{
			var normalizedInput = SpacesRegex().Replace(input.ToLower(), " ").Trim();
			return ComputeHash_Internal(normalizedInput);
		}

		public static string ComputeHash_Internal(byte[] bytes) => ComputeHash_Internal((ReadOnlySpan<byte>)bytes);

		public static string ComputeHash_Internal(ReadOnlySpan<byte> bytes)
		{
			byte[] hashBytes = XxHash3.Hash(bytes);
			return Convert.ToHexStringLower(hashBytes);
		}

		public static string ComputeHash_Internal(string input)
		{
			return ComputeHash_Internal(Encoding.UTF8.GetBytes(input));
		}
	}
}

