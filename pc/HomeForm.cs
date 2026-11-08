using System.Drawing.Drawing2D;

namespace MiniAppBox;

/// <summary>
/// 盒子首页：M3 自绘卡片列表、导入（本地 zip / 链接）、右键管理、设备同步入口、
/// 托盘后台常驻（关闭窗口缩到托盘，同步服务随托盘运行）。
/// 对应手机端 HomeActivity。
/// </summary>
public class HomeForm : Form
{
    private ListBox _cards;
    private readonly Dictionary<string, Bitmap> _icons = new();
    private readonly List<MiniApp> _apps = new();
    private Label _emptyHint;

    private readonly AppSettings _settings;
    private readonly SyncServer _server;
    private readonly Discovery _discovery;
    private NotifyIcon _tray;
    private bool _allowExit;
    private bool _trayTipShown;

    public HomeForm()
    {
        _settings = AppSettings.Load();
        _server = new SyncServer(_settings);
        _discovery = new Discovery(() => _settings.EffectiveDeviceName, () => SyncServer.Port);

        Text = "小程序盒子";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 600);
        Size = new Size(560, 760);
        M3.Apply(this);

        // ---- 顶栏（M3 TopAppBar：Surface 背景 + 大标题）
        var top = new Panel { Dock = DockStyle.Top, Height = 120, BackColor = M3.Surface };
        var title = new Label
        {
            Text = "小程序盒子",
            Location = new Point(20, 16),
            AutoSize = true,
            Font = M3.Font(20f),
            ForeColor = M3.OnSurface
        };
        var subtitle = new Label
        {
            Text = "网页小程序容器 · 与手机端互通",
            Location = new Point(22, 60),
            AutoSize = true,
            Font = M3.Font(9f),
            ForeColor = M3.OnSurfaceVariant
        };
        // 右侧按钮容器用 Dock=Right（避免构造期 Anchor=Right 按未来宽度计算被推出容器）
        var rightBtns = new Panel { Dock = DockStyle.Right, Width = 224, BackColor = M3.Surface };
        var importBtn = new M3Button("＋ 导入", M3ButtonKind.Filled, 104, 40) { Location = new Point(0, 52) };
        var syncBtn = new M3Button("同步", M3ButtonKind.Tonal, 88, 40) { Location = new Point(112, 52) };
        rightBtns.Controls.Add(importBtn);
        rightBtns.Controls.Add(syncBtn);
        top.Controls.Add(title);
        top.Controls.Add(subtitle);
        top.Controls.Add(rightBtns);

