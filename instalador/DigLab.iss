; El instalador de DigLab: DigLab-Setup.exe, el asistente normal de Windows.
; No se compila a mano: lo arma crear_setup.ps1, que prepara la carpeta de carga (sin la
; contrasena de la base) y le pasa estas definiciones con /D:
;   Carga          la carpeta de carga: DigLab\, Vigilante\ y Configurador\
;   Temporales     el archivo con la lista de lo que se extrae antes de instalar
;   BaseServidor, BaseNombre, BaseUsuario, BasePuerto    la conexion, SIN contrasena
;   VistaPrevia    (opcional) para ver las paginas sin instalar nada ni pedir administrador
;
; Lo que el asistente no puede hacer solo (comprobar la base, configurar el equipo) lo hace
; DigLab.Configurador.exe. Las contrasenas le llegan en variables de entorno, nunca en la
; linea de comandos, que cualquiera puede leer en el Administrador de tareas. Nada de
; PowerShell en el equipo de destino: en algunas PCs esta bloqueado o limitado.

#ifndef Carga
  #error Arma el instalador con crear_setup.ps1
#endif

#ifdef VistaPrevia
  ; Su valor es la carpeta. Una que no existe: las paginas del camino completo, aunque DigLab
  ; este instalado.
  #define Destino VistaPrevia
  ; Y la pagina de resultados antes de instalar, con datos de muestra: asi su codigo corre de
  ; verdad, que en la vista previa nunca se llega a instalar.
  #define PaginaAntesDelResultado "wpReady"
#else
  #define Destino "C:\DigLab"
  #define PaginaAntesDelResultado "wpInstalling"
#endif
#define MiVersion GetVersionNumbersString(Carga + "\DigLab\DigLab.exe")

