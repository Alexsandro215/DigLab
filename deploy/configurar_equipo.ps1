<#
.SYNOPSIS
    Prepara un equipo del laboratorio para que DigBit sea el shell de Windows
    de la cuenta de laboratorio (fase 5).

.DESCRIPTION
    Ejecutar como administrador en cada equipo, con DigBit ya compilado en
    Release (.\build.cmd -Configuration Release -NoRun). Hace, en orden:

      1. Copia DigBit y el vigilante a la carpeta de destino (C:\DigBit).
      2. Activa el modo kiosco y el laboratorio del equipo en la configuracion
         desplegada, y escribe la cadena de conexion si se indica.
      3. Crea la cuenta local estandar del laboratorio si no existe, fija su
         contrasena y crea su perfil.
      4. Registra DigBit como shell SOLO para esa cuenta (Winlogon\Shell en su
         perfil). Los administradores siguen entrando con explorer.exe.
      5. Directivas para esa cuenta: sin Administrador de tareas, sin Ejecutar
         (Win+R), sin bloquear el equipo, sin cambiar contrasena, sin cerrar
         sesion desde Inicio ni desde Ctrl+Alt+Supr, sin protector de pantalla
         con contrasena.
      6. Inicio de sesion automatico con esa cuenta y sin pedir contrasena al
         despertar.
      7. Instala y arranca el servicio DigBit.Vigilante.

    Para entrar como administrador en un equipo ya configurado: Ctrl+Alt+Supr
    -> Cambiar de usuario, o mantener Mayus pulsada mientras arranca (salta el
    inicio automatico).

    -Revertir deshace 4, 5, 6 y 7 y deja los archivos y la cuenta.
    -SoloMostrar escribe lo que haria sin cambiar nada (no necesita administrador).

.EXAMPLE
    .\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena 'Lab-2026' -Laboratorio 'Laboratorio de Computo 1' -CadenaConexion 'Database=teschi_otru;Server=10.0.0.5;User Id=digbit;Password=xxx'
    .\configurar_equipo.ps1 -Cuenta laboratorio -Contrasena 'Lab-2026' -Laboratorio 'Laboratorio de Computo 1' -SoloMostrar
    .\configurar_equipo.ps1 -Cuenta laboratorio -Revertir
#>
[CmdletBinding()]
param(
    # Que clase de equipo se esta preparando:
    #   maquina  = equipo del laboratorio. Kiosco, cuenta propia, inicio de sesion
    #              automatico, vigilante y directivas. Es el valor por defecto.
    #   adm      = equipo del administrador. Misma aplicacion, SIN kiosco.
    #   maestro  = equipo de un profesor. Igual que adm, pero con el usuario de
    #              MySQL restringido en su cadena de conexion.
    # La diferencia entre los tres no es la aplicacion, que es la misma, sino
    # que en 'adm' y 'maestro' no se bloquea la pantalla ni se toca la cuenta.
    [ValidateSet('maquina', 'adm', 'maestro')]
    [string]$Modo = 'maquina',

    # Solo para -Modo maquina: la cuenta local del laboratorio.
    [string]$Cuenta,

    [string]$Contrasena,

    [string]$CadenaConexion,

    # Nombre EXACTO del laboratorio (laboratorios.nombre_laboratorio) en el que
    # esta este equipo. Un codigo de acceso solo vale en su laboratorio (fase 6).
    [string]$Laboratorio,

    # Como se identifica ESTA maquina en la bitacora: el numero pegado en el
    # equipo ('12', 'LAB1-12'...). Vacio: el nombre de Windows, que si las
    # maquinas se llaman DESKTOP-A7F3K2 no le sirve a quien vaya a buscar la que
    # falla. A diferencia de -Laboratorio, este valor es DISTINTO en cada equipo.
    [string]$NumeroMaquina,

    # Autoriza como llave de emergencia la memoria USB que este conectada en
    # este momento (tiene que haber EXACTAMENTE UNA). Con ella, cuando DigBit no
    # pueda validar ningun codigo, se abre el equipo sin bitacora. El numero de
    # serie se guarda hasheado, nunca en claro.
    [switch]$AutorizarMemoria,

    # Raiz del repositorio (o cualquier carpeta con DigBit\bin\Release y
    # DigBit.Vigilante\bin\Release dentro).
    [string]$Origen,

    [string]$Destino = 'C:\DigBit',

    # Carpeta donde DigBit deja lo que NO se puede perder al reiniciar: las
    # bitacoras que aun no llegaron al servidor, la copia local del horario y los
    # registros. En un equipo con Deep Freeze tiene que estar FUERA del disco
    # congelado: un ThawSpace (por convencion T:\DigBitDatos) o una particion sin
    # congelar. Vacio: el perfil del usuario, que Deep Freeze borra cada noche.
    [string]$CarpetaDatos,

    # Seguir aunque el equipo tenga Deep Freeze y no se indique -CarpetaDatos.
    # Solo tiene sentido si este equipo nunca se queda sin servidor.
    [switch]$AceptarSinPersistencia,

    [switch]$Revertir,

    [switch]$SoloMostrar
)

$ErrorActionPreference = 'Stop'

# --- Comprobaciones previas --------------------------------------------------
$identidad = [Security.Principal.WindowsIdentity]::GetCurrent()
$esAdmin = (New-Object Security.Principal.WindowsPrincipal $identidad).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin -and -not $SoloMostrar) {
    throw 'Ejecuta este guion como administrador (o con -SoloMostrar para ver que haria).'
}
$esKiosco = ($Modo -eq 'maquina')

if (-not $Revertir) {
    if ($esKiosco) {
        if (-not $Cuenta)      { throw 'Indica -Cuenta con el nombre de la cuenta local del laboratorio.' }
        if (-not $Contrasena)  { throw 'Indica -Contrasena: hace falta para la cuenta y para el inicio de sesion automatico.' }
        if (-not $Laboratorio) { throw "Indica -Laboratorio con el nombre exacto del laboratorio de este equipo (por ejemplo 'Laboratorio de Computo 1')." }
    }
    else {
        # En adm y maestro no hay cuenta de laboratorio que crear ni pantalla que
        # bloquear: lo unico imprescindible es saber a que base conectarse.
        foreach ($sobra in @(
            @{ Valor = $Cuenta;        Nombre = '-Cuenta' },
            @{ Valor = $Contrasena;    Nombre = '-Contrasena' },
            @{ Valor = $Laboratorio;   Nombre = '-Laboratorio' },
            @{ Valor = $NumeroMaquina; Nombre = '-NumeroMaquina' })) {
            if ($sobra.Valor) { Write-Warning "$($sobra.Nombre) no se usa con -Modo $Modo; se ignora." }
        }
    }
}
if ($Cuenta -match '[\\/@"]') {
    throw "El nombre de cuenta '$Cuenta' tiene que ser una cuenta LOCAL, sin dominio."
}

