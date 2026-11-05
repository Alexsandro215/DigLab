<#
.SYNOPSIS
    Deja este equipo como si DigLab nunca se hubiera instalado.

.DESCRIPTION
    Lo que ya existia se queda corto para esto:
      - configurar_equipo.ps1 -Revertir quita el kiosco, pero conserva a
        proposito los archivos, la cuenta y la carpeta de datos.
      - preparar_bd.ps1 -Quitar borra el servicio de MySQL, pero deja
        C:\DigLabDB entera en el disco.
    Asi que despues de "desinstalar" seguian quedando rastros, y volver a
    instalar desde cero chocaba con ellos. Este guion cierra ese hueco.

    Lo que BORRA DATOS va detras de su propio conmutador. Sin conmutador no se
    toca: una carpeta de datos puede tener bitacoras que aun no llegaron al
    servidor, y la base puede tener el semestre entero.

.EXAMPLE
    # Ver que encontraria, sin tocar nada
    .\deploy\limpiar_equipo.ps1 -SoloMostrar

.EXAMPLE
    # Dejar el equipo limpio del todo, incluida la base
    .\deploy\limpiar_equipo.ps1 -Todo

.EXAMPLE
    # Una maquina de laboratorio: quitar tambien el kiosco y su cuenta
    .\deploy\limpiar_equipo.ps1 -Cuenta laboratorio -Todo
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    # Cuenta local del laboratorio, si este equipo tenia kiosco. Sin esto no se
    # toca nada de Winlogon: en el equipo del administrador el inicio de sesion
    # automatico puede ser suyo y no de DigLab.
    [string]$Cuenta,

    [string]$Destino = 'C:\DigLab',

    # Carpeta de datos, si se configuro con -CarpetaDatos. Si no se pasa se
    # miran igualmente las habituales.
    [string]$CarpetaDatos,

    [string]$DestinoBd = 'C:\DigLabDB',

    # Borra el servicio DigLabMySQL y C:\DigLabDB: se va la base entera.
    [switch]$BorrarBase,

    # Borra la carpeta de datos: se van las bitacoras sin enviar.
    [switch]$BorrarDatos,

    # Borra la cuenta local del laboratorio y su perfil. Necesita -Cuenta.
    [switch]$BorrarCuenta,

    # Las tres de arriba a la vez.
    [switch]$Todo,

    [switch]$SoloMostrar
)

$ErrorActionPreference = 'Stop'

# -Cuenta es el primer parametro posicional, asi que un conmutador mal pasado
# (por ejemplo al llamar a este guion con splatting de un array, que enlaza por
# posicion y no por nombre) aterriza aqui como si fuera un nombre de cuenta. El
# efecto es que -SoloMostrar deja de valer y se revierte de verdad. Ya paso.
if ($Cuenta -and $Cuenta.StartsWith('-')) {
    throw "'$Cuenta' parece un conmutador, no una cuenta. Pasa los conmutadores por su nombre: -SoloMostrar, -Todo, -BorrarBase..."
}

if ($Todo) {
    $BorrarBase = $true
    $BorrarDatos = $true
    if ($Cuenta) { $BorrarCuenta = $true }
}

function Paso([string]$texto) {
    Write-Host ''
    Write-Host "== $texto" -ForegroundColor Cyan
}

function Hacer([string]$descripcion, [scriptblock]$accion) {
    if ($SoloMostrar) {
        Write-Host "   [simulado] $descripcion" -ForegroundColor Yellow
        return
    }
    Write-Host "   $descripcion"
    & $accion
}

function NoHabia([string]$texto) {
    Write-Host "   $texto" -ForegroundColor DarkGray
}

$quedaron = New-Object Collections.Generic.List[string]

Paso "Limpiar DigLab de $env:COMPUTERNAME"
if ($SoloMostrar) { Write-Host '   (modo SoloMostrar: no se cambia nada)' -ForegroundColor Yellow }
if (-not $BorrarBase)  { Write-Host '   la base de datos NO se toca (falta -BorrarBase)' -ForegroundColor Yellow }
if (-not $BorrarDatos) { Write-Host '   la carpeta de datos NO se toca (falta -BorrarDatos)' -ForegroundColor Yellow }

