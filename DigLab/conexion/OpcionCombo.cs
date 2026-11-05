namespace DigLab.conexion
{
    public class OpcionCombo
    {
        public int Id { get; set; }
        public string Texto { get; set; }

        public override string ToString()
        {
            return Texto ?? string.Empty;
        }
    }
}
