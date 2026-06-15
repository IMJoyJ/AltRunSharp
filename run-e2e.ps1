# run-e2e.ps1
# E2E Test execution runner script

$ErrorActionPreference = "Stop"

Write-Host "=== Start E2E Test Automation Process ===" -ForegroundColor Cyan

# 0. Kill existing processes to release file locks
Write-Host "Step 0: Killing existing AltRunSharp and testhost processes to prevent compilation locks..." -ForegroundColor Yellow
Get-Process AltRunSharp -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force

# 1. Build project
Write-Host "Step 1: Building the entire solution..." -ForegroundColor Yellow
dotnet build "$PSScriptRoot\AltRunSharp.csproj" -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build AltRunSharp main application!"
    exit 1
}

dotnet build "$PSScriptRoot\tests\AltRunSharp.E2E\AltRunSharp.E2E.csproj" -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build AltRunSharp.E2E test project!"
    exit 1
}

# 2. Clean old data
Write-Host "Step 2: Cleaning old data and test remnants..." -ForegroundColor Yellow
$ConfigDir = Join-Path $PSScriptRoot "data"
if (Test-Path $ConfigDir) {
    Remove-Item -Path $ConfigDir -Recurse -Force -ErrorAction SilentlyContinue
}

$TestResultsDir = Join-Path $PSScriptRoot "tests\AltRunSharp.E2E\TestResults"
if (Test-Path $TestResultsDir) {
    Remove-Item -Path $TestResultsDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 3. Run E2E tests and generate TRX report
Write-Host "Step 3: Running E2E tests..." -ForegroundColor Yellow

$TestExitCode = 0
try {
    dotnet test "$PSScriptRoot\tests\AltRunSharp.E2E\AltRunSharp.E2E.csproj" -c Debug --logger "trx;LogFileName=e2e_results.trx" --no-build
    $TestExitCode = $LASTEXITCODE
} catch {
    # dotnet test returns non-zero when tests fail, which triggers Catch due to ErrorActionPreference="Stop"
    # We retrieve the exit code from $LASTEXITCODE
    $TestExitCode = $LASTEXITCODE
    if ($TestExitCode -eq 0) {
        $TestExitCode = 1 # Force non-zero if catch triggered but last exit code was somehow 0
    }
}

# 4. Result reporting
Write-Host "Step 4: E2E Test Run Finished." -ForegroundColor Cyan

$TrxPath = Join-Path $PSScriptRoot "tests\AltRunSharp.E2E\TestResults\e2e_results.trx"
if (-not (Test-Path $TrxPath)) {
    Write-Error "ERROR: E2E test execution failed to generate TRX report. The test host might have crashed!"
    exit 1
}

if ($TestExitCode -eq 0) {
    Write-Host "SUCCESS: All E2E tests passed successfully!" -ForegroundColor Green
} else {
    Write-Host "WARNING: Some tests did not pass (Expected behavior: unimplemented features threw Assert.Fail)." -ForegroundColor Yellow
    Write-Host "TRX report generated at: tests/AltRunSharp.E2E/TestResults/e2e_results.trx" -ForegroundColor Gray
}

exit $TestExitCode
