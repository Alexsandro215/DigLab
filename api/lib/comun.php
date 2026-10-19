<?php
// DigBit - API de recuperacion de contrasena: lo comun (configuracion, base de
// datos, respuestas, contrasenas). Ver ../LEEME.md. PHP 7.2 o posterior.

if (!defined('DIGBIT_API')) {
    http_response_code(403);
    exit;
}

// La fecha del correo, en la hora de la escuela.
date_default_timezone_set('America/Mexico_City');

/** Sin mbstring: cada caracter UTF-8 cuenta uno (los bytes de continuacion no cuentan). */
function utf8_decode_seguro($texto)
{
    return preg_replace('/[\x80-\xBF]/', '', $texto);
}

/**
 * El largo de un texto como lo cuenta DigBit (string.Length de .NET, en
 * ReglasCuenta): en unidades UTF-16, asi que un emoji cuenta 2. Con esto la
 * API acepta y rechaza las mismas contrasenas que DigBit.
 */
function largo_como_digbit($texto)
{
    if (function_exists('mb_convert_encoding')) {
        return intdiv(strlen(mb_convert_encoding((string)$texto, 'UTF-16LE', 'UTF-8')), 2);
    }

    // Sin mbstring: uno por caracter y dos por los de 4 bytes (los que .NET parte en dos).
    return strlen(utf8_decode_seguro((string)$texto)) + preg_match_all('/[\xF0-\xF7]/', (string)$texto);
}

/**
 * Si el texto es solo espacios (o esta vacio), como string.IsNullOrWhiteSpace
 * de .NET: U+0009 a U+000D (tabulador, saltos...), los separadores de Unicode
 * (Z: espacio, espacio duro, de ancho fijo...) y U+0085. La clase va escrita y
 * no con \s: con /u, \s de PCRE tambien toma U+180E, que .NET no cuenta.
 */
function solo_espacios($texto)
{
    return preg_match('/\A[\x{9}-\x{D}\x{85}\p{Z}]*\z/u', (string)$texto) === 1;
}

/**
 * La configuracion: config.php junto a recuperar.php, o la ruta de la variable
 * DIGBIT_API_CONFIG (para guardarlo fuera de la carpeta publica, con SetEnv en
 * el .htaccess, y para las pruebas).
 */
function cfg()
{
    static $c = null;
    if ($c === null) {
        $ruta = getenv('DIGBIT_API_CONFIG')
            ?: (isset($_SERVER['DIGBIT_API_CONFIG']) ? (string)$_SERVER['DIGBIT_API_CONFIG'] : '')
            ?: (isset($_SERVER['REDIRECT_DIGBIT_API_CONFIG']) ? (string)$_SERVER['REDIRECT_DIGBIT_API_CONFIG'] : '')
            ?: __DIR__ . '/../config.php';
        if (!is_file($ruta)) {
            error_log('DigBit API: falta config.php');
            responder(500, false, 'El servicio de recuperación no está configurado. Avisa a la persona responsable del laboratorio.');
        }

        $c = require $ruta;
    }

    return $c;
}

/** Responde en JSON y termina. */
function responder($http, $ok, $mensaje, array $extra = array())
{
    http_response_code($http);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    header('X-Content-Type-Options: nosniff');
    echo json_encode(array_merge(array('ok' => $ok, 'mensaje' => $mensaje), $extra), JSON_UNESCAPED_UNICODE);
    exit;
}

/** Conexion a la base de la escuela, con la hora de Mexico (como DigBit). */
function bd()
{
    static $pdo = null;
    if ($pdo === null) {
        $c = cfg();
        $dsn = 'mysql:host=' . $c['bd_servidor'] . ';port=' . (int)$c['bd_puerto'] . ';dbname=' . $c['bd_nombre'] . ';charset=utf8';
        try {
            $pdo = new PDO($dsn, $c['bd_usuario'], $c['bd_contrasena'], array(
                PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::ATTR_EMULATE_PREPARES => false,
                PDO::ATTR_TIMEOUT => 10,
            ));
        } catch (PDOException $e) {
            // El usuario de la API tiene tope de conexiones (db/13): pasado, esta ocupado, no roto.
            $n = codigo_mysql($e);
            if ($n === 1226 || $n === 1040) {
                error_log('DigBit API: sin conexiones libres con la base (' . $n . ').');
                responder(503, false, 'El servicio de recuperación está ocupado. Inténtalo en unos segundos.');
            }

            throw $e;
        }
        // El servidor de la escuela va en UTC; DigBit fija -06:00 en cada conexion.
        $pdo->exec("SET time_zone = '-06:00'");
    }

    return $pdo;
}

/** Una fila (o null). */
function fila($sql, array $parametros = array())
{
    $consulta = bd()->prepare($sql);
    $consulta->execute($parametros);
    $resultado = $consulta->fetch();
    return $resultado === false ? null : $resultado;
}

/** Un valor (o null). */
function valor($sql, array $parametros = array())
{
    $consulta = bd()->prepare($sql);
    $consulta->execute($parametros);
    $resultado = $consulta->fetchColumn();
    return $resultado === false ? null : $resultado;
}

function ejecutar($sql, array $parametros = array())
{
    $consulta = bd()->prepare($sql);
    $consulta->execute($parametros);
    return $consulta->rowCount();
}

