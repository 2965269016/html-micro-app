using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MiniAppBox;

/// <summary>
/// 动态图标：应用名首字 + 随机色圆形背景。
/// 调色板与哈希算法照抄手机端 IconGenerator.kt —— 同名小程序在两端生成同色图标。
/// </summary>
public static class IconGen
{
    private const int Size = 108;

    private static readonly string[] Palette =
    {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEAA7",
        "#DDA0DD", "#98D8C8", "#F7DC6F", "#BB8FCE", "#85C1E9"
    };

    /// <summary>与 Java/Kotlin String.hashCode() 完全一致的算法。</summary>
    private static int JavaHash(string s)
    {
        unchecked
        {
            int h = 0;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }

    public static Bitmap Generate(string appName)
    {
        // 手机端 firstOrNull 取的是首个 UTF-16 代码单元，这里 Substring(0,1) 保持一致
        string first = string.IsNullOrEmpty(appName) ? "?" : appName.Substring(0, 1);
        int hash = JavaHash(first);
        int idx = ((hash % Palette.Length) + Palette.Length) % Palette.Length;
        Color bg = System.Drawing.ColorTranslator.FromHtml(Palette[idx]);

        var bmp = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        using (var brush = new SolidBrush(bg))
            g.FillEllipse(brush, 0, 0, Size - 1, Size - 1);

        using var font = new Font("Microsoft YaHei UI", Size * 0.45f, GraphicsUnit.Pixel);
        var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var white = new SolidBrush(System.Drawing.Color.White);
        g.DrawString(first, font, white, new RectangleF(0, 0, Size, Size), center);
        return bmp;
    }
}