# --- 1. Procesos --------------------------------------------------------------
# Antes que nada: mientras DigLab.exe corra, su carpeta no se puede borrar.
Paso '1. Detener lo que este corriendo'
$vivos = @(Get-Process -Name 'DigLab', 'DigLab.Vigilante' -ErrorAction SilentlyContinue)
if ($vivos.Count -eq 0) {
    NoHabia 'no habia ningun proceso de DigLab'
} else {
    foreach ($p in $vivos) {
        Hacer ("detener {0} (pid {1}, sesion {2})" -f $p.ProcessName, $p.Id, $p.SessionId) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }.GetNewClosure()
    }
}

# --- 2. Servicio del vigilante ------------------------------------------------
Paso '2. Servicio DigLabVigilante'
if (Get-Service -Name 'DigLabVigilante' -ErrorAction SilentlyContinue) {
    Hacer 'detener y borrar el servicio' {
        Stop-Service -Name 'DigLabVigilante' -Force -ErrorAction SilentlyContinue
        $null = & sc.exe delete 'DigLabVigilante'
    }
} else {
    NoHabia 'no estaba instalado'
}

# Aunque el servicio ya no este, sus entradas de modo seguro pueden seguir ahi:
# se registran en otra rama y sc.exe delete no las toca.
foreach ($rama in 'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal',
                  'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\Network') {
    $clave = Join-Path $rama 'DigLabVigilante'
    if (Test-Path $clave) {
        Hacer "quitar $clave" { Remove-Item -Path $clave -Recurse -Force }.GetNewClosure()
    }
}

# --- 3. Kiosco y cuenta de laboratorio ----------------------------------------
Paso '3. Kiosco e inicio de sesion automatico'
if (-not $Cuenta) {
    $winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    $w = Get-ItemProperty -Path $winlogon -ErrorAction SilentlyContinue
    if ($w.AutoAdminLogon -eq '1' -or $w.ForceAutoLogon) {
        Write-Host ("   hay inicio automatico para '{0}', pero sin -Cuenta no se toca." -f $w.DefaultUserName) -ForegroundColor Yellow
        Write-Host '   Si era de DigLab, repite con -Cuenta <esa cuenta>.' -ForegroundColor Yellow
        $quedaron.Add("inicio de sesion automatico de '$($w.DefaultUserName)'")
    } else {
        NoHabia 'sin -Cuenta no hay kiosco que deshacer, y no hay inicio automatico'
    }
} else {
    $revertir = Join-Path $PSScriptRoot 'configurar_equipo.ps1'
    if (-not (Test-Path $revertir)) {
        throw "No encuentro $revertir, que es quien sabe deshacer el kiosco."
    }
    # Se delega en -Revertir en lugar de repetir aqui las claves: son muchas y
    # duplicarlas garantiza que un dia se queden desparejadas.
    if ($SoloMostrar) {
        Write-Host "   [simulado] configurar_equipo.ps1 -Cuenta $Cuenta -Revertir (Defender, Winlogon y directivas de la cuenta)" -ForegroundColor Yellow
    } else {
        Write-Host "   deshacer el kiosco de '$Cuenta' con configurar_equipo.ps1 -Revertir"
        & $revertir -Cuenta $Cuenta -Revertir -Destino $Destino
    }

    if ($BorrarCuenta) {
        if (Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue) {
            $sid = (Get-LocalUser -Name $Cuenta).SID.Value
            $perfil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'" -ErrorAction SilentlyContinue
            Hacer "borrar la cuenta local '$Cuenta'" { Remove-LocalUser -Name $Cuenta }.GetNewClosure()
            if ($perfil) {
                Hacer "borrar su perfil ($($perfil.LocalPath))" { Remove-CimInstance -InputObject $perfil }.GetNewClosure()
            }
        } else {
            NoHabia "la cuenta '$Cuenta' no existe"
        }
    } elseif (Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue) {
        $quedaron.Add("la cuenta local '$Cuenta' (usa -BorrarCuenta)")
    }
}

