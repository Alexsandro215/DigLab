<?php
// DigBit - API de recuperacion de contrasena (ver LEEME.md). PHP 7.2 o posterior.
//
// La llama DigBit desde "Olvidaste tu contrasena?" del inicio de sesion, por
// POST (formulario o JSON) y con la cabecera X-DigBit-Clave:
//   accion=solicitar  matricula                       -> manda un codigo de 6 cifras a su correo
//   accion=confirmar  matricula, codigo, contrasena   -> si el codigo vale, pone la contrasena nueva
//   accion=probar                                     -> (Ajustes del administrador) base y correo listos; no manda nada
// Responde JSON: {"ok": true|false, "mensaje": "...", ...}. El mensaje esta
// escrito para el alumno: DigBit lo ensena tal cual.

define('DIGBIT_API', true);
// Dias que se guarda cada solicitud (con la matricula, la IP y el equipo de quien
// pidio): los limites solo miran la ultima hora y el ultimo dia. Es el plazo que
// debe decir el aviso de privacidad.
define('DIAS_SOLICITUDES', 30);
require __DIR__ . '/lib/comun.php';
require __DIR__ . '/lib/correo.php';

// La contrasena del correo y la de la base no deben salir en ningun error.
ini_set('display_errors', '0');
set_exception_handler(function ($e) {
    error_log('DigBit API: ' . get_class($e) . ': ' . $e->getMessage());
    responder(500, false, 'El servicio de recuperación tuvo un problema y no pudo atenderte. Inténtalo en un momento; '
        . 'si sigue, avisa a la persona responsable del laboratorio.');
});

// DigBit no sigue redirecciones: esto solo lo ve quien abre la direccion en el navegador.
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    responder(405, false, 'Esta es la API de recuperación de contraseña de DigBit: solo atiende a DigBit, por POST.');
}

$c = cfg();
// Con el secreto de ejemplo cualquiera que lea este codigo podria calcular los
// codigos guardados: no se atiende hasta que se cambie.
if (strlen((string)$c['secreto']) < 24 || stripos((string)$c['secreto'], 'CAMBIA') !== false) {
    error_log('DigBit API: el secreto de config.php sigue siendo el de ejemplo o es muy corto.');
    responder(500, false, 'El servicio de recuperación no está configurado. Avisa a la persona responsable del laboratorio.');
}

// La clave de ejemplo tampoco: es la que cualquiera probaria primero.
if (stripos((string)$c['clave_api'], 'CAMBIA') !== false) {
    error_log('DigBit API: la clave_api de config.php sigue siendo la de ejemplo.');
    responder(500, false, 'El servicio de recuperación no está configurado. Avisa a la persona responsable del laboratorio.');
}

if (!empty($c['solo_https']) && !es_https()) {
    responder(403, false, 'El servicio de recuperación solo atiende por https.');
}

$clave = isset($_SERVER['HTTP_X_DIGBIT_CLAVE']) ? (string)$_SERVER['HTTP_X_DIGBIT_CLAVE'] : '';
if ((string)$c['clave_api'] !== '' && !hash_equals((string)$c['clave_api'], $clave)) {
    // DigBit busca "autorizado" en este mensaje (Ajustes, Probar): no cambiar esa palabra.
    responder(403, false, 'Este equipo no está autorizado para recuperar contraseñas. Avisa a la persona responsable del laboratorio.');
}

$datos = entrada();
$accion = texto($datos, 'accion', 20);
if ($accion === 'probar') {
    probar();
}

$matricula = trim(texto($datos, 'matricula', 45));
if ($matricula === '' || !preg_match('/^[A-Za-z0-9]+$/', $matricula)) {
    responder(400, false, 'Escribe tu matrícula (o tu número de empleado), sin espacios.');
}

if ($accion === 'solicitar') {
    solicitar($matricula);
} elseif ($accion === 'confirmar') {
    // 600 bytes: 128 caracteres de hasta 4 bytes caben sin cortarse antes de medirlos.
    confirmar($matricula, trim(texto($datos, 'codigo', 12)), texto($datos, 'contrasena', 600));
}

// Solo con un DigBit de otra version que la API (o alguien que no es DigBit).
responder(400, false, 'El servicio de recuperación no reconoce lo que pidió este equipo: DigBit y el servicio no son de la misma versión. '
    . 'Avisa a la administración de DigBit.');

