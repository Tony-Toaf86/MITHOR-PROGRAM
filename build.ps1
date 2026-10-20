<#
    Compila MITHOR.exe y MITHOR-Setup.exe con el compilador de C# incluido en
    .NET Framework 4.8. No requiere instalar el SDK de .NET ni Visual Studio.

    Uso:
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Clean
#>
[CmdletBinding()]
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

try {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $csc) {
        throw 'No se encontró csc.exe (parte de .NET Framework 4.x). Instala .NET Framework 4.8.'
    }
    $frameworkDir = Split-Path -Parent $csc
    Write-Host "Compilador : $csc"

    $dist = Join-Path $root 'dist'
    if ($Clean -and (Test-Path $dist)) {
        Remove-Item -Recurse -Force $dist
    }
    New-Item -ItemType Directory -Force $dist | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $root 'build') | Out-Null

    $referenceNames = @(
        'System.dll',
        'System.Core.dll',
        'System.Drawing.dll',
        'System.Windows.Forms.dll',
        'System.Management.dll',
        'System.Xml.dll',
        'System.ServiceProcess.dll'
    )
    $references = @()
    foreach ($name in $referenceNames) {
        $references += "/r:$frameworkDir\$name"
    }

    $icon = Join-Path $root 'assets\mithor.ico'
    $manifest = Join-Path $root 'src\Mithor.App\app.manifest'
    $config = Join-Path $root 'src\Mithor.App\MITHOR.exe.config'
    $appOutput = Join-Path $dist 'MITHOR.exe'

    function Invoke-Csc {
        param(
            [string[]]$Arguments,
            [string]$Name
        )
        $rsp = "build\$Name.rsp"
        $log = "build\$Name.log"
        [System.IO.File]::WriteAllLines(
            (Join-Path $root $rsp), $Arguments, (New-Object System.Text.UTF8Encoding($false)))
        cmd /c "`"$csc`" @$rsp > $log 2>&1"
        $code = $LASTEXITCODE
        if (Test-Path (Join-Path $root $log)) {
            $text = [System.IO.File]::ReadAllText((Join-Path $root $log), [System.Text.Encoding]::Default)
            if ($text.Trim().Length -gt 0) { Write-Host $text.TrimEnd() }
        }
        if ($code -ne 0) {
            throw "La compilación de $Name falló con código $code."
        }
    }

    $appSources = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notlike '*\Mithor.Setup\*' } |
        Sort-Object FullName |
        ForEach-Object { $_.FullName }
    if ($appSources.Count -eq 0) {
        throw 'No se encontraron archivos .cs para la aplicación.'
    }

    $appArgs = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:5',
        "/out:$appOutput", "/win32icon:$icon", "/win32manifest:$manifest", '/define:MITHOR_WINDOWS'
    ) + $references + $appSources
    Invoke-Csc -Arguments $appArgs -Name 'app'

    Copy-Item $config (Join-Path $dist 'MITHOR.exe.config') -Force

    $setupSources = Get-ChildItem -Path (Join-Path $root 'src\Mithor.Setup') -Recurse -Filter *.cs |
        Sort-Object FullName |
        ForEach-Object { $_.FullName }
    $setupOutput = Join-Path $dist 'MITHOR-Setup.exe'
    $setupManifest = Join-Path $root 'src\Mithor.Setup\setup.manifest'

    $setupArgs = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:5',
        "/out:$setupOutput", "/win32icon:$icon", "/win32manifest:$setupManifest",
        "/resource:$appOutput,payload.MITHOR.exe",
        "/resource:$config,payload.MITHOR.exe.config"
    ) + $references + $setupSources
    Invoke-Csc -Arguments $setupArgs -Name 'setup'

    Write-Host ''
    $appFile = Get-Item $appOutput
    $setupFile = Get-Item $setupOutput
    Write-Host ("MITHOR.exe        {0,10:N0} bytes  {1}" -f $appFile.Length, $appFile.FullName)
    Write-Host ("MITHOR-Setup.exe  {0,10:N0} bytes  {1}" -f $setupFile.Length, $setupFile.FullName)
    Write-Host ''
    Write-Host 'Instalación: ejecuta dist\MITHOR-Setup.exe como administrador.'
    Write-Host 'Prueba rápida: MITHOR.exe --list'
}
finally {
    Pop-Location
}