# --- 4. Exclusiones de Defender -----------------------------------------------
# configurar_equipo.ps1 las anade porque DigLab.exe da un falso positivo. Si se
# quedan, apuntan a una carpeta que ya no existe: no hacen dano, pero ensucian.
Paso '4. Exclusiones de Microsoft Defender'
if (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) {
    $pref = Get-MpPreference
    $rutas = @($pref.ExclusionPath | Where-Object { $_ -like '*DigLab*' })
    $procesos = @($pref.ExclusionProcess | Where-Object { $_ -like '*DigLab*' })
    if ($rutas.Count -eq 0 -and $procesos.Count -eq 0) {
        NoHabia 'no habia ninguna que mencione DigLab'
    }
    foreach ($r in $rutas) {
        Hacer "quitar exclusion de carpeta $r" { Remove-MpPreference -ExclusionPath $r -ErrorAction SilentlyContinue }.GetNewClosure()
    }
    foreach ($p in $procesos) {
        Hacer "quitar exclusion de proceso $p" { Remove-MpPreference -ExclusionProcess $p -ErrorAction SilentlyContinue }.GetNewClosure()
    }
} else {
    NoHabia 'Defender no responde en este equipo'
}

# --- 5. Archivos de la aplicacion ---------------------------------------------
Paso "5. Carpeta de la aplicacion ($Destino)"
if (Test-Path $Destino) {
    Hacer "borrar $Destino" { Remove-Item -Path $Destino -Recurse -Force }
} else {
    NoHabia 'no existe'
}

