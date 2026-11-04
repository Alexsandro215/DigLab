; El instalador de DigBit: DigBit-Setup.exe, el asistente normal de Windows.
; No se compila a mano: lo arma crear_setup.ps1, que prepara la carpeta de carga (sin la
; contrasena de la base) y le pasa estas definiciones con /D:
;   Carga          la carpeta de carga: DigBit\, Vigilante\ y Configurador\
;   Temporales     el archivo con la lista de lo que se extrae antes de instalar
;   BaseServidor, BaseNombre, BaseUsuario, BasePuerto    la conexion, SIN contrasena
;   VistaPrevia    (opcional) para ver las paginas sin instalar nada ni pedir administrador
;
; Lo que el asistente no puede hacer solo (comprobar la base, configurar el equipo) lo hace
; DigBit.Configurador.exe. Las contrasenas le llegan en variables de entorno, nunca en la
; linea de comandos, que cualquiera puede leer en el Administrador de tareas. Nada de
; PowerShell en el equipo de destino: en algunas PCs esta bloqueado o limitado.

#ifndef Carga
  #error Arma el instalador con crear_setup.ps1
#endif

#ifdef VistaPrevia
  ; Su valor es la carpeta. Una que no existe: las paginas del camino completo, aunque DigBit
  ; este instalado.
  #define Destino VistaPrevia
#else
  #define Destino "C:\DigBit"
#endif
#define MiVersion GetVersionNumbersString(Carga + "\DigBit\DigBit.exe")

