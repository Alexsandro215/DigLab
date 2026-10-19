using System;
using System.Linq;

namespace DigBit.conexion
{
    /// <summary>
    /// Los codigos que propone DigBit: el de cada profesor (db/09) y, sin la 09,
    /// el de cada clase. En mayusculas (nadie las distingue: ni el servidor, con
    /// un cotejo _ci, ni la copia local) y sin lo que se confunde al leerlo o
    /// dictarlo: ni 0 ni O, ni 1 ni I ni L. Los codigos que ya existen siguen
    /// valiendo tal como estan.
    /// </summary>
    internal class CodigoGenerador
    {
        private static readonly Random random = new Random();
        private const string caracteres = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        /// <summary>
        /// Un codigo al azar de esa longitud, con al menos una letra: los de
        /// sesion (generacion de codigos) son solo cifras y asi no se parecen.
        /// </summary>
        public static string GenerarCodigoAleatorio(int longitud)
        {
            char[] codigo = new char[Math.Max(0, longitud)];
            // Random no es seguro entre hilos.
            lock (random)
            {
                do
                {
                    for (int i = 0; i < codigo.Length; i++)
                    {
                        codigo[i] = caracteres[random.Next(caracteres.Length)];
                    }
                }
                while (codigo.Length > 0 && !codigo.Any(char.IsLetter));
            }

            return new string(codigo);
        }
    }
}
