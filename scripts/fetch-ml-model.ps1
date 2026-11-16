param(
    [string]$DestinationDirectory = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modelFileName = "model_quantized.onnx"
$modelSha256 = "783cfd05a5986af42c70923789b206afd5a9c9f3cc1220fabd1e4d8cb183e875"
$modelUrl = "https://huggingface.co/hoan/multilingual-toxic-xlm-roberta-dynamic-quantized/resolve/main/model_quantized.onnx"

$rootDir = Split-Path -Parent $PSScriptRoot
if (-not $DestinationDirectory -or [string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $rootDir "Realm.Client\Assets\MLModels\toxic-xlm-roberta"
}

New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
$modelPath = Join-Path $DestinationDirectory $modelFileName

if ((Test-Path $modelPath) -and -not $Force) {
    $existingHash = (Get-FileHash -Path $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($existingHash -eq $modelSha256) {
        Write-Host "The toxic language model is already present and matches its recorded checksum: $modelPath"
        return
    }
    Write-Host "The toxic language model at $modelPath does not match its recorded checksum and will be replaced."
}

$temporaryPath = "$modelPath.download"
Write-Host "Downloading the toxic language model from $modelUrl"
Invoke-WebRequest -Uri $modelUrl -OutFile $temporaryPath

$downloadedHash = (Get-FileHash -Path $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($downloadedHash -ne $modelSha256) {
    Remove-Item -Path $temporaryPath -Force
    throw "The downloaded model does not match its recorded checksum. Expected $modelSha256 but received $downloadedHash."
}

Move-Item -Path $temporaryPath -Destination $modelPath -Force
Write-Host "The toxic language model is ready at $modelPath"
