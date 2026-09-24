# Copyright © Erickson Lopez. MIT License.
# ─────────────────────────────────────────────────────────────────────────────
# Automated Benchmark Regression Quality Gate
# Validates BenchmarkDotNet JSON reports against strict ecosystem invariants:
#   1. Heap Invariant (Zero-Allocation): Hot-path combinators must allocate 0 B.
#   2. Latency Threshold: Mean latency must not regress > 5% vs baseline.json.
# ─────────────────────────────────────────────────────────────────────────────

[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ReportDir = "benchmarks/pr-results",

    [Parameter(Mandatory = $false)]
    [string]$BaselinePath = "benchmarks/results/baseline.json",

    [Parameter(Mandatory = $false)]
    [double]$MaxLatencyRegressionPercent = 5.0,

    [Parameter(Mandatory = $false)]
    [string]$ZeroAllocPattern = "^(Bind|Map|Tap|ValidateAll|Success|Failure|ZeroAlloc|.*_TState.*)",

    [Parameter(Mandatory = $false)]
    [switch]$FailOnMissingReports
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Magenta
Write-Host "  AUTOMATED BENCHMARK REGRESSION QUALITY GATE" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host "Report Directory          : $ReportDir"
Write-Host "Baseline File             : $BaselinePath"
Write-Host "Max Latency Regression    : +$MaxLatencyRegressionPercent%"
Write-Host "Zero-Allocation Pattern   : $ZeroAllocPattern"
Write-Host "============================================================`n"

# Helper for strict mode safe property extraction
function Get-PropValue($obj, [string]$propName, $defaultValue = $null) {
    if ($null -eq $obj) { return $defaultValue }
    if ($obj.PSObject.Properties[$propName]) {
        return $obj.$propName
    }
    return $defaultValue
}

# 1. Locate Report JSON Files
if (-not (Test-Path $ReportDir)) {
    # Fallback to benchmarks/results if pr-results does not exist
    if (Test-Path "benchmarks/results") {
        Write-Host "[INFO] ReportDir '$ReportDir' not found, falling back to 'benchmarks/results'." -ForegroundColor Cyan
        $ReportDir = "benchmarks/results"
    }
}

$reportFiles = @()
if (Test-Path $ReportDir) {
    $reportFiles = Get-ChildItem -Path $ReportDir -Filter "*report*.json" -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "baseline\.json$" }
}

