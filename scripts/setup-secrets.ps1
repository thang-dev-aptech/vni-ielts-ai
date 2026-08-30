#!/usr/bin/env pwsh
# Bootstrap local secrets from the committed example template.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiDir = Join-Path $PSScriptRoot '..' 'backend' 'src' 'Vni.Ielts.Api' | Resolve-Path
$example = Join-Path $apiDir 'secrets.example.json'
$develop = Join-Path $apiDir 'secrets.develop.json'

if (-not (Test-Path $example)) {
    Write-Error "Missing $example"
}

if (Test-Path $develop) {
    Write-Host "secrets.develop.json already exists — leaving it untouched."
    Write-Host "  $develop"
    exit 0
}

Copy-Item $example $develop
Write-Host "Created secrets.develop.json from example."
Write-Host "  Edit: $develop"
Write-Host "  Guide: $(Join-Path $apiDir 'secrets.README.md')"
