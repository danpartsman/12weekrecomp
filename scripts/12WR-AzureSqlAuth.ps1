Set-StrictMode -Version Latest

function Invoke-12WRAzText {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $oldPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = "Continue"
        $raw = & az @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Lines = @(
            $raw |
                ForEach-Object { [string]$_ }
        )
    }
}

function Get-12WRAzureAccountObject {
    $result =
        Invoke-12WRAzText `
            -Arguments @(
                "account",
                "show",
                "--output",
                "json",
                "--only-show-errors"
            )

    if ($result.ExitCode -ne 0) {
        return $null
    }

    $json =
        ($result.Lines -join [Environment]::NewLine).Trim()

    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    try {
        return $json | ConvertFrom-Json
    }
    catch {
        throw "Azure CLI account JSON could not be parsed."
    }
}

function Get-12WRAzureSqlAccessToken {
    param(
        [string]$ExpectedSubscriptionId =
            "14564a14-42f9-4d2e-8a0b-a7c844dfb5c0",

        [string]$ExpectedUser =
            "dlandon@lockedontech.com"
    )

    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI (az) is not installed or is not on PATH."
    }

    $account = Get-12WRAzureAccountObject

    if ($null -eq $account) {
        Write-Host "Azure CLI login is not cached. Signing in once..." -ForegroundColor Yellow
        & az login --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Azure CLI login failed."
        }
    }

    & az account set `
        --subscription $ExpectedSubscriptionId `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Could not select the expected 12WR TEST Azure subscription."
    }

    $account = Get-12WRAzureAccountObject
    if ($null -eq $account) {
        throw "Could not read the active Azure CLI account."
    }

    $subscriptionId = [string]$account.id
    $userName = [string]$account.user.name

    if ($subscriptionId -ne $ExpectedSubscriptionId) {
        throw "Active Azure subscription is not the expected 12WR TEST subscription."
    }

    if (-not [string]::Equals(
            $userName,
            $ExpectedUser,
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw @"
Azure CLI is signed in as a different identity.

Expected:
$ExpectedUser

Current:
$userName
"@
    }

    $tokenResult =
        Invoke-12WRAzText `
            -Arguments @(
                "account",
                "get-access-token",
                "--resource",
                "https://database.windows.net/",
                "--query",
                "accessToken",
                "--output",
                "tsv",
                "--only-show-errors"
            )

    if ($tokenResult.ExitCode -ne 0) {
        Write-Host "Cached Azure token needs refresh. Signing in once..." -ForegroundColor Yellow
        & az login --output none
        if ($LASTEXITCODE -ne 0) {
            throw "Azure CLI token refresh login failed."
        }

        & az account set `
            --subscription $ExpectedSubscriptionId `
            --only-show-errors

        if ($LASTEXITCODE -ne 0) {
            throw "Could not reselect the expected TEST subscription."
        }

        $tokenResult =
            Invoke-12WRAzText `
                -Arguments @(
                    "account",
                    "get-access-token",
                    "--resource",
                    "https://database.windows.net/",
                    "--query",
                    "accessToken",
                    "--output",
                    "tsv",
                    "--only-show-errors"
                )
    }

    if ($tokenResult.ExitCode -ne 0) {
        throw "Could not obtain an Azure SQL access token."
    }

    $token =
        (
            @(
                $tokenResult.Lines |
                    Where-Object {
                        -not [string]::IsNullOrWhiteSpace($_)
                    }
            ) |
                Select-Object -First 1
        ).Trim()

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Azure CLI returned an empty Azure SQL access token."
    }

    return $token
}
