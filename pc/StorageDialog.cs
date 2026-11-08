namespace MiniAppBox;

/// <summary>存储管理弹窗：占用统计 + 清空 JS 数据 + 打开数据文件夹（对应手机端存储管理弹窗）。</summary>
public class StorageDialog : Form
{
    private readonly MiniApp _app;
    private readonly Label _usage = new();
    private readonly Label _storeCount = new();

    public StorageDialog(MiniApp app)
    {
        _app = app;
        Text = "存储管理";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 240);

        var head = new Label
        {
            Text = $"小程序：{app.Name}",
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            Location = new Point(16, 14),
            AutoSize = true
        };
        var info = new Label
        {
            Text = $"id：{app.Id}\r\n安装时间：{DateTimeOffset.FromUnixTimeMilliseconds(app.InstalledAt).LocalDateTime:yyyy-MM-dd HH:mm:ss}",
            ForeColor = Color.DimGray,
            Location = new Point(16, 46),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        _usage.Location = new Point(16, 92);
        _usage.AutoSize = true;
        _usage.Font = new Font("Microsoft YaHei UI", 10f);
        _storeCount.Location = new Point(16, 116);
        _storeCount.AutoSize = true;
        _storeCount.Font = new Font("Microsoft YaHei UI", 9.5f);
        _storeCount.ForeColor = Color.DimGray;

        var clearBtn = new Button
        {
            Text = "清空 JS 数据",
            Location = new Point(20, 158),
            Size = new Size(120, 32)
        };
        clearBtn.Click += (_, _) =>
        {
            var answer = MessageBox.Show(this,
                $"清空「{app.Name}」保存的所有 JS 数据？\r\n小程序本体不受影响。",
                "存储管理", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            AppRepo.ClearData(app.Id);
            RefreshUsage();
            ToastForm.Show(this, "JS 数据已清空");
        };

        var openBtn = new Button
        {
            Text = "打开数据文件夹",
            Location = new Point(152, 158),
            Size = new Size(130, 32)
        };
        openBtn.Click += (_, _) =>
        {
            string dir = AppRepo.AppDir(app.Id);
            if (Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", dir);
        };

        var closeBtn = new Button
        {
            Text = "关闭",
            Location = new Point(ClientSize.Width - 104, 158),
            Size = new Size(88, 32)
        };
        closeBtn.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            head, info, _usage, _storeCount, clearBtn, openBtn, closeBtn
        });

        RefreshUsage();
    }

    private void RefreshUsage()
    {
        _usage.Text = $"总占用：{FormatBytes(AppRepo.UsageBytes(_app.Id))}（HTML 目录 + JS 存储）";
        _storeCount.Text = $"JS 存储键值对：{AppRepo.StoreJson(_app.Id).Count} 条";
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
