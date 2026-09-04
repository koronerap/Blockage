<#
    Builds and installs the Android head.

    Android needs a toolchain the desktop build does not, and none of it is on PATH:

      * a user-local .NET SDK, because installing the android workload into the machine-wide SDK
        under Program Files needs elevation
      * a JDK, unzipped rather than installed, for the same reason
      * the Android SDK's own tools

    Rather than leave three environment variables to remember, this script sets them and runs the
    build. The desktop editor is untouched by all of it and still builds with a plain `dotnet build`.

      .\build-android.ps1                # build a sideloadable debug APK
      .\build-android.ps1 -Install       # ...and install it on the attached device or emulator
      .\build-android.ps1 -Run           # ...and launch it
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Run,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$dotnetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$env:DOTNET_ROOT = $dotnetRoot
$env:JAVA_HOME = Join-Path $env:LOCALAPPDATA 'Programs\jdk-17'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'

$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
$adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
$project = Join-Path $PSScriptRoot 'src\EditorApp.Mobile\EditorApp.Mobile.csproj'

foreach ($required in $dotnet, (Join-Path $env:JAVA_HOME 'bin\java.exe'), $adb) {
    if (-not (Test-Path $required)) {
        throw "Missing part of the Android toolchain: $required"
    }
}

# EmbedAssembliesIntoApk: a debug package normally leaves the assemblies out and expects an IDE to
# push them separately. This one is sideloaded, so it has to carry everything it needs.
& $dotnet build $project -c $Configuration -t:SignAndroidPackage -p:EmbedAssembliesIntoApk=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$apk = Join-Path $PSScriptRoot "src\EditorApp.Mobile\bin\$Configuration\net10.0-android36.0\com.blockage.editor-Signed.apk"
"APK: $apk ({0:N1} MB)" -f ((Get-Item $apk).Length / 1MB)

if ($Install -or $Run) {
    & $adb install -r $apk
    if ($LASTEXITCODE -ne 0) { throw 'Install failed.' }
}

if ($Run) {
    & $adb shell am force-stop com.blockage.editor
    & $adb shell monkey -p com.blockage.editor -c android.intent.category.LAUNCHER 1 | Out-Null
}
