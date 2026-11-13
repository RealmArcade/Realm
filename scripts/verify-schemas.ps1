param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir -or [string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $rootDir "Realm.MapEditorExtension"
}

$cliProj = Join-Path $rootDir "Realm.Tools.Cli\Realm.Tools.Cli.csproj"

& dotnet run --project "$cliProj" -c Release --no-launch-profile -- verify_schemas -o "$OutputDir"
if ($LASTEXITCODE -ne 0) {
    throw "The committed map schemas no longer match the model. Run scripts/generate-schemas.ps1 and commit the regenerated schemas."
}