# --- 6. Accesos directos ------------------------------------------------------
Paso '6. Accesos directos'
$sitios = @(
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [Environment]::GetFolderPath('CommonStartMenu'),
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('StartMenu')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
$enlaces = @(Get-ChildItem -Path $sitios -Filter '*DigLab*.lnk' -Recurse -ErrorAction SilentlyContinue)
if ($enlaces.Count -eq 0) {
    NoHabia 'no habia ninguno'
} else {
    foreach ($e in $enlaces) {
        Hacer "borrar $($e.FullName)" { Remove-Item -Path $e.FullName -Force }.GetNewClosure()
    }
}

# --- 7. Carpeta de datos ------------------------------------------------------
Paso '7. Carpeta de datos'
$candidatas = New-Object Collections.Generic.List[string]
if ($CarpetaDatos) { $candidatas.Add($CarpetaDatos) }
$candidatas.Add('C:\DigLabDatos')
$candidatas.Add((Join-Path $env:ProgramData 'DigLab'))
foreach ($perfil in Get-ChildItem 'C:\Users' -Directory -ErrorAction SilentlyContinue) {
    $candidatas.Add((Join-Path $perfil.FullName 'AppData\Local\DigLab'))
}
$encontradas = @($candidatas | Select-Object -Unique | Where-Object { Test-Path $_ })
if ($encontradas.Count -eq 0) {
    NoHabia 'no hay ninguna carpeta de datos'
} elseif (-not $BorrarDatos) {
    foreach ($c in $encontradas) {
        Write-Host "   se conserva $c" -ForegroundColor Yellow
        $quedaron.Add("$c (usa -BorrarDatos)")
    }
} else {
    foreach ($c in $encontradas) {
        Hacer "borrar $c" { Remove-Item -Path $c -Recurse -Force }.GetNewClosure()
    }
}

# --- 8. Base de datos ---------------------------------------------------------
Paso "8. Base de datos ($DestinoBd)"
$servicioBd = Get-Service -Name 'DigLabMySQL' -ErrorAction SilentlyContinue
if (-not $servicioBd -and -not (Test-Path $DestinoBd)) {
    NoHabia 'no hay base portatil en este equipo'
} elseif (-not $BorrarBase) {
    if ($servicioBd) { Write-Host "   se conserva el servicio DigLabMySQL ($($servicioBd.Status))" -ForegroundColor Yellow }
    if (Test-Path $DestinoBd) { Write-Host "   se conserva $DestinoBd" -ForegroundColor Yellow }
    $quedaron.Add("la base de datos en $DestinoBd (usa -BorrarBase)")
} else {
    if ($servicioBd) {
        Hacer 'detener y borrar el servicio DigLabMySQL' {
            Stop-Service -Name 'DigLabMySQL' -Force -ErrorAction SilentlyContinue
            $null = & sc.exe delete 'DigLabMySQL'
            Start-Sleep -Seconds 2
        }
    }
    # El servicio puede tardar en soltar los archivos, y un mysqld suelto (uno
    # arrancado a mano para probar) los bloquea igual. Sin esto, el borrado de
    # abajo falla a medias y deja la carpeta peor que como estaba.
    $sueltos = @(Get-Process -Name 'mysqld' -ErrorAction SilentlyContinue |
                 Where-Object { $_.Path -and $_.Path.StartsWith($DestinoBd, 'OrdinalIgnoreCase') })
    foreach ($p in $sueltos) {
        Hacer "detener mysqld suelto (pid $($p.Id)) de $DestinoBd" { Stop-Process -Id $p.Id -Force }.GetNewClosure()
    }
    if (Test-Path $DestinoBd) {
        Hacer "borrar $DestinoBd" { Remove-Item -Path $DestinoBd -Recurse -Force }
    }
    # Windows crea estas reglas solo la primera vez que mysqld pide el puerto, y
    # sobreviven al borrado del servicio apuntando a un .exe que ya no existe.
    $reglas = @(Get-NetFirewallRule -ErrorAction SilentlyContinue | ForEach-Object {
        $filtro = $_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
        if ($filtro -and $filtro.Program -and $filtro.Program.StartsWith($DestinoBd, 'OrdinalIgnoreCase')) { $_ }
    })
    foreach ($r in $reglas) {
        Hacer "quitar regla de cortafuegos '$($r.DisplayName)'" { Remove-NetFirewallRule -Name $r.Name }.GetNewClosure()
    }
    # Esta la crea abrir_bd_en_red.ps1 por su nombre, no por programa.
    foreach ($r in @(Get-NetFirewallRule -DisplayName 'DigLab MySQL' -ErrorAction SilentlyContinue)) {
        Hacer "quitar regla de cortafuegos '$($r.DisplayName)'" { Remove-NetFirewallRule -Name $r.Name }.GetNewClosure()
    }
}

# --- 9. Otros mysqld ----------------------------------------------------------
# No se matan: pueden ser de otra instalacion de MySQL que no es asunto nuestro.
$ajenos = @(Get-Process -Name 'mysqld' -ErrorAction SilentlyContinue |
            Where-Object { -not $_.Path -or -not $_.Path.StartsWith($DestinoBd, 'OrdinalIgnoreCase') })
if ($ajenos.Count -gt 0) {
    Paso '9. Otros MySQL en marcha (no se tocan)'
    foreach ($p in $ajenos) {
        $donde = if ($p.Path) { $p.Path } else { '(ruta no legible)' }
        Write-Host ("   pid {0}  {1}" -f $p.Id, $donde) -ForegroundColor Yellow
        $quedaron.Add("mysqld ajeno en marcha, pid $($p.Id)")
    }
}

Write-Host ''
if ($SoloMostrar) {
    Write-Host 'Eso es lo que haria. Vuelve a ejecutarlo sin -SoloMostrar.' -ForegroundColor Green
} else {
    Write-Host 'Equipo limpio.' -ForegroundColor Green
}
if ($quedaron.Count -gt 0) {
    Write-Host ''
    Write-Host 'A proposito, NO se quito:' -ForegroundColor Yellow
    foreach ($q in $quedaron) { Write-Host "  - $q" -ForegroundColor Yellow }
}
Write-Host ''
Write-Host 'Comprueba con: .\deploy\diagnostico_equipo.ps1'