# OJO: $PSScriptRoot llega VACIO dentro de los valores por defecto del bloque
# param cuando el guion se lanza con "powershell -File <ruta>", que es como lo
# llama el instalador. En el cuerpo si esta, asi que se resuelve aqui. Con & o
# dot-source funcionaba de las dos formas, y por eso no se vio antes.
if (-not $Origen) { $Origen = Join-Path $PSScriptRoot '..' }
$Origen  = [IO.Path]::GetFullPath($Origen)
$Destino = [IO.Path]::GetFullPath($Destino)
if ($CarpetaDatos) { $CarpetaDatos = [IO.Path]::GetFullPath($CarpetaDatos) }

# Deep Freeze. Configurar un equipo CONGELADO es tiempo perdido: el guion termina
# en verde, el equipo se reinicia y no queda nada. Y aunque se descongele para
# instalar, los datos del dia a dia se siguen perdiendo si viven en C:.
. (Join-Path $PSScriptRoot 'deepfreeze.ps1')
$deepFreeze = Obtener-EstadoDeepFreeze
if ($deepFreeze.Presente) {
    Write-Host ''
    Write-Host "== Deep Freeze: $($deepFreeze.Detalle)" -ForegroundColor Yellow
}
if (-not $Revertir -and -not $SoloMostrar) {
    if ($deepFreeze.Congelado -eq $true) {
        throw 'Este equipo tiene Deep Freeze CONGELADO. Todo lo que escriba este guion desaparece en el siguiente reinicio. Ponlo en Thawed (Mayus + doble clic en el icono de Deep Freeze, Boot Thawed), reinicia, y vuelve a ejecutar esto.'
    }
    if ($deepFreeze.Presente -and $null -eq $deepFreeze.Congelado) {
        Write-Warning 'No se pudo confirmar que Deep Freeze este descongelado. Si estuviera congelado, esta instalacion se perderia al reiniciar.'
    }
    # Solo el kiosco guarda algo que no se puede perder (la cola de bitacoras sin
    # servidor). En adm y maestro lo local son registros y ajustes de ventana: si
    # se exigia la carpeta, el instalador no podia con esos equipos, porque no la
    # pide para ellos.
    if (-not $esKiosco -and $deepFreeze.Presente) {
        Write-Warning 'Con Deep Freeze, los registros locales de DigBit se pierden al reiniciar. Las bitacoras no: van al servidor.'
    }
    if ($esKiosco -and $deepFreeze.Presente -and -not $CarpetaDatos -and -not $AceptarSinPersistencia) {
        throw 'Este equipo tiene Deep Freeze y no indicaste -CarpetaDatos. Las bitacoras registradas sin servidor, el horario local y los registros viven en el perfil del usuario, que se borra en cada reinicio: se perderian datos de alumnos. Indica -CarpetaDatos con un ThawSpace (por ejemplo T:\DigBitDatos) o una particion sin congelar. Si este equipo nunca se queda sin servidor y aceptas perder los registros, repite con -AceptarSinPersistencia.'
    }
}

# La cuenta del kiosco NO puede ser administrador. Tras guardar la bitacora,
# SesionEquipo lanza explorer.exe con el token de ESA cuenta: si es admin, cada
# alumno se queda con un escritorio de administrador, en silencio, todo el
# semestre. Tampoco puede ser la cuenta con la que se ejecuta esto, o te quedas
# sin escritorio en mitad de la instalacion.
if (-not $Revertir -and $esKiosco) {
    # Estas comprobaciones son de la cuenta del alumno, asi que solo valen para
    # el kiosco. Antes corrian tambien en -Modo adm, donde $Cuenta esta vacio y
    # no hay ninguna cuenta que mirar: no servian de nada y ademas hacian
    # depender al equipo del administrador de un modulo que puede no estar.
    if ($Cuenta -eq $env:USERNAME) {
        throw "No apliques el kiosco sobre '$Cuenta': es la cuenta con la que estas ejecutando esto. Crea una cuenta estandar aparte."
    }

    # Get-LocalUser vive en el modulo LocalAccounts, que PowerShell normalmente
    # autocarga solo. En el PowerShell que abre el instalador no lo hizo, y la
    # instalacion murio con "no se reconoce el termino Get-LocalUser", que no
    # dice nada. El autocargado depende de PSModulePath; cargar el modulo por su
    # ruta fija no depende de nada.
    if (-not (Get-Command Get-LocalUser -ErrorAction SilentlyContinue)) {
        $moduloCuentas = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules\Microsoft.PowerShell.LocalAccounts'
        if (Test-Path $moduloCuentas) {
            Import-Module $moduloCuentas -ErrorAction SilentlyContinue
        }
    }
    if (-not (Get-Command Get-LocalUser -ErrorAction SilentlyContinue)) {
        throw @"
No encuentro Get-LocalUser, que hace falta para crear la cuenta del alumno.
Viene con Windows, en el modulo Microsoft.PowerShell.LocalAccounts, y no se ha
podido cargar ni solo ni a mano.

Ejecuta esto mismo desde un PowerShell de administrador abierto a mano:
    $env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe

Si hace falta depurarlo, PSModulePath aqui vale:
$env:PSModulePath
"@
    }

    $cuentaExistente = Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue
    if ($cuentaExistente) {
        try {
            $administradores = @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop)
            if ($administradores | Where-Object { $_.SID.Value -eq $cuentaExistente.SID.Value }) {
                throw "La cuenta '$Cuenta' es administrador. El kiosco le entrega el escritorio al alumno con su token, asi que cada alumno tendria permisos de administrador. Usa una cuenta estandar."
            }
        }
        catch [Microsoft.PowerShell.Commands.MemberNotFoundException] {
            # El grupo tiene SIDs huerfanos y el cmdlet se queja. No es motivo
            # para bloquear la instalacion, pero si para avisar.
            Write-Warning "No se pudo comprobar si '$Cuenta' es administrador. Asegurate de que NO lo sea."
        }
    }

}

# Esta si vale para los tres modos: el equipo se queda sin base de datos si se
# copia la plantilla encima del connections.config bueno y no se indica uno.
if (-not $Revertir) {
    if (-not $CadenaConexion -and -not (Test-Path (Join-Path $Destino 'connections.config'))) {
        throw "Este equipo no tiene connections.config y no indicaste -CadenaConexion: quedaria sin base de datos."
    }
}

