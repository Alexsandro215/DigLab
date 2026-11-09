using System;
using System.Security.Cryptography;
using System.Text;

namespace DigLab.Infraestructura
{
    /// <summary>
    /// La cadena de conexion de connections.config, cifrada (pedido del 2026-11-08). Los
    /// equipos del laboratorio se conectan con una cuenta de la base de permisos altos,
    /// y el estudiante, tras su bitacora, puede abrir ese archivo: DigLab corre con su
    /// sesion y tiene que poder leerlo, asi que no se le puede negar. Cifrada, quien lo
    /// abre ve "cifrada:AQAAANCM..." y no la contrasena.
    ///
    /// Con DPAPI de la MAQUINA (LocalMachine): la descifra cualquier programa de ESTE
    /// equipo (DigLab en la sesion del estudiante, el vigilante como SYSTEM, el
    /// instalador), y en otra computadora no sirve: copiar el archivo a casa no basta.
    /// Detiene al curioso que abre el archivo; NO a quien sabe programar y corre codigo en
    /// el equipo (la entropia va en DigLab, que se puede desarmar). Lo que protege de
    /// verdad es que los equipos usen digbit_equipo, de solo lectura (db/07).
    ///
    /// Una cadena sin el prefijo va tal cual: los equipos de antes y los que prepara
    /// deploy\configurar_equipo.ps1 la tienen en claro, y siguen funcionando.
    ///
    /// Este archivo se compila tambien en DigLab.Configurador (enlazado): no puede
    /// depender de nada mas del proyecto.
    /// </summary>
    internal static class CadenaCifrada
    {
        public const string Prefijo = "cifrada:";

        private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("DigLab connections.config v1");

        public static bool EstaCifrada(string valor)
        {
            return valor != null && valor.StartsWith(Prefijo, StringComparison.Ordinal);
        }

        /// <summary>"cifrada:" y la cadena cifrada para este equipo, en base64.</summary>
        public static string Cifrar(string cadena)
        {
            if (cadena == null)
            {
                throw new ArgumentNullException("cadena");
            }

            byte[] cifrados = ProtectedData.Protect(Encoding.UTF8.GetBytes(cadena), Entropia, DataProtectionScope.LocalMachine);
            return Prefijo + Convert.ToBase64String(cifrados);
        }

        /// <summary>
        /// La cadena en claro. Sin el prefijo, la devuelve tal cual. Lanza
        /// CryptographicException si no se puede descifrar en este equipo (se cifro en
        /// otro, o el archivo se dano).
        /// </summary>
        public static string Descifrar(string valor)
        {
            if (!EstaCifrada(valor))
            {
                return valor;
            }

            byte[] cifrados;
            try
            {
                cifrados = Convert.FromBase64String(valor.Substring(Prefijo.Length).Trim());
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("La cadena cifrada no es base64 valido.", ex);
            }

            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cifrados, Entropia, DataProtectionScope.LocalMachine));
        }
    }
}
