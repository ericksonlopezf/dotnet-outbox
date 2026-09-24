# Copyright © Erickson Lopez. MIT License.
# Unit tests for verify-benchmark-gate.ps1

$scriptPath = Join-Path $PSScriptRoot "verify-benchmark-gate.ps1"
$testTempDir = Join-Path $PSScriptRoot "temp-bench-test"

if (Test-Path $testTempDir) { Remove-Item -Path $testTempDir -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $testTempDir "reports") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $testTempDir "baseline") -Force | Out-Null

$reportsDir = Join-Path $testTempDir "reports"
$baselineDir = Join-Path $testTempDir "baseline"
$baselineFile = Join-Path $baselineDir "baseline.json"

function Run-Gate($repDir, $baseFile, [double]$threshold = 5.0) {
    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
    $pinfo.FileName = "powershell.exe"
    $pinfo.Arguments = "-ExecutionPolicy Bypass -File `"$scriptPath`" -ReportDir `"$repDir`" -BaselinePath `"$baseFile`" -MaxLatencyRegressionPercent $threshold"
    $pinfo.RedirectStandardOutput = $true
    $pinfo.RedirectStandardError = $true
    $pinfo.UseShellExecute = $false

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $pinfo
    $p.Start() | Out-Null
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return [PSCustomObject]@{
        ExitCode = $p.ExitCode
        Stdout   = $stdout
        Stderr   = $stderr
    }
}

Write-Host "Running tests for verify-benchmark-gate.ps1...`n" -ForegroundColor Cyan

# Test 1: No reports found (should exit 0 with warning)
{
    $emptyDir = Join-Path $testTempDir "empty"
    New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
    $res = Run-Gate $emptyDir $baselineFile
    if ($res.ExitCode -eq 0 -and $res.Stdout -match "No BenchmarkDotNet JSON reports") {
        Write-Host "✅ Test 1 Passed: Missing reports directory exits 0 with warning." -ForegroundColor Green
    } else {
        Write-Error "❌ Test 1 Failed: Expected exit 0 with warning, got exit $($res.ExitCode)"
    }
}

# Test 2: Clean 0B and within baseline
{
    $baselineData = @{
        Benchmarks = @{
            "Bind_Success_TState" = @{ MeanNs = 10.0; AllocatedBytes = 0 }
            "Map_Success_TState"  = @{ MeanNs = 5.0;  AllocatedBytes = 0 }
            "Success_NonGeneric"   = @{ MeanNs = 1.0;  AllocatedBytes = 0 }
        }
    } | ConvertTo-Json -Depth 5
    Set-Content -Path $baselineFile -Value $baselineData -Encoding utf8

    $reportData = @{
        Title = "CleanReport"
        Benchmarks = @(
            @{
                Method = "Bind_Success_TState"
                Statistics = @{ Mean = 10.2 } # +2% (<= 5%)
                Memory = @{ BytesAllocatedPerOperation = 0 }
            },
            @{
                Method = "Map_Success_TState"
                Statistics = @{ Mean = 4.9 } # -2%
                Memory = @{ BytesAllocatedPerOperation = 0 }
            },
            @{
                Method = "Success_NonGeneric"
                Statistics = @{ Mean = 1.03 } # +3%
                Memory = @{ BytesAllocatedPerOperation = 0 }
            }
        )
    } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $reportsDir "BenchmarkRun-clean-report.json") -Value $reportData -Encoding utf8

    $res = Run-Gate $reportsDir $baselineFile
    if ($res.ExitCode -eq 0 -and $res.Stdout -match "BENCHMARK GATE PASSED") {
        Write-Host "✅ Test 2 Passed: Clean 0B and valid latency passes gate." -ForegroundColor Green
    } else {
        Write-Error "❌ Test 2 Failed: Expected pass, got exit $($res.ExitCode). Stdout: $($res.Stdout)"
    }
}

# Test 3: Zero-Allocation violation (AllocatedBytes > 0 on hot path)
{
    $badAllocReport = @{
        Title = "BadAllocReport"
        Benchmarks = @(
            @{
                Method = "Bind_Success_TState"
                Statistics = @{ Mean = 10.0 }
                Memory = @{ BytesAllocatedPerOperation = 24 } # VIOLATION!
            }
        )
    } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $reportsDir "BenchmarkRun-clean-report.json") -Value $badAllocReport -Encoding utf8

    $res = Run-Gate $reportsDir $baselineFile
    if ($res.ExitCode -eq 1 -and $res.Stdout -match "Zero-allocation invariant VIOLATED") {
        Write-Host "✅ Test 3 Passed: Hot-path allocation (> 0 B) triggers gate failure." -ForegroundColor Green
    } else {
        Write-Error "❌ Test 3 Failed: Expected failure on allocation > 0, got exit $($res.ExitCode)"
    }
}

# Test 4: Latency regression violation (> 5% slower than baseline)
{
    $badLatencyReport = @{
        Title = "BadLatencyReport"
        Benchmarks = @(
            @{
                Method = "Success_NonGeneric"
                Statistics = @{ Mean = 1.15 } # +15% vs baseline 1.0 (threshold 5%)
                Memory = @{ BytesAllocatedPerOperation = 0 }
            }
        )
    } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $reportsDir "BenchmarkRun-clean-report.json") -Value $badLatencyReport -Encoding utf8

    $res = Run-Gate $reportsDir $baselineFile -threshold 5.0
    if ($res.ExitCode -eq 1 -and $res.Stdout -match "Latency regressed") {
        Write-Host "✅ Test 4 Passed: Latency regression (+15% > +5%) triggers gate failure." -ForegroundColor Green
    } else {
        Write-Error "❌ Test 4 Failed: Expected failure on latency regression, got exit $($res.ExitCode)"
    }
}

# Test 5: Domain-specific / baseline-declared ZeroAlloc method violation (non-Result method)
{
    $domainBaseline = @{
        Benchmarks = @{
            "CustomCrypto_Hash" = @{ MeanNs = 100.0; AllocatedBytes = 0; ZeroAlloc = $true }
        }
    } | ConvertTo-Json -Depth 5
    Set-Content -Path $baselineFile -Value $domainBaseline -Encoding utf8

    $domainReport = @{
        Title = "DomainReport"
        Benchmarks = @(
            @{
                Method = "CustomCrypto_Hash"
                Statistics = @{ Mean = 100.0 }
                Memory = @{ BytesAllocatedPerOperation = 16 } # VIOLATION!
            }
        )
    } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $reportsDir "BenchmarkRun-clean-report.json") -Value $domainReport -Encoding utf8

    $res = Run-Gate $reportsDir $baselineFile
    if ($res.ExitCode -eq 1 -and $res.Stdout -match "Zero-allocation invariant VIOLATED: Method 'CustomCrypto_Hash'") {
        Write-Host "✅ Test 5 Passed: Baseline-declared zero-alloc method triggers failure when allocating > 0 B." -ForegroundColor Green
    } else {
        Write-Error "❌ Test 5 Failed: Expected failure for CustomCrypto_Hash, got exit $($res.ExitCode)"
    }
}

# Cleanup
Remove-Item -Path $testTempDir -Recurse -Force
Write-Host "`nAll verify-benchmark-gate tests passed successfully!" -ForegroundColor Green