$nombreServicio = 'DigBitVigilante'
$claveWinlogon  = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
$clavePasswordLess = 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device'
$exeDigBit      = Join-Path $Destino 'DigBit.exe'
$exeVigilante   = Join-Path $Destino 'Vigilante\DigBit.Vigilante.exe'
$instalador     = Join-Path $PSScriptRoot 'instalar_vigilante.ps1'

# Lo que se escribe en el perfil de la cuenta (rutas relativas a su HKU\<SID>).
$valoresUsuario = @(
    @{ Clave = 'Software\Microsoft\Windows NT\CurrentVersion\Winlogon';           Nombre = 'Shell';                 Tipo = 'REG_SZ';    Valor = $exeDigBit; Que = 'DigBit como shell de la cuenta' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\System';        Nombre = 'DisableTaskMgr';        Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin Administrador de tareas' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\System';        Nombre = 'DisableLockWorkstation'; Tipo = 'REG_DWORD'; Valor = '1';       Que = 'sin bloquear el equipo' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\System';        Nombre = 'DisableChangePassword'; Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin cambiar contrasena' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer';      Nombre = 'NoRun';                 Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin Ejecutar (Win+R)' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer';      Nombre = 'NoLogoff';              Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin Cerrar sesion en Ctrl+Alt+Supr' }
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer';      Nombre = 'StartMenuLogOff';       Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin Cerrar sesion en Inicio' }
    @{ Clave = 'Software\Policies\Microsoft\Windows\Control Panel\Desktop';        Nombre = 'ScreenSaveActive';      Tipo = 'REG_SZ';    Valor = '0';        Que = 'sin protector de pantalla' }
    @{ Clave = 'Software\Policies\Microsoft\Windows\Control Panel\Desktop';        Nombre = 'ScreenSaverIsSecure';   Tipo = 'REG_SZ';    Valor = '0';        Que = 'protector sin contrasena' }

    # Atajos de accesibilidad. Cinco pulsaciones de Mayus abren el dialogo de
    # Teclas especiales, que lo lanza Winlogon y aparece INCLUSO en una sesion
    # sin shell; desde su enlace se llega al Centro de accesibilidad y de ahi al
    # Panel de control. Quitar el bit 4 (HOTKEYACTIVE) deja la funcion disponible
    # desde Configuracion pero mata el atajo. Valores por defecto 510/62/126/126.
    @{ Clave = 'Control Panel\Accessibility\StickyKeys';                           Nombre = 'Flags';                 Tipo = 'REG_SZ';    Valor = '506';      Que = 'sin atajo de Teclas especiales (5 x Mayus)' }
    @{ Clave = 'Control Panel\Accessibility\ToggleKeys';                           Nombre = 'Flags';                 Tipo = 'REG_SZ';    Valor = '58';       Que = 'sin atajo de Teclas de alternancia' }
    @{ Clave = 'Control Panel\Accessibility\Keyboard Response';                    Nombre = 'Flags';                 Tipo = 'REG_SZ';    Valor = '122';      Que = 'sin atajo de Teclas filtro' }
    @{ Clave = 'Control Panel\Accessibility\HighContrast';                         Nombre = 'Flags';                 Tipo = 'REG_SZ';    Valor = '122';      Que = 'sin atajo de Contraste alto' }

    # Cierra la puerta de atras del dialogo de accesibilidad, y de paso el resto
    # del Panel de control. OJO: esto tambien afecta al escritorio que se le
    # entrega al alumno tras la bitacora, no solo al kiosco.
    @{ Clave = 'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer';      Nombre = 'NoControlPanel';        Tipo = 'REG_DWORD'; Valor = '1';        Que = 'sin Panel de control ni Configuracion' }
)

# --- Ayudantes ---------------------------------------------------------------
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

# reg.exe en vez del proveedor de PowerShell: no deja descriptores abiertos
# sobre el subarbol cargado y permite descargarlo despues.
function Reg([string[]]$argumentos) {
    $null = & reg.exe @argumentos
    if ($LASTEXITCODE -ne 0) {
        throw "reg.exe $($argumentos -join ' ') termino con codigo $LASTEXITCODE."
    }
}

function RegBorrarValor([string]$clave, [string]$nombre) {
    # reg.exe devuelve 1 y escribe en stderr si el valor no existe; eso no es un
    # error al revertir. En PowerShell 5.1 redirigir stderr de un programa nativo
    # con la preferencia en 'Stop' lanza excepcion, por eso se relaja aqui.
    $ErrorActionPreference = 'Continue'
    $null = & reg.exe delete $clave /v $nombre /f 2>$null
}

function ObtenerSid {
    return (Get-LocalUser -Name $Cuenta -ErrorAction Stop).SID.Value
}

function ObtenerPerfil([string]$sid) {
    $lista = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
    if (-not (Test-Path $lista)) {
        return $null
    }
    return (Get-ItemProperty $lista).ProfileImagePath
}

# Raiz del registro de la cuenta: su HKU\<SID> si tiene sesion abierta, o el
# NTUSER.DAT cargado a mano en HKU\DigBitLab si no.
$script:raizUsuario = $null
$script:hiveCargado = $false

function AbrirRegistroCuenta {
    $sid = ObtenerSid
    if (Test-Path "Registry::HKEY_USERS\$sid") {
        $script:raizUsuario = "HKU\$sid"
        return
    }

    $perfil = ObtenerPerfil $sid
    if (-not $perfil) {
        throw "La cuenta '$Cuenta' no tiene perfil todavia; no se puede escribir su registro."
    }
    $ntuser = Join-Path $perfil 'NTUSER.DAT'
    if (-not (Test-Path $ntuser)) {
        throw "No se encontro $ntuser."
    }

    Reg @('load', 'HKU\DigBitLab', $ntuser)
    $script:raizUsuario = 'HKU\DigBitLab'
    $script:hiveCargado = $true
}

function CerrarRegistroCuenta {
    if ($script:hiveCargado) {
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        Reg @('unload', 'HKU\DigBitLab')
        $script:hiveCargado = $false
    }
    $script:raizUsuario = $null
}

function DescribirRaiz {
    if ($script:raizUsuario) {
        return $script:raizUsuario
    }
    return "HKU\<SID de $Cuenta>"
}

