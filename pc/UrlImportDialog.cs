namespace MiniAppBox;

/// <summary>从 GitHub / Gitee 链接导入小程序（M3 风格）。支持多应用仓库选择。</summary>
public class UrlImportDialog : Form
{
    private readonly TextBox _url = new();
    private readonly M3Button _btnImport;
    private readonly Label _status = new();
    private readonly Action _onChanged;

    public UrlImportDialog(Action onChanged)
    {
        _onChanged = onChanged;
        Text = "从链接导入小程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 190);
        M3.Apply(this);

        var tip = new Label
        {
            Text = "支持 GitHub / Gitee 仓库链接（可含 /tree/分支/子目录）和 .zip 直链：",
            Location = new Point(20, 14),
            AutoSize = true,
            ForeColor = M3.OnSurfaceVariant
        };
        _url.Location = new Point(20, 42);
        _url.Size = new Size(520, 26);
        _url.Font = M3.Font(10f);
        _url.BorderStyle = BorderStyle.FixedSingle;
        _url.PlaceholderText = "https://github.com/用户名/仓库　或　https://gitee.com/用户名/仓库";

        _btnImport = new M3Button("导入", M3ButtonKind.Filled, 96, 36) { Location = new Point(344, 84) };
        _btnImport.Click += (_, _) => Run();

        var btnCancel = new M3Button("取消", M3ButtonKind.Text, 72, 36) { Location = new Point(448, 84) };
        btnCancel.Click += (_, _) => Close();

        _status.Location = new Point(20, 132);
        _status.Size = new Size(520, 40);
        _status.ForeColor = M3.OnSurfaceVariant;

        Controls.AddRange(new Control[] { tip, _url, _btnImport, btnCancel, _status });
        AcceptButton = _btnImport;
    }

    private async void Run()
    {
        string url = _url.Text.Trim();
        var repo = UrlImporter.Parse(url);
        if (repo == null)
        {
            MessageBox.Show(this, "这不是支持的链接。\n支持：github.com/用户/仓库、gitee.com/用户/仓库\n（可带 /tree/分支/子目录），或 .zip 直链。",
                "从链接导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        _status.Text = "解析仓库分支…";
        try
        {
            string zipPath = Path.Combine(Path.GetTempPath(), "miniappbox_url_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                var downloaded = await UrlImporter.DownloadAndFindRootsAsync(repo, zipPath);
                _status.Text = $"仓库解析成功：{downloaded.Roots.Count} 个小程序候选";
                var imported = new List<string>();

                if (downloaded.Roots.Count == 1)
                {
                    var app = AppRepo.ImportDirectory(downloaded.Roots[0], downloaded.RepoName);
                    imported.Add(app.Name);
                }
                else
                {
                    using var picker = new MultiRootPicker(downloaded.Roots, downloaded.RepoName);
                    if (picker.ShowDialog(this) != DialogResult.OK)
                    {
                        _status.Text = "已取消。";
                        SetBusy(false);
                        return;
                    }
                    foreach (string root in picker.SelectedRoots)
                    {
                        var app = AppRepo.ImportDirectory(root, downloaded.RepoName);
                        imported.Add(app.Name);
                    }
                }

                _onChanged.Invoke();
                _status.Text = "导入完成：" + string.Join("、", imported);
                ToastForm.Show(this, $"已导入 {imported.Count} 个小程序");
                if (Modal) DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
        catch (Exception e)
        {
            _status.ForeColor = M3.ErrorColor;
            _status.Text = "失败：" + e.Message;
            _status.ForeColor = M3.OnSurfaceVariant;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _btnImport.Enabled = !busy;
        _url.Enabled = !busy;
        UseWaitCursor = busy;
    }
}

/// <summary>多应用仓库选择器（勾选，支持全选）。</summary>
internal class MultiRootPicker : Form
{
    public List<string> SelectedRoots { get; } = new();
    private readonly CheckedListBox _list = new();

    public MultiRootPicker(List<string> roots, string repoName)
    {
        Text = "这个仓库里有 " + roots.Count + " 个小程序";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 380);
        M3.Apply(this);

        var tip = new Label
        {
            Text = $"仓库「{repoName}」包含多个小程序，勾选要导入的：",
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = M3.OnSurfaceVariant
        };
        _list.Location = new Point(16, 40);
        _list.Size = new Size(488, 268);
        _list.CheckOnClick = true;
        _list.Font = M3.Font(10f);
        foreach (string root in roots)
        {
            string name = SafeName(root);
            _list.Items.Add(name, true);
        }
        _list.ItemCheck += (_, _) => _list.Invalidate();

        var btnAll = new M3Button("全选", M3ButtonKind.Tonal, 72, 36) { Location = new Point(16, 324) };
        btnAll.Click += (_, _) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, true); };

        var btnOk = new M3Button("导入所选", M3ButtonKind.Filled, 110, 36) { Location = new Point(ClientSize.Width - 206, 324) };
        btnOk.Click += (_, _) =>
        {
            for (int i = 0; i < _list.Items.Count; i++)
                if (_list.GetItemChecked(i)) SelectedRoots.Add(roots[i]);
            if (SelectedRoots.Count == 0)
            {
                ToastForm.Show(this, "没有勾选任何小程序");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new M3Button("取消", M3ButtonKind.Text, 64, 36) { Location = new Point(ClientSize.Width - 88, 324) };
        btnCancel.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { tip, _list, btnAll, btnOk, btnCancel });
    }

    private static string SafeName(string root)
    {
        // 展示相对路径 + 仓库内 manifest 名称（如果有）
        string manifest = Path.Combine(root, "manifest.json");
        if (File.Exists(manifest))
        {
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest));
                string? name = node?["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name + "（" + Path.GetFileName(root) + "）";
            }
            catch { /* 解析失败用目录名 */ }
        }
        return Path.GetFileName(root);
    }
}
