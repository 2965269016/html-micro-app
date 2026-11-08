using System.Drawing.Drawing2D;

namespace MiniAppBox;

/// <summary>
/// Material Design 3（谷歌设计方案）浅色主题令牌与绘制助手。
/// 令牌取自 M3 Baseline 调色板，动态取色仅安卓端有（随壁纸），PC 端固定 Baseline。
/// </summary>
internal static class M3
{
    // ------------------------------------------------------------ 颜色令牌
    public static readonly Color Primary = FromHex("#6750A4");
    public static readonly Color OnPrimary = FromHex("#FFFFFF");
    public static readonly Color PrimaryContainer = FromHex("#EADDFF");
    public static readonly Color OnPrimaryContainer = FromHex("#21005D");
    public static readonly Color SecondaryContainer = FromHex("#E8DEF8");
    public static readonly Color OnSecondaryContainer = FromHex("#1D192B");
    public static readonly Color Surface = FromHex("#FEF7FF");
    public static readonly Color SurfaceContainerLowest = FromHex("#FFFFFF");
    public static readonly Color SurfaceContainerLow = FromHex("#F7F2FA");
    public static readonly Color SurfaceContainer = FromHex("#F3EDF7");
    public static readonly Color SurfaceContainerHigh = FromHex("#ECE6F0");
    public static readonly Color OnSurface = FromHex("#1D1B20");
    public static readonly Color OnSurfaceVariant = FromHex("#49454F");
    public static readonly Color Outline = FromHex("#79747E");
    public static readonly Color OutlineVariant = FromHex("#CAC4D0");
    public static readonly Color ErrorColor = FromHex("#B3261E");
    public static readonly Color ErrorContainer = FromHex("#F9DEDC");

    public const string FontName = "Microsoft YaHei UI";

    public static Color FromHex(string hex) => ColorTranslator.FromHtml(hex);

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        new(FontName, size, style, GraphicsUnit.Point);

    public static void Apply(Form f, Color? background = null)
    {
        f.BackColor = background ?? Surface;
        f.Font = Font(9.5f);
        f.ForeColor = OnSurface;
    }

    // ------------------------------------------------------------ 圆角路径

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
    }
}

/// <summary>M3 按钮样式。</summary>
public enum M3ButtonKind { Filled, Tonal, Outlined, Text, Error }

/// <summary>M3 圆角自绘按钮（带 hover/按下态；实现 IButtonControl 以支持 AcceptButton/CancelButton）。</summary>
public class M3Button : Control, System.Windows.Forms.IButtonControl
{
    private readonly M3ButtonKind _kind;
    private bool _hover, _down;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; }
    public void NotifyDefault(bool value) { }
    public void PerformClick() => OnClick(EventArgs.Empty);

    public M3Button(string text, M3ButtonKind kind = M3ButtonKind.Filled, int width = 108, int height = 40)
    {
        Text = text;
        _kind = kind;
        Size = new Size(width, height);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
    }

    private Color Back => _kind switch
    {
        M3ButtonKind.Filled => M3.Primary,
        M3ButtonKind.Tonal => M3.SecondaryContainer,
        M3ButtonKind.Error => M3.ErrorContainer,
        _ => Color.Transparent
    };
    private Color Fore => _kind switch
    {
        M3ButtonKind.Filled => M3.OnPrimary,
        M3ButtonKind.Tonal => M3.OnSecondaryContainer,
        M3ButtonKind.Error => M3.ErrorColor,
        M3ButtonKind.Outlined => M3.Primary,
        _ => M3.Primary
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
        var radius = Height / 2f;

        if (_kind is M3ButtonKind.Filled or M3ButtonKind.Tonal or M3ButtonKind.Error)
        {
            using var b = new SolidBrush(Back);
            g.FillPath(b, M3.Rounded(r, radius));
            if (_hover)
                using (var overlay = new SolidBrush(Color.FromArgb(_down ? 40 : 22,
                    _kind == M3ButtonKind.Filled ? Color.White : Color.Black)))
                    g.FillPath(overlay, M3.Rounded(r, radius));
        }
        else if (_kind == M3ButtonKind.Outlined)
        {
            if (_hover)
                using (var b = new SolidBrush(Color.FromArgb(_down ? 40 : 18, M3.Primary)))
                    g.FillPath(b, M3.Rounded(r, radius));
            using var pen = new Pen(M3.Outline, 1f);
            g.DrawPath(pen, M3.Rounded(r, radius));
        }
        else // Text
        {
            if (_hover)
                using (var b = new SolidBrush(Color.FromArgb(_down ? 40 : 18, M3.Primary)))
                    g.FillPath(b, M3.Rounded(r, radius));
        }

        TextRenderer.DrawText(g, Text, M3.Font(10f, FontStyle.Bold),
            new Rectangle(0, 0, Width, Height), Fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
}