# =============================================================================
if ($Revertir) {
    Paso "Revertir la configuracion de laboratorio de '$Cuenta' en $env:COMPUTERNAME"

    Hacer 'Quitar el servicio DigBit.Vigilante' {
        if (Get-Service -Name $nombreServicio -ErrorAction SilentlyContinue) {
            & $instalador -Desinstalar
        }
    }

    Hacer "Quitar la exclusion de $Destino en Microsoft Defender" {
        if (Get-Command Remove-MpPreference -ErrorAction SilentlyContinue) {
            foreach ($exe in @((Join-Path $Destino 'DigBit.exe'), $exeVigilante)) {
                try { Remove-MpPreference -ExclusionProcess $exe -ErrorAction Stop } catch { }
            }
            try { Remove-MpPreference -ExclusionPath $Destino -ErrorAction Stop } catch { }
        }
    }

    Hacer 'Apagar el inicio de sesion automatico y borrar la contrasena guardada' {
        Reg @('add', $claveWinlogon, '/v', 'AutoAdminLogon', '/t', 'REG_SZ', '/d', '0', '/f')
        RegBorrarValor $claveWinlogon 'DefaultPassword'
        RegBorrarValor $claveWinlogon 'ForceAutoLogon'
    }

    Hacer 'Volver a pedir contrasena al despertar' {
        & powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 1 | Out-Null
        & powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 1 | Out-Null
        & powercfg.exe /SETACTIVE SCHEME_CURRENT | Out-Null
    }

    if (-not (Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue)) {
        Write-Warning "La cuenta '$Cuenta' no existe; no hay shell ni directivas que quitar."
    }
    else {
        if (-not $SoloMostrar) { AbrirRegistroCuenta }
        try {
            foreach ($v in $valoresUsuario) {
                Hacer "Quitar $($v.Que) ($(DescribirRaiz)\$($v.Clave) -> $($v.Nombre))" {
                    RegBorrarValor "$($script:raizUsuario)\$($v.Clave)" $v.Nombre
                }
            }
        }
        finally {
            CerrarRegistroCuenta
        }
    }

    Write-Host ''
    Write-Host "Listo. La cuenta '$Cuenta' y los archivos de $Destino se conservan. Reinicia el equipo." -ForegroundColor Green
    if ($CarpetaDatos) {
        Write-Host "No se toca ${CarpetaDatos}: ahi pueden quedar bitacoras sin enviar al servidor." -ForegroundColor Yellow
    }
    return
}

# =============================================================================
Paso $(if ($esKiosco) { "Preparar $env:COMPUTERNAME como equipo de laboratorio con la cuenta '$Cuenta'" }
       else           { "Preparar $env:COMPUTERNAME como equipo de $Modo (sin kiosco)" })
if ($SoloMostrar) {
    Write-Host '   (modo SoloMostrar: no se cambia nada)' -ForegroundColor Yellow
}

# --- 1. Archivos ---------------------------------------------------------------
Paso "1. Archivos -> $Destino"
$origenDigBit    = Join-Path $Origen 'DigBit\bin\Release'
$origenVigilante = Join-Path $Origen 'DigBit.Vigilante\bin\Release'
foreach ($carpeta in @($origenDigBit, $origenVigilante)) {
    if (-not (Test-Path (Join-Path $carpeta '*.exe'))) {
        $mensaje = "No hay compilacion Release en $carpeta. Ejecuta .\build.cmd -Configuration Release -NoRun o indica -Origen."
        if ($SoloMostrar) { Write-Warning $mensaje } else { throw $mensaje }
    }
}

Hacer 'Detener DigBit y el vigilante si estan corriendo' {
    Get-Process -Name 'DigBit', 'DigBit.Vigilante' -ErrorAction SilentlyContinue | Stop-Process -Force
    $servicio = Get-Service -Name $nombreServicio -ErrorAction SilentlyContinue
    if ($servicio -and $servicio.Status -ne 'Stopped') {
        # Si el servicio acaba de arrancar esta en StartPending, y Stop-Service
        # falla por tiempo de espera aunque acabe parandose solo. Sin reintento,
        # eso abortaba el despliegue entero por nada.
        for ($intento = 1; $intento -le 3; $intento++) {
            try {
                Stop-Service -Name $nombreServicio -Force -ErrorAction Stop
                break
            }
            catch {
                Write-Host "   el servicio no se detuvo al intento $intento; esperando..." -ForegroundColor DarkGray
                Start-Sleep -Seconds 3
            }
        }
        $servicio.Refresh()
        if ($servicio.Status -ne 'Stopped') {
            throw "No se pudo detener el servicio $nombreServicio (sigue en '$($servicio.Status)'). Paralo a mano con 'Stop-Service $nombreServicio -Force' y vuelve a ejecutar esto."
        }
    }
}

# La carpeta de compilacion trae un connections.config de PLANTILLA, con
# "Server=SERVIDOR". Si se copia encima y no se indica -CadenaConexion, el equipo
# se queda sin base de datos y el guion termina en verde igualmente. Se guarda
# antes y se repone despues si no hay una cadena nueva.
$rutaConexion = Join-Path $Destino 'connections.config'
$conexionGuardada = $null
if (Test-Path $rutaConexion) {
    $conexionGuardada = Get-Content -Raw $rutaConexion
}

# Defender detecta a DigBit como "Trojan:Win32/Bearfoos.A!ml" y lo pone en
# cuarentena. Es un falso positivo del modelo de aprendizaje automatico, y lo que
# lo dispara es lo que hacemos: un ejecutable SIN FIRMAR que se registra como
# shell de Winlogon y cierra sesiones. Se excluye ANTES de copiar, o se lleva el
# archivo recien puesto. Es una concesion, no la solucion: lo correcto es firmar
# el ejecutable (ver deploy\permitir_en_defender.ps1).
if (Get-Command Add-MpPreference -ErrorAction SilentlyContinue) {
    Hacer "Excluir $Destino en Microsoft Defender (falso positivo conocido)" {
        Add-MpPreference -ExclusionPath $Destino
        Add-MpPreference -ExclusionProcess (Join-Path $Destino 'DigBit.exe')
        Add-MpPreference -ExclusionProcess $exeVigilante
    }
}
else {
    Write-Warning 'Sin comandos de Microsoft Defender en este equipo. Si usa otro antivirus, excluye la carpeta en el suyo o pondra DigBit en cuarentena.'
}

Hacer "Copiar DigBit desde $origenDigBit" {
    New-Item -ItemType Directory -Force $Destino | Out-Null
    Copy-Item (Join-Path $origenDigBit '*') $Destino -Recurse -Force
}

Hacer "Copiar el vigilante desde $origenVigilante" {
    New-Item -ItemType Directory -Force (Split-Path $exeVigilante) | Out-Null
    Copy-Item (Join-Path $origenVigilante '*') (Split-Path $exeVigilante) -Recurse -Force
}

# --- 2. Configuracion desplegada -----------------------------------------------
Paso '2. Configuracion desplegada'
$config          = Join-Path $Destino 'DigBit.exe.config'
$configVigilante = Join-Path $Destino 'Vigilante\DigBit.Vigilante.exe.config'

