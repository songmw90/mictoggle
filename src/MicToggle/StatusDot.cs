using System.Drawing.Drawing2D;

namespace MicToggle;

internal sealed class StatusDot : Control
{
    private Color _dotColor = Color.FromArgb(164, 171, 181);

    public StatusDot()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    public Color DotColor
    {
        get => _dotColor;
        set
        {
            if (_dotColor == value)
            {
                return;
            }

            _dotColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        const int diameter = 7;
        var x = (ClientSize.Width - diameter) / 2;
        var y = (ClientSize.Height - diameter) / 2;
        using var brush = new SolidBrush(_dotColor);
        e.Graphics.FillEllipse(brush, x, y, diameter, diameter);
    }
}
