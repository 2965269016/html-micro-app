namespace MiniAppBox;

/// <summary>权限管理弹窗：4 项权限开关，实时写回 meta.json（对应手机端 dialog_permissions）。</summary>
public class PermissionDialog : Form
{
    private static readonly (string Key, string Label)[] Items =
    {
        (AppRepo.PERM_NOTIFICATION, "通知 —— Native.notify() 弹出桌面右下角通知"),
        (AppRepo.PERM_VIBRATE,      "振动 —— Native.vibrate()（电脑端表现为窗口闪烁提醒）"),
        (AppRepo.PERM_CLIPBOARD,    "剪贴板 —— Native.copyText() / pasteText()"),
        (AppRepo.PERM_GEOLOCATION,  "定位 —— 网页 HTML 地理定位 API")
    };

    private readonly MiniApp _app;
    private readonly Dictionary<string, CheckBox> _boxes = new();

    public PermissionDialog(MiniApp app)
    {
        _app = app;
        Text = "权限管理";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 288);

        var head = new Label
        {
            Text = $"小程序：{app.Name}",
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            Location = new Point(16, 14),
            AutoSize = true
        };
        Controls.Add(head);

        int y = 56;
        foreach (var (key, label) in Items)
        {
            var box = new CheckBox
            {
                Text = label,
                Checked = AppRepo.HasPermission(app.Id, key),
                Location = new Point(20, y),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f)
            };
            _boxes[key] = box;
            Controls.Add(box);
            y += 42;
        }

        var hint = new Label
        {
            Text = "关掉立即生效；开启通知/定位时电脑不会再弹系统授权框，由盒子直接放行。",
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(20, y + 2),
            AutoSize = true
        };
        Controls.Add(hint);

        var save = new Button
        {
            Text = "保存",
            Location = new Point(ClientSize.Width - 200, ClientSize.Height - 40),
            Size = new Size(88, 30)
        };
        save.Click += (_, _) =>
        {
            foreach (var (key, _) in Items)
                AppRepo.SetPermission(app.Id, key, _boxes[key].Checked);
            Close();
        };
        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(ClientSize.Width - 104, ClientSize.Height - 40),
            Size = new Size(88, 30)
        };
        cancel.Click += (_, _) => Close();
        Controls.Add(save);
        Controls.Add(cancel);
        AcceptButton = save;
        CancelButton = cancel;
    }
}
