namespace MiniAppBox;

/// <summary>
/// 盒子首页：小程序卡片列表、导入、右键菜单（打开/权限/存储/导出/删除）。
/// 对应手机端 HomeActivity。
/// </summary>
public class HomeForm : Form
{
    private ListView _list;
    private ImageList _icons;
    private Label _emptyHint;

    public HomeForm()
    {
        Text = "小程序盒子";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(460, 560);
        Size = new Size(520, 700);

        var top = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = ColorTranslator.FromHtml("#6750A4") };
        var title = new Label
        {
            Text = "小程序盒子",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0)
        };
        var importBtn = new Button
        {
            Text = "＋ 导入小程序",
            Dock = DockStyle.Right,
            Width = 128,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = ColorTranslator.FromHtml("#7C6BB8"),
            Font = new Font("Microsoft YaHei UI", 10f)
        };
        importBtn.FlatAppearance.BorderSize = 0;
        importBtn.Click += (_, _) => ImportZip();
        top.Controls.Add(title);
        top.Controls.Add(importBtn);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.LargeIcon,
            MultiSelect = false,
            HideSelection = false,
            BackColor = ColorTranslator.FromHtml("#FAF8FF")
        };
        _icons = new ImageList { ImageSize = new Size(96, 96), ColorDepth = ColorDepth.Depth32Bit };
        _list.LargeImageList = _icons;
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; OpenSelected(); }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => OpenSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("权限管理", null, (_, _) => ShowPermissions());
        menu.Items.Add("存储管理", null, (_, _) => ShowStorage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出 zip（含数据，可导入手机端）", null, (_, _) => ExportSelected());
        menu.Items.Add("删除", null, (_, _) => DeleteSelected());
        _list.ContextMenuStrip = menu;

        _emptyHint = new Label
        {
            Text = "还没有小程序\r\n点击右上角「＋ 导入小程序」选择一个 zip 包\r\n（手机端导出的包可以直接导入）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 11f),
            BackColor = ColorTranslator.FromHtml("#FAF8FF")
        };

        Controls.Add(_list);
        Controls.Add(_emptyHint);
        Controls.Add(top);

        Load += (_, _) => Reload();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.I)) { ImportZip(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Reload()
    {
        var apps = AppRepo.List();
        _list.BeginUpdate();
        _list.Items.Clear();
        _icons.Images.Clear();
        foreach (var app in apps)
        {
            _icons.Images.Add(app.Id, IconGen.Generate(app.Name));
            _list.Items.Add(new ListViewItem(app.Name)
            {
                Tag = app,
                ImageKey = app.Id,
                ToolTipText = app.Id
            });
        }
        _list.EndUpdate();
        _emptyHint.Visible = apps.Count == 0;
        _list.Visible = apps.Count > 0;
    }

    private MiniApp Selected =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as MiniApp : null;

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
            MessageBox.Show(this, "导入失败：" + e.Message, "小程序盒子",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "导入失败：" + e.Message, "小程序盒子",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
            MessageBox.Show(this, "导出失败：" + e.Message, "小程序盒子",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
