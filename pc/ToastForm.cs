namespace MiniAppBox;

/// <summary>轻量 toast：深色圆角条，底部浮出，自动淡出，不抢焦点（对应手机端 Toast）。</summary>
public class ToastForm : Form
{
    private const int StayTicks = 28; // 每 tick 50ms，约停留 1.4s 后开始淡出

    private ToastForm(string message)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(48, 48, 48);
        Opacity = 0.95;

        var label = new Label
        {
            Text = string.IsNullOrWhiteSpace(message) ? " " : message,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10.5f),
            Margin = new Padding(16, 10, 16, 10)
        };
        Padding = new Padding(16, 9, 16, 9);
        Controls.Add(label);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>在 anchor 窗口底部居中弹一条提示，自动消失。</summary>
    public static void Show(Form anchor, string message)
    {
        var toast = new ToastForm(message);
        toast.Show();
        // AutoSize 计算完成后定位
        var area = (anchor ?? Form.ActiveForm)?.Bounds
                   ?? Screen.PrimaryScreen.WorkingArea;
        int x = area.Left + (area.Width - toast.Width) / 2;
        int y = area.Bottom - toast.Height - 96;
        toast.Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));

        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (ticks < StayTicks) return;
            toast.Opacity -= 0.08;
            if (toast.Opacity <= 0.05)
            {
                timer.Stop();
                timer.Dispose();
                toast.Close();
                toast.Dispose();
            }
        };
        timer.Start();
    }
}
