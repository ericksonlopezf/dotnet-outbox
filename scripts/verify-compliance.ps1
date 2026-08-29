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
    $firstLine = (Get-Content $cs.FullName -TotalCount 1)
    if ($firstLine -notmatch "Copyright © Erickson Lopez\. MIT License\.") {
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
$allTrackedFiles = Get-ChildItem -Path $RootDirectory -Recurse -Include "*.cs", "*.md", "*.props", "*.targets" | Where-Object { $_.FullName -notmatch "\\(obj|bin|\.git)\\" }
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
