#!/usr/bin/env pwsh
param(
    [string[]]$PackagesConfig = @(
        "packages.config",
        "SizerDataCollector.Core\packages.config",
        "SizerDataCollector.Service\packages.config"
    ),
    [string]$VulnerabilityIndexUrl = "https://api.nuget.org/v3/vulnerabilities/index.json"
)

$ErrorActionPreference = "Stop"

function Convert-ToComparableVersion {
    param([string]$Value)

    $main = ($Value.Trim() -split '[-+]')[0]
    return [Version]$main
}

function Test-VersionRange {
    param(
        [string]$Version,
        [string]$Range
    )

    $rangeText = $Range.Trim()
    if (-not ($rangeText.StartsWith("[") -or $rangeText.StartsWith("("))) {
        return (Convert-ToComparableVersion $Version).CompareTo((Convert-ToComparableVersion $rangeText)) -eq 0
    }

    $includeLower = $rangeText.StartsWith("[")
    $includeUpper = $rangeText.EndsWith("]")
    $parts = $rangeText.Substring(1, $rangeText.Length - 2).Split(",", 2)
    $lower = $parts[0].Trim()
    $upper = if ($parts.Count -gt 1) { $parts[1].Trim() } else { "" }
    $versionValue = Convert-ToComparableVersion $Version

    if ($lower) {
        $comparison = $versionValue.CompareTo((Convert-ToComparableVersion $lower))
        if (($includeLower -and $comparison -lt 0) -or (-not $includeLower -and $comparison -le 0)) {
            return $false
        }
    }

    if ($upper) {
        $comparison = $versionValue.CompareTo((Convert-ToComparableVersion $upper))
        if (($includeUpper -and $comparison -gt 0) -or (-not $includeUpper -and $comparison -ge 0)) {
            return $false
        }
    }

    return $true
}

function Get-SeverityName {
    param([int]$Severity)

    switch ($Severity) {
        0 { "Low" }
        1 { "Moderate" }
        2 { "High" }
        3 { "Critical" }
        default { "Unknown" }
    }
}

function Get-VulnerabilityMap {
    param([string]$IndexUrl)

    $index = Invoke-RestMethod -Uri $IndexUrl
    $map = @{}

    foreach ($entry in @($index)) {
        $page = Invoke-RestMethod -Uri $entry.'@id'
        foreach ($property in $page.PSObject.Properties) {
            $id = $property.Name.ToLowerInvariant()
            if (-not $map.ContainsKey($id)) {
                $map[$id] = New-Object System.Collections.Generic.List[object]
            }

            foreach ($advisory in @($property.Value)) {
                $map[$id].Add($advisory)
            }
        }
    }

    return [PSCustomObject]@{
        Map = $map
        Updated = (@($index) | ForEach-Object { $_.'@updated' }) -join ", "
        Sources = (@($index) | ForEach-Object { $_.'@id' }) -join ", "
    }
}

$feed = Get-VulnerabilityMap -IndexUrl $VulnerabilityIndexUrl
$findings = New-Object System.Collections.Generic.List[object]
$packagesSeen = 0

foreach ($path in $PackagesConfig) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "packages.config not found: $path"
    }

    [xml]$xml = Get-Content -LiteralPath $path -Raw
    foreach ($package in @($xml.packages.package)) {
        $packagesSeen++
        $id = [string]$package.id
        $version = [string]$package.version
        $key = $id.ToLowerInvariant()
        if (-not $feed.Map.ContainsKey($key)) {
            continue
        }

        foreach ($advisory in $feed.Map[$key]) {
            if (Test-VersionRange -Version $version -Range ([string]$advisory.versions)) {
                $findings.Add([PSCustomObject]@{
                    File = $path
                    Package = $id
                    Version = $version
                    Severity = Get-SeverityName ([int]$advisory.severity)
                    Range = [string]$advisory.versions
                    Advisory = [string]$advisory.url
                })
            }
        }
    }
}

Write-Host "NuGet vulnerability feed updated: $($feed.Updated)"
Write-Host "Audited packages.config files: $($PackagesConfig -join ', ')"
Write-Host "Package entries checked: $packagesSeen"

if ($findings.Count -eq 0) {
    Write-Host "No vulnerable packages found in packages.config entries."
    exit 0
}

$findings | Sort-Object File, Package, Version, Advisory | Format-Table -AutoSize
exit 2
