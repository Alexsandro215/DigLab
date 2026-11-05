<?php
// DigLab - API de recuperacion: enviar un correo. Un cliente SMTP pequeno, sin
// librerias, para Gmail (STARTTLS en el 587 o SSL en el 465, con la
// "contrasena de aplicacion" de la cuenta). Si smtp_servidor esta vacio, usa
// mail() del hosting (llega peor: muchos hostings lo mandan a spam).

if (!defined('DIGLAB_API')) {
    http_response_code(403);
    exit;
}

/**
 * Un fallo al mandar el correo, con su causa: 'conexion', 'cifrado',
 * 'autenticacion', 'remitente', 'destinatario', 'tiempo', 'corte', 'respuesta'
 * o 'mail'. getMessage() trae el detalle tecnico (lo que contesto el servidor,
 * para error_log); explicar_fallo_correo() lo que se le dice a quien prueba.
 */
class FalloCorreo extends RuntimeException
{
    public $causa;

    public function __construct($causa, $detalle)
    {
        parent::__construct($detalle);
        $this->causa = $causa;
    }
}

/**
 * Por que no salio el correo, en espanol y con que revisar en config.php, para
 * quien prueba la API (Probar, en los Ajustes de DigLab, y probar_correo.php).
 * Empieza en minuscula: va despues de "el correo x fallo: ". Corto: en Ajustes,
 * con lo de delante, caben 4 lineas.
 */
function explicar_fallo_correo(Throwable $e)
{
    $c = cfg();
    $servidor = (string)$c['smtp_servidor'] . ':' . (int)$c['smtp_puerto'];
    switch ($e instanceof FalloCorreo ? $e->causa : '') {
        case 'conexion':
            // Si ya va por el 465 con 'ssl', la otra forma de Gmail es el 587 con 'tls'.
            $otra = strtolower((string)$c['smtp_seguridad']) === 'ssl'
                ? "el puerto 587 con 'smtp_seguridad' => 'tls'" : "el puerto 465 con 'smtp_seguridad' => 'ssl'";
            return 'no se pudo conectar con ' . $servidor . '; quizá el hosting no deja salir al correo. Prueba ' . $otra
                . ' en config.php.';
        case 'cifrado':
            return 'no se pudo cifrar la conexión con ' . $servidor . ' (STARTTLS). Prueba el puerto 465 '
                . "con 'smtp_seguridad' => 'ssl' en config.php.";
        case 'autenticacion':
            return 'el servidor no aceptó la cuenta. Revisa smtp_usuario y smtp_contrasena en config.php: '
                . 'con Gmail va la contraseña de aplicación (16 letras).';
        case 'remitente':
            return 'el servidor de correo no aceptó el remitente. En config.php, remitente tiene que ser la misma cuenta que smtp_usuario.';
        case 'destinatario':
            return 'el servidor de correo no aceptó la dirección de destino. Revisa que esté bien escrita.';
        case 'tiempo':
            return 'el servidor de correo tardó demasiado en contestar. Inténtalo de nuevo en un momento.';
        case 'corte':
            return 'se cortó la conexión con el servidor de correo. Inténtalo de nuevo en un momento.';
        case 'mail':
            return 'mail() del hosting no pudo enviarlo. Pregunta al hosting si deja enviar correo, o pon el de Gmail en smtp_servidor (config.php).';
        default:
            return 'el servidor de correo contestó algo inesperado. Inténtalo de nuevo; si sigue, el detalle quedó en el registro '
                . 'de errores del hosting (error_log).';
    }
}

/**
 * La direccion del aviso de privacidad integral del TESCHI (aviso_privacidad_url
 * en config.php), o '' si no esta: entonces el correo sale sin el pie del
 * aviso. Solo una direccion http(s) completa; si es otra cosa tampoco sale (lo
 * dice error_log), para no poner en el correo un enlace que no sirva.
 */
function aviso_privacidad_url()
{
    $c = cfg();
    $url = isset($c['aviso_privacidad_url']) ? trim((string)$c['aviso_privacidad_url']) : '';
    if ($url === '') {
        return '';
    }

    $esquema = strtolower((string)parse_url($url, PHP_URL_SCHEME));
    if (filter_var($url, FILTER_VALIDATE_URL) === false || ($esquema !== 'https' && $esquema !== 'http')) {
        error_log('DigLab API: aviso_privacidad_url de config.php no es una direccion http(s) completa; el correo sale sin el pie del aviso.');
        return '';
    }

    return $url;
}

