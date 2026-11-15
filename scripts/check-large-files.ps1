param(
    [string]$Base = "",
    [string]$Head = "HEAD",
    [switch]$Staged,
    [long]$PerFileWarningBytes = 2097152,
    [long]$CommitWarningBytes = 8388608,
    [long]$ContributionFloorBytes = 262144
)

$ErrorActionPreference = "Stop"

function Format-Size([long]$Bytes) {
    $unitNames = @("B", "KiB", "MiB", "GiB")
    $remaining = [double]$Bytes
    $unitIndex = 0
    while ($remaining -ge 1024 -and $unitIndex -lt 3) {
        $remaining = $remaining / 1024
        $unitIndex++
    }
    return "{0:F1} {1}" -f $remaining, $unitNames[$unitIndex]
}

function Get-AddedBlobPath([string]$FilePath, [string]$BlobPrefix) {
    return "$BlobPrefix$FilePath"
}

function Get-LfsFilePaths([string[]]$FilePaths) {
    $lfsFilePaths = @()
    $maximumBatchSize = 200
    for ($offset = 0; $offset -lt $FilePaths.Count; $offset += $maximumBatchSize) {
        $lastIndex = [Math]::Min($offset + $maximumBatchSize - 1, $FilePaths.Count - 1)
        $attributeLines = & git check-attr filter -- @($FilePaths[$offset..$lastIndex])
        foreach ($attributeLine in $attributeLines) {
            if ($attributeLine -match '^(.+): filter: lfs$') { $lfsFilePaths += $Matches[1] }
        }
    }
    return $lfsFilePaths
}

function Get-AddedBlobSizes([string[]]$FilePaths, [string]$BlobPrefix) {
    $blobPaths = @($FilePaths | ForEach-Object { Get-AddedBlobPath $_ $BlobPrefix })
    return @($blobPaths | & git cat-file '--batch-check=%(objecttype) %(objectsize)')
}

if ($Staged) {
    $filePaths = @(& git diff --cached --name-only --no-renames --diff-filter=ACM)
    $blobPrefix = ":"
    $scopeDescription = "the next commit"
} elseif (-not [string]::IsNullOrWhiteSpace($Base)) {
    $filePaths = @(& git diff --name-only --no-renames --diff-filter=ACM "$Base...$Head")
    $blobPrefix = "${Head}:"
    $scopeDescription = "the change from $Base to $Head"
} else {
    Write-Host "Pass -Staged to inspect what the next commit would add, or -Base <ref> to inspect what a branch adds."
    exit 0
}

$filePaths = @($filePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

$lfsFilePaths = @(Get-LfsFilePaths $filePaths)
$blobSizeLines = @(Get-AddedBlobSizes $filePaths $blobPrefix)

$oversizedFiles = @()
$contributions = @()
$contributionBytes = [long]0

for ($fileIndex = 0; $fileIndex -lt $filePaths.Count; $fileIndex++) {
    $filePath = $filePaths[$fileIndex]
    if ($lfsFilePaths -contains $filePath) { continue }

    $blobSizeFields = @(($blobSizeLines[$fileIndex]) -split ' ')
    if ($blobSizeFields.Count -lt 2 -or $blobSizeFields[0] -ne "blob") { continue }
    $fileSize = [long]$blobSizeFields[1]

    if ($fileSize -ge $PerFileWarningBytes) {
        $oversizedFiles += [pscustomobject]@{ Path = $filePath; Bytes = $fileSize }
    }

    if ($fileSize -ge $ContributionFloorBytes) {
        $contributions += [pscustomobject]@{ Path = $filePath; Bytes = $fileSize }
        $contributionBytes += $fileSize
    }
}

$oversizedCommit = ($contributions.Count -gt 1) -and ($contributionBytes -ge $CommitWarningBytes)
$isGitHubAction = $env:GITHUB_ACTIONS -eq "true"

if ($oversizedFiles.Count -eq 0 -and -not $oversizedCommit) {
    Write-Host "No file size warning for $scopeDescription."
    exit 0
}

$summaryLines = @("## Repository size warning", "", "Scope: $scopeDescription", "")

if ($oversizedFiles.Count -gt 0) {
    Write-Host "Large file warning: $($oversizedFiles.Count) file(s) at or above $(Format-Size $PerFileWarningBytes)."
    $summaryLines += "Files at or above $(Format-Size $PerFileWarningBytes):"
    $summaryLines += ""
    foreach ($oversizedFile in $oversizedFiles) {
        Write-Host "  $(Format-Size $oversizedFile.Bytes)  $($oversizedFile.Path)"
        $summaryLines += "- ``$($oversizedFile.Path)`` - $(Format-Size $oversizedFile.Bytes)"
        if ($isGitHubAction) {
            Write-Host "::warning file=$($oversizedFile.Path)::$(Format-Size $oversizedFile.Bytes) is above the $(Format-Size $PerFileWarningBytes) per-file budget. Every byte committed is downloaded by every future clone, even after the file is deleted."
        }
    }
    $summaryLines += ""
}

if ($oversizedCommit) {
    Write-Host "Large commit warning: $($contributions.Count) files of $(Format-Size $ContributionFloorBytes) or more add up to $(Format-Size $contributionBytes), above the $(Format-Size $CommitWarningBytes) commit budget."
    $summaryLines += "Files of $(Format-Size $ContributionFloorBytes) or more add up to $(Format-Size $contributionBytes), above the $(Format-Size $CommitWarningBytes) budget:"
    $summaryLines += ""
    foreach ($contribution in $contributions | Sort-Object -Property Bytes -Descending | Select-Object -First 5) {
        Write-Host "  $(Format-Size $contribution.Bytes)  $($contribution.Path)"
        $summaryLines += "- ``$($contribution.Path)`` - $(Format-Size $contribution.Bytes)"
    }
    if ($contributions.Count -gt 5) {
        Write-Host "  and $($contributions.Count - 5) more"
    }
    $summaryLines += ""
    if ($isGitHubAction) {
        Write-Host "::warning::This change adds $(Format-Size $contributionBytes) in files of $(Format-Size $ContributionFloorBytes) or more, above the $(Format-Size $CommitWarningBytes) budget. A clone downloads all of it forever."
    }
}

Write-Host ""
Write-Host "Every byte committed is downloaded by every future clone, even after the file is deleted."
Write-Host "Prefer a downscaled image encoded with cwebp -q 90, or a download script under scripts/ over a committed asset."
Write-Host "This warning never blocks."

if ($isGitHubAction -and $env:GITHUB_STEP_SUMMARY) {
    $summaryLines += "Every byte committed is downloaded by every future clone, even after the file is deleted. A deliberate large file is allowed; state why it is an exception. Otherwise prefer a downscaled image encoded with ``cwebp -q 90``, or a download script under ``scripts/``."
    $summaryLines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

exit 0
