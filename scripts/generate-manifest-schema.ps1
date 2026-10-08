param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath -or [string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $rootDir "Realm.MapEditorExtension\manifest.schema.json"
}

$cliProj = Join-Path $rootDir "Realm.Tools.Cli\Realm.Tools.Cli.csproj"

& dotnet run --project "$cliProj" -c Release --no-launch-profile -- generate_manifest_schema -o "$OutputPath"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate manifest schema via Realm.Tools.Cli"
}
