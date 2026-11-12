using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Realm.WasmBindgen;

namespace Realm.WasmBindgen.Tests;

[TestFixture]
public class WasmDebuggerIntegrationTests
{
	private string _repoRoot = null!;
	private string _generatedScriptPath = null!;

	[OneTimeSetUp]
	public void OneTimeSetUp()
	{
		_repoRoot = FindRepoRoot();
		_generatedScriptPath = Path.Combine(Path.GetTempPath(), $"realm_lldb_{Guid.NewGuid():N}.py");
		WasmLldbScriptWriter.GenerateLldbScript(_repoRoot, CreateCompilation(), _generatedScriptPath);
	}

	[OneTimeTearDown]
	public void OneTimeTearDown()
	{
		if (File.Exists(_generatedScriptPath)) File.Delete(_generatedScriptPath);
	}

	[Test]
	public void EveryHandleTypeGetsASyntheticProvider()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		foreach (string handleType in new[] { "PlayerId", "UnitId", "ResourceNodeId", "LootItemId" })
		{
			Assert.That(script, Does.Contain($"\"{handleType}\""), $"{handleType} was not discovered as a handle type");
		}

		Assert.That(script, Does.Contain("RealmHandleSyntheticProvider"), "the generic synthetic provider is missing");
		Assert.That(script, Does.Not.Contain("PLAYER_FIELDS"), "the old hard-coded player field list is still present");
		Assert.That(script, Does.Not.Contain("ENTITY_FIELDS"), "the old hard-coded entity field list is still present");
	}

	[Test]
	public void EveryComponentBackedPropertyIsGenerated()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		foreach (string fieldName in new[]
		         {
			         "Charges", "MaximumCharges", "MagicResistance", "IsBuilding", "IsHero", "Invulnerable",
			         "AnimationsFrozen", "CommandsFrozen", "Frozen", "HideHealthAndManaBars", "Kills",
			         "CurrentPopulation", "MaxPopulation", "IsActive", "IsComputer", "Team", "Gold", "Wood",
			         "Stone", "Health", "MaxHealth", "Mana", "MaxMana", "ManaRegen", "Damage", "Range",
			         "AttackSpeed", "Armor", "Speed", "Position", "Player", "Level", "Experience", "Scale",
			         "Facing", "IsDead", "IsValid", "Value"
		         })
		{
			Assert.That(script, Does.Contain($"\"{fieldName}\""), $"{fieldName} was not generated");
		}
	}

	[Test]
	public void HostBackedPropertiesReportTheBoundaryInsteadOfBackingFields()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		Assert.That(script, Does.Contain("Inspection requires threads to execute. Copy value to local variable to inspect"));
		Assert.That(script, Does.Contain("INSPECTION_REQUIRES_THREADS"));
	}

	[Test]
	public void HostWrapperPropertiesReportTheBoundary()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		Assert.That(script, Does.Contain("HOST_TYPES"));
		Assert.That(script, Does.Contain("GameAPI_WasmModule"));
		Assert.That(script, Does.Contain("PlayerCount"));
		Assert.That(script, Does.Contain("HostTypeSyntheticProvider"));
	}

	[Test]
	public void ComponentOffsetsAndSizesAreDiscovered()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		Assert.That(script, Does.Contain("COMPONENT_OFFSETS"));
		Assert.That(script, Does.Contain("COMPONENT_SIZES"));
		Assert.That(script, Does.Contain("COMPONENT_IDS"));
		Assert.That(script, Does.Contain("\"Health\""));
		Assert.That(script, Does.Contain("\"UnitCharges\""));
		Assert.That(script, Does.Contain("\"PlayerDamageEffectiveness\""));
	}

	[Test]
	public void GeneratedScriptReadsRealValuesAndReportsHostBoundaries()
	{
		string harnessPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "realm_lldb_fake_test.py");
		Assert.That(File.Exists(harnessPath), Is.True, "the fake LLDB harness was not copied to the test output");

		var startInfo = new ProcessStartInfo
		{
			FileName = "python",
			Arguments = $"\"{harnessPath}\" \"{_generatedScriptPath}\"",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using Process process = Process.Start(startInfo)!;
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();

		Assert.That(process.ExitCode, Is.EqualTo(0), $"the generated debugger script failed:\n{output}\n{error}");
		Assert.That(output, Does.Contain("OK"));
	}

	[Test]
	public void GeneratedScriptCarriesVersionMetadataAndDiagnosticCommands()
	{
		string script = File.ReadAllText(_generatedScriptPath);

		Assert.That(script, Does.Contain("REALM_LLDB_SCRIPT_VERSION"));
		Assert.That(script, Does.Contain("REALM_LLDB_SCRIPT_HASH"));
		Assert.That(script, Does.Contain("REALM_LLDB_SOURCE_COMMIT"));
		Assert.That(script, Does.Contain("def realm_version"));
		Assert.That(script, Does.Contain("def realm_verify"));
		Assert.That(script, Does.Contain("def realm_doctor"));
		Assert.That(script, Does.Contain("def realm_inspect"));
		Assert.That(script, Does.Contain("def realm_watch"));
		Assert.That(script, Does.Contain("def realm_plot"));
		Assert.That(script, Does.Contain("def realm_export"));
		Assert.That(script, Does.Contain("def realm_step"));
		Assert.That(script, Does.Contain("def realm_reload"));
		Assert.That(script, Does.Contain("def realm_where"));
		Assert.That(script, Does.Contain("def realm_exception"));
		Assert.That(script, Does.Contain("def find_authority_pid"));
	}

	[Test]
	public void MapApiComponentsAreDiscoveredFromMarkerAttribute()
	{
		string generated = WasmEcsComponentEmitter.GenerateEcsComponents(_repoRoot);

		foreach (string typeName in new[]
		         {
			         "ResourceId", "SplashType", "Attack", "Resistances", "Position", "PreviousPosition",
			         "Health", "Mana", "PlayerPopulation", "ScriptPlayer", "UnitOwnerPlayer", "Experience",
			         "Level", "ModelScale", "RotationY", "PreviousRotationY", "MovementType", "MovementStats",
			         "PlayerResources"
		         })
		{
			Assert.That(generated, Does.Contain(typeName), $"{typeName} was not discovered from the marker attribute");
		}

		Assert.That(generated, Does.Not.Contain("struct Ability"), "an unannotated component was exposed to map scripts");
	}

	[Test]
	public void LaunchConfigurationSkipsHostAndWasmBoundaryCode()
	{
		string launchPath = Path.Combine(_repoRoot, "MapTemplate", ".vscode", "launch.json");
		string launch = File.ReadAllText(launchPath);

		Assert.That(launch, Does.Contain("step-avoid-regexp"));
		Assert.That(launch, Does.Contain("GameAPI_WasmModule"));
		Assert.That(launch, Does.Contain("wasmImport"));
		Assert.That(launch, Does.Contain("process handle SIGTRAP"));
		Assert.That(launch, Does.Contain("step-in-avoid-nodebug"));
		Assert.That(launch, Does.Contain("step-out-avoid-nodebug"));
	}

	[Test]
	public void CompilePathRefreshesDebuggerAssetsBeforeCompiling()
	{
		string fileIoPath = Path.Combine(_repoRoot, "Realm.Client", "UI", "MapEditorHUD.FileIO.cs");
		string source = File.ReadAllText(fileIoPath);

		int refreshIndex = source.IndexOf("MapWorkspaceService.RefreshDebuggerAssets(workspace)", StringComparison.Ordinal);
		int compileIndex = source.IndexOf("PerformCompilationStepAsync(workspace, isDebug)", StringComparison.Ordinal);

		Assert.That(refreshIndex, Is.GreaterThanOrEqualTo(0), "the compile path does not refresh the debugger assets");
		Assert.That(compileIndex, Is.GreaterThan(refreshIndex), "the debugger assets are refreshed after the compile instead of before it");
	}

	private static Compilation CreateCompilation()
	{
		var references = new List<MetadataReference>();
		foreach (string assemblyPath in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
		{
			try
			{
				references.Add(MetadataReference.CreateFromFile(assemblyPath));
			}
			catch (BadImageFormatException)
			{
			}
		}

		return CSharpCompilation.Create(
			"Realm.WasmBindgen.Tests.DebuggerCompilation",
			references: references,
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
	}

	private static string FindRepoRoot()
	{
		string? directory = TestContext.CurrentContext.TestDirectory;
		while (!string.IsNullOrEmpty(directory))
		{
			if (Directory.Exists(Path.Combine(directory, "Realm.WasmBindgen")) &&
			    Directory.Exists(Path.Combine(directory, "MapTemplate")) &&
			    Directory.Exists(Path.Combine(directory, "Realm.Ecs")))
			{
				return directory;
			}

			directory = Path.GetDirectoryName(directory);
		}

		throw new DirectoryNotFoundException("Could not find the Realm repository root from the test output directory.");
	}
}
