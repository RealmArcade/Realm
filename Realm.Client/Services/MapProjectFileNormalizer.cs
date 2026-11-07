using System;
using System.Text.RegularExpressions;

namespace Realm.Client.Services;

/// <summary>
///     Rewrites a map script project file so it points at the portable MapAPI/Ecs DLLs and carries the AOT and WASI
///     targets the build needs. It works on text only, so the caller supplies the target triple and nothing here touches
///     the filesystem.
/// </summary>
internal static partial class MapProjectFileNormalizer
{
	private const string MapApiDllRelativePath = "lib/Realm.MapAPI.dll";
	private const string EcsDllRelativePath = "lib/Realm.Ecs.dll";
	private const string EcsUsingItem = "<Using Include=\"Realm.Ecs\" />";

	/// <summary>The target triple the map project should build for, taken from the template when it declares one.</summary>
	internal static string ResolveDefaultIlcLlvmTarget(string? templatePath)
	{
		if (!string.IsNullOrEmpty(templatePath) && System.IO.File.Exists(templatePath))
		{
			string templateContent = System.IO.File.ReadAllText(templatePath);
			var match = IlcLlvmTargetRegex().Match(templateContent);
			if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
			{
				return match.Groups[1].Value.Trim();
			}
		}

		return WasiSdkResolver.GetDefaultIlcLlvmTarget();
	}

	internal static string Normalize(string csprojContent, string defaultIlcLlvmTarget)
	{
		csprojContent = csprojContent.Replace("\r\n", "\n");

		string mapApiReference = $"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>";
		string ecsReference = $"<Reference Include=\"Realm.Ecs\">\n      <HintPath>{EcsDllRelativePath}</HintPath>\n    </Reference>";

		bool hasEcsReference = ReferenceEcsRegex().IsMatch(csprojContent);

		csprojContent = ProjectReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = ReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = HintPathMapApiRegex().Replace(csprojContent,
			$"<HintPath>{MapApiDllRelativePath}</HintPath>");

		csprojContent = ReferenceEcsRegex().Replace(csprojContent, ecsReference);
		csprojContent = HintPathEcsRegex().Replace(csprojContent, $"<HintPath>{EcsDllRelativePath}</HintPath>");

		if (!hasEcsReference && csprojContent.Contains(mapApiReference, StringComparison.Ordinal))
		{
			csprojContent = csprojContent.Replace(mapApiReference, mapApiReference + "\n    " + ecsReference);
		}

		if (!csprojContent.Contains(EcsUsingItem, StringComparison.Ordinal))
		{
			string anchor = "<Using Include=\"Realm.Maps\" />";
			if (csprojContent.Contains(anchor, StringComparison.Ordinal))
				csprojContent = csprojContent.Replace(anchor, anchor + "\n    " + EcsUsingItem);
			else if (csprojContent.Contains(ecsReference, StringComparison.Ordinal))
				csprojContent = csprojContent.Replace(ecsReference, ecsReference + "\n    " + EcsUsingItem);
		}

		if (!csprojContent.Contains("TrimmerRootAssembly"))
		{
			csprojContent = csprojContent.Replace("</Project>",
				"  <ItemGroup>\n    <TrimmerRootAssembly Include=\"$(MSBuildProjectName)\" />\n  </ItemGroup>\n</Project>");
		}

		string defaultTarget = defaultIlcLlvmTarget;

		if (IlcLlvmTargetRegex().IsMatch(csprojContent))
		{
			csprojContent = IlcLlvmTargetRegex().Replace(csprojContent,
				$"<IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>");
		}
		else if (csprojContent.Contains("EnableAotLate", StringComparison.OrdinalIgnoreCase))
		{
			csprojContent = Regex.Replace(csprojContent, @"(<Target\s+Name=""EnableAotLate""[^>]*>\s*<PropertyGroup>)",
				$"$1\n      <IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>", RegexOptions.IgnoreCase);
		}
		else
		{
			string aotTarget = $"  <Target Name=\"EnableAotLate\" BeforeTargets=\"ImportRuntimeIlcPackageTarget;IlcCompile;_ComputeAssembliesToCompileToNative\">\n    <PropertyGroup>\n      <PublishAot>true</PublishAot>\n      <IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>\n    </PropertyGroup>\n  </Target>\n";
			int projectEnd = csprojContent.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
			csprojContent = projectEnd >= 0 ? csprojContent.Insert(projectEnd, aotTarget) : csprojContent + aotTarget;
		}

		string fixWasiLinkArgsTarget = "  <Target Name=\"FixWasiSdkLinkArgs\" BeforeTargets=\"LinkNative;LinkNativeLlvm\">\n    <PropertyGroup>\n      <IlcWasmGlobalBase>1048576</IlcWasmGlobalBase>\n    </PropertyGroup>\n    <ItemGroup>\n      <WasmComponentTypeWit Remove=\"@(WasmComponentTypeWit)\" />\n    </ItemGroup>\n  </Target>\n";

		if (ClearComponentWitTargetRegex().IsMatch(csprojContent))
		{
			csprojContent = ClearComponentWitTargetRegex().Replace(csprojContent, fixWasiLinkArgsTarget.TrimEnd());
		}
		else if (csprojContent.Contains("FixWasiSdkLinkArgs", StringComparison.OrdinalIgnoreCase))
		{
			if (!csprojContent.Contains("IlcWasmGlobalBase", StringComparison.OrdinalIgnoreCase))
			{
				csprojContent = Regex.Replace(csprojContent, @"(<Target\s+Name=""FixWasiSdkLinkArgs""[^>]*>)",
					"$1\n    <PropertyGroup>\n      <IlcWasmGlobalBase>1048576</IlcWasmGlobalBase>\n    </PropertyGroup>", RegexOptions.IgnoreCase);
			}
		}
		else
		{
			int projectEnd = csprojContent.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
			csprojContent = projectEnd >= 0 ? csprojContent.Insert(projectEnd, fixWasiLinkArgsTarget) : csprojContent + fixWasiLinkArgsTarget;
		}

		return csprojContent;
	}

	[GeneratedRegex(@"<ProjectReference\s+Include=""[^""]*Realm\.MapAPI\.csproj""[^>]*/>", RegexOptions.Singleline)]
	private static partial Regex ProjectReferenceMapApiRegex();

	[GeneratedRegex(@"<Reference\s+Include=""Realm\.MapAPI""\s*>.*?</Reference>", RegexOptions.Singleline)]
	private static partial Regex ReferenceMapApiRegex();

	[GeneratedRegex(@"<HintPath>[^<]*Realm\.MapAPI\.dll</HintPath>", RegexOptions.Singleline)]
	private static partial Regex HintPathMapApiRegex();

	[GeneratedRegex(@"<Reference\s+Include=""Realm\.Ecs""\s*>.*?</Reference>", RegexOptions.Singleline)]
	private static partial Regex ReferenceEcsRegex();

	[GeneratedRegex(@"<HintPath>[^<]*Realm\.Ecs\.dll</HintPath>", RegexOptions.Singleline)]
	private static partial Regex HintPathEcsRegex();

	[GeneratedRegex(@"<IlcLlvmTarget>\s*([^<\s]+)\s*</IlcLlvmTarget>", RegexOptions.IgnoreCase)]
	private static partial Regex IlcLlvmTargetRegex();

	[GeneratedRegex(@"<Target\s+Name=""ClearComponentWit""[^>]*>.*?</Target>", RegexOptions.Singleline)]
	private static partial Regex ClearComponentWitTargetRegex();
}