# Escribe (o crea) claves de appSettings sin tocar el resto del archivo.
function EscribirAjustes([string]$archivo, [hashtable[]]$ajustes) {
    [xml]$xml = Get-Content -Raw $archivo
    if (-not $xml.configuration.appSettings) {
        $xml.configuration.AppendChild($xml.CreateElement('appSettings')) | Out-Null
    }
    foreach ($par in $ajustes) {
        $nodo = $xml.configuration.appSettings.add | Where-Object { $_.key -eq $par.Clave }
        if (-not $nodo) {
            $nodo = $xml.CreateElement('add')
            $nodo.SetAttribute('key', $par.Clave)
            $xml.configuration.appSettings.AppendChild($nodo) | Out-Null
        }
        $nodo.SetAttribute('value', $par.Valor)
    }
    $xml.Save($archivo)
}

# La carpeta de datos tiene que existir y poder escribirla la cuenta del
# laboratorio (usuario estandar) y el vigilante (LocalSystem). Se usan SIDs y no
# nombres porque en Windows en espanol el grupo se llama 'Usuarios'.
if ($CarpetaDatos) {
    Hacer "Crear $CarpetaDatos y darle permiso de escritura" {
        New-Item -ItemType Directory -Force $CarpetaDatos | Out-Null
        $acl = Get-Acl $CarpetaDatos
        foreach ($par in @(
            @{ Sid = 'S-1-5-32-545'; Permiso = 'Modify' }       # BUILTIN\Usuarios
            @{ Sid = 'S-1-5-18';     Permiso = 'FullControl' }  # SYSTEM, que es quien corre el vigilante
        )) {
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
                (New-Object Security.Principal.SecurityIdentifier $par.Sid),
                $par.Permiso, 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        }
        Set-Acl $CarpetaDatos $acl
    }
}

# CarpetaDatos solo se escribe si se indico. Si no, una reinstalacion sin ese
# parametro dejaria el equipo apuntando otra vez al perfil, en silencio, y las
# bitacoras pendientes volverian a perderse cada noche.
$ajustesDigBit = @(
    @{ Clave = 'ModoKiosco';  Valor = $(if ($esKiosco) { 'true' } else { 'false' }) }
    @{ Clave = 'Laboratorio'; Valor = $Laboratorio }
)
if ($CarpetaDatos)  { $ajustesDigBit += @{ Clave = 'CarpetaDatos';  Valor = $CarpetaDatos } }
if ($NumeroMaquina) { $ajustesDigBit += @{ Clave = 'NumeroMaquina'; Valor = $NumeroMaquina } }

Hacer "$(($ajustesDigBit | ForEach-Object { "$($_.Clave)='$($_.Valor)'" }) -join ', ') en $config" {
    EscribirAjustes $config $ajustesDigBit
}

if ($CarpetaDatos) {
    Hacer "CarpetaDatos='$CarpetaDatos' en los dos config (DigBit y vigilante)" {
        EscribirAjustes $configVigilante @(@{ Clave = 'CarpetaDatos'; Valor = $CarpetaDatos })
    }
}

if ($CadenaConexion) {
    Hacer "Escribir $rutaConexion" {
        $cadenaXml = [Security.SecurityElement]::Escape($CadenaConexion)
        $texto = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n" +
                 "<connectionStrings>`r`n" +
                 "  <add name=`"DigBit`" connectionString=`"$cadenaXml`" providerName=`"MySql.Data.MySqlClient`" />`r`n" +
                 "</connectionStrings>`r`n"
        [IO.File]::WriteAllText($rutaConexion, $texto, (New-Object Text.UTF8Encoding $false))
    }
}
elseif ($conexionGuardada) {
    Hacer "Reponer el connections.config que ya tenia el equipo" {
        [IO.File]::WriteAllText($rutaConexion, $conexionGuardada, (New-Object Text.UTF8Encoding $false))
    }
}
else {
    Write-Warning "No se indico -CadenaConexion: revisa que $rutaConexion apunte al servidor del laboratorio."
}