/**
 * Manda un correo en UTF-8: el texto y, si se da, tambien la version con diseno
 * (HTML) con sus imagenes dentro del mismo correo: el HTML las pide como
 * "cid:nombre@diglab" y se ven sin descargar nada. Los programas que no ensenan
 * HTML ensenan el texto. Lanza RuntimeException si no se pudo, con la respuesta
 * del servidor (nunca con la contrasena).
 *
 * $imagenes: array('nombre' => ruta de un .png, .jpg o .gif).
 */
function enviar_correo($para, $nombrePara, $asunto, $texto, $html = null, array $imagenes = array())
{
    $c = cfg();
    $remitente = (string)$c['remitente'];
    $cabeceras = array(
        'Date: ' . date('r'),
        'From: ' . direccion_con_nombre($c['nombre_remitente'], $remitente),
        'To: ' . direccion_con_nombre($nombrePara, $para),
        'Subject: ' . encabezado_nombre($asunto),
        'Message-ID: <' . bin2hex(random_bytes(12)) . '@' . substr($remitente, strrpos($remitente, '@') + 1) . '>',
        'MIME-Version: 1.0',
    );

    if ($html === null) {
        $cabeceras[] = 'Content-Type: text/plain; charset=UTF-8';
        $cabeceras[] = 'Content-Transfer-Encoding: base64';
        $cuerpo = base64_en_lineas(con_crlf($texto));
    } else {
        // multipart/alternative: el texto o el HTML; el HTML va en un
        // multipart/related con sus imagenes.
        $alternativa = 'diglab-alt-' . bin2hex(random_bytes(8));
        $relacionado = 'diglab-rel-' . bin2hex(random_bytes(8));
        $cabeceras[] = 'Content-Type: multipart/alternative; boundary="' . $alternativa . '"';
        $partes = array(
            '--' . $alternativa,
            'Content-Type: text/plain; charset=UTF-8',
            'Content-Transfer-Encoding: base64',
            '',
            base64_en_lineas(con_crlf($texto)),
            '--' . $alternativa,
            'Content-Type: multipart/related; boundary="' . $relacionado . '"',
            '',
            '--' . $relacionado,
            'Content-Type: text/html; charset=UTF-8',
            'Content-Transfer-Encoding: base64',
            '',
            base64_en_lineas(con_crlf($html)),
        );
        foreach ($imagenes as $nombre => $ruta) {
            // Una imagen que no se pueda leer no impide que llegue el codigo: el
            // HTML ensena su texto alternativo.
            $datos = @file_get_contents($ruta);
            if ($datos === false || $datos === '') {
                error_log('DigLab: no se pudo leer ' . $ruta . '; el correo sale sin esa imagen.');
                continue;
            }

            $archivo = basename($ruta);
            $partes[] = '--' . $relacionado;
            $partes[] = 'Content-Type: ' . tipo_de_imagen($archivo) . '; name="' . $archivo . '"';
            $partes[] = 'Content-Transfer-Encoding: base64';
            $partes[] = 'Content-ID: <' . $nombre . '@diglab>';
            $partes[] = 'Content-Disposition: inline; filename="' . $archivo . '"';
            $partes[] = '';
            $partes[] = base64_en_lineas($datos);
        }

        $partes[] = '--' . $relacionado . '--';
        $partes[] = '--' . $alternativa . '--';
        $cuerpo = implode("\r\n", $partes);
    }

    // Nada que escapar para SMTP: en base64 no hay lineas que empiecen por "."
    // ni bytes de 8 bits, y las demas son cabeceras o separadores ("--...").
    if (trim((string)$c['smtp_servidor']) === '') {
        // mail() pone To y Subject aparte.
        $resto = array_filter($cabeceras, function ($h) {
            return stripos($h, 'To: ') !== 0 && stripos($h, 'Subject: ') !== 0;
        });
        if (!mail($para, encabezado_nombre($asunto), $cuerpo, implode("\r\n", $resto))) {
            throw new FalloCorreo('mail', 'mail() del hosting no pudo enviarlo.');
        }

        return;
    }

    smtp_enviar($remitente, $para, implode("\r\n", $cabeceras) . "\r\n\r\n" . $cuerpo . "\r\n");
}

