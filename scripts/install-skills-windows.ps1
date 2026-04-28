param(
  [string]$SkillName = "trope-cua",
  [switch]$Force
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Source = Join-Path $Root "Skills\$SkillName"

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
  throw "Skill source not found: $Source"
}

$targets = @()

$claudeSkills = Join-Path $HOME ".claude\skills"
if (Test-Path -LiteralPath $claudeSkills -PathType Container) {
  $targets += [pscustomobject]@{ Label = "Claude Code"; Path = $claudeSkills }
}

$agentsSkills = Join-Path $HOME ".agents\skills"
if ((Test-Path -LiteralPath (Join-Path $HOME ".codex") -PathType Container) -and -not (Test-Path -LiteralPath $agentsSkills -PathType Container)) {
  New-Item -ItemType Directory -Force -Path $agentsSkills | Out-Null
}
if (Test-Path -LiteralPath $agentsSkills -PathType Container) {
  $targets += [pscustomobject]@{ Label = "agent harness"; Path = $agentsSkills }
}

$openClawSkills = Join-Path $HOME ".openclaw\skills"
if (Test-Path -LiteralPath (Join-Path $HOME ".openclaw") -PathType Container) {
  New-Item -ItemType Directory -Force -Path $openClawSkills | Out-Null
}
if (Test-Path -LiteralPath $openClawSkills -PathType Container) {
  $targets += [pscustomobject]@{ Label = "OpenClaw"; Path = $openClawSkills }
}

if ($targets.Count -eq 0) {
  Write-Host "No known skill directories found. Skipping skill install."
  return
}

foreach ($target in $targets) {
  $destination = Join-Path $target.Path $SkillName
  if ((Test-Path -LiteralPath $destination) -and -not $Force) {
    Write-Host "$($target.Label) skill already exists at $destination (skipping)"
    continue
  }

  if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
  }

  Copy-Item -LiteralPath $Source -Destination $destination -Recurse
  Write-Host "Installed $($target.Label) skill at $destination"
}