[Setup]
AppId={{5B8E2F14-7C3A-4D19-9F6E-2A1C8D3B7E60}
AppName=DigBit
AppVersion={#MiVersion}
AppVerName=DigBit {#MiVersion}
VersionInfoVersion={#MiVersion}
VersionInfoDescription=Instalador de DigBit
AppPublisher=TESCHI - Sistemas Computacionales
; C:\DigBit y sin preguntar: es donde ya esta instalado en los equipos, y el kiosco lo probo
; de punta a punta ahi. El shell de Winlogon y sus permisos dependen de esa ruta.
DefaultDirName={#Destino}
AppendDefaultDirName=no
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
DisableWelcomePage=no
OutputBaseFilename=DigBit-Setup
SetupIconFile={#Carga}\icono.ico
UninstallDisplayIcon={app}\DigBit.exe
UninstallDisplayName=DigBit
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=no
; DigBit y el vigilante los detiene el configurador antes de copiar.
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
Source: "{#Carga}\DigBit\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Carga}\Vigilante\*"; DestDir: "{app}\Vigilante"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EsKiosco
; Se queda instalado para poder deshacer el kiosco al desinstalar.
Source: "{#Carga}\Configurador\DigBit.Configurador.exe"; DestDir: "{app}\Configurador"; Flags: ignoreversion

[Icons]
; En un kiosco DigBit ES el escritorio: no hace falta nada donde hacer doble clic.
Name: "{autoprograms}\DigBit"; Filename: "{app}\DigBit.exe"; WorkingDir: "{app}"; Check: not EsKiosco
Name: "{autodesktop}\DigBit"; Filename: "{app}\DigBit.exe"; WorkingDir: "{app}"; Check: not EsKiosco

[Run]
; Como el usuario y no como administrador: elevado, sus registros y datos irian a otro sitio.
Filename: "{app}\DigBit.exe"; Description: "Abrir DigBit"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: not EsKiosco

[UninstallDelete]
; connections.config y demas los escribe el configurador: el desinstalador no los conoce.
Type: filesandordirs; Name: "{app}"

[Code]
const
  VarSetup = 'DIGBIT_SETUP';
  VarServidor = 'DIGBIT_BD_SERVIDOR';
  VarBase = 'DIGBIT_BD_BASE';
  VarPuerto = 'DIGBIT_BD_PUERTO';
  VarUsuario = 'DIGBIT_BD_USUARIO';
  VarClave = 'DIGBIT_BD_CLAVE';
  VarClaveCuenta = 'DIGBIT_CLAVE_CUENTA';
  ModoAdm = 'adm';
  ModoMaestro = 'maestro';
  ModoKiosco = 'kiosco';
  VkCapital = 20;

var
  PaginaTipo: TInputOptionWizardPage;
  PaginaBase: TInputQueryWizardPage;
  PaginaEquipo: TWizardPage;
  PaginaResultado: TOutputMsgMemoWizardPage;
  CboLaboratorio: TNewComboBox;
  TxtNumero, TxtCuenta: TNewEdit;
  TxtClave, TxtClave2: TPasswordEdit;
  ChkMemoria: TNewCheckBox;
  LblMemoria: TNewStaticText;
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
  if not Exec(ExpandConstant('{tmp}\DigBit.Configurador.exe'), Orden + ' ' + Parametros + ' --salida ' + AddQuotes(Archivo),
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

function CuentaElegida: String;
begin
  if Actualizando then
    Result := CuentaPrevia
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

function InitializeSetup: Boolean;
var
  Lineas: TArrayOfString;
begin
  Result := True;
  if not IsDotNetInstalled(net472, 0) then
  begin
    MsgBox('DigBit necesita Microsoft .NET Framework 4.7.2 o más nuevo, y este equipo no lo tiene.'#13#10#13#10 +
      'Instálalo con Windows Update o desde la página de Microsoft, y vuelve a abrir este instalador.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

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

procedure MemoriaCambiada(Sender: TObject);
var
  Lineas: TArrayOfString;
  Cuantas, I: Integer;
begin
  LblMemoria.Caption := '';
  if not ChkMemoria.Checked then
    Exit;
  Configurador('memorias', '', Lineas);
  Cuantas := 0;
  for I := 0 to GetArrayLength(Lineas) - 1 do
    if Pos('MEMORIA ', Lineas[I]) = 1 then
      Cuantas := Cuantas + 1;
  if Cuantas = 1 then
    LblMemoria.Caption := 'Se autorizará la memoria conectada: ' + Renglon(Lineas, 'MEMORIA')
  else if Cuantas = 0 then
    LblMemoria.Caption := 'No hay ninguna memoria USB conectada: conéctala antes de seguir.'
  else
    LblMemoria.Caption := 'Hay ' + IntToStr(Cuantas) + ' memorias conectadas: deja solo la que quieras autorizar.';
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
  PaginaTipo.Add('Equipo de laboratorio (kiosco): DigBit será la pantalla de los estudiantes');
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

  Etiqueta(PaginaEquipo, 'Cuenta de Windows del laboratorio (se crea si no existe):', ScaleY(96));
  TxtCuenta := TNewEdit.Create(PaginaEquipo);
  TxtCuenta.Text := 'laboratorio';
  ColocarCaja(TxtCuenta, PaginaEquipo, ScaleY(114), ScaleX(200));

  Etiqueta(PaginaEquipo, 'Contraseña de esa cuenta, y otra vez para comprobarla:', ScaleY(144));
  TxtClave := TPasswordEdit.Create(PaginaEquipo);
  ColocarCaja(TxtClave, PaginaEquipo, ScaleY(162), ScaleX(200));
  TxtClave2 := TPasswordEdit.Create(PaginaEquipo);
  ColocarCaja(TxtClave2, PaginaEquipo, ScaleY(162), ScaleX(200));
  TxtClave2.Left := ScaleX(210);

  ChkMemoria := TNewCheckBox.Create(PaginaEquipo);
  ChkMemoria.Caption := 'Autorizar la memoria USB conectada como llave de emergencia';
  ColocarCaja(ChkMemoria, PaginaEquipo, ScaleY(194), Ancho);
  ChkMemoria.Height := ScaleY(18);
  ChkMemoria.OnClick := @MemoriaCambiada;
  LblMemoria := Etiqueta(PaginaEquipo, '', ScaleY(214));

  PaginaResultado := CreateOutputMsgMemoPage(wpInstalling, 'Comprobación del equipo',
    'Esto es lo que se revisó al terminar.', '', '');
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
      'Si este equipo todavía no tiene red, puedes instalar igual: DigBit se conectará cuando la tenga.'#13#10 +
      '¿Instalar de todos modos?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
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
    Texto := 'Se actualiza DigBit a la versión {#MiVersion}.' + NewLine + NewLine +
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
      Texto := Texto + Space + 'La cuenta «' + Trim(TxtCuenta.Text) + '» entrará sola y DigBit será su pantalla.' + NewLine +
        Space + 'Se instala el servicio DigBit Vigilante.' + NewLine +
        Space + 'Para entrar como administrador: Ctrl+Alt+Supr, Cambiar de usuario.' + NewLine;
    end;
  end;

  Texto := Texto + NewLine + 'Microsoft Defender:' + NewLine +
    Space + 'Se excluye DigBit.exe';
  if EsKiosco then
    Texto := Texto + ' y DigBit.Vigilante.exe';
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
      Parametros := Parametros + ' --cuenta ' + AddQuotes(Trim(TxtCuenta.Text)) + ' --laboratorio ' + AddQuotes(Trim(CboLaboratorio.Text));
      if Trim(TxtNumero.Text) <> '' then
        Parametros := Parametros + ' --numero ' + AddQuotes(Trim(TxtNumero.Text));
      if ChkMemoria.Checked then
        Parametros := Parametros + ' --memoria';
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
// la sesion: el Finalizar lo ofrece.
function NeedRestart: Boolean;
begin
  Result := EsKiosco and ConfiguradoBien;
end;

// ---------------------------------------------------------------------------
// Desinstalar
// ---------------------------------------------------------------------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Parametros, Exe, CuentaKiosco: String;
  Codigo: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  Exe := ExpandConstant('{app}\Configurador\DigBit.Configurador.exe');
  Parametros := 'revertir --destino ' + AddQuotes(ExpandConstant('{app}'));
  if MsgBox('¿Borrar también los datos locales de DigBit?'#13#10#13#10 +
    'Ahí pueden quedar bitácoras que todavía no llegaron al servidor. Si no estás seguro, responde No.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    Parametros := Parametros + ' --borrar-datos';
  if RegQueryStringValue(HKLM, 'SOFTWARE\DigBit', 'CuentaKiosco', CuentaKiosco) and (CuentaKiosco <> '') then
    if MsgBox('¿Borrar también la cuenta de Windows «' + CuentaKiosco + '» y su perfil?'#13#10#13#10 +
      'Si respondes No, se queda como una cuenta normal, sin DigBit.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Parametros := Parametros + ' --borrar-cuenta';
  SetEnvironmentVariable(VarSetup, '1');
  if not Exec(Exe, Parametros, ExpandConstant('{app}\Configurador'), SW_HIDE, ewWaitUntilTerminated, Codigo) or (Codigo <> 0) then
    MsgBox('No se pudo deshacer toda la configuración de este equipo. Reinícialo y revisa que puedas entrar con tu cuenta.', mbError, MB_OK);
  SetEnvironmentVariable(VarSetup, '');
end;
