using System.Drawing;
using System.Windows.Forms;

namespace DigLab.RJControls
{
    internal class CustomComboBox
    {
        public Color BackColor { get; internal set; }
        public Color BorderColor { get; internal set; }
        public int BorderSize { get; internal set; }
        public ComboBoxStyle DropDownStyle { get; internal set; }
        public string Texts { get; internal set; }
        public int TabIndex { get; internal set; }
        public Size Size { get; internal set; }
        public Color ForeColor { get; internal set; }
        public Color IconColor { get; internal set; }
        public Color ListBackColor { get; internal set; }
        public Color ListTextColor { get; internal set; }
        public Point Location { get; internal set; }
        public Size MinimumSize { get; internal set; }
        public string Name { get; internal set; }
        public Padding Padding { get; internal set; }
    }
}