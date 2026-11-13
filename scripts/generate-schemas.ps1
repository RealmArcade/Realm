param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$defaultOutputDir = Join-Path $rootDir "Realm.MapEditorExtension"
$usesDefaultOutput = -not $OutputDir -or [string]::IsNullOrWhiteSpace($OutputDir)
if ($usesDefaultOutput) {
    $OutputDir = $defaultOutputDir
}

$cliProj = Join-Path $rootDir "Realm.Tools.Cli\Realm.Tools.Cli.csproj"

& dotnet run --project "$cliProj" -c Release --no-launch-profile -- generate_schemas -o "$OutputDir"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate map schemas via Realm.Tools.Cli"
}

if ($usesDefaultOutput) {
    $mapTemplateSchemaDir = Join-Path $rootDir "MapTemplate\.vscode"
    if (Test-Path $mapTemplateSchemaDir) {
        foreach ($schemaFile in @("metadata.schema.json", "terrain.schema.json", "manifest.schema.json")) {
            Copy-Item -Path (Join-Path $OutputDir $schemaFile) -Destination (Join-Path $mapTemplateSchemaDir $schemaFile) -Force
        }

        Write-Host "Copied generated schemas to $mapTemplateSchemaDir"
    }
}
