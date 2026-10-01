<#
.SYNOPSIS
    Dice en que estado quedo un equipo despues de configurar_equipo.ps1.

.DESCRIPTION
    No cambia nada: solo mira y reporta. Util cuando reinicias y el equipo
    arranca con el escritorio de siempre en vez de con DigBit.

    Ejecutar como administrador, con la sesion de un administrador (no con la
    cuenta del laboratorio).

.EXAMPLE
    .\diagnostico_equipo.ps1
    .\diagnostico_equipo.ps1 -Cuenta laboratorio
#>
[CmdletBinding()]
param(
    [string]$Cuenta = 'laboratorio',
    [string]$Destino = 'C:\DigBit',
    [string]$BaseDatos = 'C:\DigBitDB'
)

$ErrorActionPreference = 'Continue'

function Linea([string]$etiqueta, $valor, [bool]$bien) {
    $color = if ($bien) { 'Green' } else { 'Red' }
    Write-Host ('  {0,-34} ' -f ($etiqueta + ':')) -NoNewline
    Write-Host $valor -ForegroundColor $color
}

function Titulo([string]$texto) {
    Write-Host ''
    Write-Host "== $texto" -ForegroundColor Cyan
}

Titulo 'Quien soy'
Write-Host "  Equipo: $env:COMPUTERNAME"
Write-Host "  Sesion actual: $env:USERNAME"
$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$esAdmin = (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Linea 'Ventana elevada' $esAdmin $esAdmin
if (-not $esAdmin) {
    Write-Warning 'Sin elevar no se pueden leer todas las claves. Vuelve a abrir PowerShell como administrador.'
}

Titulo '0. Deep Freeze'
# Es lo primero porque explica casi todos los "lo configure y no quedo nada".
. (Join-Path $PSScriptRoot 'deepfreeze.ps1')
$deepFreeze = Obtener-EstadoDeepFreeze
Write-Host "  $($deepFreeze.Detalle)" -ForegroundColor $(if ($deepFreeze.Congelado -eq $true) { 'Yellow' } elseif ($deepFreeze.Presente) { 'Green' } else { 'Gray' })
$persistencia = Probar-PersistenciaDelDisco -Marca (Join-Path $Destino '.instalado')
Write-Host "  $($persistencia.Detalle)" -ForegroundColor $(if ($persistencia.Persiste) { 'Green' } else { 'Yellow' })
if ($deepFreeze.Congelado -eq $true) {
    Write-Host '     -> Con el disco congelado, cualquier cambio que hagas ahora desaparece al reiniciar.' -ForegroundColor Yellow
    Write-Host '        Para configurar: Mayus + doble clic en el icono de Deep Freeze, Boot Thawed, reiniciar.' -ForegroundColor Yellow
}

Titulo '1. Archivos'
$hayExe = Test-Path (Join-Path $Destino 'DigBit.exe')
Linea "$Destino\DigBit.exe" $hayExe $hayExe
if (-not $hayExe) {
    Write-Host '     -> configurar_equipo.ps1 NO llego a copiar nada.' -ForegroundColor Yellow
    Write-Host '        Seguramente se ejecuto con -SoloMostrar, que no cambia nada.' -ForegroundColor Yellow
}
if ($hayExe) {
    # Si Usuarios puede escribir aqui, el alumno sustituye DigBit.exe por un
    # cmd.exe y el siguiente inicio automatico le da un simbolo del sistema.
    # Se mira la ACL con Get-Acl y no el texto de icacls: icacls saca cada
    # permiso en su propio parentesis (Authenticated Users:(I)(M)) y ademas
    # traduce los nombres al idioma de Windows.
    $quienNoDebeEscribir = @('S-1-5-32-545', 'S-1-5-11', 'S-1-1-0', 'S-1-5-32-546')  # Usuarios, Autenticados, Todos, Invitados
    $derechosDeEscritura = [Security.AccessControl.FileSystemRights]'WriteData,AppendData,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'
    $reglasMalas = @((Get-Acl $Destino).Access | Where-Object {
        if ($_.AccessControlType -ne 'Allow') { return $false }
        try { $sidRegla = $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { return $false }
        ($quienNoDebeEscribir -contains $sidRegla) -and ($_.FileSystemRights -band $derechosDeEscritura)
    })
    $escribiblePorUsuarios = $reglasMalas.Count -gt 0
    Linea 'Carpeta protegida contra escritura' $(if ($escribiblePorUsuarios) { "NO: $($reglasMalas[0].IdentityReference) tiene $($reglasMalas[0].FileSystemRights)" } else { 'si, solo lectura para Usuarios' }) (-not $escribiblePorUsuarios)
    if ($escribiblePorUsuarios) {
        Write-Host "     -> El alumno puede sustituir DigBit.exe. Vuelve a ejecutar configurar_equipo.ps1," -ForegroundColor Yellow
        Write-Host "        o a mano: icacls $Destino /inheritance:r y conceder Usuarios solo RX." -ForegroundColor Yellow
    }
}

$hayVig = Test-Path (Join-Path $Destino 'Vigilante\DigBit.Vigilante.exe')
Linea 'Vigilante copiado' $hayVig $hayVig
$hayDatos = Test-Path (Join-Path $BaseDatos 'data')
Linea "$BaseDatos\data" $hayDatos $hayDatos

if ($hayExe) {
    $config = Join-Path $Destino 'DigBit.exe.config'
    if (Test-Path $config) {
        [xml]$xml = Get-Content -Raw $config
        $kiosco = ($xml.configuration.appSettings.add | Where-Object { $_.key -eq 'ModoKiosco' }).value
        $lab = ($xml.configuration.appSettings.add | Where-Object { $_.key -eq 'Laboratorio' }).value
        Linea 'ModoKiosco en el config' $kiosco ($kiosco -eq 'true')
        Linea 'Laboratorio en el config' $lab (-not [string]::IsNullOrWhiteSpace($lab))

        # Sin fijar no es un error: se usa el nombre de Windows. Pero si ese
        # nombre es un DESKTOP-XXXX, la bitacora no le dice nada a nadie.
        $maquina = ($xml.configuration.appSettings.add | Where-Object { $_.key -eq 'NumeroMaquina' }).value
        $nombreFeo = $env:COMPUTERNAME -match '^(DESKTOP|LAPTOP|WIN)-'
        if ([string]::IsNullOrWhiteSpace($maquina)) {
            Linea 'NumeroMaquina en el config' "sin fijar, se usara '$env:COMPUTERNAME'" (-not $nombreFeo)
            if ($nombreFeo) {
                Write-Host '     -> Ese nombre no le dice nada a quien tenga que ir a buscar la maquina.' -ForegroundColor Yellow
                Write-Host '        Vuelve a ejecutar configurar_equipo.ps1 con -NumeroMaquina "12".' -ForegroundColor Yellow
            }
        } else {
            Linea 'NumeroMaquina en el config' $maquina $true
        }

        # Los dos factores del acceso de emergencia. Se comprueba que esten los
        # DOS: con uno solo la opcion no funciona, y peor aun, el encargado se
        # entera el dia que la necesita.
        $emMemoria = ($xml.configuration.appSettings.add | Where-Object { $_.key -eq 'EmergenciaMemoria' }).value
        if ([string]::IsNullOrWhiteSpace($emMemoria)) {
            Linea 'Memoria de emergencia' 'sin autorizar (el boton avisara y no abrira nada)' $true
        }
        elseif ($emMemoria -like 'pbkdf2-*') {
            Linea 'Memoria de emergencia' 'autorizada (el numero de serie va hasheado)' $true
        }
        else {
            Linea 'Memoria de emergencia' 'guardada SIN hashear' $false
            Write-Host '     -> Deberia empezar por "pbkdf2-". Vuelve a ejecutar configurar_equipo.ps1' -ForegroundColor Yellow
            Write-Host '        con -AutorizarMemoria y la memoria conectada.' -ForegroundColor Yellow
        }

        # Lo que de verdad se pierde con Deep Freeze no es la instalacion (esa se
        # rehace), son las bitacoras que un alumno registro sin servidor.
        $carpetaDatos = ($xml.configuration.appSettings.add | Where-Object { $_.key -eq 'CarpetaDatos' }).value
        if ([string]::IsNullOrWhiteSpace($carpetaDatos)) {
            Linea 'CarpetaDatos en el config' 'sin fijar (se usa el perfil)' (-not $deepFreeze.Presente)
            if ($deepFreeze.Presente) {
                Write-Host '     -> Con Deep Freeze, el horario local y las bitacoras sin enviar se borran cada reinicio.' -ForegroundColor Yellow
                Write-Host '        Vuelve a ejecutar configurar_equipo.ps1 con -CarpetaDatos T:\DigBitDatos.' -ForegroundColor Yellow
            }
        }
        else {
            $escribible = $false
            try {
                $prueba = Join-Path $carpetaDatos '.diagnostico'
                [IO.File]::WriteAllText($prueba, 'x'); Remove-Item $prueba -Force
                $escribible = $true
            } catch { }
            Linea 'CarpetaDatos en el config' $carpetaDatos $escribible
            if (-not $escribible) {
                Write-Host '     -> No se puede escribir ahi. DigBit volvera al perfil y perdera los datos al reiniciar.' -ForegroundColor Yellow
            }

            $cola = Join-Path $carpetaDatos 'bitacoras_pendientes.xml'
            if (Test-Path $cola) {
                $cuantas = ([xml](Get-Content -Raw $cola)).SelectNodes('//BitacoraPendiente').Count
                Linea 'Bitacoras sin enviar al servidor' $cuantas ($cuantas -eq 0)
                if ($cuantas -gt 0) {
                    Write-Host "     -> Hay $cuantas bitacoras esperando servidor en $cola. NO borres esa carpeta." -ForegroundColor Yellow
                }
            }
        }
    }

    # Sin esto, un equipo con la PLANTILLA de connections.config pasaba el
    # diagnostico entero en verde y solo se descubria al arrancar DigBit.
    $conexion = Join-Path $Destino 'connections.config'
    if (Test-Path $conexion) {
        $cadena = ([xml](Get-Content -Raw $conexion)).connectionStrings.add | Where-Object { $_.name -eq 'DigBit' }
        if ($cadena) {
            # Tambien entre comillas: el instalador las pone si la contrasena lleva ';'.
            $texto = $cadena.connectionString -replace '(?i)(password\s*=\s*)("(?:[^"]|"")*"|''(?:[^'']|'''')*''|[^;]*)', '$1***'
            Linea 'connections.config' $texto ($texto -notmatch 'SERVIDOR|NOMBRE_BD|USUARIO')
            if ($texto -match 'SERVIDOR|NOMBRE_BD|USUARIO') {
                Write-Host '     -> es la PLANTILLA, no una cadena real. DigBit no podra conectar.' -ForegroundColor Yellow
            }
        } else {
            Linea 'connections.config' 'sin entrada DigBit' $false
        }
    } else {
        Linea 'connections.config' 'NO existe' $false
    }
}

Titulo '1b. Microsoft Defender'
# Esto es lo unico que separa a un equipo de arrancar en negro. DigBit.exe no
# esta firmado y se registra como shell de Winlogon, asi que el modelo de
# Defender lo marca como Trojan:Win32/Bearfoos.A!ml y se lo lleva a cuarentena.
# La exclusion la pone configurar_equipo.ps1 ANTES de copiar; si falta, el
# equipo puede arrancar bien hoy y amanecer sin DigBit cualquier otro dia.
if (-not (Get-Command Get-MpPreference -ErrorAction SilentlyContinue)) {
    Write-Host '  Este equipo no tiene los comandos de Microsoft Defender.' -ForegroundColor Yellow
    Write-Host '  Si usa otro antivirus, hay que excluir la carpeta en el suyo.' -ForegroundColor Yellow
}
else {
    $pref = Get-MpPreference
    $carpetaExcluida = @($pref.ExclusionPath) -contains $Destino
    Linea "Carpeta $Destino excluida" $(if ($carpetaExcluida) { 'si' } else { 'NO' }) $carpetaExcluida

    foreach ($exe in @((Join-Path $Destino 'DigBit.exe'), (Join-Path $Destino 'Vigilante\DigBit.Vigilante.exe'))) {
        $excluido = @($pref.ExclusionProcess) -contains $exe
        Linea "  proceso $(Split-Path $exe -Leaf)" $(if ($excluido) { 'si' } else { 'NO' }) $excluido
    }

    if (-not $carpetaExcluida) {
        Write-Host '     -> SIN ESTO el equipo puede amanecer sin DigBit.exe y arrancar en negro.' -ForegroundColor Yellow
        Write-Host '        Se arregla con deploy\permitir_en_defender.ps1 (tambien recupera lo' -ForegroundColor Yellow
        Write-Host '        que ya estuviera en cuarentena).' -ForegroundColor Yellow
    }

    # Si el antivirus esta apagado, hoy no hay riesgo... y tampoco proteccion.
    try {
        $estado = Get-MpComputerStatus
        Linea 'Proteccion en tiempo real' $(if ($estado.RealTimeProtectionEnabled) { 'encendida' } else { 'APAGADA' }) $true
        if (-not $estado.RealTimeProtectionEnabled) {
            Write-Host '     -> Con Defender apagado no hay cuarentena, pero tampoco antivirus.' -ForegroundColor Yellow
            Write-Host '        Y se vuelve a encender solo: la exclusion tiene que estar igual.' -ForegroundColor Yellow
        }
    } catch { }
}

Titulo '2. Cuenta del laboratorio'
$usuario = Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue
Linea "Cuenta '$Cuenta'" $(if ($usuario) { "existe, habilitada=$($usuario.Enabled)" } else { 'NO existe' }) ($null -ne $usuario)
$perfil = Join-Path $env:SystemDrive "Users\$Cuenta"
Linea 'Su perfil de Windows' $(if (Test-Path $perfil) { $perfil } else { 'NO creado' }) (Test-Path $perfil)

Titulo '3. Shell y directivas de esa cuenta'
if ($usuario) {
    $sid = $usuario.SID.Value
    $cargada = $false
    $raiz = "Registry::HKEY_USERS\$sid"
    if (-not (Test-Path $raiz)) {
        $ntuser = Join-Path $perfil 'NTUSER.DAT'
        if (Test-Path $ntuser) {
            & reg.exe load "HKU\$sid" $ntuser 2>&1 | Out-Null
            $cargada = Test-Path $raiz
        }
    }

    if (Test-Path $raiz) {
        $claveShell = "$raiz\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
        $shell = $null
        if (Test-Path $claveShell) { $shell = (Get-ItemProperty $claveShell -ErrorAction SilentlyContinue).Shell }
        Linea 'Shell de la cuenta' $(if ($shell) { $shell } else { 'NO configurado (usaria explorer)' }) ($null -ne $shell)

        # La clave donde vive Shell esta en la colmena del alumno, que por
        # defecto la hereda con control total y puede devolverla a explorer.exe.
        if (Test-Path $claveShell) {
            $aclShell = Get-Acl $claveShell
            # Se traduce a SID: IdentityReference suele venir como 'EQUIPO\cuenta',
            # asi que compararlo con el SID en texto no casaba NUNCA y esto salia
            # en verde aunque la cuenta tuviera control total.
            $suya = @()
            foreach ($regla in $aclShell.Access) {
                if ($regla.AccessControlType -ne 'Allow') { continue }
                if ($regla.RegistryRights -eq 'ReadKey') { continue }
                $sidRegla = $null
                try { $sidRegla = $regla.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { continue }
                if ($sidRegla -eq $sid) { $suya += $regla }
            }
            Linea 'No puede cambiar su propio Shell' $(if ($suya.Count) { "NO: tiene $($suya[0].RegistryRights)" } else { 'si, solo lectura' }) ($suya.Count -eq 0)
            if ($suya.Count) {
                Write-Host '     -> Puede poner Shell=explorer.exe y entrar al escritorio sin pasar por DigBit.' -ForegroundColor Yellow
                Write-Host '        Se arregla volviendo a ejecutar configurar_equipo.ps1.' -ForegroundColor Yellow
            }

            # Aunque solo tenga lectura, si es la DUENA de la clave puede
            # reescribir los permisos y recuperar el control.
            $duena = $false
            try { $duena = ([Security.Principal.NTAccount]$aclShell.Owner).Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid } catch { }
            Linea 'La clave Shell no es suya' $(if ($duena) { "NO: es la duena ($($aclShell.Owner))" } else { "duena: $($aclShell.Owner)" }) (-not $duena)
            if ($duena) {
                Write-Host '     -> Siendo duena puede devolverse los permisos. Deberia serlo SYSTEM.' -ForegroundColor Yellow
            }
        }

        $claveSistema = "$raiz\Software\Microsoft\Windows\CurrentVersion\Policies\System"
        $claveExplorer = "$raiz\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
        foreach ($par in @(
            @{ Clave = $claveSistema;  Nombre = 'DisableTaskMgr';   Que = 'Sin Administrador de tareas' },
            @{ Clave = $claveSistema;  Nombre = 'DisableLockWorkstation'; Que = 'Sin bloquear el equipo' },
            @{ Clave = $claveExplorer; Nombre = 'NoRun';            Que = 'Sin Ejecutar (Win+R)' },
            @{ Clave = $claveExplorer; Nombre = 'NoLogoff';         Que = 'Sin Cerrar sesion' },
            @{ Clave = $claveExplorer; Nombre = 'NoControlPanel';   Que = 'Sin Panel de control' })) {
            $valor = $null
            if (Test-Path $par.Clave) { $valor = (Get-ItemProperty $par.Clave -ErrorAction SilentlyContinue).($par.Nombre) }
            Linea $par.Que $(if ($null -ne $valor) { $valor } else { 'sin fijar' }) ($valor -eq 1)
        }

        # Los atajos de accesibilidad son texto, no DWORD, y el valor bueno no es
        # 1 sino el de por defecto sin el bit 4. 5 x Mayus abre un dialogo que
        # funciona hasta en una sesion sin shell.
        foreach ($par in @(
            @{ Clave = 'StickyKeys';        Bueno = '506'; Que = 'Sin atajo de Teclas especiales' },
            @{ Clave = 'ToggleKeys';        Bueno = '58';  Que = 'Sin atajo de Teclas alternancia' },
            @{ Clave = 'Keyboard Response'; Bueno = '122'; Que = 'Sin atajo de Teclas filtro' },
            @{ Clave = 'HighContrast';      Bueno = '122'; Que = 'Sin atajo de Contraste alto' })) {
            $ruta = "$raiz\Control Panel\Accessibility\$($par.Clave)"
            $valor = $null
            if (Test-Path $ruta) { $valor = (Get-ItemProperty $ruta -ErrorAction SilentlyContinue).Flags }
            Linea $par.Que $(if ($null -ne $valor) { $valor } else { 'sin fijar' }) ($valor -eq $par.Bueno)
        }
    } else {
        Write-Host '  No se pudo leer su rama del registro. Si esa cuenta tiene sesion abierta, cierrala.' -ForegroundColor Yellow
    }

    if ($cargada) {
        [gc]::Collect()
        Start-Sleep -Milliseconds 500
        & reg.exe unload "HKU\$sid" 2>&1 | Out-Null
    }
}

Titulo '4. Inicio de sesion automatico'
$winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$w = Get-ItemProperty $winlogon -ErrorAction SilentlyContinue
Linea 'AutoAdminLogon' $(if ($w.AutoAdminLogon) { $w.AutoAdminLogon } else { 'sin fijar' }) ($w.AutoAdminLogon -eq '1')
Linea 'DefaultUserName' $(if ($w.DefaultUserName) { $w.DefaultUserName } else { 'sin fijar' }) ($w.DefaultUserName -eq $Cuenta)
Linea 'DefaultDomainName' $(if ($w.DefaultDomainName) { $w.DefaultDomainName } else { 'sin fijar' }) (-not [string]::IsNullOrWhiteSpace($w.DefaultDomainName))
Linea 'DefaultPassword' $(if ($w.DefaultPassword) { '(fijada)' } else { 'sin fijar' }) (-not [string]::IsNullOrWhiteSpace($w.DefaultPassword))
Linea 'AutoRestartShell' $(if ($null -ne $w.AutoRestartShell) { $w.AutoRestartShell } else { 'sin fijar' }) ($w.AutoRestartShell -eq 1)
Linea 'ForceAutoLogon' $(if ($w.ForceAutoLogon) { $w.ForceAutoLogon } else { 'sin fijar' }) ($w.ForceAutoLogon -eq '1')
if ($w.ForceAutoLogon -ne '1') {
    Write-Host '     -> Sin esto el equipo se queda pidiendo contrasena cuando el vigilante cierra' -ForegroundColor Yellow
    Write-Host '        la sesion al terminar la clase. AutoAdminLogon solo actua al arrancar.' -ForegroundColor Yellow
}
$pwless = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device' -ErrorAction SilentlyContinue
Linea 'DevicePasswordLessBuildVersion' $(if ($null -ne $pwless.DevicePasswordLessBuildVersion) { $pwless.DevicePasswordLessBuildVersion } else { 'sin fijar' }) ($pwless.DevicePasswordLessBuildVersion -eq 0)

Titulo '5. Servicios'
foreach ($nombre in @('DigBitVigilante', 'DigBitMySQL')) {
    $s = Get-Service -Name $nombre -ErrorAction SilentlyContinue
    if ($s) {
        Linea $nombre "$($s.Status), arranque $($s.StartType)" ($s.Status -eq 'Running')
    } else {
        Linea $nombre 'NO instalado' $false
    }
}

# Mayus + Reiniciar lleva al modo seguro, que arranca con cmd.exe como shell y
# solo con los servicios de su lista. Sin esto, ahi no hay ni DigBit ni
# vigilante, y la sesion no se cierra nunca.
$enSeguro = @('Minimal', 'Network') | Where-Object {
    Test-Path "HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot\$_\DigBitVigilante"
}
Linea 'El vigilante arranca en modo seguro' $(if ($enSeguro.Count -eq 2) { 'si, Minimal y Network' } elseif ($enSeguro.Count) { "solo en $($enSeguro -join ', ')" } else { 'NO' }) ($enSeguro.Count -eq 2)
if ($enSeguro.Count -ne 2) {
    Write-Host '     -> Vuelve a ejecutar deploy\instalar_vigilante.ps1.' -ForegroundColor Yellow
}

$shellSeguro = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot' -ErrorAction SilentlyContinue).AlternateShell
Linea 'Shell del modo seguro' $(if ($shellSeguro) { $shellSeguro } else { 'sin fijar' }) $false
Write-Host '     -> Windows siempre pone cmd.exe aqui. Con la contrasena del inicio' -ForegroundColor Yellow
Write-Host '        automatico (que esta en claro en el registro) se puede entrar en' -ForegroundColor Yellow
Write-Host '        modo seguro y tener un simbolo del sistema sin DigBit. PENDIENTE.' -ForegroundColor Yellow

Titulo '6. Registros'
$dondeMirar = @(
    (Join-Path $perfil 'AppData\Local\DigBit\logs'),
    (Join-Path $env:ProgramData 'DigBit\logs'),
    (Join-Path $BaseDatos 'error.log'))
if ($carpetaDatos) { $dondeMirar = @((Join-Path $carpetaDatos 'logs')) + $dondeMirar }
foreach ($ruta in $dondeMirar) {
    if (Test-Path $ruta) {
        $ultimo = Get-ChildItem $ruta -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($ultimo) { Write-Host "  $($ultimo.FullName)  ($($ultimo.LastWriteTime))" } else { Write-Host "  $ruta" }
    } else {
        Write-Host "  (no hay) $ruta" -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host 'Si "C:\DigBit\DigBit.exe" sale en rojo, vuelve a ejecutar configurar_equipo.ps1 SIN -SoloMostrar.' -ForegroundColor Yellow
