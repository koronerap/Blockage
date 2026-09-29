# Checks Blockage's exports in the programs they are for (Fullreleaseplan 8.6).
#
#     .\tools\validate-exports.ps1            # Blender only: quick
#     .\tools\validate-exports.ps1 -Unity     # Unity as well: a throwaway project, a few minutes
#
# Builds the editor, has it write its sample level in every format (--export-to), then opens the
# OBJ, GLB, glTF and FBX in Blender and the FBX and OBJ in Unity, each in batch mode, and checks
# that what arrives is what left: every object, its texture, its size and place, the hierarchy,
# linked copies sharing one mesh, markers with their properties, lights and collision. With -Unity
# the Blockage Importer package (integrations/unity) is checked too, on the .vxlevel itself. Godot
# is looked for and, when it is not installed, said to be skipped. Exits with 1 on any failure.

param(
    [string]$Out = (Join-Path $env:TEMP 'blockage-validate'),
    [switch]$Unity
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$failed = $false

Write-Host '== Building the editor'
dotnet build (Join-Path $repo 'src\EditorApp\EditorApp.csproj') -c Release -v quiet -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
$exe = Join-Path $repo 'src\EditorApp\bin\Release\net10.0\Blockage.exe'

Write-Host "== Exporting the sample level to $Out"
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
& $exe "--export-to=$Out" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'The export failed.' }

# ---- Blender ------------------------------------------------------------------------------------

$blender = Get-ChildItem 'C:\Program Files\Blender Foundation' -Filter blender.exe -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if ($blender) {
    Write-Host "== Blender: $($blender.FullName)"
    & $blender.FullName -b --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'validate\blender_check.py') -- $Out |
        Where-Object { $_ -match '^(PASS|FAIL)|failure' } | Out-Host
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
else {
    Write-Host '== Blender: not found, skipped'
}

# ---- Unity --------------------------------------------------------------------------------------

if ($Unity) {
    $editor = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($editor) {
        Write-Host "== Unity: $($editor.FullName)"
        $project = Join-Path $Out 'UnityProject'
        & $editor.FullName -batchmode -nographics -createProject $project -quit -logFile (Join-Path $Out 'unity-create.log') | Out-Null
        $imported = New-Item -ItemType Directory -Force (Join-Path $project 'Assets\Imported')
        $scripts = New-Item -ItemType Directory -Force (Join-Path $project 'Assets\Editor')
        foreach ($file in 'sample.fbx', 'sample.png', 'sample.obj', 'sample.mtl', 'sample-obj.png') {
            Copy-Item (Join-Path $Out $file) $imported
        }

        Copy-Item (Join-Path $PSScriptRoot 'validate\unity\ValidateImport.cs') $scripts
        & $editor.FullName -batchmode -nographics -projectPath $project -executeMethod ValidateImport.Run -logFile (Join-Path $Out 'unity-validate.log') | Out-Null
        if ($LASTEXITCODE -ne 0) { $failed = $true }
        $result = Join-Path $project 'validate-result.txt'
        if (Test-Path $result) { Get-Content $result | Out-Host } else { Write-Host 'FAIL Unity wrote no result: see unity-validate.log'; $failed = $true }

        # The importer package, on the level file itself.
        Write-Host '== Unity: the Blockage Importer package'
        $manifestPath = Join-Path $project 'Packages\manifest.json'
        $packagePath = (Join-Path $repo 'integrations\unity\com.blockage.importer') -replace '\\', '/'
        $manifest = Get-Content $manifestPath -Raw
        $manifest = $manifest -replace '"dependencies": \{', ('"dependencies": {' + "`n    `"com.blockage.importer`": `"file:$packagePath`",")
        [IO.File]::WriteAllText($manifestPath, $manifest)
        $levels = New-Item -ItemType Directory -Force (Join-Path $project 'Assets\Levels')
        Copy-Item (Join-Path $Out 'sample.vxlevel') $levels
        Copy-Item (Join-Path $PSScriptRoot 'validate\unity\CheckVxLevel.cs') $scripts
        & $editor.FullName -batchmode -nographics -projectPath $project -executeMethod CheckVxLevel.Run -logFile (Join-Path $Out 'unity-vxlevel.log') | Out-Null
        if ($LASTEXITCODE -ne 0) { $failed = $true }
        $result = Join-Path $project 'vxlevel-result.txt'
        if (Test-Path $result) { Get-Content $result | Out-Host } else { Write-Host 'FAIL Unity wrote no result: see unity-vxlevel.log'; $failed = $true }
    }
    else {
        Write-Host '== Unity: not found, skipped'
    }
}

# ---- Godot --------------------------------------------------------------------------------------

$godot = Get-Command godot*, Godot* -ErrorAction SilentlyContinue | Select-Object -First 1
if ($godot) {
    Write-Host "== Godot: $($godot.Source) is installed, but has no check here yet: open $Out\sample.glb in it by hand"
}
else {
    Write-Host '== Godot: not found, skipped'
}

if ($failed) {
    Write-Host '== Some checks FAILED'
    exit 1
}

Write-Host '== Every check passed'
exit 0