# Una carpeta creada en la raiz de C: hereda "Authenticated Users:(M)", asi que
# por defecto el alumno puede REESCRIBIR C:\DigBit\DigBit.exe. Como ese binario es
# el shell de su sesion, renombrarlo y dejar un cmd.exe con ese nombre le da un
# simbolo del sistema en el siguiente inicio automatico; editar DigBit.exe.config
# le basta para apagar el modo kiosco. Y encima esta carpeta esta excluida del
# antivirus. Se rompe la herencia y se deja en solo lectura para Usuarios.
# Comprobado: icacls C:\ muestra Authenticated Users:(OI)(CI)(IO)(M), y una
# carpeta real de la raiz hereda (I)(M) efectivo.
Hacer "Dejar $Destino en solo lectura para los usuarios normales" {
    & icacls.exe $Destino /inheritance:r /q | Out-Null
    foreach ($regla in @(
        '*S-1-5-32-544:(OI)(CI)F',   # Administradores
        '*S-1-5-18:(OI)(CI)F',       # SYSTEM
        '*S-1-5-32-545:(OI)(CI)RX'   # Usuarios: leer y ejecutar, nada mas
    )) {
        & icacls.exe $Destino /grant $regla /q | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { throw "icacls devolvio $LASTEXITCODE sobre $Destino." }
}

# --- Fin para los equipos que NO son del laboratorio -------------------------------
# De aqui abajo todo es del kiosco: comprobar el laboratorio, crear la cuenta,
# poner DigBit como shell, las directivas, el inicio de sesion automatico y el
# vigilante. Un equipo de administrador o de profesor no necesita nada de eso;
# es la MISMA aplicacion, solo que sin bloquear la pantalla.
if (-not $esKiosco) {
    # En un equipo de laboratorio DigBit ES el escritorio, asi que no hace falta
    # nada para abrirlo. En el del administrador o el del profesor es un programa
    # normal, y sin esto quedaba una carpeta en C: que hay que saber que existe.
    # Que es lo unico que estos dos equipos necesitan: la copia, la conexion y
    # algo donde hacer doble clic.
    Paso 'Acceso directo en el escritorio'
    $escritorio = [Environment]::GetFolderPath('CommonDesktopDirectory')
    if (-not $escritorio) { $escritorio = [Environment]::GetFolderPath('Desktop') }
    $enlace = Join-Path $escritorio 'DigBit.lnk'
    Hacer "Crear $enlace" {
        $shell = New-Object -ComObject WScript.Shell
        $acceso = $shell.CreateShortcut($enlace)
        $acceso.TargetPath = $exeDigBit
        $acceso.WorkingDirectory = $Destino
        $acceso.Description = "DigBit - bitacora de laboratorios ($Modo)"
        $acceso.Save()
        [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    }

    Paso 'Marca de instalacion'
    Hacer "Escribir $(Join-Path $Destino '.instalado')" {
        [IO.File]::WriteAllText(
            (Join-Path $Destino '.instalado'),
            (Get-Date).ToString('o', [Globalization.CultureInfo]::InvariantCulture),
            (New-Object Text.UTF8Encoding $false))
    }

    Write-Host ''
    Write-Host "Equipo de $Modo preparado." -ForegroundColor Green
    Write-Host "  - Abrelo con el acceso directo 'DigBit' del escritorio."
    Write-Host "  - DigBit esta en $Destino y se abre como cualquier programa."
    Write-Host '  - Sin kiosco: la pantalla no se bloquea y Windows funciona normal.'
    Write-Host '  - Sin cuenta de laboratorio, sin inicio de sesion automatico y sin vigilante.'
    if ($Modo -eq 'adm') {
        Write-Host '  - Entra con la cuenta de administrador de DigBit (tipo 3) para gestionar horarios.'
    } else {
        Write-Host '  - Entra con tu cuenta de profesor para ver tus clases y sus bitacoras.'
    }
    Write-Host '  - Comprueba con: .\deploy\diagnostico_equipo.ps1'
    return
}

# --- 2c. Acceso de emergencia -----------------------------------------------------
# Los dos factores se guardan HASHEADOS con el mismo PBKDF2 que las contrasenas
# de los usuarios, llamando al codigo de verdad (DigBit.exe) en vez de reescribir
# el algoritmo aqui: si algun dia cambian las iteraciones, esto sigue valiendo.
if ($AutorizarMemoria -and -not $SoloMostrar) {
    Paso '2c. Autorizar la memoria de emergencia'

    $series = @(Get-CimInstance Win32_DiskDrive -Filter "InterfaceType='USB'" -ErrorAction SilentlyContinue |
                ForEach-Object { $_.SerialNumber } |
                Where-Object { $_ -and $_.Trim() } |
                ForEach-Object { $_.Trim() })

    if ($series.Count -eq 0) {
        throw 'Indicaste -AutorizarMemoria pero no hay ninguna memoria USB conectada. Conecta la que quieras autorizar y repite.'
    }
    if ($series.Count -gt 1) {
        throw "Hay $($series.Count) memorias USB conectadas y no se cual autorizar. Deja conectada SOLO la que quieras y repite."
    }

    $global:CarpetaDigBit = $Destino
    $global:Resolviendo = @{}
    [AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler] {
        param($remitente, $evento)
        $corto = ($evento.Name -split ',')[0]
        if ($global:Resolviendo.ContainsKey($corto)) { return $null }
        $global:Resolviendo[$corto] = $true
        try {
            $dll = Join-Path $global:CarpetaDigBit "$corto.dll"
            if (Test-Path $dll) { return [Reflection.Assembly]::LoadFrom($dll) }
            return $null
        } finally { $global:Resolviendo.Remove($corto) }
    })

    $ensamblado = [Reflection.Assembly]::LoadFrom((Join-Path $Destino 'DigBit.exe'))
    $tipo = $ensamblado.GetType('DigBit.conexion.Contrasenas')
    $metodo = $tipo.GetMethod('Hash', [Reflection.BindingFlags]'Public,NonPublic,Static')
    if (-not $metodo) { throw 'No se encontro Contrasenas.Hash en DigBit.exe.' }

    Hacer 'Guardar la memoria autorizada (hasheada, nunca en claro)' {
        EscribirAjustes $config @(
            @{ Clave = 'EmergenciaMemoria'; Valor = $metodo.Invoke($null, @([string]$series[0])) }
        )
    }
    Write-Host "   memoria autorizada: $($series[0])" -ForegroundColor Green
    Write-Host '   (el numero se guarda hasheado; esta linea es lo unico que lo muestra)' -ForegroundColor DarkGray
}

# --- 2b. Comprobar que el laboratorio existe de verdad -----------------------------
# El nombre se copia del ADM a mano, y un acento o un espacio de mas deja el
# equipo rechazando TODOS los codigos. Antes eso se descubria en la primera
# clase; ahora se descubre aqui, con la lista de nombres buenos delante.
# Se usa el propio MySql.Data.dll que se acaba de copiar, asi que no hace falta
# tener cliente de MySQL instalado en el equipo.
if (-not $SoloMostrar) {
    Paso '2b. Comprobar el laboratorio contra la base'

    $cadena = $CadenaConexion
    if (-not $cadena -and (Test-Path $rutaConexion)) {
        $nodo = ([xml](Get-Content -Raw $rutaConexion)).connectionStrings.add | Where-Object { $_.name -eq 'DigBit' }
        if ($nodo) { $cadena = $nodo.connectionString }
    }

    if (-not $cadena) {
        Write-Warning '   Sin cadena de conexion: no se puede comprobar. Revisalo con diagnostico_equipo.ps1.'
    }
    else {
        # Las dependencias de MySql.Data viven junto al DLL, no junto a
        # powershell.exe, asi que hay que decirle al CLR donde buscarlas.
        $global:CarpetaDigBit = $Destino
        $global:Resolviendo = @{}
        [AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler] {
            param($remitente, $evento)
            $corto = ($evento.Name -split ',')[0]
            if ($global:Resolviendo.ContainsKey($corto)) { return $null }
            $global:Resolviendo[$corto] = $true
            try {
                $dll = Join-Path $global:CarpetaDigBit "$corto.dll"
                if (Test-Path $dll) { return [Reflection.Assembly]::LoadFrom($dll) }
                return $null
            } finally { $global:Resolviendo.Remove($corto) }
        })

        $nombres = $null
        try {
            Add-Type -Path (Join-Path $Destino 'MySql.Data.dll')
            $conexion = New-Object MySql.Data.MySqlClient.MySqlConnection ($cadena + ';Connection Timeout=8')
            $conexion.Open()
            try {
                $consulta = $conexion.CreateCommand()
                $consulta.CommandText = 'SELECT nombre_laboratorio FROM laboratorios ORDER BY idlaboratorios'
                $lector = $consulta.ExecuteReader()
                $nombres = @()
                while ($lector.Read()) { $nombres += $lector.GetString(0) }
                $lector.Close()
            } finally { $conexion.Close() }
        }
        catch {
            Write-Warning "   No se pudo consultar la base ($($_.Exception.Message))."
            Write-Warning '   El equipo queda instalado, pero NADIE ha comprobado que el laboratorio exista.'
        }

        if ($null -ne $nombres) {
            # Se compara SIN distinguir mayusculas y sin espacios al borde, que es
            # como se va a comparar de verdad: MySQL no distingue caja por defecto
            # y LaboratorioEquipo hace Trim() del valor. Ser mas estricto aqui
            # seria rechazar etiquetas que funcionan.
            $recortado = $Laboratorio.Trim()
            $canonico = $nombres | Where-Object { $_ -eq $recortado } | Select-Object -First 1

            if ($canonico) {
                if ($canonico -cne $Laboratorio) {
                    # Vale igual, pero se guarda como lo escribe el ADM: asi el
                    # config y la pantalla del administrador dicen lo mismo.
                    Write-Host "   '$Laboratorio' vale, y se guarda como '$canonico' (tal cual esta en la base)." -ForegroundColor Yellow
                    $Laboratorio = $canonico
                    EscribirAjustes $config @(@{ Clave = 'Laboratorio'; Valor = $canonico })
                }
                else {
                    Write-Host "   '$Laboratorio' existe en la base." -ForegroundColor Green
                }
            }
            else {
                Write-Host ''
                Write-Host '   Los laboratorios que hay en la base son:' -ForegroundColor Yellow
                foreach ($n in $nombres) { Write-Host "     `"$n`"" -ForegroundColor Yellow }
                Write-Host ''
                throw "El laboratorio '$Laboratorio' no existe en la base. Copia uno de los de arriba TAL CUAL (mayusculas, acentos y espacios incluidos) y vuelve a ejecutar esto. El equipo ya tiene los archivos copiados; repetir el guion no rompe nada."
            }
        }
    }
}

# --- 3. Cuenta -----------------------------------------------------------------
Paso "3. Cuenta local estandar '$Cuenta'"
$segura = $null
if ($Contrasena) {
    $segura = ConvertTo-SecureString $Contrasena -AsPlainText -Force
}

if (Get-LocalUser -Name $Cuenta -ErrorAction SilentlyContinue) {
    Hacer 'Ya existe: se fija la contrasena indicada y se deja sin caducidad' {
        Set-LocalUser -Name $Cuenta -Password $segura -PasswordNeverExpires $true -UserMayChangePassword $false
    }
}
else {
    Hacer 'Crear la cuenta (sin privilegios, contrasena sin caducidad)' {
        New-LocalUser -Name $Cuenta -Password $segura -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires -Description 'Cuenta de laboratorio DigBit' | Out-Null
    }
}

Hacer 'Asegurar que pertenece al grupo Usuarios' {
    try {
        Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $Cuenta -ErrorAction Stop
    }
    catch {
        if ($_.Exception.Message -notmatch 'miembro|member') { throw }
    }
}

Hacer 'Crear su perfil de Windows (inicio de sesion silencioso, si aun no lo tiene)' {
    if (-not (ObtenerPerfil (ObtenerSid))) {
        $seclogon = Get-Service -Name 'seclogon' -ErrorAction SilentlyContinue
        if ($seclogon -and $seclogon.Status -ne 'Running') {
            Start-Service -Name 'seclogon'
        }
        $credencial = New-Object Management.Automation.PSCredential ("$env:COMPUTERNAME\$Cuenta", $segura)
        Start-Process -FilePath "$env:WINDIR\System32\cmd.exe" -ArgumentList '/c', 'exit' `
            -Credential $credencial -LoadUserProfile -WorkingDirectory "$env:WINDIR\System32" -WindowStyle Hidden -Wait
        if (-not (ObtenerPerfil (ObtenerSid))) {
            throw "No se pudo crear el perfil de '$Cuenta'. Inicia sesion con esa cuenta una vez y vuelve a ejecutar el guion."
        }
    }
}

# --- 4 y 5. Shell y directivas de la cuenta --------------------------------------
Paso '4 y 5. Shell y directivas en el perfil de la cuenta'
if (-not $SoloMostrar) { AbrirRegistroCuenta }
try {
    foreach ($v in $valoresUsuario) {
        Hacer "$($v.Que): $(DescribirRaiz)\$($v.Clave) -> $($v.Nombre) = $($v.Valor)" {
            Reg @('add', "$($script:raizUsuario)\$($v.Clave)", '/v', $v.Nombre, '/t', $v.Tipo, '/d', $v.Valor, '/f')
        }
    }

    # La clave Shell vive en la colmena del PROPIO alumno, que la hereda con
    # control total: puede poner Shell=explorer.exe y entrar a un escritorio
    # completo sin pasar por DigBit. Y no hace falta reiniciar (Deep Freeze no
    # salva), porque el relevo entre alumnos es un cierre de sesion.
    # Windows ya protege asi la clave de Policies: herencia rota, dueno SYSTEM,
    # Usuarios solo lectura. Se copia ese mismo patron sobre Winlogon.
    Hacer "Impedir que la cuenta cambie su propio Shell ($(DescribirRaiz)\Software\Microsoft\Windows NT\CurrentVersion\Winlogon)" {
        $ramaCuenta = $script:raizUsuario.Split([char]92)[-1]   # 'HKU\<SID>' o 'HKU\DigBitLab' -> lo de despues de la barra
        $clave = "Registry::HKEY_USERS\$ramaCuenta\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
        $sidCuenta = (Get-LocalUser -Name $Cuenta).SID

        # Se construye un descriptor NUEVO y se escribe entero. Los dos caminos
        # "obvios" no funcionan, y los dos fallan en silencio o tarde:
        #   - romper la herencia copiandola ($true,$true) y luego quitar la regla
        #     de la cuenta: las heredadas se vuelven copias explicitas que ni
        #     RemoveAccessRuleSpecific ni PurgeAccessRules encuentran, y la cuenta
        #     se queda con control total aunque el guion termine en verde;
        #   - partir del descriptor de la clave y vaciarlo con RemoveAccessRule:
        #     traduce cada identidad a nombre y lanza IdentityNotMappedException,
        #     porque esta clave lleva ACEs de contenedores de aplicacion
        #     (S-1-15-3-1024-...) que no tienen nombre.
        # La lista es la misma que Windows pone en HKCU\...\Policies.
        $acl = New-Object Security.AccessControl.RegistrySecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($par in @(
            @{ Sid = 'S-1-5-18';     Permiso = 'FullControl' }   # SYSTEM
            @{ Sid = 'S-1-5-32-544'; Permiso = 'FullControl' }   # Administradores
            @{ Sid = 'S-1-5-12';     Permiso = 'ReadKey' }       # RESTRICTED
            @{ Sid = 'S-1-15-2-1';   Permiso = 'ReadKey' }       # ALL APPLICATION PACKAGES
        )) {
            $acl.AddAccessRule((New-Object Security.AccessControl.RegistryAccessRule(
                (New-Object Security.Principal.SecurityIdentifier $par.Sid),
                $par.Permiso, 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        }
        $acl.AddAccessRule((New-Object Security.AccessControl.RegistryAccessRule(
            $sidCuenta, 'ReadKey', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        Set-Acl -Path $clave -AclObject $acl

        # No basta con que no de error: se relee y se comprueba que de verdad
        # quedo en solo lectura, porque la version anterior "funcionaba" y no.
        # (bucle y no Where-Object: en PowerShell 5.1 'try' no vale como expresion)
        $sobrantes = @()
        foreach ($regla in (Get-Acl $clave).Access) {
            if ($regla.AccessControlType -ne 'Allow') { continue }
            if ($regla.RegistryRights -eq 'ReadKey') { continue }
            $sidRegla = $null
            try { $sidRegla = $regla.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { continue }
            if ($sidRegla -eq $sidCuenta.Value) { $sobrantes += $regla }
        }
        if ($sobrantes.Count -gt 0) {
            throw "La cuenta '$Cuenta' sigue con $($sobrantes[0].RegistryRights) sobre su clave Winlogon; podria devolver el Shell a explorer.exe."
        }
    }
}
finally {
    CerrarRegistroCuenta
}

# --- 6. Inicio de sesion automatico --------------------------------------------
Paso '6. Inicio de sesion automatico'
Hacer "Winlogon: AutoAdminLogon=1, DefaultUserName=$Cuenta, DefaultDomainName=$env:COMPUTERNAME, DefaultPassword=(la indicada)" {
    Reg @('add', $claveWinlogon, '/v', 'AutoAdminLogon',    '/t', 'REG_SZ', '/d', '1', '/f')
    Reg @('add', $claveWinlogon, '/v', 'DefaultUserName',   '/t', 'REG_SZ', '/d', $Cuenta, '/f')
    Reg @('add', $claveWinlogon, '/v', 'DefaultDomainName', '/t', 'REG_SZ', '/d', $env:COMPUTERNAME, '/f')
    Reg @('add', $claveWinlogon, '/v', 'DefaultPassword',   '/t', 'REG_SZ', '/d', $Contrasena, '/f')
    RegBorrarValor $claveWinlogon 'AutoLogonCount'
}

Hacer 'Winlogon: AutoRestartShell=1 (si DigBit termina, Windows lo relanza)' {
    Reg @('add', $claveWinlogon, '/v', 'AutoRestartShell', '/t', 'REG_DWORD', '/d', '1', '/f')
}

# AutoAdminLogon sirve para el ARRANQUE. Cuando el vigilante cierra la sesion al
# terminar la clase no hay arranque, asi que sin esto el equipo se queda en la
# pantalla de contrasena y el ciclo no se cierra: el siguiente alumno encuentra
# un equipo pidiendo una clave que no tiene. ForceAutoLogon=1 hace que la cuenta
# vuelva a entrar sola tambien despues de un cierre de sesion.
# Contrapartida conocida: rompe el escritorio remoto (entra y sale al momento).
# Para el administrador no cambia nada, porque la via es Ctrl+Alt+Supr ->
# Cambiar de usuario, que no cierra la sesion del kiosco.
Hacer 'Winlogon: ForceAutoLogon=1 (vuelve a entrar sola tras cerrar sesion, no solo al arrancar)' {
    Reg @('add', $claveWinlogon, '/v', 'ForceAutoLogon', '/t', 'REG_SZ', '/d', '1', '/f')
}

Hacer 'Permitir el inicio automatico con contrasena (DevicePasswordLessBuildVersion=0)' {
    # En Windows 10/11 recientes esta clave en 2 oculta la opcion y hace que
    # Windows ignore AutoAdminLogon con cuentas locales.
    Reg @('add', $clavePasswordLess, '/v', 'DevicePasswordLessBuildVersion', '/t', 'REG_DWORD', '/d', '0', '/f')
}

Hacer 'No pedir contrasena al despertar (powercfg CONSOLELOCK=0)' {
    & powercfg.exe /SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0 | Out-Null
    & powercfg.exe /SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0 | Out-Null
    & powercfg.exe /SETACTIVE SCHEME_CURRENT | Out-Null
}

# --- 7. Servicio -----------------------------------------------------------------
Paso '7. Servicio DigBit.Vigilante'
Hacer "Instalar y arrancar desde $exeVigilante" {
    & $instalador -Ruta $exeVigilante
}

# --- 8. Marca de instalacion -----------------------------------------------------
# Sirve para demostrar despues, sin depender del fabricante, si el disco conserva
# lo que se escribe: si esta marca sigue aqui tras un reinicio, no estaba
# congelado. La lee diagnostico_equipo.ps1.
Paso '8. Marca de instalacion'
Hacer "Escribir $(Join-Path $Destino '.instalado')" {
    [IO.File]::WriteAllText(
        (Join-Path $Destino '.instalado'),
        (Get-Date).ToString('o', [Globalization.CultureInfo]::InvariantCulture),
        (New-Object Text.UTF8Encoding $false))
}

# --- Resumen ---------------------------------------------------------------------
Write-Host ''
Write-Host 'Equipo preparado. Reinicia para comprobarlo:' -ForegroundColor Green
Write-Host "  - Windows entrara solo como '$Cuenta' y arrancara DigBit sin escritorio."
Write-Host '  - Tras guardar la bitacora aparece el escritorio; a la hora de salida se cierra la sesion.'
Write-Host '  - Para entrar como administrador: Ctrl+Alt+Supr -> Cambiar de usuario, o Mayus pulsada durante el arranque.'
if ($CarpetaDatos) {
    Write-Host "  - Datos y logs: $CarpetaDatos (fuera del disco congelado; ahi quedan las bitacoras sin enviar)."
}
else {
    Write-Host "  - Logs: %LOCALAPPDATA%\DigBit\logs (DigBit, en el perfil de '$Cuenta') y %ProgramData%\DigBit\logs (vigilante)."
}
if ($deepFreeze.Presente) {
    Write-Host ''
    Write-Host 'FALTA UN PASO: vuelve a CONGELAR el equipo (Boot Frozen) y reinicia.' -ForegroundColor Yellow
    Write-Host 'Comprueba antes que todo funciona descongelado: una vez congelado ya no' -ForegroundColor Yellow
    Write-Host 'se corrige nada sin repetir el ciclo de descongelar, arreglar y reiniciar.' -ForegroundColor Yellow
}