// ---------------------------------------------------------------------------

/**
 * Alumnos y profesores activos (el administrador se recupera a mano). Null si
 * no existe, esta dado de baja o es de otro tipo.
 */
function buscar_usuario($matricula)
{
    $activo = tiene_bajas() ? ' AND activo = 1' : '';
    return fila('SELECT idusuarios, nombre, correo FROM usuarios
                 WHERE numero_identificador = ? AND fk_tipo_usuario IN (1, 2, 4)' . $activo . '
                 ORDER BY idusuarios LIMIT 1', array($matricula));
}

/**
 * El boton "Probar" de Ajustes en DigBit: si llega aqui, la direccion y la
 * clave son buenas. Comprueba la base (y los permisos que usan solicitar y
 * confirmar) y que la cuenta de correo deja entrar, sin mandar ningun correo.
 * Tambien borra las solicitudes viejas, como solicitar: sin permiso para eso
 * la recuperacion funciona igual, pero se dice que orden darle a root.
 */
function probar()
{
    $c = cfg();
    // Ninguna de estas toca una fila, pero el servidor comprueba los permisos al ejecutarlas.
    $consultas = array(
        'SELECT COUNT(*) FROM recuperacion_contrasena WHERE 1 = 0',
        'SELECT equipo, correo_hash FROM recuperacion_contrasena WHERE 1 = 0',
        'SELECT idusuarios, numero_identificador, nombre, correo, fk_tipo_usuario FROM usuarios WHERE 1 = 0',
        "UPDATE usuarios SET password = '' WHERE 1 = 0 AND idusuarios = 0 AND fk_tipo_usuario IN (1, 2, 4)",
        'UPDATE recuperacion_contrasena SET intentos = intentos WHERE 1 = 0',
    );
    try {
        foreach ($consultas as $sql) {
            ejecutar($sql);
        }

        // Con db/08 la API filtra a los dados de baja: sin permiso para ver
        // "activo" no lo notaria (information_schema la esconde) y los dejaria pasar.
        try {
            ejecutar('SELECT activo FROM usuarios WHERE 1 = 0');
        } catch (PDOException $e) {
            if (codigo_mysql($e) !== 1054) {
                throw $e;
            }
        }

        limitar_pruebas();
        anotar_sin_codigo(null, '(prueba)');
    } catch (PDOException $e) {
        error_log('DigBit API (probar): ' . $e->getMessage());
        responder(500, false, 'La API responde y la clave es correcta, pero no puede usar la base: ' . problema_de_base($e));
    }

    // Con lo que falta, los textos de abajo son mas cortos: en Ajustes caben 4 lineas.
    $falta = borrar_antiguas() ? '' : ' Falta un permiso para borrar las solicitudes de más de ' . DIAS_SOLICITUDES
        . ' días; pide a root: ' . orden_para_borrar();

    $cuenta = (string)$c['smtp_usuario'] !== '' ? (string)$c['smtp_usuario'] : (string)$c['remitente'];
    if (trim((string)$c['smtp_servidor']) === '') {
        responder(200, true, $falta !== ''
            ? 'La API responde y la base está lista; el correo sale con mail() del hosting (pruébalo pidiendo un código).' . $falta
            : 'La API responde, la clave es correcta y la base está lista. El correo sale con mail() del hosting, '
                . 'que no se puede probar sin enviar: pide un código con una cuenta de prueba que tenga tu correo.');
    }

    try {
        smtp_probar();
    } catch (Throwable $e) {
        // Lo que contesto el servidor de correo (a veces en ingles) va al registro;
        // a quien prueba, la causa y que revisar.
        error_log('DigBit API (probar): ' . $e->getMessage());
        // Corto: en Ajustes caben 4 lineas, tras "El servicio contesto: ".
        responder(502, false, 'La API y la base están listas, pero el correo ' . $cuenta . ' falló: ' . explicar_fallo_correo($e));
    }

    responder(200, true, $falta !== ''
        ? 'La API responde, la base está lista y el correo ' . $cuenta . ' deja entrar.' . $falta
        : 'Todo listo: la API responde, la clave es correcta, la base tiene la tabla de códigos y los permisos, '
            . 'y el correo ' . $cuenta . ' deja entrar con su contraseña de aplicación.');
}

/** Para cada limite, lo de config.php o, si ese config.php es anterior, el valor por omision. */
function limite($clave, $porOmision)
{
    $c = cfg();
    return isset($c[$clave]) ? (int)$c[$clave] : $porOmision;
}

/**
 * El equipo que pide, o '' si no lo dice. DigBit manda "numero @ laboratorio"
 * codificado como en una direccion (X-DigBit-Equipo: 12%20%40%20Sistemas...):
 * se guarda legible, con sus acentos, y solo con letras, cifras y los signos
 * de los nombres de equipo y laboratorio.
 */
function equipo_cliente()
{
    $equipo = isset($_SERVER['HTTP_X_DIGBIT_EQUIPO']) ? rawurldecode((string)$_SERVER['HTTP_X_DIGBIT_EQUIPO']) : '';
    $limpio = preg_replace(array('/[^\p{L}\p{N} @._\-#()\/]/u', '/[\x{10000}-\x{10FFFF}]/u'), '', $equipo);
    if ($limpio === null) {
        // No es UTF-8 valido: solo lo ASCII imprimible.
        $limpio = preg_replace('/[^\x20-\x7E]/', '', $equipo);
    }

    return mb_substr_seguro(trim(preg_replace('/ {2,}/', ' ', $limpio)), 60);
}

/**
 * Matriculas que no existen o sin correo desde esta IP. Aparte de los codigos
 * enviados: quien teclee basura no deja a los demas sin recuperar su cuenta.
 */
function limitar_fallidas()
{
    $fallidas = (int)valor("SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE ip = ? AND codigo_hash IS NULL AND matricula <> '(prueba)' AND creado_en > NOW() - INTERVAL 1 HOUR", array(ip_cliente()));
    if ($fallidas >= limite('fallidas_por_ip_hora', 200)) {
        responder(429, false, 'Se escribieron demasiadas matrículas equivocadas desde esta red en la última hora. Revisa la tuya y espera un rato.');
    }
}

/** El boton Probar entra en la cuenta de correo cada vez: pocas por hora. */
function limitar_pruebas()
{
    $pruebas = (int)valor("SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE ip = ? AND matricula = '(prueba)' AND creado_en > NOW() - INTERVAL 1 HOUR", array(ip_cliente()));
    if ($pruebas >= 30) {
        responder(429, false, 'Se probó demasiadas veces en la última hora. Espera un rato.');
    }
}

/**
 * Los cupos antes de mandar un codigo: por cuenta, por equipo, por IP (toda la
 * escuela puede salir a internet por una sola) y de toda la API (Gmail admite
 * unos 500 correos al dia desde una cuenta normal; pasado eso deja de enviar y
 * puede bloquearla). Solo cuentan los codigos que salieron. Si se acabo alguno
 * pero la cuenta tiene un codigo que todavia vale, se le dice que use ese (con
 * el correo en la respuesta, DigBit pasa a escribirlo).
 */
function comprobar_cupos($id, $correo)
{
    $porCuenta = (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 HOUR', array($id));
    $equipo = equipo_cliente();
    $porEquipo = $equipo === '' ? 0 : (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE equipo = ? AND codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 HOUR', array($equipo));
    $porIp = (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE ip = ? AND codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 HOUR', array(ip_cliente()));
    $enLaHora = (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 HOUR');
    $enElDia = (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 DAY');

    if ($porCuenta >= limite('solicitudes_por_usuario_hora', 3)) {
        $motivo = 'cuenta';
    } elseif ($equipo !== '' && $porEquipo >= limite('codigos_por_equipo_hora', 6)) {
        $motivo = 'equipo';
    } elseif ($porIp >= limite('solicitudes_por_ip_hora', 60)) {
        $motivo = 'ip';
    } elseif ($enLaHora >= limite('correos_por_hora', 100) || $enElDia >= limite('correos_por_dia', 400)) {
        $motivo = 'todos';
        error_log('DigBit API: se llego al tope de correos (' . $enLaHora . ' en una hora, ' . $enElDia . ' en un dia).');
    } else {
        return;
    }

    $vivo = (int)valor('SELECT COUNT(*) FROM recuperacion_contrasena
        WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND usado_en IS NULL AND expira_en > NOW()', array($id)) > 0;
    if ($vivo) {
        responder(429, false, ($motivo === 'cuenta'
                ? 'Ya te enviamos ' . $porCuenta . ' códigos en la última hora a ' . ocultar_correo($correo) . '.'
                : 'Ahora no se pueden mandar más códigos.')
            . ' Usa el del último correo que te llegó (revisa también la carpeta de spam): todavía vale.',
            array('correo' => ocultar_correo($correo)));
    }

    if ($motivo === 'cuenta') {
        // Hacia arriba: el cupo se libera en el segundo exacto, no al empezar ese minuto.
        $cuando = valor("SELECT DATE_FORMAT(MIN(creado_en) + INTERVAL 1 HOUR + INTERVAL 59 SECOND, '%H:%i') FROM recuperacion_contrasena
            WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND creado_en > NOW() - INTERVAL 1 HOUR", array($id));
        responder(429, false, 'Ya te enviamos ' . $porCuenta . ' códigos en la última hora y ya no vale ninguno. '
            . 'Podrás pedir otro a las ' . $cuando . ', o pide a la administración de DigBit que te ponga una contraseña nueva.');
    }

    if ($motivo === 'equipo') {
        responder(429, false, 'Se pidieron demasiados códigos desde este equipo en la última hora. Usa otro equipo o espera un rato.');
    }

    if ($motivo === 'ip') {
        responder(429, false, 'Se pidieron demasiados códigos desde esta red en la última hora. Espera un rato o avisa a la persona responsable del laboratorio.');
    }

    responder(429, false, 'Ahora se están pidiendo demasiados códigos. Inténtalo más tarde o pide a la administración de DigBit que te ponga una contraseña nueva.');
}

/**
 * Para volver a comprobar los cupos e insertar el codigo de uno en uno: sin
 * esto, varias peticiones a la vez leen el mismo contador y se los saltan. Solo
 * llegan aqui las que van a mandar un correo (lo demas se rechaza antes, sin
 * candado), y se suelta antes de mandarlo. Si algo corta antes, lo suelta el
 * servidor al cerrarse la conexion, al terminar la peticion.
 */
function tomar_candado()
{
    if ((int)valor("SELECT GET_LOCK('digbit_recuperar', 3)") !== 1) {
        responder(503, false, 'El servicio de recuperación está ocupado. Inténtalo en unos segundos.');
    }
}

function soltar_candado()
{
    valor("SELECT RELEASE_LOCK('digbit_recuperar')");
}

/** Deja constancia de una peticion sin codigo (para los limites). */
function anotar_sin_codigo($idUsuario, $matricula)
{
    ejecutar('INSERT INTO recuperacion_contrasena (usuario_id, matricula, codigo_hash, creado_en, expira_en, ip, equipo)
              VALUES (?, ?, NULL, NOW(), NOW(), ?, ?)', array($idUsuario, $matricula, ip_cliente(), equipo_cliente()));
}

/**
 * Borra las solicitudes de hace mas de DIAS_SOLICITUDES dias: ya no cuentan
 * para ningun limite y guardan datos de quien pidio. Nunca corta la peticion:
 * si falla, queda en el registro de errores. Devuelve false solo si a la cuenta
 * de la base le falta DELETE en la tabla (la receta de db/13 ya lo da; una
 * cuenta creada con una receta anterior no lo tiene).
 */
function borrar_antiguas()
{
    // Conectar, fuera: si la base no contesta, la peticion falla como siempre (y
    // no se espera dos veces a la conexion, que DigBit corta a los 20 segundos).
    bd();
    try {
        // Por el indice de creado_en, y sin LIMIT: borra lo mismo en una replica.
        ejecutar('DELETE FROM recuperacion_contrasena WHERE creado_en < NOW() - INTERVAL ' . (int)DIAS_SOLICITUDES . ' DAY');
    } catch (PDOException $e) {
        error_log('DigBit API: no se pudieron borrar las solicitudes de mas de ' . DIAS_SOLICITUDES . ' dias: ' . $e->getMessage());
        return codigo_mysql($e) !== 1142;
    }

    return true;
}

/** Lo que root tiene que ejecutar para que la cuenta de la API pueda borrarlas (Probar). */
function orden_para_borrar()
{
    // CURRENT_USER() es la cuenta que tiene los permisos, como "digbit_api@%".
    $yo = (string)valor('SELECT CURRENT_USER()');
    $arroba = strrpos($yo, '@');
    $cuenta = $arroba === false ? "'" . $yo . "'"
        : "'" . substr($yo, 0, $arroba) . "'@'" . substr($yo, $arroba + 1) . "'";
    return 'GRANT DELETE ON ' . cfg()['bd_nombre'] . '.recuperacion_contrasena TO ' . $cuenta . ';';
}

function solicitar($matricula)
{
    $c = cfg();
    borrar_antiguas();
    $usuario = buscar_usuario($matricula);
    if ($usuario === null) {
        limitar_fallidas();
        anotar_sin_codigo(null, $matricula);
        responder(404, false, 'No hay ninguna cuenta activa con la matrícula ' . $matricula . '. Revisa que esté bien escrita.');
    }

    $id = (int)$usuario['idusuarios'];
    $correo = trim((string)$usuario['correo']);
    if ($correo === '' || !filter_var($correo, FILTER_VALIDATE_EMAIL)) {
        limitar_fallidas();
        anotar_sin_codigo($id, $matricula);
        responder(409, false, $correo === ''
            ? 'Tu cuenta no tiene un correo registrado. Pide a la administración de DigBit que lo agregue o que te ponga una contraseña nueva.'
            : 'El correo de tu cuenta (' . ocultar_correo($correo) . ') no es válido. Pide a la administración de DigBit que lo corrija '
                . 'o que te ponga una contraseña nueva.');
    }

    // Lo que se va a rechazar se rechaza sin candado; lo que pasa se vuelve a
    // mirar con el candado puesto (otra peticion pudo llevarse el cupo) y se inserta.
    comprobar_cupos($id, $correo);
    tomar_candado();
    comprobar_cupos($id, $correo);
    $codigo = str_pad((string)random_int(0, 999999), 6, '0', STR_PAD_LEFT);
    $minutos = (int)$c['minutos_codigo'];
    // El codigo queda atado a la cuenta (HMAC con su id) y al correo al que sale:
    // si ese correo cambia, deja de valer (confirmar).
    ejecutar('INSERT INTO recuperacion_contrasena (usuario_id, matricula, codigo_hash, correo_hash, creado_en, expira_en, ip, equipo)
              VALUES (?, ?, ?, ?, NOW(), NOW() + INTERVAL ' . $minutos . ' MINUTE, ?, ?)',
        array($id, $matricula, hash_codigo($id, $codigo), hash_correo($correo), ip_cliente(), equipo_cliente()));
    $idCodigo = (int)bd()->lastInsertId();
    soltar_candado();

    // Sin nombre en "To": el nombre lo escribe cualquiera (Crear cuenta) y el
    // saludo ya lleva el que se puede poner.
    list($asunto, $texto, $html, $imagenes) = correo_de_codigo((string)$usuario['nombre'], $codigo, $minutos);
    try {
        enviar_correo($correo, '', $asunto, $texto, $html, $imagenes);
    } catch (Throwable $e) {
        // Sin correo, ese codigo no le sirve a nadie (ni cuenta para su limite),
        // y el del correo anterior, si lo hay, sigue valiendo.
        ejecutar('UPDATE recuperacion_contrasena SET usado_en = NOW(), codigo_hash = NULL WHERE idrecuperacion = ?', array($idCodigo));
        error_log('DigBit API: no se pudo enviar el codigo a ' . ocultar_correo($correo) . ': ' . $e->getMessage());
        responder(502, false, 'No se pudo enviar el correo con tu código. Inténtalo en unos minutos; si sigue, avisa a la persona '
            . 'responsable del laboratorio.');
    }

    // El nuevo ya salio: anula los ANTERIORES (no uno posterior que se haya
    // pedido mientras este se enviaba). Vale el del ultimo correo.
    ejecutar('UPDATE recuperacion_contrasena SET usado_en = NOW()
              WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND usado_en IS NULL AND idrecuperacion < ?', array($id, $idCodigo));

    responder(200, true, 'Te enviamos un código a ' . ocultar_correo($correo) . '. Vale ' . $minutos . ' minutos.',
        array('correo' => ocultar_correo($correo), 'minutos' => $minutos));
}

function confirmar($matricula, $codigo, $contrasena)
{
    $c = cfg();
    if (!preg_match('/^[0-9]{6}$/', $codigo)) {
        responder(400, false, 'El código son las 6 cifras del correo.');
    }

    // Las mismas reglas, en el mismo orden, que DigBit (conexion/ReglasCuenta.cs):
    // de 6 a 128 caracteres contados como DigBit, y no solo espacios (con solo
    // espacios el inicio de sesion cree que el campo esta vacio). Su texto de
    // guia ya no estorba (es una marca de agua), asi que esa palabra se admite.
    $largo = largo_como_digbit($contrasena);
    if ($largo < 6) {
        responder(400, false, 'La contraseña nueva debe tener al menos 6 caracteres.');
    }

    if (solo_espacios($contrasena)) {
        responder(400, false, 'La contraseña nueva no puede ser solo espacios.');
    }

    if ($largo > 128) {
        responder(400, false, 'La contraseña nueva es demasiado larga (128 caracteres como mucho).');
    }

    $usuario = buscar_usuario($matricula);
    if ($usuario === null) {
        responder(400, false, 'Ese código no es válido. Pide uno nuevo.');
    }

    $id = (int)$usuario['idusuarios'];
    $pdo = bd();
    $pdo->beginTransaction();
    try {
        // El ultimo codigo sin usar; bloqueado para que dos intentos a la vez no
        // se salten el limite de intentos.
        $fila = fila('SELECT idrecuperacion, codigo_hash, correo_hash, intentos, expira_en < NOW() AS caducado
                      FROM recuperacion_contrasena
                      WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND usado_en IS NULL
                      ORDER BY idrecuperacion DESC LIMIT 1 FOR UPDATE', array($id));
        if ($fila === null) {
            $pdo->commit();
            responder(400, false, 'No tienes ningún código pendiente. Pide uno nuevo.');
        }

        $idCodigo = (int)$fila['idrecuperacion'];
        if ((int)$fila['caducado'] === 1) {
            ejecutar('UPDATE recuperacion_contrasena SET usado_en = NOW() WHERE idrecuperacion = ?', array($idCodigo));
            $pdo->commit();
            responder(400, false, 'Ese código ya caducó. Pide uno nuevo.');
        }

        // Salio hacia un correo que la cuenta ya no tiene (lo corrigieron en Editar
        // o en Mi perfil): quien lo recibio alli no debe poder usarlo.
        if ($fila['correo_hash'] !== null && !hash_equals((string)$fila['correo_hash'], hash_correo((string)$usuario['correo']))) {
            ejecutar('UPDATE recuperacion_contrasena SET usado_en = NOW() WHERE idrecuperacion = ?', array($idCodigo));
            $pdo->commit();
            responder(400, false, 'Ese código ya no vale: el correo de tu cuenta cambió después de pedirlo. Pide uno nuevo.');
        }

        $maximo = (int)$c['intentos_por_codigo'];
        if (!hash_equals((string)$fila['codigo_hash'], hash_codigo($id, $codigo))) {
            $intentos = (int)$fila['intentos'] + 1;
            ejecutar('UPDATE recuperacion_contrasena SET intentos = ?' . ($intentos >= $maximo ? ', usado_en = NOW()' : '')
                . ' WHERE idrecuperacion = ?', array($intentos, $idCodigo));
            $pdo->commit();
            responder(400, false, $intentos >= $maximo
                ? 'Ese código no es correcto y ya no quedan intentos. Pide uno nuevo.'
                : 'Ese código no es correcto. Te quedan ' . ($maximo - $intentos) . ' intentos.');
        }

        // Nunca una cuenta de administrador, aunque el usuario de la base pudiera.
        $cambiadas = ejecutar('UPDATE usuarios SET password = ? WHERE idusuarios = ? AND fk_tipo_usuario IN (1, 2, 4)',
            array(hash_contrasena($contrasena), $id));
        if ($cambiadas === 0) {
            $pdo->rollBack();
            responder(400, false, 'Ese código no es válido. Pide uno nuevo.');
        }

        ejecutar('UPDATE recuperacion_contrasena SET usado_en = NOW()
                  WHERE usuario_id = ? AND codigo_hash IS NOT NULL AND usado_en IS NULL', array($id));
        $pdo->commit();
    } catch (Throwable $e) {
        if ($pdo->inTransaction()) {
            $pdo->rollBack();
        }

        throw $e;
    }

    error_log('DigBit API: contrasena cambiada con codigo por correo para el usuario ' . $id . ' desde ' . ip_cliente() . '.');
    responder(200, true, 'Listo: ya puedes entrar con tu contraseña nueva.');
}