/**
 * El correo del codigo: asunto, texto y version con diseno (correo/plantilla.html,
 * con los logos de correo/*.png dentro del mismo correo). Sin plantilla solo va
 * el texto; si falta un logo (o no se puede leer), en su lugar va su nombre escrito.
 * Con aviso_privacidad_url en config.php, las dos versiones llevan al pie el
 * enlace al aviso de privacidad; sin ella, ningun pie.
 * Devuelve array(asunto, texto, html o null, imagenes).
 */
function correo_de_codigo($nombre, $codigo, $minutos)
{
    $nombre = nombre_para_saludo($nombre);
    $aviso = aviso_privacidad_url();
    $asunto = 'Tu código de DigLab: ' . $codigo;
    $texto = 'Hola' . ($nombre !== '' ? ', ' . $nombre : '') . ":\n\n"
        . "Tu código para poner una contraseña nueva en DigLab es:\n\n"
        . '        ' . $codigo . "\n\n"
        . 'Vale ' . $minutos . " minutos y una sola vez. Escríbelo en DigLab, en «¿Olvidaste tu contraseña?», junto con tu contraseña nueva.\n\n"
        . "Si no lo pediste tú, ignora este correo: tu contraseña no cambia. Nadie del TESCHI te pedirá este código.\n\n"
        . "DigLab - Laboratorios de cómputo\nIngeniería en Sistemas Computacionales - TESCHI\n"
        // El pie del aviso (como en el diseno), solo con aviso_privacidad_url en config.php.
        . ($aviso !== '' ? "\nTus datos se tratan conforme al aviso de privacidad del TESCHI: " . $aviso . "\n" : '');

    // Sin plantilla (o vacia, o cortada antes del codigo) solo va el texto.
    $carpeta = __DIR__ . '/../correo/';
    $plantilla = @file_get_contents($carpeta . 'plantilla.html');
    if ($plantilla === false || strpos($plantilla, '{{codigo}}') === false) {
        return array($asunto, $texto, null, array());
    }

    $imagenes = array();
    $logo = function ($archivo, $ancho, $alt, $sinImagen) use ($carpeta, &$imagenes) {
        $ruta = $carpeta . $archivo . '.png';
        if (!is_file($ruta) || !is_readable($ruta) || (int)@filesize($ruta) === 0) {
            return $sinImagen;
        }

        $imagenes[$archivo] = $ruta;
        return '<img src="cid:' . $archivo . '@diglab" width="' . $ancho . '" alt="' . htmlspecialchars($alt, ENT_QUOTES, 'UTF-8')
            . '" style="display:block;border:0;outline:none;text-decoration:none;height:auto;max-width:100%;">';
    };
    $letra = 'font-family:Arial,Helvetica,sans-serif;font-weight:bold;color:#007040;';

    // El pie del aviso de privacidad va entre <!--{{aviso}}--> y <!--{{/aviso}}-->:
    // sin direccion se quita entero; con ella, solo esas marcas.
    $plantilla = $aviso === ''
        ? preg_replace('/<!--\{\{aviso\}\}-->.*?<!--\{\{\/aviso\}\}-->/s', '', $plantilla)
        : str_replace(array('<!--{{aviso}}-->', '<!--{{/aviso}}-->'), '', $plantilla);

    $html = strtr($plantilla, array(
        '{{preencabezado}}' => 'Tu código es ' . $codigo . '. Vale ' . $minutos . ' minutos.',
        '{{logo_teschi}}' => $logo('teschi', 200, 'TESCHI - Tecnológico de Estudios Superiores de Chimalhuacán',
            '<span style="' . $letra . 'font-size:24px;line-height:28px;">TESCHI</span>'),
        '{{logo_carrera}}' => $logo('sistemas', 168, 'Ingeniería en Sistemas Computacionales',
            '<span style="' . $letra . 'font-size:12px;line-height:16px;">Ingeniería en<br>Sistemas Computacionales</span>'),
        '{{logo_diglab}}' => $logo('diglab', 32, 'DigLab', ''),
        '{{saludo}}' => $nombre !== '' ? 'Hola, ' . htmlspecialchars($nombre, ENT_QUOTES, 'UTF-8') . ':' : 'Hola:',
        '{{codigo}}' => htmlspecialchars((string)$codigo, ENT_QUOTES, 'UTF-8'),
        '{{minutos}}' => (string)(int)$minutos,
        '{{aviso_privacidad_url}}' => htmlspecialchars($aviso, ENT_QUOTES, 'UTF-8'),
    ));

    return array($asunto, $texto, $html, $imagenes);
}