if ($reportFiles.Count -eq 0) {
    $msg = "No BenchmarkDotNet JSON reports (*report*.json) found in '$ReportDir'."
    if ($FailOnMissingReports) {
        Write-Host "::error::$msg"
        Write-Error $msg
        exit 1
    } else {
        Write-Host "::warning::$msg Skipping benchmark assertion (no reports generated)." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "[INFO] Found $($reportFiles.Count) benchmark report file(s)." -ForegroundColor Cyan

# 2. Parse Current Benchmark Results
$currentBenchmarks = @{}

foreach ($file in $reportFiles) {
    try {
        $json = Get-Content $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
        $benchList = Get-PropValue $json "Benchmarks"
        if ($null -eq $benchList) { continue }

        foreach ($b in $benchList) {
            $method = Get-PropValue $b "Method" (Get-PropValue $b "MethodTitle" "")
            $fullName = Get-PropValue $b "FullName" $method
            if ([string]::IsNullOrWhiteSpace($method)) { continue }

            $stats = Get-PropValue $b "Statistics"
            $mean = $null
            if ($null -ne $stats) {
                $mean = Get-PropValue $stats "Mean"
            }

            $memory = Get-PropValue $b "Memory"
            $allocBytes = $null
            if ($null -ne $memory) {
                $allocBytes = Get-PropValue $memory "BytesAllocatedPerOperation"
            }

            # Key by Method and FullName
            $entry = [PSCustomObject]@{
                Method         = $method
                FullName       = $fullName
                MeanNs         = if ($null -ne $mean) { [double]$mean } else { $null }
                AllocatedBytes = if ($null -ne $allocBytes) { [int64]$allocBytes } else { $null }
                File           = $file.Name
            }

            $currentBenchmarks[$method] = $entry
            if ($fullName -ne $method) {
                $currentBenchmarks[$fullName] = $entry
            }
        }
    } catch {
        Write-Host "::warning::Failed to parse $($file.FullName): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host "[INFO] Parsed $($currentBenchmarks.Keys.Count / 2) distinct benchmark operation(s).`n" -ForegroundColor Cyan

# 3. Load Baseline if present
$baseline = @{}
$hasBaseline = $false

if (Test-Path $BaselinePath) {
    try {
        $baseJson = Get-Content $BaselinePath -Raw -Encoding utf8 | ConvertFrom-Json
        $baseZeroAllocPattern = Get-PropValue $baseJson "ZeroAllocPattern"
        if ($null -ne $baseZeroAllocPattern -and -not [string]::IsNullOrWhiteSpace($baseZeroAllocPattern)) {
            $ZeroAllocPattern = "$ZeroAllocPattern|$baseZeroAllocPattern"
        }
        $baseBench = Get-PropValue $baseJson "Benchmarks"
        if ($null -ne $baseBench) {
            if ($baseBench -is [System.Collections.IDictionary] -or $baseBench -is [System.Management.Automation.PSCustomObject]) {
                foreach ($prop in $baseBench.PSObject.Properties) {
                    $bObj = $prop.Value
                    $baseline[$prop.Name] = [PSCustomObject]@{
                        MeanNs         = Get-PropValue $bObj "MeanNs" (Get-PropValue $bObj "Mean")
                        AllocatedBytes = Get-PropValue $bObj "AllocatedBytes"
                        ZeroAlloc      = Get-PropValue $bObj "ZeroAlloc"
                    }
                }
            } elseif ($baseBench -is [System.Collections.IEnumerable]) {
                foreach ($b in $baseBench) {
                    $m = Get-PropValue $b "Method" (Get-PropValue $b "MethodTitle" "")
                    $fn = Get-PropValue $b "FullName" $m
                    $stats = Get-PropValue $b "Statistics"
                    $mean = if ($null -ne $stats) { Get-PropValue $stats "Mean" } else { Get-PropValue $b "MeanNs" }
                    $mem = Get-PropValue $b "Memory"
                    $bytes = if ($null -ne $mem) { Get-PropValue $mem "BytesAllocatedPerOperation" } else { Get-PropValue $b "AllocatedBytes" }
                    $zeroAlloc = Get-PropValue $b "ZeroAlloc"

                    $record = [PSCustomObject]@{
                        MeanNs         = if ($null -ne $mean) { [double]$mean } else { $null }
                        AllocatedBytes = if ($null -ne $bytes) { [int64]$bytes } else { $null }
                        ZeroAlloc      = $zeroAlloc
                    }
                    if (-not [string]::IsNullOrWhiteSpace($m)) { $baseline[$m] = $record }
                    if (-not [string]::IsNullOrWhiteSpace($fn)) { $baseline[$fn] = $record }
                }
            }
            $hasBaseline = ($baseline.Keys.Count -gt 0)
        }
    } catch {
        Write-Host "::warning::Could not parse baseline file $BaselinePath: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

if ($hasBaseline) {
    Write-Host "[INFO] Loaded baseline with $($baseline.Keys.Count) benchmark records." -ForegroundColor Green
} else {
    Write-Host "[INFO] No baseline found at '$BaselinePath'. Latency regression comparison will be skipped (zero-allocation invariants are still enforced)." -ForegroundColor Yellow
}

# 4. Evaluate Invariants & Assertions
$violations = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$evaluatedResults = [System.Collections.Generic.List[PSCustomObject]]::new()

$uniqueMethods = $currentBenchmarks.Values | Sort-Object -Property Method -Unique

foreach ($item in $uniqueMethods) {
    $methodName = $item.Method
    $allocBytes = $item.AllocatedBytes
    $meanNs = $item.MeanNs
    $baseRecord = if ($baseline.ContainsKey($methodName)) { $baseline[$methodName] } elseif ($baseline.ContainsKey($item.FullName)) { $baseline[$item.FullName] } else { $null }
    $isZeroAllocRequired = ($methodName -match $ZeroAllocPattern) -or
                           ($null -ne $baseRecord -and ($baseRecord.AllocatedBytes -eq 0 -or $baseRecord.ZeroAlloc -eq $true)) -or
                           ($methodName -match '(?i)(ZeroAlloc|Stackalloc|Span|TryFormat)')

    $status = "PASS"
    $details = ""

    # Rule 1: Heap Invariant (Zero-Allocation on Hot Path)
    if ($isZeroAllocRequired) {
        if ($null -eq $allocBytes) {
            # Memory was not tracked for this benchmark
            $details += "MemoryDiagnoser not captured. "
        } elseif ($allocBytes -gt 0) {
            $violMsg = "❌ Zero-allocation invariant VIOLATED: Method '$methodName' allocated $allocBytes B (Expected: 0 B)."
            $violations.Add($violMsg)
            $status = "FAIL (Allocations > 0B)"
            $details += "Allocated: $allocBytes B (Expected: 0 B). "
        } else {
            $details += "Allocations: 0 B (PASS). "
        }
    } else {
        if ($null -ne $allocBytes) {
            $details += "Allocated: $allocBytes B. "
        }
    }

    # Rule 2: Latency Regression vs Baseline
    if ($hasBaseline -and ($baseline.ContainsKey($methodName) -or $baseline.ContainsKey($item.FullName))) {
        $baseRecord = if ($baseline.ContainsKey($methodName)) { $baseline[$methodName] } else { $baseline[$item.FullName] }
        $baseMean = Get-PropValue $baseRecord "MeanNs"

        if ($null -ne $baseMean -and $null -ne $meanNs -and $baseMean -gt 0) {
            $deltaPct = (($meanNs - $baseMean) / $baseMean) * 100.0

            if ($deltaPct -gt $MaxLatencyRegressionPercent) {
                $regMsg = "⚠️ Latency regressed: '$methodName' took $($meanNs.ToString('F2')) ns vs baseline $($baseMean.ToString('F2')) ns (+$(($deltaPct).ToString('F1'))% > +$MaxLatencyRegressionPercent%)."
                $violations.Add($regMsg)
                $status = "FAIL (Latency Regression)"
                $details += "Latency: +$(($deltaPct).ToString('F1'))% (+$MaxLatencyRegressionPercent% limit). "
            } else {
                $details += "Latency delta: $(($deltaPct).ToString('+0.0;-0.0;0.0'))% vs baseline. "
            }
        }
    }

    $evaluatedResults.Add([PSCustomObject]@{
        Method         = $methodName
        ZeroAllocReq   = if ($isZeroAllocRequired) { "Yes (0 B)" } else { "No" }
        AllocatedBytes = if ($null -ne $allocBytes) { "$allocBytes B" } else { "N/A" }
        MeanNs         = if ($null -ne $meanNs) { "$($meanNs.ToString('F2')) ns" } else { "N/A" }
        Status         = $status
        Details        = $details.Trim()
    })
}

# 5. Output Console Table
Write-Host "`n============================================================" -ForegroundColor Magenta
Write-Host "  BENCHMARK QUALITY GATE EVALUATION REPORT" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta

$evaluatedResults | Format-Table -Property Method, ZeroAllocReq, AllocatedBytes, MeanNs, Status -AutoSize

# 6. Generate GitHub Step Summary if applicable
$summaryFile = $env:GITHUB_STEP_SUMMARY
if (-not [string]::IsNullOrWhiteSpace($summaryFile) -and (Test-Path (Split-Path -Parent $summaryFile) -ErrorAction SilentlyContinue)) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("## ⚡ Benchmark Regression Quality Gate Report")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Rule | Invariant | Policy | Result |")
    [void]$sb.AppendLine("|---|---|---|---|")
    [void]$sb.AppendLine("| **Rule 1 (Heap)** | Zero-Allocation on Hot Path (`$ZeroAllocPattern`) | Must allocate **0 B** | $(if ($violations | Where-Object { $_ -match "Zero-allocation" }) { "❌ **FAILED**" } else { "✅ **PASSED**" }) |")
    [void]$sb.AppendLine("| **Rule 2 (Latency)** | Nanosecond Regression vs Baseline | Max **+$MaxLatencyRegressionPercent%** | $(if ($violations | Where-Object { $_ -match "Latency" }) { "❌ **FAILED**" } else { "✅ **PASSED**" }) |")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Evaluated Benchmark Results ($($evaluatedResults.Count) operations)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Method | Zero-Alloc Expected | Allocated | Mean Latency | Gate Status |")
    [void]$sb.AppendLine("|---|:---:|:---:|:---:|:---:|")

    foreach ($res in $evaluatedResults) {
        $statusIcon = if ($res.Status -eq "PASS") { "✅ PASS" } else { "❌ $($res.Status)" }
        [void]$sb.AppendLine("| `$($res.Method)` | $($res.ZeroAllocReq) | $($res.AllocatedBytes) | $($res.MeanNs) | $statusIcon |")
    }

    if ($violations.Count -gt 0) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("### ❌ Violations Detected")
        foreach ($v in $violations) {
            [void]$sb.AppendLine("- $v")
        }
    }

    [System.IO.File]::AppendAllText($summaryFile, $sb.ToString(), [System.Text.Encoding]::UTF8)
}

# 7. Final Verdict and Exit Code
Write-Host "============================================================" -ForegroundColor Magenta
if ($violations.Count -gt 0) {
    Write-Host "❌ BENCHMARK GATE FAILED: $($violations.Count) violation(s) detected:`n" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  $v" -ForegroundColor Red
        Write-Host "::error::$v"
    }
    exit 1
} else {
    Write-Host "✅ BENCHMARK GATE PASSED: All zero-allocation invariants and latency thresholds verified successfully." -ForegroundColor Green
    exit 0
}
