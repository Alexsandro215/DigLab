<?php
// DigBit - configuracion de la API de recuperacion de contrasena.
//
// Copia este archivo como config.php, en esta misma carpeta, y llena los datos.
// config.php lleva contrasenas: NO se sube a git (.gitignore) ni se comparte, y
// el .htaccess de esta carpeta impide abrirlo desde el navegador.

return array(
    // --- Base de datos de la escuela (la misma de DigBit) ---------------------
    'bd_servidor'   => 'sql.teschi.edu.mx',
    'bd_puerto'     => 3306,
    'bd_nombre'     => 'teschi_otru',
    // Mejor un usuario solo para la API, con los permisos que dice
    // db/13_recuperacion_contrasena.sql al final.
    'bd_usuario'    => 'digbit_api',
    'bd_contrasena' => 'CAMBIA_ESTO',

    // --- Correo que envia los codigos -----------------------------------------
    // La cuenta de Gmail de DigBit. La contrasena NO es la de la cuenta: es una
    // "contrasena de aplicacion" de 16 letras (Cuenta de Google > Seguridad >
    // Verificacion en dos pasos > Contrasenas de aplicaciones). Sin la
    // verificacion en dos pasos, Gmail no deja crearla.
    'smtp_servidor'    => 'smtp.gmail.com',
    'smtp_puerto'      => 587,
    // 'tls' (STARTTLS, puerto 587), 'ssl' (puerto 465) o 'ninguna' (solo para pruebas en local).
    'smtp_seguridad'   => 'tls',
    'smtp_usuario'     => 'digbit.teschi@gmail.com',
    'smtp_contrasena'  => 'CONTRASENA_DE_APLICACION',
    // Con Gmail, el mismo correo de la cuenta (si no, Gmail lo cambia).
    'remitente'        => 'digbit.teschi@gmail.com',
    'nombre_remitente' => 'DigBit TESCHI',
    // Si smtp_servidor se deja vacio se usa mail() del hosting (peor: suele
    // acabar en spam). Solo si el hosting no deja salir al puerto 587 ni al 465.

    // --- Seguridad -------------------------------------------------------------
    // Con el se guardan los codigos (HMAC): 32 caracteres o mas al azar, por
    // ejemplo los de https://www.random.org/strings/ o los de
    //   php -r "echo bin2hex(random_bytes(24));"
    // Mientras sea el de ejemplo, la API no atiende. Si se cambia, los codigos
    // ya enviados dejan de valer (nada mas).
    'secreto' => 'CAMBIA_ESTO_POR_32_O_MAS_CARACTERES_AL_AZAR',

    // La que manda DigBit en cada peticion: la misma que pongas en DigBit, en
    // Ajustes (arriba en el panel de administracion), junto con la direccion de
    // recuperar.php. No es un secreto fuerte (la pueden leer los equipos), pero
    // frena a quien encuentre la direccion. Mientras sea la de ejemplo, la API no
    // atiende; vacia, no se pide. Por ejemplo:
    //   php -r "echo bin2hex(random_bytes(12));"
    'clave_api' => 'CAMBIA_ESTO',

    // Solo por https. Dejalo en true en el hosting; false solo para probar en local.
    'solo_https' => true,

    // --- Aviso de privacidad ---------------------------------------------------
    // La direccion del aviso de privacidad integral del TESCHI (la misma que en
    // DigBit, Ajustes > Aviso de privacidad), completa y con https://. El correo
    // del codigo la pone al pie: "Tus datos se tratan conforme al aviso de
    // privacidad del TESCHI: leer el aviso de privacidad completo". Vacia (o sin
    // esta linea), el correo sale sin ese pie. Si no es una direccion http(s)
    // completa tampoco sale, y error_log lo dice.
    'aviso_privacidad_url' => '',

    // --- Limites ---------------------------------------------------------------
    'minutos_codigo' => 15,                // lo que vale cada codigo
    'intentos_por_codigo' => 5,            // codigos equivocados antes de anularlo
    'solicitudes_por_usuario_hora' => 3,   // codigos por cuenta en una hora
    'codigos_por_equipo_hora' => 6,        // codigos pedidos desde un mismo equipo (lo dice DigBit) en una hora
    // Codigos ENVIADOS desde una misma IP en una hora. Todos los equipos de la
    // escuela pueden salir a internet por la misma IP: no lo bajes mucho.
    'solicitudes_por_ip_hora' => 60,
    // Matriculas que no existen (o sin correo) desde una misma IP en una hora.
    // Van aparte: quien teclee basura no deja sin codigo a los demas.
    'fallidas_por_ip_hora' => 200,
    // Correos en total, de donde sea. Gmail deja mandar unos 500 al dia desde
    // una cuenta normal; pasado eso deja de enviar y puede bloquear la cuenta.
    'correos_por_hora' => 100,
    'correos_por_dia' => 400,
);