/**
 * El nombre del saludo: solo el primero, y solo si son letras (hasta 25). Los
 * nombres se escriben a mano (tambien en "Crear cuenta" del kiosco, con un
 * correo que nadie comprueba): asi nadie mete su propio texto en un correo con
 * los logos de la escuela. Si viene en mayusculas, solo la primera ("PRUEBA" ->
 * "Prueba"). Si no sirve, "" y el saludo queda en "Hola:".
 */
function nombre_para_saludo($nombre)
{
    // Separan nombres el espacio, el tabulador, el salto de linea y el espacio duro.
    $partes = preg_split('/[\s\x{00A0}]+/u', (string)$nombre, -1, PREG_SPLIT_NO_EMPTY);
    $primero = $partes ? $partes[0] : '';
    // \z y no $: $ tambien acepta un salto de linea al final.
    if (preg_match('/^\p{L}[\p{L}\p{M}]{0,24}\z/u', $primero) !== 1) {
        return '';
    }

    if (function_exists('mb_convert_case') && mb_strtoupper($primero, 'UTF-8') === $primero) {
        $primero = mb_convert_case($primero, MB_CASE_TITLE, 'UTF-8');
    }

    return $primero;
}

/**
 * "Nombre" <correo> para From y To: el nombre entre comillas (asi una coma o un
 * "<" suyos no se leen como otra direccion) o, si no es ASCII, =?UTF-8?B?...?=.
 */
function direccion_con_nombre($nombre, $correo)
{
    $nombre = trim(str_replace(array("\r", "\n"), ' ', (string)$nombre));
    if ($nombre === '') {
        return '<' . $correo . '>';
    }

    return (preg_match('/[^\x20-\x7E]/', $nombre) ? '=?UTF-8?B?' . base64_encode($nombre) . '?=' : '"' . addcslashes($nombre, '"\\') . '"')
        . ' <' . $correo . '>';
}

/** Saltos de linea CRLF, como pide el correo. */
function con_crlf($texto)
{
    return str_replace("\n", "\r\n", str_replace("\r\n", "\n", (string)$texto));
}

/** Base64 en lineas de 76 caracteres. */
function base64_en_lineas($datos)
{
    return rtrim(chunk_split(base64_encode($datos), 76, "\r\n"));
}

function tipo_de_imagen($archivo)
{
    $extension = strtolower(pathinfo($archivo, PATHINFO_EXTENSION));
    if ($extension === 'jpg' || $extension === 'jpeg') {
        return 'image/jpeg';
    }

    return $extension === 'gif' ? 'image/gif' : 'image/png';
}

/** El asunto: =?UTF-8?B?...?= si lleva algo que no sea ASCII (los acentos). */
function encabezado_nombre($texto)
{
    $texto = str_replace(array("\r", "\n"), ' ', $texto);
    return preg_match('/[^\x20-\x7E]/', $texto) ? '=?UTF-8?B?' . base64_encode($texto) . '?=' : $texto;
}

