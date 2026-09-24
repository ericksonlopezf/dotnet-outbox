# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Architecture & Quality Standards Compliance Verification Script for EricksonLopez.Outbox.
.DESCRIPTION
    Validates architectural invariants:
    1. Kebab-case naming for all markdown documentation.
    2. Zero [Obsolete] usages in production code (src/).
    3. Presence of canonical MIT copyright header across all source files.
    4. Single top-level type per file in src/.
    5. Valid GitHub repository links referencing ericksonlopezf/dotnet-outbox.
    6. Official support and security email normalization (ericksonlopezf@gmail.com).
    7. Zero prohibited <NoWarn> suppressions across all projects.
#>

[CmdletBinding()]
param (
    [string]$RootDirectory = "."
)

$ErrorActionPreference = "Stop"
$violations = 0

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  REPOSITORY COMPLIANCE & ARCHITECTURE AUDITOR    " -ForegroundColor Cyan
Write-Host "  Repository: EricksonLopez.Outbox                " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Kebab-case documentation verification
Write-Host "`n[1/7] Checking documentation file naming (kebab-case)..." -ForegroundColor Yellow
$docsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "docs") -Recurse -Filter "*.md" -ErrorAction SilentlyContinue
$badDocNames = 0
if ($docsFiles) {
    foreach ($doc in $docsFiles) {
        $filename = $doc.Name
        if ($filename -ne "README.md" -and ($filename -cne $filename.ToLower() -or $filename -match "_")) {
            Write-Host "  ❌ Non-kebab-case document: $($doc.FullName)" -ForegroundColor Red
            $violations++
            $badDocNames++
        }
    }
}
if ($badDocNames -eq 0) { Write-Host "  ✅ All documentation files use valid kebab-case naming." -ForegroundColor Green }

# 2. Zero Obsolete APIs in src/
Write-Host "`n[2/7] Checking for [Obsolete] attribute usages in src/..." -ForegroundColor Yellow
$srcCsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "src") -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }
$obsoleteCount = 0
foreach ($cs in $srcCsFiles) {
    $lines = Get-Content $cs.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^\s*\[Obsolete\b" -and $lines[$i] -notmatch "^\s*//") {
            Write-Host "  ❌ [Obsolete] found in $($cs.FullName):$($i + 1)" -ForegroundColor Red
            $violations++
            $obsoleteCount++
        }
    }
}
if ($obsoleteCount -eq 0) { Write-Host "  ✅ Zero [Obsolete] attributes in production code." -ForegroundColor Green }

# 3. Canonical MIT Copyright Header
Write-Host "`n[3/7] Checking canonical MIT copyright headers..." -ForegroundColor Yellow
$missingHeaders = 0
foreach ($cs in $srcCsFiles) {
    $firstLine = (Get-Content $cs.FullName -TotalCount 1 -Encoding UTF8)
    if ($firstLine -notmatch "Copyright .* Erickson Lopez.* MIT License") {
        Write-Host "  ❌ Missing MIT header in $($cs.FullName)" -ForegroundColor Red
        $violations++
        $missingHeaders++
    }
}
if ($missingHeaders -eq 0) { Write-Host "  ✅ All production C# files contain the required MIT copyright header." -ForegroundColor Green }

# 4. One Type Per File in src/
Write-Host "`n[4/7] Checking 'One Type Per File' rule in src/..." -ForegroundColor Yellow
$multiTypeFiles = 0
foreach ($cs in $srcCsFiles) {
    $rawContent = [System.IO.File]::ReadAllText($cs.FullName)
    $codeWithoutStrings = [System.Text.RegularExpressions.Regex]::Replace($rawContent, '@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"', '')
    $codeWithoutComments = [System.Text.RegularExpressions.Regex]::Replace($codeWithoutStrings, '/\*[\s\S]*?\*/|//.*', '')
    $typeDecls = [System.Text.RegularExpressions.Regex]::Matches($codeWithoutComments, '(?m)^(?:public|internal|protected)\s+(?:sealed\s+|readonly\s+|abstract\s+|static\s+)*(?:class|struct|record|interface|enum|delegate)\s+([A-Za-z0-9_]+)')
    if ($typeDecls.Count -gt 1) {
        Write-Host "  ❌ Multiple top-level types in $($cs.FullName):" -ForegroundColor Red
        foreach ($td in $typeDecls) {
            Write-Host "     Type: $($td.Value.Trim())" -ForegroundColor DarkRed
        }
        $violations++
        $multiTypeFiles++
    }
}
if ($multiTypeFiles -eq 0) { Write-Host "  ✅ Every production file satisfies the 'One Type Per File' invariant." -ForegroundColor Green }