/** El numero de error de MySQL (1045, 1142, 2002...) de una excepcion de PDO, o 0. */
function codigo_mysql(PDOException $e)
{
    if (is_array($e->errorInfo) && !empty($e->errorInfo[1])) {
        return (int)$e->errorInfo[1];
    }

    // Al conectar no hay errorInfo: el numero va en el mensaje ("SQLSTATE[HY000] [1045] ...").
    if (preg_match('/\[(\d{4})\]/', $e->getMessage(), $m)) {
        return (int)$m[1];
    }

    return is_numeric($e->getCode()) ? (int)$e->getCode() : 0;
}

/**
 * Que le pasa a la base, con la causa y que revisar, para quien prueba la API
 * (Probar, en los Ajustes de DigBit, y probar_correo.php).
 */
function problema_de_base(PDOException $e)
{
    $n = codigo_mysql($e);
    if ($n === 1146 || (string)$e->getCode() === '42S02') {
        return 'falta aplicar db/13_recuperacion_contrasena.sql.';
    }

    if ($n === 1054) {
        return 'a la tabla de códigos le faltan columnas de esta versión. Vuelve a aplicar db/13_recuperacion_contrasena.sql '
            . '(es repetible: las añade sin tocar nada más).';
    }

    if ($n === 1142 || $n === 1143) {
        return 'al usuario de la base le faltan permisos. Dale los GRANT del final de db/13_recuperacion_contrasena.sql '
            . '(el usuario de los equipos, digbit_equipo, no sirve para esto).';
    }

    if ($n === 1045) {
        return 'el usuario o la contraseña no son correctos (bd_usuario y bd_contrasena en su config.php).';
    }

    if ($n === 1044 || $n === 1049) {
        return 'ese usuario no puede entrar en la base ' . cfg()['bd_nombre'] . ' (bd_nombre en su config.php).';
    }

    if ($n >= 2000 && $n < 3000) {
        return 'no llega al servidor de la base (bd_servidor y bd_puerto en su config.php). '
            . 'Si la base solo admite ciertas direcciones, hay que permitir la del hosting.';
    }

    return 'revisa los datos de la base en su config.php (error ' . $n . ').';
}

/** Si la base tiene usuarios.activo (db/08): sin ella nadie esta dado de baja. */
function tiene_bajas()
{
    static $tiene = null;
    if ($tiene === null) {
        $tiene = (int)valor("SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = 'activo'") > 0;
    }

    return $tiene;
}

/** La IP de quien llama (la del equipo o la de la red de la escuela). */
function ip_cliente()
{
    return substr(isset($_SERVER['REMOTE_ADDR']) ? (string)$_SERVER['REMOTE_ADDR'] : '', 0, 45);
}

function es_https()
{
    if (!empty($_SERVER['HTTPS']) && strtolower((string)$_SERVER['HTTPS']) !== 'off') {
        return true;
    }

    // Detras del proxy del hosting.
    return isset($_SERVER['HTTP_X_FORWARDED_PROTO']) && strtolower((string)$_SERVER['HTTP_X_FORWARDED_PROTO']) === 'https';
}

/** Lo que mando DigBit: formulario (lo normal) o JSON. */
function entrada()
{
    $tipo = isset($_SERVER['CONTENT_TYPE']) ? strtolower((string)$_SERVER['CONTENT_TYPE']) : '';
    if (strpos($tipo, 'application/json') !== false) {
        $datos = json_decode((string)file_get_contents('php://input'), true);
        return is_array($datos) ? $datos : array();
    }

    return $_POST;
}

function texto(array $datos, $clave, $largo)
{
    $valor = isset($datos[$clave]) && is_scalar($datos[$clave]) ? (string)$datos[$clave] : '';
    return mb_substr_seguro($valor, $largo);
}

/** Corta a un largo maximo sin romper un caracter UTF-8 (sin depender de mbstring). */
function mb_substr_seguro($texto, $largo)
{
    if (strlen($texto) <= $largo) {
        return $texto;
    }

    return function_exists('mb_strcut') ? mb_strcut($texto, 0, $largo, 'UTF-8') : substr($texto, 0, $largo);
}

/**
 * La contrasena en el formato que comprueba DigBit (conexion/Contrasenas.cs):
 * pbkdf2-sha256$<iteraciones>$<sal en base64>$<hash en base64>, con 16 bytes de
 * sal, 32 de hash y SHA-256. Rfc2898DeriveBytes de .NET pasa la contrasena a
 * UTF-8, que es como llega aqui.
 */
function hash_contrasena($contrasena)
{
    $iteraciones = 100000;
    $sal = random_bytes(16);
    $hash = hash_pbkdf2('sha256', $contrasena, $sal, $iteraciones, 32, true);
    return 'pbkdf2-sha256$' . $iteraciones . '$' . base64_encode($sal) . '$' . base64_encode($hash);
}

/** El codigo guardado: HMAC con el secreto y atado al usuario (no sirve para otro). */
function hash_codigo($idUsuario, $codigo)
{
    return hash_hmac('sha256', $idUsuario . ':' . $codigo, (string)cfg()['secreto']);
}

/** El correo al que salio un codigo, sin guardarlo en claro: si la cuenta cambia de correo, el codigo deja de valer. */
function hash_correo($correo)
{
    return hash_hmac('sha256', 'correo:' . strtolower(trim((string)$correo)), (string)cfg()['secreto']);
}

/** "a***@teschi.edu.mx": para que sepa a donde le llego, sin ensenarlo entero. */
function ocultar_correo($correo)
{
    $arroba = strrpos($correo, '@');
    if ($arroba === false || $arroba < 1) {
        return '***';
    }

    $local = substr($correo, 0, $arroba);
    $visibles = strlen($local) > 4 ? 2 : 1;
    return substr($local, 0, $visibles) . str_repeat('*', 3) . substr($correo, $arroba);
}
