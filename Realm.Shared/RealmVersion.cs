using System.Diagnostics;
using System.Reflection;

namespace Realm.Shared;

public static class RealmVersion
{
	public const string GameBuildNumber = "v0.0.4";
	public static readonly string GameBinaryVersion = GetGameBinaryVersion();

	public static string GetGameBinaryVersion(Assembly? assembly = null)
	{
		const string defaultVersionString = "v0.0.3_Pre-Alpha";
		try
		{
			assembly ??= typeof(RealmVersion).Assembly;

			string? version = GetInformationalVersion(assembly)
				?? GetAssemblyVersion(assembly)
				?? GetFileVersion(assembly)
				?? GetProductVersion(assembly);

			if (version != null)
			{
				return version;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to read version from assembly: {ex.Message}");
		}

		return defaultVersionString;
	}

	private static string? GetInformationalVersion(Assembly assembly)
	{
		var infoVerAttr = (AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
			assembly, typeof(AssemblyInformationalVersionAttribute));

		if (infoVerAttr == null || string.IsNullOrWhiteSpace(infoVerAttr.InformationalVersion))
		{
			return null;
		}

		string infoVer = infoVerAttr.InformationalVersion;
		int plusIdx = infoVer.IndexOf('+');
		if (plusIdx > 0)
		{
			infoVer = infoVer.Substring(0, plusIdx);
		}

		if (string.IsNullOrWhiteSpace(infoVer))
		{
			return null;
		}

		return infoVer.Trim();
	}

	private static string? GetAssemblyVersion(Assembly assembly)
	{
		var ver = assembly.GetName().Version;
		if (ver == null || (ver.Major <= 0 && ver.Minor <= 0 && ver.Build <= 0))
		{
			return null;
		}

		return $"v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
	}

	private static string? GetFileVersion(Assembly assembly)
	{
		var fileVerAttr = (AssemblyFileVersionAttribute?)Attribute.GetCustomAttribute(
			assembly, typeof(AssemblyFileVersionAttribute));

		if (fileVerAttr == null || string.IsNullOrWhiteSpace(fileVerAttr.Version))
		{
			return null;
		}

		return $"v{fileVerAttr.Version.Trim()}";
	}

	private static string? GetProductVersion(Assembly assembly)
	{
		if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
		{
			return null;
		}

		var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
		if (string.IsNullOrEmpty(versionInfo.ProductVersion))
		{
			return null;
		}

		return versionInfo.ProductVersion.Trim();
	}
}
