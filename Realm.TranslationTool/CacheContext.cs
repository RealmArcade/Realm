using System.Text;

namespace Realm.TranslationTool
{
	public sealed class CacheContext
	{
		private static readonly AsyncLocal<CacheContext?> _current = new();

		public static CacheContext? Current => _current.Value;

		public string? Task { get; init; }
		public string? Step { get; init; }
		public string? AssetCategory { get; init; }
		public string? PromptName { get; init; }

		public static IDisposable Push(string? task = null, string? step = null, string? assetCategory = null, string? promptName = null)
		{
			var prev = _current.Value;
			var next = new CacheContext
			{
				Task = task ?? prev?.Task,
				Step = step ?? prev?.Step,
				AssetCategory = assetCategory ?? prev?.AssetCategory,
				PromptName = GetNextPromptName(task, step, assetCategory, promptName, prev)
			};
			_current.Value = next;
			return new DisposableAction(() => _current.Value = prev);
		}

		private static string? GetNextPromptName(string? task, string? step, string? assetCategory, string? promptName, CacheContext? prev)
		{
			if (promptName != null) return promptName;
			if (task == null && step == null && assetCategory == null) return prev?.PromptName;
			return null;
		}

		public static IDisposable PushTask(string task) => Push(task: task);
		public static IDisposable PushStep(string step) => Push(step: step);
		public static IDisposable PushCategory(string category) => Push(assetCategory: category);

		public static IDisposable ForPrompt(string promptName)
		{
			var prev = _current.Value;
			var next = new CacheContext
			{
				Task = prev?.Task,
				Step = prev?.Step,
				AssetCategory = prev?.AssetCategory,
				PromptName = promptName
			};
			_current.Value = next;
			return new DisposableAction(() => _current.Value = prev);
		}

		public static string SanitizeFolderName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return "";
			var invalidChars = Path.GetInvalidFileNameChars();
			var sb = new StringBuilder(name.Length);
			foreach (char c in name.Trim())
			{
				if (Array.IndexOf(invalidChars, c) >= 0 || c == '/' || c == '\\')
					sb.Append('_');
				else
					sb.Append(c);
			}
			return sb.ToString();
		}

		public static string ResolveCacheDirectory(string rootCacheDir, string? task = null, string? step = null, string? assetCategory = null, string? promptName = null)
		{
			var current = Current;
			string? effectivePrompt = GetEffective(promptName, current?.PromptName);

			if (!string.IsNullOrWhiteSpace(effectivePrompt))
			{
				return Path.Combine(rootCacheDir, SanitizeFolderName(effectivePrompt));
			}

			var parts = new List<string> { rootCacheDir };
			AddPartIfValid(parts, GetEffective(task, current?.Task));
			AddPartIfValid(parts, GetEffective(step, current?.Step));
			AddPartIfValid(parts, GetEffective(assetCategory, current?.AssetCategory));

			return Path.Combine(parts.ToArray());
		}

		private static string? GetEffective(string? primary, string? fallback)
		{
			if (!string.IsNullOrWhiteSpace(primary)) return primary;
			return fallback;
		}

		private static void AddPartIfValid(List<string> parts, string? value)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				parts.Add(SanitizeFolderName(value));
			}
		}

		public static string GetCacheFilePath(string rootCacheDir, string cacheKeyWithExt, string? task = null, string? step = null, string? assetCategory = null, string? promptName = null)
		{
			string targetDir = ResolveCacheDirectory(rootCacheDir, task, step, assetCategory, promptName);
			string targetPath = Path.Combine(targetDir, cacheKeyWithExt);
			string legacyPath = Path.Combine(rootCacheDir, cacheKeyWithExt);

			if (File.Exists(targetPath))
			{
				return targetPath;
			}

			if (File.Exists(legacyPath))
			{
				try
				{
					if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
					File.Copy(legacyPath, targetPath, true);
					return targetPath;
				}
				catch
				{
					return legacyPath;
				}
			}

			return targetPath;
		}

		private sealed class DisposableAction : IDisposable
		{
			private readonly Action _action;
			public DisposableAction(Action action) => _action = action;
			public void Dispose() => _action();
		}
	}
}