# 5. GitHub Repository Identity & Links
Write-Host "`n[5/7] Checking GitHub identity links (ericksonlopezf/dotnet-outbox)..." -ForegroundColor Yellow
$badLinks = 0
$allTrackedFiles = Get-ChildItem -Path $RootDirectory -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|\.git)[\\/]' -and ($_.Extension -in '.cs', '.md', '.props', '.targets') }
foreach ($f in $allTrackedFiles) {
    $lines = Get-Content $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "github\.com/ericksonlopez/dotnet-outbox\b") {
            Write-Host "  ❌ Incorrect GitHub repo link in $($f.FullName):$($i + 1)" -ForegroundColor Red
            $violations++
            $badLinks++
        }
    }
}
if ($badLinks -eq 0) { Write-Host "  ✅ All GitHub URLs correctly target ericksonlopezf/dotnet-outbox." -ForegroundColor Green }

# 6. Official Contact & Support Email Normalization
Write-Host "`n[6/7] Checking contact and security email normalization (ericksonlopezf@gmail.com)..." -ForegroundColor Yellow
$badEmails = 0
$metaFiles = @("SECURITY.md", "CODE_OF_CONDUCT.md", "SUPPORT.md")
foreach ($meta in $metaFiles) {
    $fullPath = Join-Path $RootDirectory $meta
    if (Test-Path $fullPath) {
        $lines = Get-Content $fullPath
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "ericksonlopez\.dev@gmail\.com") {
                Write-Host "  ❌ Legacy email detected in $meta : line $($i + 1)" -ForegroundColor Red
                $violations++
                $badEmails++
            }
        }
    }
}
if ($badEmails -eq 0) { Write-Host "  ✅ Official contact emails normalized to ericksonlopezf@gmail.com." -ForegroundColor Green }

# -----------------------------------------------------------------------------
# Stryker.NET Configuration, Concurrency, Anti-Gaming Blacklist & Matrix Synchronization
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Stryker] Validating Stryker.NET configuration, concurrency, anti-gaming blacklist & package matrix..." -ForegroundColor Yellow
$strykerErrors = 0
$targetRoot = if (Get-Variable -Name "RootDirectory" -Scope 0 -ErrorAction SilentlyContinue) { $RootDirectory } elseif (Get-Variable -Name "WorkspaceRoot" -Scope 0 -ErrorAction SilentlyContinue) { $WorkspaceRoot } elseif (Get-Variable -Name "repoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $repoRoot } elseif (Get-Variable -Name "RepoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $RepoRoot } else { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }

$strykerConfigFiles = Get-ChildItem -Path $targetRoot -Recurse -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|StrykerOutput|BenchmarkDotNet\.Artifacts|node_modules)[\\/]' -and
    $_.Name -ne "stryker-config.master.json"
}

