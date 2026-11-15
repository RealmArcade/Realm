param(
    [string]$OutputDir = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$defaultOutputDir = Join-Path $rootDir "Realm.MapEditorExtension"
$usesDefaultOutput = -not $OutputDir -or [string]::IsNullOrWhiteSpace($OutputDir)
if ($usesDefaultOutput) {
    $OutputDir = $defaultOutputDir
}

$cliProj = Join-Path $rootDir "Realm.Tools.Cli\Realm.Tools.Cli.csproj"

$schemaDirs = @($OutputDir)
if ($usesDefaultOutput) {
    $mapTemplateSchemaDir = Join-Path $rootDir "MapTemplate\.vscode"
    if (Test-Path $mapTemplateSchemaDir) {
        $schemaDirs += $mapTemplateSchemaDir
    }
}

$cliAssembly = Join-Path $rootDir (Join-Path "Realm.Tools.Cli" (Join-Path (Join-Path "bin" $Configuration) (Join-Path "net10.0" "Realm.Tools.Cli.dll")))

foreach ($schemaDir in $schemaDirs) {
    if (Test-Path $cliAssembly) {
        & dotnet exec "$cliAssembly" verify_schemas -o "$schemaDir"
    } else {
        & dotnet run --project "$cliProj" -c "$Configuration" --no-launch-profile -- verify_schemas -o "$schemaDir"
    }

    if ($LASTEXITCODE -ne 0) {
        throw "The committed map schemas in $schemaDir no longer match the model. Run scripts/generate-schemas.ps1 and commit the regenerated schemas."
    }
}
