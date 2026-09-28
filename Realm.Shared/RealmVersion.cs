using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Realm.Shared;

public static class RealmVersion
{
	public const string GameBuildNumber = "v0.0.3";
	public static readonly string GameBinaryVersion = GetGameBinaryVersion();

	public static string GetGameBinaryVersion(Assembly? assembly = null)
	{
		const string defaultVersionString = "v0.0.3_Pre-Alpha";
		try
		{
			assembly ??= typeof(RealmVersion).Assembly;

			var infoVerAttr = (AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
				assembly, typeof(AssemblyInformationalVersionAttribute));
			if (infoVerAttr != null && !string.IsNullOrWhiteSpace(infoVerAttr.InformationalVersion))
			{
				string infoVer = infoVerAttr.InformationalVersion;
				int plusIdx = infoVer.IndexOf('+');
				if (plusIdx > 0)
				{
					infoVer = infoVer.Substring(0, plusIdx);
				}
				if (!string.IsNullOrWhiteSpace(infoVer))
				{
					return infoVer.Trim();
				}
			}

			var ver = assembly.GetName().Version;
			if (ver != null && (ver.Major > 0 || ver.Minor > 0 || ver.Build > 0))
			{
				return $"v{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
			}

			var fileVerAttr = (AssemblyFileVersionAttribute?)Attribute.GetCustomAttribute(
				assembly, typeof(AssemblyFileVersionAttribute));
			if (fileVerAttr != null && !string.IsNullOrWhiteSpace(fileVerAttr.Version))
			{
				return $"v{fileVerAttr.Version.Trim()}";
			}

			if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
			{
				var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
				if (!string.IsNullOrEmpty(versionInfo.ProductVersion))
				{
					return versionInfo.ProductVersion.Trim();
				}
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to read version from assembly: {ex.Message}");
		}

		return defaultVersionString;
	}
}