if (-not $strykerConfigFiles -or $strykerConfigFiles.Count -eq 0) {
    Write-Host "  ❌ Zero Stryker configuration files found in repository." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Zero Stryker configuration files found.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $strykerErrors++
} else {
    foreach ($sf in $strykerConfigFiles) {
        $json = Get-Content $sf.FullName -Raw | ConvertFrom-Json
        $cfg = if ($json.PSObject.Properties['stryker-config']) { $json.'stryker-config' } else { $json }

        if ($cfg.PSObject.Properties['thresholds']) {
            $th = $cfg.thresholds
            if ($th.high -ne 100 -or $th.low -ne 98 -or $th.break -ne 95) {
                Write-Host "  ❌ Non-compliant mutation thresholds in $($sf.FullName): high=$($th.high), low=$($th.low), break=$($th.break). Required: high=100, low=98, break=95." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant mutation thresholds in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['concurrency']) {
            if ($cfg.concurrency -ne 2) {
                Write-Host "  ❌ Non-compliant Stryker concurrency in $($sf.FullName): $($cfg.concurrency). Required: 2." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant Stryker concurrency in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['ignore-methods'] -and $cfg.'ignore-methods') {
            foreach ($m in $cfg.'ignore-methods') {
                if ($m -match 'ThrowIf|Exception|Guard|ScrubEphemeralMemory') {
                    Write-Host "  ❌ Prohibited anti-gaming method exclusion '$m' detected in $($sf.FullName)." -ForegroundColor Red
                    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Prohibited anti-gaming exclusion '$m' in $($sf.FullName)") } else { $Violations++ } }
                    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                    $strykerErrors++
                }
            }
        }
    }

        $strykerProfiles = Get-ChildItem -Path $targetRoot -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -ne "stryker-config.master.json" -and
        ($_.Name -match '^stryker(-.+)?-config\.json$' -or $_.Name -eq "stryker-config.json")
    }

    $srcProjects = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\/](bin|obj)[\/]'
    }

    # Verify exact 1:1 count parity between Stryker profile configs and src/ projects
    if ($strykerProfiles.Count -ne $srcProjects.Count) {
        Write-Host "  ❌ Stryker profile count ($($strykerProfiles.Count)) does not match exactly the number of projects in src/ ($($srcProjects.Count))." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile count ($($strykerProfiles.Count)) does not match project count in src/ ($($srcProjects.Count)).") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    }

    foreach ($proj in $srcProjects) {
        $projName = $proj.Name
        $matched = $false
        foreach ($sf in $strykerProfiles) {
            $raw = Get-Content $sf.FullName -Raw
            if ($raw -match [regex]::Escape($projName) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matched = $true
                break
            }
        }

        if (-not $matched) {
            Write-Host "  ❌ Project '$projName' has no corresponding Stryker configuration profile." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding Stryker configuration profile.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    foreach ($sf in $strykerProfiles) {
        $raw = Get-Content $sf.FullName -Raw
        $matchedProj = $false
        foreach ($proj in $srcProjects) {
            if ($raw -match [regex]::Escape($proj.Name) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matchedProj = $true
                break
            }
        }
        if (-not $matchedProj) {
            Write-Host "  ❌ Stryker profile '$($sf.Name)' does not correspond to any project in src/ (orphaned profile)." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile '$($sf.Name)' does not correspond to any project in src/.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    $mutationWfPath = Join-Path $targetRoot ".github/workflows/mutation-testing.yml"
    if (-not (Test-Path $mutationWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/mutation-testing.yml" -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/mutation-testing.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    } else {
        $wfContent = Get-Content $mutationWfPath -Raw
        if ($wfContent -match '--concurrency\s*[:\s]\s*([3-9]|\d{2,})') {
            Write-Host "  ❌ Mutation workflow overrides concurrency with value > 2 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides concurrency > 2") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
        if ($wfContent -match '--break-at\s*[:\s]\s*([0-8]\d|\d{1})(?!\d)') {
            Write-Host "  ❌ Mutation workflow overrides break threshold with value < 90 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides break threshold < 90") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }

        foreach ($sf in $strykerConfigFiles) {
            if ($sf.Name -eq "stryker-config.json" -and $strykerConfigFiles.Count -gt 1) {
                continue
            }
            if ($sf.Name -eq "stryker-config-unit.json") {
                continue
            }
            $pkgIdent = if ($sf.Name -match '^stryker-(.+)-config\.json$') { $Matches[1] } else { $sf.Name }
            if ($wfContent -notmatch [regex]::Escape($sf.Name) -and $wfContent -notmatch "(?i)name:\s*$pkgIdent" -and $wfContent -notmatch "(?i)working-dir:.*$pkgIdent") {
                Write-Host "  ❌ Stryker configuration '$($sf.Name)' is missing from .github/workflows/mutation-testing.yml matrix." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker configuration '$($sf.Name)' is missing from matrix") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }
    }
}

if ($strykerErrors -eq 0) {
    Write-Host "  ✅ Stryker.NET configuration, 100/98/95 thresholds, concurrency 2, anti-gaming blacklist, and package matrix synchronization verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Mutation Testing Release Gate Scripts, Workflow Gate & docs/testing-roadmap.md
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Release Gate] Validating mutation release gate scripts, workflow enforcement & docs/testing-roadmap.md..." -ForegroundColor Yellow
$gateErrors = 0

$gateScriptPath = Join-Path $targetRoot "scripts/verify-mutation-gate.js"
if (-not (Test-Path $gateScriptPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.js release gate script." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$gateTestPath = Join-Path $targetRoot "scripts/verify-mutation-gate.test.js"
if (-not (Test-Path $gateTestPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.test.js unit tests." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.test.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$roadmapPath = Join-Path $targetRoot "docs/testing-roadmap.md"
if (-not (Test-Path $roadmapPath)) {
    Write-Host "  ❌ Missing docs/testing-roadmap.md governance document." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing docs/testing-roadmap.md") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$publishWfPath = Join-Path $targetRoot ".github/workflows/publish.yml"
if (Test-Path $publishWfPath) {
    $pubContent = Get-Content $publishWfPath -Raw
    if ($pubContent -notmatch "verify-mutation-gate\.js") {
        Write-Host "  ❌ .github/workflows/publish.yml does not enforce verify-mutation-gate.js before publishing." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("publish.yml does not enforce verify-mutation-gate.js") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $gateErrors++
    }
}

if ($gateErrors -eq 0) {
    Write-Host "  ✅ Mutation release gate scripts, publish pipeline gate, and docs/testing-roadmap.md verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Benchmark Regression Quality Gate & CI Enforcement
# -----------------------------------------------------------------------------
$hasBenchProject = (Get-ChildItem -Path $targetRoot -Filter "*Benchmark*.csproj" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|MEGA-AUDITORIA|StrykerOutput)[\\/]' } | Select-Object -First 1) -ne $null
if ($hasBenchProject) {
    Write-Host "`n[Gate: Benchmark Gate] Validating benchmark regression scripts & workflow enforcement..." -ForegroundColor Yellow
    $benchGateErrors = 0

    $benchScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.ps1"
    if (-not (Test-Path $benchScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.ps1 regression assertion script." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchTestScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.test.ps1"
    if (-not (Test-Path $benchTestScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.test.ps1 unit tests." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.test.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchWfPath = Join-Path $targetRoot ".github/workflows/benchmark-regression-gate.yml"
    if (-not (Test-Path $benchWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/benchmark-regression-gate.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmark-regression-gate.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    } else {
        $benchWfContent = Get-Content $benchWfPath -Raw -Encoding utf8
        if ($benchWfContent -notmatch "verify-benchmark-gate\.ps1" -or $benchWfContent -notmatch "--exporters json") {
            Write-Host "  ❌ .github/workflows/benchmark-regression-gate.yml does not enforce verify-benchmark-gate.ps1 and --exporters json." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid benchmark-regression-gate.yml") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $benchGateErrors++
        }
    }

    $baselinePath = Join-Path $targetRoot "benchmarks/results/baseline.json"
    if (-not (Test-Path $baselinePath)) {
        Write-Host "  ❌ Missing benchmarks/results/baseline.json baseline file." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmarks/results/baseline.json") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    if ($benchGateErrors -eq 0) {
        Write-Host "  ✅ Benchmark regression assertion script, baseline, and CI workflow verified." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# SourceLink & Central Package Management Integration Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: SourceLink] Validating centralized Microsoft.SourceLink.GitHub integration..." -ForegroundColor Yellow
$sourceLinkErrors = 0

$pkgPropsPath = Join-Path $targetRoot "Directory.Packages.props"
$bldPropsPath = Join-Path $targetRoot "Directory.Build.props"

if (-not (Test-Path $pkgPropsPath)) {
    Write-Host "  ❌ Missing Directory.Packages.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Packages.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $pkgContent = Get-Content $pkgPropsPath -Raw -Encoding utf8
    if ($pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props must declare 'Microsoft.SourceLink.GitHub' instead of generic or missing package." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Packages.props missing Microsoft.SourceLink.GitHub") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($pkgContent -match 'PackageVersion\s+Include="Microsoft\.SourceLink\.Common"' -and $pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props uses generic Microsoft.SourceLink.Common without GitHub provider." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Generic Microsoft.SourceLink.Common used") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if (-not (Test-Path $bldPropsPath)) {
    Write-Host "  ❌ Missing Directory.Build.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Build.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $bldContent = Get-Content $bldPropsPath -Raw -Encoding utf8
    if ($bldContent -notmatch 'PackageReference\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Build.props must centralize '<PackageReference Include=""Microsoft.SourceLink.GitHub"" PrivateAssets=""All"" />'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing Microsoft.SourceLink.GitHub PackageReference") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($bldContent -notmatch '<PublishRepositoryUrl>\s*true\s*</PublishRepositoryUrl>' -and $bldContent -notmatch '<PublishRepositoryUrl\s+Condition=') {
        Write-Host "  ❌ Directory.Build.props must specify '<PublishRepositoryUrl>true</PublishRepositoryUrl>'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing PublishRepositoryUrl") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if ($sourceLinkErrors -eq 0) {
    Write-Host "  ✅ SourceLink integration (Microsoft.SourceLink.GitHub) verified in Directory.Packages.props & Directory.Build.props." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Native AOT Test Gate & Compilation Smoke Test Invariants
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Native AOT] Validating Native AOT compilation smoke tests & workflow enforcement..." -ForegroundColor Yellow
$aotErrors = 0

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

# 1. Discover AOT-applicable projects in src/
$aotApplicableProjects = @()
foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $projDir = $proj.DirectoryName
    
    # Exclude Roslyn Analyzers and Source Generators
    if ($projName -match '(Analyzers?|Generators?)\.csproj$' -or $projDir -match '[\\/](Analyzers?|Generators?)[\\/]?$') {
        continue
    }
    # Exclude API endpoints / applications if applicable
    if ($projName -match '\.Api\.csproj$') {
        continue
    }
    
    $projContent = Get-Content $proj.FullName -Raw
    # Exclude projects explicitly marked as non-AOT compatible
    if ($projContent -match '<IsAotCompatible>\s*false\s*</IsAotCompatible>' -or 
        $projContent -match '<PublishAot>\s*false\s*</PublishAot>') {
        continue
    }
    
    $aotApplicableProjects += $proj
}

if ($aotApplicableProjects.Count -gt 0) {
    Write-Host "  [INFO] Detected $($aotApplicableProjects.Count) Native AOT applicable project(s) in src/." -ForegroundColor Gray
    
    # 2. Check for dedicated Native AOT smoke test project in tests/ or samples/
    $aotTestProjects = @()
    foreach ($searchDir in @("tests", "samples")) {
        $dirPath = Join-Path $targetRoot $searchDir
        if (Test-Path $dirPath) {
            $candidateTests = Get-ChildItem -Path $dirPath -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            }
            foreach ($t in $candidateTests) {
                $content = Get-Content $t.FullName -Raw
                if ($content -match '<PublishAot>\s*true\s*</PublishAot>' -or $t.Name -match 'AotSmokeTest|AotTest|NativeAot') {
                    $aotTestProjects += $t
                }
            }
        }
    }
    
    if ($aotTestProjects.Count -eq 0) {
        Write-Host "  ❌ Missing Native AOT smoke test project in tests/ or samples/ for $($aotApplicableProjects.Count) AOT-applicable project(s)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Native AOT smoke test project in tests/ or samples/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $hasValidExecutable = $false
        foreach ($aotProj in $aotTestProjects) {
            $aotContent = Get-Content $aotProj.FullName -Raw
            if ($aotContent -match '<OutputType>\s*Exe\s*</OutputType>' -and ($aotContent -match '<PublishAot>\s*true\s*</PublishAot>' -or $aotContent -match 'PublishAot')) {
                $hasValidExecutable = $true
                break
            }
        }
        if (-not $hasValidExecutable) {
            Write-Host "  ❌ At least one AOT smoke test project must declare OutputType=Exe and PublishAot=true." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("AOT smoke test project must declare OutputType=Exe and PublishAot=true.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
    
    # 3. Check for CI workflow .github/workflows/aot-smoke-test.yml
    $aotWorkflowPath = Join-Path $targetRoot ".github/workflows/aot-smoke-test.yml"
    if (-not (Test-Path $aotWorkflowPath)) {
        Write-Host "  ❌ Missing .github/workflows/aot-smoke-test.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/aot-smoke-test.yml CI workflow.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $wfContent = Get-Content $aotWorkflowPath -Raw
        if ($wfContent -notmatch 'dotnet publish' -or ($wfContent -notmatch 'linux-x64|win-x64' -and $wfContent -notmatch 'PublishAot')) {
            Write-Host "  ❌ Workflow .github/workflows/aot-smoke-test.yml does not execute a valid Native AOT publish step." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid aot-smoke-test.yml workflow.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
} else {
    Write-Host "  [INFO] Zero Native AOT applicable projects in src/ (pure analyzer/generator repository). Native AOT test gate skipped." -ForegroundColor Gray
}

if ($aotErrors -eq 0) {
    Write-Host "  ✅ Native AOT test project(s) and CI workflow verified." -ForegroundColor Green
}



# -----------------------------------------------------------------------------
# README Package Table Parity Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: README Package Table Parity] Validating documentation package table synchronization..." -ForegroundColor Yellow
$readmeErrors = 0
$readmePath = Join-Path $targetRoot "README.md"

if (-not (Test-Path $readmePath)) {
    Write-Host "  ❌ Missing README.md in repository root." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing README.md in repository root.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $readmeErrors++
} else {
    $readmeContent = Get-Content $readmePath -Raw -Encoding utf8
    $allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

    foreach ($proj in $allSrcProjs) {
        $projName = $proj.Name
        $baseName = $proj.BaseName
        
        # Check if project appears in README.md inside a table or package reference
        $escapedBase = [regex]::Escape($baseName)
        $isDocumented = ($readmeContent -match ('\|\s*`?' + $escapedBase + '`?\s*\|')) -or 
                        ($readmeContent -match ('\[`?' + $escapedBase + '`?\]')) -or
                        ($readmeContent -match "/packages/$escapedBase") -or
                        ($readmeContent -match ('\|\s*\[`?' + $escapedBase + '`?\]'))

        if (-not $isDocumented) {
            Write-Host "  ❌ Project '$projName' is missing from the packages table in README.md." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' is missing from the packages table in README.md.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $readmeErrors++
        }
    }

    if ($readmeErrors -eq 0) {
        Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified in README.md package table." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# Test Suite Symmetry & Coverage Gate (Principle 12)
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Test Suite Symmetry] Validating test project symmetry & project references across tests/..." -ForegroundColor Yellow
$testSymErrors = 0
$testsDir = Join-Path $targetRoot "tests"

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

$allTestProjs = @()
$testProjectReferences = @{}
if (Test-Path $testsDir) {
    $allTestProjs = Get-ChildItem -Path $testsDir -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }
    foreach ($tp in $allTestProjs) {
        $tContent = Get-Content $tp.FullName -Raw -Encoding utf8
        $refs = [regex]::Matches($tContent, '<ProjectReference\s+Include="([^"]+)"')
        foreach ($m in $refs) {
            $refFile = Split-Path $m.Groups[1].Value.Replace('\', '/') -Leaf
            $testProjectReferences[$refFile] = $true
        }
    }
}

foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $baseName = $proj.BaseName
    
    # Check 1: Named test suite matching base name
    $hasNamedTest = ($allTestProjs | Where-Object { $_.BaseName -match "^$([regex]::Escape($baseName))(\..+)?Tests?$" -or $_.BaseName -like "*$baseName*" }) -ne $null
    
    # Check 2: Direct ProjectReference in any test project
    $hasReference = $testProjectReferences.ContainsKey($projName)
    
    if (-not $hasNamedTest -and -not $hasReference) {
        Write-Host "  ❌ Project '$projName' has no corresponding test suite in tests/ (missing test project or ProjectReference)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding test suite in tests/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $testSymErrors++
    }
}

if ($testSymErrors -eq 0) {
    Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified with corresponding test suite in tests/." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Changelog Structure, Version Prefix & Quadruple Synchronization Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Changelog & Versioning] Validating changelog structure, VersionPrefix parity & release manifest..." -ForegroundColor Yellow
$changelogErrors = 0
$changelogPath = if (Test-Path (Join-Path $targetRoot "CHANGELOG.md")) { Join-Path $targetRoot "CHANGELOG.md" } elseif (Test-Path (Join-Path $targetRoot "changelog.md")) { Join-Path $targetRoot "changelog.md" } else { $null }

if (-not $changelogPath) {
    Write-Host "  ❌ Missing CHANGELOG.md or changelog.md in repository root." -ForegroundColor Red
    $violations++
    $changelogErrors++
} else {
    $clLines = Get-Content $changelogPath
    $hasUnreleased = $false
    $firstReleaseVersion = $null
    $firstReleaseDate = $null

    for ($i = 0; $i -lt $clLines.Count; $i++) {
        $line = $clLines[$i].Trim()
        if ($line -match '^##\s*\[Unreleased\]\s*$') {
            $hasUnreleased = $true
            continue
        }
        if ($hasUnreleased -and $line -match '^##\s*\[(\d+\.\d+\.\d+)\]\s*-\s*(\d{4}-\d{2}-\d{2})\s*$') {
            $firstReleaseVersion = $Matches[1]
            $firstReleaseDate = $Matches[2]
            break
        }
    }

    if (-not $hasUnreleased) {
        Write-Host "  ❌ Changelog must contain '## [Unreleased]' section at the top." -ForegroundColor Red
        $violations++
        $changelogErrors++
    }

    if (-not $firstReleaseVersion) {
        Write-Host "  ❌ Changelog first release under [Unreleased] must strictly match '## [VERSION] - AAAA-MM-DD' (e.g. '## [2.0.0] - 2026-08-30')." -ForegroundColor Red
        $violations++
        $changelogErrors++
    }

    # Extract VersionPrefix from Directory.Build.props
    $propsPath = Join-Path $targetRoot "Directory.Build.props"
    $versionPrefix = $null
    if (Test-Path $propsPath) {
        $propsRaw = Get-Content $propsPath -Raw
        if ($propsRaw -match '<VersionPrefix>\s*([^<\s]+)\s*</VersionPrefix>') {
            $versionPrefix = $Matches[1]
        }
    }

    if (-not $versionPrefix) {
        Write-Host "  ❌ Could not resolve <VersionPrefix> from Directory.Build.props." -ForegroundColor Red
        $violations++
        $changelogErrors++
    } elseif ($firstReleaseVersion -and $firstReleaseVersion -ne $versionPrefix) {
        Write-Host "  ❌ Version mismatch: Directory.Build.props <VersionPrefix> ($versionPrefix) does not match changelog latest version ($firstReleaseVersion)." -ForegroundColor Red
        $violations++
        $changelogErrors++
    }

    # Verify .release-please-manifest.json synchronization
    $manifestPath = Join-Path $targetRoot ".release-please-manifest.json"
    if (Test-Path $manifestPath) {
        $manifestJson = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $manifestVer = $manifestJson.'.'
        if ($manifestVer -and $versionPrefix -and $manifestVer -ne $versionPrefix) {
            Write-Host "  ❌ Version mismatch: .release-please-manifest.json ($manifestVer) does not match <VersionPrefix> ($versionPrefix)." -ForegroundColor Red
            $violations++
            $changelogErrors++
        }
    } else {
        Write-Host "  ❌ Missing .release-please-manifest.json in repository root." -ForegroundColor Red
        $violations++
        $changelogErrors++
    }
}

if ($changelogErrors -eq 0) {
    Write-Host "  ✅ Changelog structure (## [Unreleased] + ## [VERSION] - AAAA-MM-DD), <VersionPrefix> parity, and release manifest synchronized." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Prohibition of ./local-packages & Local Package Feeds Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Package Feeds & Local Packages] Validating prohibition of ./local-packages & local feeds..." -ForegroundColor Yellow
$feedErrors = 0

$localPkgDirs = Get-ChildItem -Path $targetRoot -Recurse -Directory -Filter "local-packages" -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](\.git|bin|obj|StrykerOutput|BenchmarkDotNet\.Artifacts)[\\/]'
}
if ($localPkgDirs -and $localPkgDirs.Count -gt 0) {
    foreach ($d in $localPkgDirs) {
        Write-Host "  ❌ Prohibited directory detected: $($d.FullName)" -ForegroundColor Red
        $violations++
        $feedErrors++
    }
}

$nugetConfigPath = Join-Path $targetRoot "nuget.config"
if (Test-Path $nugetConfigPath) {
    $nugetXml = Get-Content $nugetConfigPath -Raw
    if ($nugetXml -match 'key="local-packages"' -or $nugetXml -match 'value="[.\\/]+local-packages"') {
        Write-Host "  ❌ Prohibited local package feed 'local-packages' detected in nuget.config." -ForegroundColor Red
        $violations++
        $feedErrors++
    }
    if ($nugetXml -match '<add\s+[^>]*value="(\.[\\/]|[a-zA-Z]:\\)' -and $nugetXml -notmatch 'https?://') {
        Write-Host "  ❌ Local directory package source found in nuget.config. Only official remote feeds allowed." -ForegroundColor Red
        $violations++
        $feedErrors++
    }
}

if ($feedErrors -eq 0) {
    Write-Host "  ✅ Prohibition of ./local-packages and local feeds verified. Packages resolve directly via NuGet.org." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Implicit Usings & PackageProjectUrl Verification Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Explicit Usings & PackageProjectUrl] Validating ImplicitUsings=disable & PackageProjectUrl..." -ForegroundColor Yellow
$configGateErrors = 0

$propsPath = Join-Path $targetRoot "Directory.Build.props"
if (Test-Path $propsPath) {
    $propsContent = Get-Content $propsPath -Raw
    if ($propsContent -notmatch '<ImplicitUsings>\s*disable\s*</ImplicitUsings>') {
        Write-Host "  ❌ Directory.Build.props must specify '<ImplicitUsings>disable</ImplicitUsings>'." -ForegroundColor Red
        $violations++
        $configGateErrors++
    }
    if ($propsContent -match '<PackageProjectUrl>[^<]*dotnet-[^<]*</PackageProjectUrl>') {
        Write-Host "  ❌ PackageProjectUrl must NOT contain 'dotnet-' prefix. Required: https://ericksonlopez.dev/{lib}." -ForegroundColor Red
        $violations++
        $configGateErrors++
    }
    if ($propsContent -notmatch '<PackageProjectUrl>\s*https://ericksonlopez\.dev/outbox\s*</PackageProjectUrl>') {
        Write-Host "  ❌ PackageProjectUrl must be 'https://ericksonlopez.dev/outbox'." -ForegroundColor Red
        $violations++
        $configGateErrors++
    }
}

$allCsprojs = Get-ChildItem -Path $targetRoot -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]'
}
foreach ($cp in $allCsprojs) {
    $cpContent = Get-Content $cp.FullName -Raw
    if ($cpContent -match '<ImplicitUsings>\s*(enable|true)\s*</ImplicitUsings>') {
        Write-Host "  ❌ Prohibited <ImplicitUsings>enable</ImplicitUsings> in $($cp.FullName)." -ForegroundColor Red
        $violations++
        $configGateErrors++
    }
    if ($cpContent -match '<PackageProjectUrl>[^<]*dotnet-[^<]*</PackageProjectUrl>') {
        Write-Host "  ❌ Prohibited 'dotnet-' prefix in PackageProjectUrl in $($cp.FullName)." -ForegroundColor Red
        $violations++
        $configGateErrors++
    }
}

if ($configGateErrors -eq 0) {
    Write-Host "  ✅ ImplicitUsings=disable enforced globally and PackageProjectUrl free of 'dotnet-' prefix." -ForegroundColor Green
}


# 7. Summary & Exit Code
Write-Host "`n==================================================" -ForegroundColor Cyan
if ($violations -gt 0) {
    Write-Host "  FAILED: $violations compliance violation(s) detected. " -ForegroundColor Red -BackgroundColor Black
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 1
} else {
    Write-Host "  SUCCESS: 100% Governance & Compliance Verified. Zero violations. " -ForegroundColor Green -BackgroundColor Black
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 0
}
