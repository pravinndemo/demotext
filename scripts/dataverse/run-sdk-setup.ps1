param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [string]$SolutionName = "CTP_BulkData_Creation",
    [string]$SolutionPublisher = "voa",
    [string]$SolutionPrefix = "voa",

    [string]$RequestEntity,
    [string]$JobEntity,
    [switch]$NoPublish,
    [switch]$ColumnsOnly
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runnerDir = Join-Path $scriptDir ".sdk-runner"
$projectFile = Join-Path $runnerDir "BulkProcessor.DataverseSetup.csproj"
$programFile = Join-Path $runnerDir "Program.cs"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK is required. Install .NET SDK 8+ and retry."
}

if (-not (Get-Command pac -ErrorAction SilentlyContinue)) {
    throw "Power Platform CLI (pac) is required for environment auth checks."
}

$authOutput = pac auth list | Out-String
if ([string]::IsNullOrWhiteSpace($authOutput) -or $authOutput -notmatch "\*") {
    throw "No active pac auth profile found. Run: pac auth create --url $Url"
}

New-Item -ItemType Directory -Path $runnerDir -Force | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.2.3" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $projectFile -Encoding UTF8

Copy-Item (Join-Path $scriptDir "BulkProcessorDataverseSetup.cs") $programFile -Force

$runArgs = @(
    "run",
    "--project", $projectFile,
    "--",
    "--url", $Url,
    "--solution", $SolutionName,
    "--publisher", $SolutionPublisher,
    "--prefix", $SolutionPrefix
)

if ($RequestEntity) {
    $runArgs += @("--requestEntity", $RequestEntity)
}

if ($JobEntity) {
    $runArgs += @("--jobEntity", $JobEntity)
}

if ($NoPublish) {
    $runArgs += "--noPublish"
}

if ($ColumnsOnly) {
    $runArgs += "--columnsOnly"
}

Write-Host "Running Dataverse metadata setup..." -ForegroundColor Cyan
& dotnet @runArgs
