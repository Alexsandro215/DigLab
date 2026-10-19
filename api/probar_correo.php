<?php
// DigBit - prueba de envio de la API de recuperacion, solo desde la linea de
// comandos (en el navegador no hace nada):
//
//   php probar_correo.php tu_correo@ejemplo.com [ruta de config.php]
//
// Usa config.php (el de esta carpeta, o el de la ruta, si lo guardaste fuera
// de la carpeta publica: desde la consola no se lee el SetEnv del .htaccess).
// Si el correo llega, la API puede enviar los codigos. Si no, dice que contesto
// el servidor de correo (sin ensenar la contrasena).

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

if (isset($argv[2]) && $argv[2] !== '') {
    putenv('DIGBIT_API_CONFIG=' . $argv[2]);
}

define('DIGBIT_API', true);
require __DIR__ . '/lib/comun.php';
require __DIR__ . '/lib/correo.php';

$para = isset($argv[1]) ? $argv[1] : '';
if (!filter_var($para, FILTER_VALIDATE_EMAIL)) {
    fwrite(STDERR, "Uso: php probar_correo.php tu_correo@ejemplo.com\n");
    exit(2);
}

try {
    // El mismo correo que recibe un alumno, con un codigo de ejemplo: asi se ve tambien el diseno.
    list($asunto, $texto, $html, $imagenes) = correo_de_codigo('Prueba', '123456', (int)cfg()['minutos_codigo']);
    enviar_correo($para, '', 'Prueba de DigBit (código de ejemplo)', $texto, $html, $imagenes);
    echo "Enviado a $para. Revisa la bandeja de entrada (y la de spam).\n";
} catch (Throwable $e) {
    // Desde la consola, la causa y que revisar, y debajo lo que contesto el servidor.
    fwrite(STDERR, 'No se pudo enviar: ' . explicar_fallo_correo($e) . "\nDetalle: " . $e->getMessage() . "\n");
    exit(1);
}

$aviso = aviso_privacidad_url();
echo $aviso !== ''
    ? "Lleva al pie el enlace al aviso de privacidad: $aviso\n"
    : "Sale sin el pie del aviso de privacidad: no hay aviso_privacidad_url en config.php (o no es una dirección http(s) completa).\n";

try {
    bd()->query('SELECT 1 FROM recuperacion_contrasena LIMIT 1');
    echo "La base responde y tiene la tabla recuperacion_contrasena (db/13).\n";
} catch (Throwable $e) {
    fwrite(STDERR, 'La base no responde o no sirve: ' . ($e instanceof PDOException ? problema_de_base($e) : 'error inesperado.')
        . "\nDetalle: " . $e->getMessage() . "\n");
    exit(1);
}
