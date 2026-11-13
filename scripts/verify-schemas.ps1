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

$schemaDirs = @($OutputDir)
if ($usesDefaultOutput) {
    $mapTemplateSchemaDir = Join-Path $rootDir "MapTemplate\.vscode"
    if (Test-Path $mapTemplateSchemaDir) {
        $schemaDirs += $mapTemplateSchemaDir
    }
}

foreach ($schemaDir in $schemaDirs) {
    & dotnet run --project "$cliProj" -c Release --no-launch-profile -- verify_schemas -o "$schemaDir"
    if ($LASTEXITCODE -ne 0) {
        throw "The committed map schemas in $schemaDir no longer match the model. Run scripts/generate-schemas.ps1 and commit the regenerated schemas."
    }
}