/** Conecta, cifra y se identifica. Devuelve la conexion abierta (la cierra si algo falla). */
function smtp_abrir()
{
    $c = cfg();
    $servidor = (string)$c['smtp_servidor'];
    $puerto = (int)$c['smtp_puerto'];
    $seguridad = strtolower((string)$c['smtp_seguridad']);
    $contexto = stream_context_create(array('ssl' => array('verify_peer' => true, 'verify_peer_name' => true, 'peer_name' => $servidor)));
    $errno = 0;
    $errstr = '';
    $s = @stream_socket_client(($seguridad === 'ssl' ? 'ssl://' : 'tcp://') . $servidor . ':' . $puerto, $errno, $errstr, 15,
        STREAM_CLIENT_CONNECT, $contexto);
    if (!$s) {
        throw new FalloCorreo('conexion', 'No se pudo conectar con ' . $servidor . ':' . $puerto . ' (' . $errstr . '). '
            . 'Puede que el hosting no deje salir al correo.');
    }

    stream_set_timeout($s, 20);
    try {
        smtp_leer($s, array(220));
        $yo = 'diglab.local';
        smtp_comando($s, 'EHLO ' . $yo, array(250));
        if ($seguridad === 'tls') {
            smtp_comando($s, 'STARTTLS', array(220));
            $metodo = STREAM_CRYPTO_METHOD_TLS_CLIENT;
            if (defined('STREAM_CRYPTO_METHOD_TLSv1_2_CLIENT')) {
                $metodo = STREAM_CRYPTO_METHOD_TLSv1_2_CLIENT;
                if (defined('STREAM_CRYPTO_METHOD_TLSv1_3_CLIENT')) {
                    $metodo |= STREAM_CRYPTO_METHOD_TLSv1_3_CLIENT;
                }
            }

            if (!stream_socket_enable_crypto($s, true, $metodo)) {
                throw new FalloCorreo('cifrado', 'No se pudo cifrar la conexion con ' . $servidor . ' (STARTTLS).');
            }

            smtp_comando($s, 'EHLO ' . $yo, array(250));
        }

        if ((string)$c['smtp_usuario'] !== '') {
            smtp_comando($s, 'AUTH LOGIN', array(334));
            // Tambien el usuario como credencial: si lo rechaza, es la autenticacion.
            smtp_comando($s, base64_encode((string)$c['smtp_usuario']), array(334), true);
            smtp_comando($s, base64_encode((string)$c['smtp_contrasena']), array(235), true);
        }
    } catch (Throwable $e) {
        fclose($s);
        throw $e;
    }

    return $s;
}

function smtp_enviar($de, $para, $mensaje)
{
    $s = smtp_abrir();
    try {
        smtp_comando($s, 'MAIL FROM:<' . $de . '>', array(250));
        smtp_comando($s, 'RCPT TO:<' . $para . '>', array(250, 251));
        smtp_comando($s, 'DATA', array(354));
        fwrite($s, $mensaje . ".\r\n");
        smtp_leer($s, array(250));
        @fwrite($s, "QUIT\r\n");
    } finally {
        fclose($s);
    }
}

/** Para "probar": que el servidor de correo deja entrar con la cuenta de config.php. No manda nada. */
function smtp_probar()
{
    $s = smtp_abrir();
    @fwrite($s, "QUIT\r\n");
    fclose($s);
}

/** Manda una linea y espera uno de esos codigos. Con secreto, el error no repite lo enviado. */
function smtp_comando($s, $linea, array $esperados, $secreto = false)
{
    if (fwrite($s, $linea . "\r\n") === false) {
        throw new FalloCorreo('corte', 'Se corto la conexion con el servidor de correo.');
    }

    return smtp_leer($s, $esperados, $secreto ? '(credenciales)' : $linea);
}

/** Lee una respuesta (de varias lineas "250-..." hasta la "250 ...") y comprueba el codigo. */
function smtp_leer($s, array $esperados, $tras = '')
{
    $respuesta = '';
    while (($linea = fgets($s, 1024)) !== false) {
        $respuesta .= $linea;
        if (strlen($linea) < 4 || $linea[3] !== '-') {
            break;
        }
    }

    $codigo = (int)substr($respuesta, 0, 3);
    if (!in_array($codigo, $esperados, true)) {
        $info = stream_get_meta_data($s);
        $autenticacion = strpos($tras, 'AUTH') === 0 || $tras === '(credenciales)';
        // La causa, para explicar_fallo_correo; el mensaje sigue siendo el detalle tecnico.
        if (!empty($info['timed_out'])) {
            $causa = 'tiempo';
        } elseif (trim($respuesta) === '') {
            $causa = 'corte';
        } elseif ($autenticacion) {
            $causa = 'autenticacion';
        } elseif (strpos($tras, 'MAIL FROM') === 0) {
            $causa = 'remitente';
        } elseif (strpos($tras, 'RCPT TO') === 0) {
            $causa = 'destinatario';
        } else {
            $causa = 'respuesta';
        }

        throw new FalloCorreo($causa, 'El servidor de correo respondio "' . trim($respuesta) . '"'
            . ($tras !== '' ? ' a ' . ($autenticacion ? 'la autenticacion' : strtok($tras, ' ')) : '')
            . (!empty($info['timed_out']) ? ' (tiempo agotado)' : '') . '.');
    }

    return $respuesta;
}
