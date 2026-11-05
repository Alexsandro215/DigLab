using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public class RoundedCornerLabel : Label
{
    private int _cornerRadius = 30; // Radio de la esquina redondeada
    public int CornerRadius
    {
        get { return _cornerRadius; }
        set
        {
            _cornerRadius = value;
            this.Invalidate(); // Invalidar el control para que se redibuje con el nuevo radio de la esquina
        }
    }

    // Constructor
    public RoundedCornerLabel()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;

    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (GraphicsPath path = new GraphicsPath())
        {

            path.AddArc(Width - CornerRadius, Height - CornerRadius, CornerRadius, CornerRadius, -26, 90);// Esquina inferior derecha
            path.AddArc(0, 5, CornerRadius, CornerRadius, 170, 95); // Esquina superior izquierda
            path.AddLine(CornerRadius, 0, Width, 0); // Línea superior
            path.CloseFigure();

            Region = new Region(path);
        }
    }
}