        // ---- 卡片列表（自绘 M3 卡片）
        _cards = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 86,
            BorderStyle = BorderStyle.None,
            BackColor = M3.Surface,
            IntegralHeight = false
        };
        _cards.DrawItem += DrawCard;
        _cards.DoubleClick += (_, _) => OpenSelected();
        _cards.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenSelected(); } };

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => OpenSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("权限管理", null, (_, _) => ShowPermissions());
        menu.Items.Add("存储管理", null, (_, _) => ShowStorage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出 zip（含数据，可导入手机端）", null, (_, _) => ExportSelected());
        menu.Items.Add("删除", null, (_, _) => DeleteSelected());
        _cards.ContextMenuStrip = menu;

        _emptyHint = new Label
        {
            Text = "还没有小程序\r\n点右上角「＋ 导入」选择 zip 包，\r\n或「从链接导入」贴一个 GitHub / Gitee 仓库地址\r\n（手机端导出的包可以直接导入）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = M3.OnSurfaceVariant,
            Font = M3.Font(11f),
            BackColor = M3.Surface
        };

        Controls.Add(_cards);
        Controls.Add(_emptyHint);
        Controls.Add(top);

        var importMenu = new ContextMenuStrip();
        importMenu.Items.Add("导入本地 zip…", null, (_, _) => ImportZip());
        importMenu.Items.Add("从 GitHub / Gitee 链接导入…", null, (_, _) => ImportUrl());
        importBtn.Click += (_, _) => importMenu.Show(importBtn, new Point(0, importBtn.Height));
        syncBtn.Click += (_, _) => OpenSync();

        // ---- 托盘常驻
        _tray = new NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "小程序盒子",
            Visible = true
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("打开盒子", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add("设备同步…", null, (_, _) => { ShowFromTray(); OpenSync(); });
        var syncToggle = new ToolStripMenuItem("允许其他设备同步到本机") { Checked = _settings.SyncEnabled };
        syncToggle.Click += (_, _) =>
        {
            syncToggle.Checked = !syncToggle.Checked;
            _settings.SyncEnabled = syncToggle.Checked;
            _settings.Save();
            if (syncToggle.Checked) _server.Start(); else _server.Stop();
        };
        trayMenu.Items.Add(syncToggle);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => { _allowExit = true; Close(); });
        _tray.ContextMenuStrip = trayMenu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        // ---- 同步服务随托盘运行
        if (_settings.SyncEnabled) _server.Start();
        if (_settings.SyncEnabled) _discovery.Start();
        _server.AppsChanged += () => BeginInvoke(() => Reload());

        Load += (_, _) => Reload();
        FormClosed += (_, _) => Cleanup();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.I)) { ImportZip(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------ 托盘行为

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_allowExit)
        {
            e.Cancel = true;
            Hide();
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                _tray.ShowBalloonTip(3000, "小程序盒子", "已最小化到托盘，双击图标打开；右键图标可同步或退出。", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
        Activate();
    }

    private void Cleanup()
    {
        _server.Dispose();
        _discovery.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
    }

    private static Icon MakeTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(M3.Primary);
            g.FillEllipse(b, 1, 1, 30, 30);
            using var f = M3.Font(15f, FontStyle.Bold);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var w = new SolidBrush(Color.White);
            g.DrawString("盒", f, w, new RectangleF(0, 1, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ------------------------------------------------------------ 列表与绘制

    private void Reload()
    {
        _apps.Clear();
        _apps.AddRange(AppRepo.List());
        _icons.Clear();
        foreach (var app in _apps)
            _icons[app.Id] = IconGen.Generate(app.Name);
        _cards.BeginUpdate();
        _cards.Items.Clear();
        foreach (var app in _apps) _cards.Items.Add(app.Id);
        _cards.EndUpdate();
        _emptyHint.Visible = _apps.Count == 0;
        _cards.Visible = _apps.Count > 0;
    }

    private MiniApp Selected =>
        _cards.SelectedIndex >= 0 && _cards.SelectedIndex < _apps.Count ? _apps[_cards.SelectedIndex] : null;

    private void DrawCard(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _apps.Count) return;
        var app = _apps[e.Index];
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        var cardRect = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 4, e.Bounds.Width - 16, e.Bounds.Height - 8);
        M3.FillRounded(g, cardRect, 16, selected ? M3.SurfaceContainerHigh : M3.SurfaceContainerLow);

        // 图标
        if (_icons.TryGetValue(app.Id, out var icon))
            g.DrawImage(icon, new Rectangle((int)cardRect.X + 16, (int)cardRect.Y + 17, 48, 48));

        // 名称 + 日期
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var nameBrush = new SolidBrush(M3.OnSurface))
            g.DrawString(app.Name, M3.Font(11f, FontStyle.Bold), nameBrush, cardRect.X + 80, cardRect.Y + 13);
        string meta = $"安装于 {DateTimeOffset.FromUnixTimeMilliseconds(app.InstalledAt).LocalDateTime:yyyy-MM-dd}";
        using (var metaBrush = new SolidBrush(M3.OnSurfaceVariant))
            g.DrawString(meta, M3.Font(8.5f), metaBrush, cardRect.X + 81, cardRect.Y + 42);
    }

    // ------------------------------------------------------------ 操作

    private void OpenSelected()
    {
        var app = Selected;
        if (app == null) return;
        new RunForm(app).Show();
    }

    private void ImportZip()
    {
        using var dlg = new OpenFileDialog { Filter = "小程序包 (*.zip)|*.zip", Title = "选择小程序包" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var app = AppRepo.Import(dlg.FileName, Path.GetFileName(dlg.FileName));
            Reload();
            ToastForm.Show(this, $"已导入「{app.Name}」");
        }
        catch (AppRepo.ImportException e)
        {
            MessageBox.Show(this, "导入失败：" + e.Message, "小程序盒子", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "导入失败：" + e.Message, "小程序盒子", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportUrl()
    {
        using var dlg = new UrlImportDialog(Reload);
        dlg.ShowDialog(this);
    }

    private void OpenSync()
    {
        using var dlg = new SyncDialog(_settings, _server, _discovery);
        dlg.ShowDialog(this);
    }

    private void ShowPermissions()
    {
        var app = Selected;
        if (app == null) return;
        using var dlg = new PermissionDialog(app);
        dlg.ShowDialog(this);
    }

    private void ShowStorage()
    {
        var app = Selected;
        if (app == null) return;
        using var dlg = new StorageDialog(app);
        dlg.ShowDialog(this);
    }

    private void ExportSelected()
    {
        var app = Selected;
        if (app == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "小程序包 (*.zip)|*.zip",
            FileName = app.Name + ".zip",
            Title = "导出小程序（含数据）"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AppRepo.Export(app.Id, dlg.FileName);
            ToastForm.Show(this, "已导出：" + dlg.FileName);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "导出失败：" + e.Message, "小程序盒子", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        var app = Selected;
        if (app == null) return;
        var answer = MessageBox.Show(this,
            $"删除小程序「{app.Name}」？\r\n它的 HTML 文件和 JS 数据都会被清除。",
            "小程序盒子", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        AppRepo.Delete(app.Id);
        Reload();
        ToastForm.Show(this, "已删除");
    }
}