[Setup]
; Otro que el de cuando se llamaba DigBit (ClaveDigBit): aquella era otra instalacion, en otra carpeta.
AppId={{DCC91FAF-3B4C-4429-AFE3-6A27993A0E3E}
AppName=DigLab
AppVersion={#MiVersion}
AppVerName=DigLab {#MiVersion}
VersionInfoVersion={#MiVersion}
VersionInfoDescription=Instalador de DigLab
AppPublisher=TESCHI - Sistemas Computacionales
; C:\DigLab y sin preguntar: el shell de Winlogon y sus permisos dependen de esa ruta.
DefaultDirName={#Destino}
AppendDefaultDirName=no
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
DisableWelcomePage=no
OutputBaseFilename=DigLab-Setup
SetupIconFile={#Carga}\icono.ico
UninstallDisplayIcon={app}\DigLab.exe
UninstallDisplayName=DigLab
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=no
; DigLab y el vigilante los detiene el configurador antes de copiar.
CloseApplications=no
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64compatible
#ifdef VistaPrevia
PrivilegesRequired=lowest
Uninstallable=no
#else
PrivilegesRequired=admin
#endif

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
; El configurador y lo que necesita para hablar con la base, disponibles desde antes de
; instalar (dontcopy: se extraen al temporal; ver Temporales).
Source: "{#Carga}\Configurador\*"; Flags: dontcopy
Source: "{#Carga}\DigLab\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Carga}\Vigilante\*"; DestDir: "{app}\Vigilante"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EsKiosco
; Se queda instalado para poder deshacer el kiosco al desinstalar.
Source: "{#Carga}\Configurador\DigLab.Configurador.exe"; DestDir: "{app}\Configurador"; Flags: ignoreversion

[Icons]
; En un kiosco DigLab ES el escritorio: no hace falta nada donde hacer doble clic.
Name: "{autoprograms}\DigLab"; Filename: "{app}\DigLab.exe"; WorkingDir: "{app}"; Check: not EsKiosco
Name: "{autodesktop}\DigLab"; Filename: "{app}\DigLab.exe"; WorkingDir: "{app}"; Check: not EsKiosco

[Run]
; Como el usuario y no como administrador: elevado, sus registros y datos irian a otro sitio.
Filename: "{app}\DigLab.exe"; Description: "Abrir DigLab"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: not EsKiosco
; Cuando DigLab se actualizo desde la base (db/29): lo lanzo en silencio con /ABRIRDIGLAB y
; se cerro para dejarse reemplazar; aqui se vuelve a abrir solo, como quien lo usaba.
Filename: "{app}\DigLab.exe"; Flags: nowait runasoriginaluser; Check: AbrirTrasActualizar

[UninstallDelete]
; connections.config y demas los escribe el configurador: el desinstalador no los conoce.
Type: filesandordirs; Name: "{app}"

[Code]
const
  VarSetup = 'DIGLAB_SETUP';
  VarServidor = 'DIGLAB_BD_SERVIDOR';
  VarBase = 'DIGLAB_BD_BASE';
  VarPuerto = 'DIGLAB_BD_PUERTO';
  VarUsuario = 'DIGLAB_BD_USUARIO';
  VarClave = 'DIGLAB_BD_CLAVE';
  VarClaveCuenta = 'DIGLAB_CLAVE_CUENTA';
  ModoAdm = 'adm';
  ModoMaestro = 'maestro';
  ModoKiosco = 'kiosco';
  VkCapital = 20;
  // Lo que Windows sabe de la instalacion de cuando el programa se llamaba DigBit.
  ClaveDigBit = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{5B8E2F14-7C3A-4D19-9F6E-2A1C8D3B7E60}_is1';

var
  PaginaTipo: TInputOptionWizardPage;
  PaginaBase: TInputQueryWizardPage;
  PaginaEquipo: TWizardPage;
  PaginaResultado: TOutputMsgMemoWizardPage;
  CboLaboratorio: TNewComboBox;
  TxtNumero, TxtCuenta: TNewEdit;
  TxtClave, TxtClave2: TPasswordEdit;
  RbOriginal, RbNueva: TNewRadioButton;
  LblClave: TNewStaticText;
  CuentaActual: String;
  // Las memorias de emergencia vigentes en la base (db/28), segun "probar": un numero,
  // 'NO' si la base aun no las tiene, '' si no se supo.
  MemoriasRegistradas: String;
  Actualizando: Boolean;
  ModoPrevio, CuentaPrevia, LaboratorioPrevio: String;
  ConfiguradoBien: Boolean;

function SetEnvironmentVariable(lpName, lpValue: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';
function GetKeyState(nVirtKey: Integer): SmallInt;
  external 'GetKeyState@user32.dll stdcall';

// ---------------------------------------------------------------------------
// El configurador
// ---------------------------------------------------------------------------

// Lo lanza sin ventana; lo que dice queda en un archivo, un renglon por linea.
function Configurador(const Orden, Parametros: String; var Lineas: TArrayOfString): Integer;
var
  Archivo: String;
  Codigo: Integer;
begin
  Archivo := ExpandConstant('{tmp}\salida_') + Orden + '.txt';
  DeleteFile(Archivo);
  SetArrayLength(Lineas, 0);
  if not Exec(ExpandConstant('{tmp}\DigLab.Configurador.exe'), Orden + ' ' + Parametros + ' --salida ' + AddQuotes(Archivo),
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Codigo) then
    Codigo := -1;
  if not LoadStringsFromFile(Archivo, Lineas) then
    SetArrayLength(Lineas, 0);
  Result := Codigo;
end;

// El resto del renglon que empieza con esa palabra, o '' si no hay.
function Renglon(const Lineas: TArrayOfString; const Palabra: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Lineas) - 1 do
    if Pos(Palabra + ' ', Lineas[I]) = 1 then
    begin
      Result := Copy(Lineas[I], Length(Palabra) + 2, Length(Lineas[I]));
      Exit;
    end;
end;

function HayRenglon(const Lineas: TArrayOfString; const Palabra: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to GetArrayLength(Lineas) - 1 do
    if (Lineas[I] = Palabra) or (Pos(Palabra + ' ', Lineas[I]) = 1) then
    begin
      Result := True;
      Exit;
    end;
end;

function ModoElegido: String;
begin
  if Actualizando then
    Result := ModoPrevio
  else if PaginaTipo.SelectedValueIndex = 0 then
    Result := ModoAdm
  else if PaginaTipo.SelectedValueIndex = 1 then
    Result := ModoMaestro
  else
    Result := ModoKiosco;
end;

function EsKiosco: Boolean;
begin
  Result := ModoElegido = ModoKiosco;
end;

// DigLab se actualizo desde la base (Actualizador): el instalador corre en silencio y,
// fuera del kiosco, vuelve a abrir DigLab al terminar.
function AbrirTrasActualizar: Boolean;
begin
  Result := WizardSilent and (not EsKiosco) and (Pos('/ABRIRDIGLAB', Uppercase(GetCmdTail)) > 0);
end;

function CuentaElegida: String;
begin
  if Actualizando then
    Result := CuentaPrevia
  else if RbOriginal.Checked then
    Result := CuentaActual
  else
    Result := Trim(TxtCuenta.Text);
end;

function NombreDelTipo: String;
begin
  if ModoElegido = ModoAdm then
    Result := 'Administración'
  else if ModoElegido = ModoMaestro then
    Result := 'Docente'
  else
    Result := 'Equipo de laboratorio (kiosco)';
end;

// La conexion del asistente, en el entorno que heredan los procesos que se lancen.
procedure PonerConexion;
begin
  SetEnvironmentVariable(VarServidor, Trim(PaginaBase.Values[0]));
  SetEnvironmentVariable(VarBase, Trim(PaginaBase.Values[1]));
  SetEnvironmentVariable(VarUsuario, Trim(PaginaBase.Values[2]));
  SetEnvironmentVariable(VarPuerto, Trim(PaginaBase.Values[3]));
  SetEnvironmentVariable(VarClave, PaginaBase.Values[4]);
end;

procedure QuitarSecretos;
begin
  SetEnvironmentVariable(VarClave, '');
  SetEnvironmentVariable(VarClaveCuenta, '');
end;

// ---------------------------------------------------------------------------
// Antes del asistente
// ---------------------------------------------------------------------------

#ifndef VistaPrevia
// El desinstalador de cuando el programa se llamaba DigBit, o '' si este equipo no lo tiene.
function DesinstaladorDeDigBit: String;
begin
  Result := '';
  // Anidados: en un Windows de 32 bits, leer HKLM64 es un error.
  if IsWin64 then
    if RegQueryStringValue(HKLM64, ClaveDigBit, 'UninstallString', Result) then
      Exit;
  if not RegQueryStringValue(HKLM32, ClaveDigBit, 'UninstallString', Result) then
    Result := '';
end;

// DigBit era otra instalacion, en C:\DigBit: las dos juntas se estorbarian, cada una con su
// vigilante y su shell. Se quita con su propio desinstalador, que deshace su kiosco. Los datos
// locales que deje (su cola de bitacoras) los toma DigLab al arrancar (NombreAnterior).
function QuitarDigBit: Boolean;
var
  Desinstalador: String;
  Codigo, I: Integer;
begin
  Result := True;
  Desinstalador := DesinstaladorDeDigBit;
  if Desinstalador = '' then
    Exit;

  Result := False;
  if MsgBox('Este equipo tiene la versión anterior de este programa, cuando se llamaba DigBit. Hay que quitarla antes de instalar DigLab.'#13#10#13#10 +
    'Si pregunta si borra los datos locales, responde No: DigLab se queda con ellos. Después tendrás que volver a elegir el tipo de equipo y a escribir la contraseña de la base.'#13#10#13#10 +
    '¿Quitarla ahora?', mbConfirmation, MB_YESNO) <> IDYES then
    Exit;

  Exec(RemoveQuotes(Desinstalador), '', '', SW_SHOW, ewWaitUntilTerminated, Codigo);
  // Por si su desinstalador devuelve el control antes de terminar.
  for I := 1 to 20 do
  begin
    if DesinstaladorDeDigBit = '' then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;

  MsgBox('DigBit sigue instalado. Quítalo desde Configuración de Windows, en Aplicaciones, y vuelve a abrir este instalador.', mbError, MB_OK);
end;

#endif
function InitializeSetup: Boolean;
var
  Lineas: TArrayOfString;
begin
  Result := True;
  if not IsDotNetInstalled(net472, 0) then
  begin
    MsgBox('DigLab necesita Microsoft .NET Framework 4.7.2 o más nuevo, y este equipo no lo tiene.'#13#10#13#10 +
      'Instálalo con Windows Update o desde la página de Microsoft, y vuelve a abrir este instalador.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

#ifndef VistaPrevia
  if not QuitarDigBit then
  begin
    Result := False;
    Exit;
  end;

#endif
#include Temporales

  // Lo que ya hay: si la conexion existe, el asistente es corto y conserva todo.
  Actualizando := False;
  if (Configurador('estado', '--destino ' + AddQuotes('{#Destino}'), Lineas) = 0) and (Renglon(Lineas, 'INSTALADO') = 'SI') then
  begin
    ModoPrevio := Renglon(Lineas, 'MODO');
    CuentaPrevia := Renglon(Lineas, 'CUENTA');
    LaboratorioPrevio := Renglon(Lineas, 'LABORATORIO');
    Actualizando := Renglon(Lineas, 'CONEXION') <> 'NO';
  end;
end;

// ---------------------------------------------------------------------------
// Las paginas
// ---------------------------------------------------------------------------

function Etiqueta(Pagina: TWizardPage; const Texto: String; Y: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(Pagina);
  Result.Parent := Pagina.Surface;
  Result.Caption := Texto;
  Result.Left := 0;
  Result.Top := Y;
  Result.AutoSize := True;
end;

procedure ColocarCaja(Caja: TWinControl; Pagina: TWizardPage; Y, Ancho: Integer);
begin
  Caja.Parent := Pagina.Surface;
  Caja.Left := 0;
  Caja.Top := Y;
  Caja.Width := Ancho;
end;

// La cuenta original no se crea: la contrasena es la que ya tiene, y se pide una sola vez.
procedure CuentaCambiada(Sender: TObject);
begin
  // Al cambiar de opcion: la de una cuenta no debe probarse contra la otra.
  if Sender <> nil then
  begin
    TxtClave.Text := '';
    TxtClave2.Text := '';
  end;
  TxtCuenta.Enabled := RbNueva.Checked;
  TxtClave2.Visible := RbNueva.Checked;
  if RbNueva.Checked then
    LblClave.Caption := 'Contraseña para la cuenta nueva (la inventas), dos veces:'
  else
    LblClave.Caption := 'Contraseña actual de esa cuenta; no cambia (si no tiene, déjala vacía):';
end;

procedure InitializeWizard;
var
  Ancho: Integer;
begin
  PaginaTipo := CreateInputOptionPage(wpWelcome, '¿Qué equipo es este?',
    'El programa es el mismo; cambia cómo se usa.',
    'Elige una opción y pulsa Siguiente.', True, False);
  PaginaTipo.Add('Administración: gestiona horarios, docentes y reportes');
  PaginaTipo.Add('Docente: ve sus clases y sus bitácoras');
  PaginaTipo.Add('Equipo de laboratorio (kiosco): DigLab será la pantalla de los estudiantes');
  PaginaTipo.SelectedValueIndex := 0;

  PaginaBase := CreateInputQueryPage(PaginaTipo.ID, 'Base de datos',
    '¿A qué base se conecta este equipo?',
    'Los datos ya vienen puestos; solo falta la contraseña. Se comprueba antes de instalar y no se guarda en este instalador.');
  PaginaBase.Add('Servidor:', False);
  PaginaBase.Add('Base de datos:', False);
  PaginaBase.Add('Usuario:', False);
  PaginaBase.Add('Puerto:', False);
  PaginaBase.Add('Contraseña (distingue mayúsculas y minúsculas):', True);
  PaginaBase.Values[0] := '{#BaseServidor}';
  PaginaBase.Values[1] := '{#BaseNombre}';
  PaginaBase.Values[2] := '{#BaseUsuario}';
  PaginaBase.Values[3] := '{#BasePuerto}';

  PaginaEquipo := CreateCustomPage(PaginaBase.ID, 'Equipo de laboratorio',
    'El laboratorio y el número son lo que cambia de un equipo a otro.');
  Ancho := PaginaEquipo.SurfaceWidth;

  Etiqueta(PaginaEquipo, 'Laboratorio donde está este equipo:', 0);
  CboLaboratorio := TNewComboBox.Create(PaginaEquipo);
  CboLaboratorio.Style := csDropDown;
  ColocarCaja(CboLaboratorio, PaginaEquipo, ScaleY(18), Ancho);

  Etiqueta(PaginaEquipo, 'Número de la máquina (el que tiene pegado; opcional, sale en las bitácoras):', ScaleY(48));
  TxtNumero := TNewEdit.Create(PaginaEquipo);
  ColocarCaja(TxtNumero, PaginaEquipo, ScaleY(66), ScaleX(120));

  CuentaActual := ExpandConstant('{username}');
  Etiqueta(PaginaEquipo, '¿Con qué cuenta de Windows entra este equipo?', ScaleY(96));
  RbOriginal := TNewRadioButton.Create(PaginaEquipo);
  RbOriginal.Caption := 'La que ya tiene el equipo, sin crear otra: ' + CuentaActual;
  ColocarCaja(RbOriginal, PaginaEquipo, ScaleY(114), Ancho);
  RbOriginal.Height := ScaleY(18);
  RbNueva := TNewRadioButton.Create(PaginaEquipo);
  RbNueva.Caption := 'Una cuenta nueva, sin permisos de administrador:';
  ColocarCaja(RbNueva, PaginaEquipo, ScaleY(135), ScaleX(300));
  RbNueva.Height := ScaleY(18);
  TxtCuenta := TNewEdit.Create(PaginaEquipo);
  TxtCuenta.Text := 'laboratorio';
  ColocarCaja(TxtCuenta, PaginaEquipo, ScaleY(133), Ancho - ScaleX(305));
  TxtCuenta.Left := ScaleX(305);
  // La original por omision: sus programas y permisos estan ahi (decidido el 2026-11-04).
  RbOriginal.Checked := True;
  RbOriginal.OnClick := @CuentaCambiada;
  RbNueva.OnClick := @CuentaCambiada;

  LblClave := Etiqueta(PaginaEquipo, '', ScaleY(161));
  TxtClave := TPasswordEdit.Create(PaginaEquipo);
  ColocarCaja(TxtClave, PaginaEquipo, ScaleY(179), ScaleX(200));
  TxtClave2 := TPasswordEdit.Create(PaginaEquipo);
  ColocarCaja(TxtClave2, PaginaEquipo, ScaleY(179), ScaleX(200));
  TxtClave2.Left := ScaleX(210);

  // Ya no se autoriza ninguna memoria aqui (db/28, 2026-11-07): se registran en DigLab y
  // el equipo copia la lista de la base al configurarse.
  Etiqueta(PaginaEquipo, 'Memoria de emergencia: cualquiera de las registradas en DigLab (Laboratorios >', ScaleY(213));
  Etiqueta(PaginaEquipo, 'Memorias de emergencia). No hace falta conectar ninguna.', ScaleY(229));

  // Con algo en el tercer texto: vacio, Inno no crea SubCaptionLabel (es nil) y escribirle da
  // "Could not call proc". MostrarResultado pone ahi el veredicto.
  PaginaResultado := CreateOutputMsgMemoPage({#PaginaAntesDelResultado}, 'Comprobación del equipo',
    'Esto es lo que se revisó al terminar.', 'Comprobando...', '');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = PaginaTipo.ID) or (PageID = PaginaBase.ID) then
    Result := Actualizando
  else if PageID = PaginaEquipo.ID then
    Result := Actualizando or not EsKiosco;
end;

// ---------------------------------------------------------------------------
// Al pulsar Siguiente
// ---------------------------------------------------------------------------

// False si hay que quedarse en la pagina. Con la lista de laboratorios, si se pudo leer.
function ComprobarBase: Boolean;
var
  Lineas: TArrayOfString;
  Estado, Aviso: String;
  I: Integer;
begin
  Result := False;
  if (Trim(PaginaBase.Values[0]) = '') or (Trim(PaginaBase.Values[1]) = '') or (Trim(PaginaBase.Values[2]) = '') then
  begin
    MsgBox('Faltan datos: el servidor, la base y el usuario son necesarios.', mbError, MB_OK);
    Exit;
  end;

#ifndef VistaPrevia
  if PaginaBase.Values[4] = '' then
  begin
    MsgBox('Escribe la contraseña de la base de datos.', mbError, MB_OK);
    Exit;
  end;

#endif
  PonerConexion;
  WizardForm.NextButton.Enabled := False;
  try
    Configurador('probar', '', Lineas);
  finally
    WizardForm.NextButton.Enabled := True;
  end;

  Estado := Renglon(Lineas, 'ESTADO');
  MemoriasRegistradas := Renglon(Lineas, 'MEMORIAS');
  if Estado = 'OK' then
  begin
    CboLaboratorio.Items.Clear;
    for I := 0 to GetArrayLength(Lineas) - 1 do
      if Pos('LAB ', Lineas[I]) = 1 then
        CboLaboratorio.Items.Add(Copy(Lineas[I], 5, Length(Lineas[I])));
    if CboLaboratorio.Items.Count > 0 then
      CboLaboratorio.Style := csDropDownList;
    Result := True;
  end
  else if Estado = 'DENEGADO' then
  begin
    Aviso := 'La base rechazó el usuario «' + Trim(PaginaBase.Values[2]) + '» con esa contraseña, así que no se instala nada.'#13#10#13#10 +
      'La contraseña distingue mayúsculas y minúsculas: revísala.';
    if (GetKeyState(VkCapital) and 1) <> 0 then
      Aviso := Aviso + #13#10#13#10 + 'Bloq Mayús está activado.';
    MsgBox(Aviso, mbError, MB_OK);
  end
  else
    Result := MsgBox('No se pudo conectar con la base para comprobar la contraseña:'#13#10#13#10 +
      Renglon(Lineas, 'DETALLE') + #13#10#13#10 +
      'Si este equipo todavía no tiene red, puedes instalar igual: DigLab se conectará cuando la tenga.'#13#10 +
      '¿Instalar de todos modos?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

// La cuenta original: Windows dice si esa es su contrasena, antes de tocar nada; con una mala,
// el equipo se quedaria en la pantalla de contrasena en cada arranque. Y como su pantalla sera
// DigLab, sin memoria de emergencia ni otra cuenta no hay otro camino a su escritorio.
function ComprobarCuentaOriginal: Boolean;
var
  Lineas: TArrayOfString;
  Estado, SinMemorias: String;
begin
  Result := False;
#ifndef VistaPrevia
  SetEnvironmentVariable(VarClaveCuenta, TxtClave.Text);
  try
    Configurador('validar-cuenta', '--original --cuenta ' + AddQuotes(CuentaActual), Lineas);
  finally
    SetEnvironmentVariable(VarClaveCuenta, '');
  end;

  Estado := Renglon(Lineas, 'ESTADO');
  if Estado <> 'OK' then
  begin
    if Estado = '' then
      Estado := Renglon(Lineas, 'ERROR');
    MsgBox(Estado, mbError, MB_OK);
    Exit;
  end;

#endif
  // Las memorias se registran en DigLab, no aqui: solo se avisa si se sabe que no hay ninguna.
  SinMemorias := '';
  if MemoriasRegistradas = '0' then
    SinMemorias := 'Todavía no hay ninguna memoria de emergencia registrada en DigLab (se registran en Laboratorios > Memorias de emergencia).'
  else if MemoriasRegistradas = 'NO' then
    SinMemorias := 'La base todavía no registra memorias de emergencia: le falta la actualización db/28.';
  if SinMemorias <> '' then
    if MsgBox(SinMemorias + #13#10#13#10 +
      'DigLab será la pantalla de «' + CuentaActual + '». Si falla y no hay otra cuenta en el equipo, no habrá cómo llegar a su escritorio.'#13#10#13#10 +
      '¿Seguir sin memoria de emergencia?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
      Exit;

  Result := True;
end;

function ComprobarEquipo: Boolean;
var
  Lineas: TArrayOfString;
  Estado: String;
begin
  Result := False;
  if Trim(CboLaboratorio.Text) = '' then
  begin
    MsgBox('Elige el laboratorio donde está este equipo.', mbError, MB_OK);
    Exit;
  end;

  if RbOriginal.Checked then
  begin
    Result := ComprobarCuentaOriginal;
    Exit;
  end;

  if TxtClave.Text = '' then
  begin
    MsgBox('Escribe la contraseña de la cuenta del laboratorio: hace falta para que entre sola.', mbError, MB_OK);
    Exit;
  end;

  if TxtClave.Text <> TxtClave2.Text then
  begin
    MsgBox('Las dos contraseñas de la cuenta no son iguales.', mbError, MB_OK);
    Exit;
  end;

  Configurador('validar-cuenta', '--cuenta ' + AddQuotes(Trim(TxtCuenta.Text)), Lineas);
  Estado := Renglon(Lineas, 'ESTADO');
  if Estado <> 'OK' then
  begin
    if Estado = '' then
      Estado := Renglon(Lineas, 'ERROR');
    MsgBox(Estado, mbError, MB_OK);
    Exit;
  end;

  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PaginaBase.ID then
    Result := ComprobarBase
  else if CurPageID = PaginaEquipo.ID then
    Result := ComprobarEquipo;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  Texto: String;
begin
  if Actualizando then
  begin
    Texto := 'Se actualiza DigLab a la versión {#MiVersion}.' + NewLine + NewLine +
      'Este equipo sigue como: ' + NombreDelTipo + NewLine;
    if EsKiosco then
      Texto := Texto + Space + 'Laboratorio: ' + LaboratorioPrevio + NewLine + Space + 'Cuenta: ' + CuentaPrevia + NewLine +
        Space + 'Si esa cuenta tiene sesión abierta, se cierra: que nadie lo esté usando.' + NewLine +
        Space + 'Al terminar se pide reiniciar, para que vuelva a ser kiosco.' + NewLine;
    Texto := Texto + NewLine + 'Se conservan su conexión a la base, sus ajustes y sus datos.' + NewLine;
  end
  else
  begin
    Texto := 'Tipo de equipo: ' + NombreDelTipo + NewLine + 'Carpeta: {#Destino}' + NewLine +
      'Base de datos: ' + Trim(PaginaBase.Values[1]) + ' en ' + Trim(PaginaBase.Values[0]) + NewLine;
    if EsKiosco then
    begin
      Texto := Texto + NewLine + 'En este equipo:' + NewLine +
        Space + 'Laboratorio: ' + Trim(CboLaboratorio.Text) + NewLine;
      if Trim(TxtNumero.Text) <> '' then
        Texto := Texto + Space + 'Máquina: ' + Trim(TxtNumero.Text) + NewLine;
      Texto := Texto + Space + 'La cuenta «' + CuentaElegida + '» entrará sola y DigLab será su pantalla.' + NewLine;
      if RbOriginal.Checked then
        Texto := Texto + Space + 'No se crea otra cuenta; su contraseña y sus permisos no cambian.' + NewLine +
          Space + 'Su contraseña queda guardada en Windows para poder entrar sola.' + NewLine +
          Space + 'Tras la bitácora, se usa su escritorio normal, con sus permisos.' + NewLine +
          Space + 'Se instala el servicio DigLab Vigilante.' + NewLine +
          Space + 'Para llegar al escritorio sin bitácora: una memoria de emergencia registrada en DigLab.' + NewLine
      else
        Texto := Texto + Space + 'Se instala el servicio DigLab Vigilante.' + NewLine +
          Space + 'Para entrar como administrador: Ctrl+Alt+Supr, Cambiar de usuario.' + NewLine;
    end;
  end;

  Texto := Texto + NewLine + 'Microsoft Defender:' + NewLine +
    Space + 'Se excluye DigLab.exe';
  if EsKiosco then
    Texto := Texto + ' y DigLab.Vigilante.exe';
  Result := Texto + ',' + NewLine +
    Space + 'porque Defender los puede confundir con un virus (falso positivo).' + NewLine +
    Space + 'Se quita al desinstalar.' + NewLine;
end;

// ---------------------------------------------------------------------------
// Instalar
// ---------------------------------------------------------------------------

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Lineas: TArrayOfString;
begin
  Result := '';
#ifdef VistaPrevia
  Result := 'Es la vista previa: aquí se termina, sin instalar nada.';
  Exit;
#endif
  SetEnvironmentVariable(VarSetup, '1');
  if Configurador('preparar', '--destino ' + AddQuotes('{#Destino}') + ' --modo ' + ModoElegido +
    ' --ajustes ' + AddQuotes(ExpandConstant('{tmp}\ajustes.txt')), Lineas) <> 0 then
  begin
    Result := Renglon(Lineas, 'ERROR');
    if Result = '' then
      Result := 'No se pudo preparar el equipo para instalar.';
  end;
end;

procedure MostrarResultado(const Configurar, Revisar: TArrayOfString; Bien: Boolean);
var
  Texto: String;
  I, Malas: Integer;
begin
  Texto := '';
  if not Bien then
    Texto := 'LA CONFIGURACIÓN NO TERMINÓ:'#13#10 + Renglon(Configurar, 'ERROR') + #13#10#13#10 +
      'Los archivos quedaron copiados. Corrige eso y vuelve a abrir este instalador: repetirlo no rompe nada.'#13#10#13#10;
  for I := 0 to GetArrayLength(Configurar) - 1 do
    if Pos('AVISO ', Configurar[I]) = 1 then
      Texto := Texto + 'Aviso: ' + Copy(Configurar[I], 7, Length(Configurar[I])) + #13#10;

  Malas := 0;
  for I := 0 to GetArrayLength(Revisar) - 1 do
    if Pos('BIEN ', Revisar[I]) = 1 then
      Texto := Texto + '[ok]  ' + Copy(Revisar[I], 6, Length(Revisar[I])) + #13#10
    else if Pos('MAL ', Revisar[I]) = 1 then
    begin
      Texto := Texto + '[REVISAR]  ' + Copy(Revisar[I], 5, Length(Revisar[I])) + #13#10;
      Malas := Malas + 1;
    end
    else if Pos('AVISO ', Revisar[I]) = 1 then
      Texto := Texto + '[aviso]  ' + Copy(Revisar[I], 7, Length(Revisar[I])) + #13#10;

  if Bien and (Malas = 0) then
    PaginaResultado.SubCaptionLabel.Caption := 'Todo quedó bien.'
  else
    PaginaResultado.SubCaptionLabel.Caption := 'Hay cosas que revisar.';
  PaginaResultado.RichEditViewer.Lines.Text := Texto;
end;

#ifdef VistaPrevia
// El resultado bueno y luego uno con de todo, que es el que se queda a la vista.
procedure MostrarMuestra;
var
  Configurar, Revisar: TArrayOfString;
begin
  SetArrayLength(Configurar, 1);
  Configurar[0] := 'PASO Sin avisos';
  SetArrayLength(Revisar, 1);
  Revisar[0] := 'BIEN DigLab está en {#Destino}';
  MostrarResultado(Configurar, Revisar, True);
  if PaginaResultado.SubCaptionLabel.Caption <> 'Todo quedó bien.' then
    MsgBox('La vista previa esperaba «Todo quedó bien.»', mbError, MB_OK);

  SetArrayLength(Configurar, 2);
  Configurar[0] := 'ERROR Un error de muestra al configurar';
  Configurar[1] := 'AVISO Un aviso de muestra al configurar';
  SetArrayLength(Revisar, 3);
  Revisar[0] := 'BIEN Algo que quedó bien';
  Revisar[1] := 'MAL Algo que hay que revisar';
  Revisar[2] := 'AVISO Algo que conviene saber';
  MostrarResultado(Configurar, Revisar, False);
end;
#endif

// Cada vez que se llega a la pagina.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = PaginaEquipo.ID then
    CuentaCambiada(nil);
#ifdef VistaPrevia
  if CurPageID = PaginaResultado.ID then
    MostrarMuestra;
#endif
end;

procedure ConfigurarEquipo;
var
  Lineas, Revision: TArrayOfString;
  Parametros: String;
begin
  Parametros := '--destino ' + AddQuotes('{#Destino}') + ' --modo ' + ModoElegido;
  if Actualizando then
  begin
    Parametros := Parametros + ' --actualizar --ajustes ' + AddQuotes(ExpandConstant('{tmp}\ajustes.txt'));
    if CuentaPrevia <> '' then
      Parametros := Parametros + ' --cuenta ' + AddQuotes(CuentaPrevia);
  end
  else
  begin
    PonerConexion;
    if EsKiosco then
    begin
      SetEnvironmentVariable(VarClaveCuenta, TxtClave.Text);
      Parametros := Parametros + ' --cuenta ' + AddQuotes(CuentaElegida) + ' --laboratorio ' + AddQuotes(Trim(CboLaboratorio.Text));
      if RbOriginal.Checked then
        Parametros := Parametros + ' --original';
      if Trim(TxtNumero.Text) <> '' then
        Parametros := Parametros + ' --numero ' + AddQuotes(Trim(TxtNumero.Text));
    end;
  end;

  WizardForm.StatusLabel.Caption := 'Configurando el equipo; puede tardar un minuto...';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    ConfiguradoBien := Configurador('configurar', Parametros, Lineas) = 0;
    QuitarSecretos;
    WizardForm.StatusLabel.Caption := 'Comprobando cómo quedó...';
    Parametros := '--destino ' + AddQuotes('{#Destino}') + ' --modo ' + ModoElegido;
    if CuentaElegida <> '' then
      Parametros := Parametros + ' --cuenta ' + AddQuotes(CuentaElegida);
    Configurador('diagnostico', Parametros, Revision);
    MostrarResultado(Lineas, Revision, ConfiguradoBien);
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
    QuitarSecretos;
    SetEnvironmentVariable(VarSetup, '');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigurarEquipo;
end;

// Un kiosco arranca como kiosco despues de reiniciar, tambien al actualizar, que le cierra
// la sesion: el Finalizar lo ofrece, y en silencio (la actualizacion desde la base que lanza
// el vigilante, /VERYSILENT) reinicia solo. Sin mirar ConfiguradoBien: Inno pregunta esto
// ANTES de ssPostInstall, cuando el configurador aun no corre (con "and ConfiguradoBien" nunca
// se ofrecia; visto en la VM el 2026-11-13).
function NeedRestart: Boolean;
begin
  Result := EsKiosco;
end;

// ---------------------------------------------------------------------------
// Desinstalar
// ---------------------------------------------------------------------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Parametros, Exe, CuentaKiosco, Original: String;
  Codigo: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  Exe := ExpandConstant('{app}\Configurador\DigLab.Configurador.exe');
  Parametros := 'revertir --destino ' + AddQuotes(ExpandConstant('{app}'));
  if MsgBox('¿Borrar también los datos locales de DigLab?'#13#10#13#10 +
    'Ahí pueden quedar bitácoras que todavía no llegaron al servidor. Si no estás seguro, responde No.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    Parametros := Parametros + ' --borrar-datos';
  // La cuenta que el equipo ya tenia no se ofrece: no la creo DigLab, y ahi esta todo lo del equipo.
  if not RegQueryStringValue(HKLM, 'SOFTWARE\DigLab', 'CuentaOriginal', Original) then
    Original := '';
  if (Original <> '1') and RegQueryStringValue(HKLM, 'SOFTWARE\DigLab', 'CuentaKiosco', CuentaKiosco) and (CuentaKiosco <> '') then
    if MsgBox('¿Borrar también la cuenta de Windows «' + CuentaKiosco + '» y su perfil?'#13#10#13#10 +
      'Si respondes No, se queda como una cuenta normal, sin DigLab.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Parametros := Parametros + ' --borrar-cuenta';
  SetEnvironmentVariable(VarSetup, '1');
  if not Exec(Exe, Parametros, ExpandConstant('{app}\Configurador'), SW_HIDE, ewWaitUntilTerminated, Codigo) or (Codigo <> 0) then
    MsgBox('No se pudo deshacer toda la configuración de este equipo. Reinícialo y revisa que puedas entrar con tu cuenta.', mbError, MB_OK);
  SetEnvironmentVariable(VarSetup, '');
end;
