using System;
using GdUnit4;
using Realm.Client.Services;

namespace Realm.Client.Tests;

[TestSuite]
public class MapProjectFileNormalizerTests
{
	[TestCase]
	public void DebugProjectUsesDwarf5()
	{
		const string input = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <LinkerArg Include="-gdwarf-4" Condition="'$(Configuration)' == 'Debug'" />
  </ItemGroup>
</Project>
""";

		string normalized = MapProjectFileNormalizer.Normalize(input, "wasm32-unknown-wasip1");

		if (!normalized.Contains("<IlcDwarfVersion>5</IlcDwarfVersion>", StringComparison.Ordinal))
			throw new Exception("The Debug project was not given IlcDwarfVersion 5.");
		if (!normalized.Contains("-gdwarf-5", StringComparison.Ordinal))
			throw new Exception("The Debug linker argument was not upgraded to DWARF 5.");
		if (normalized.Contains("-gdwarf-4", StringComparison.Ordinal))
			throw new Exception("The Debug linker argument still contains DWARF 4.");
		if (normalized.Contains("<Optimize>true</Optimize>", StringComparison.Ordinal))
			throw new Exception("The Debug project was not forced to Optimize=false.");
	}
}
