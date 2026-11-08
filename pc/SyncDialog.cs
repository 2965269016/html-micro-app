using System.Text;

namespace MiniAppBox;

/// <summary>
/// 设备同步窗口（协议第 9 节）：局域网发现/手动连接 → 应用差异对比 →
/// 逐项选方向（推送给对方 / 从对方获取 / 跳过）或一键"时间新者胜"。
/// </summary>
public class SyncDialog : Form
{
    private readonly AppSettings _settings;
    private readonly SyncServer _server;
    private readonly Discovery _discovery;

    private readonly TextBox _deviceName = new();
    private readonly CheckBox _allowSync = new();
    private readonly ListBox _peers = new();
    private readonly TextBox _manual = new();
    private readonly Label _peerLabel = new();
    private readonly Panel _diffPanel = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new();
    private readonly M3Button _btnConnect, _btnProbe, _btnRun, _btnAuto, _btnClose;
    private readonly M3Button _btnRescan;

    private readonly Dictionary<string, Discovery.Peer> _peerMap = new();
    private SyncClient.PeerInfo? _remote;
    private string _remoteHost = "", _remotePort = "";
    private readonly List<DiffRow> _rows = new();

    private sealed class DiffRow
    {
        public MiniApp? Local;
        public SyncClient.RemoteApp? Remote;
        public string State = "";
        public ComboBox Combo = new();
        public string Name => Local?.Name ?? Remote?.Name ?? "?";
    }

    public SyncDialog(AppSettings settings, SyncServer server, Discovery discovery)
    {
        _settings = settings;
        _server = server;
        _discovery = discovery;

        Text = "设备同步（局域网 · IPv4/IPv6）";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(660, 600);
        Size = new Size(700, 660);
        M3.Apply(this);

        int y = 14;
        var lblName = new Label { Text = "本机设备名：", Location = new Point(18, y + 4), AutoSize = true };
        _deviceName.Location = new Point(110, y);
        _deviceName.Width = 220;
        _deviceName.BorderStyle = BorderStyle.FixedSingle;
        _deviceName.Text = settings.EffectiveDeviceName;
        _deviceName.TextChanged += (_, _) => { _settings.DeviceName = _deviceName.Text.Trim(); _settings.Save(); };
        _allowSync.Text = "允许其他设备同步到本机";
        _allowSync.Location = new Point(360, y + 3);
        _allowSync.AutoSize = true;
        _allowSync.Checked = settings.SyncEnabled;
        _allowSync.CheckedChanged += (_, _) =>
        {
            _settings.SyncEnabled = _allowSync.Checked;
            _settings.Save();
            if (_allowSync.Checked) _server.Start(); else _server.Stop();
            Log(_allowSync.Checked ? "已开启接收（本机作为同步服务端）" : "已关闭接收");
        };
        Controls.Add(lblName); Controls.Add(_deviceName); Controls.Add(_allowSync);

        y += 40;
        var sep1 = Hr(y); Controls.Add(sep1);
        var lblDisc = new Label { Text = "局域网设备（自动发现 IPv4 广播 + IPv6 多播）", Location = new Point(18, y + 8), AutoSize = true, ForeColor = M3.OnSurfaceVariant };
        Controls.Add(lblDisc);

        y += 34;
        _peers.Location = new Point(18, y);
        _peers.Size = new Size(500, 92);
        _peers.BorderStyle = BorderStyle.FixedSingle;
        _peers.Font = M3.Font(9.5f);
        _btnRescan = new M3Button("重新探测", M3ButtonKind.Tonal, 100, 34) { Location = new Point(530, y + 4) };
        _btnRescan.Click += (_, _) => { _peerMap.Clear(); _peers.Items.Clear(); _discovery.SendProbe(null, null); Log("已重新探测…"); };

        _btnProbe = new M3Button("探测", M3ButtonKind.Tonal, 100, 34) { Location = new Point(530, y + 44) };
        _btnProbe.Visible = false;
        Controls.Add(_peers); Controls.Add(_btnRescan); Controls.Add(_btnProbe);

        y += 100;
        var lblManual = new Label { Text = "手动连接：", Location = new Point(18, y + 6), AutoSize = true };
        _manual.Location = new Point(110, y + 2);
        _manual.Width = 280;
        _manual.BorderStyle = BorderStyle.FixedSingle;
        _manual.PlaceholderText = "IP 或 [IPv6]:端口，如 192.168.1.8:47777";
        _btnConnect = new M3Button("连接", M3ButtonKind.Filled, 88, 32) { Location = new Point(400, y) };
        _btnConnect.Click += (_, _) => ConnectManual();
        _peers.DoubleClick += (_, _) => ConnectSelected();
        Controls.Add(lblManual); Controls.Add(_manual); Controls.Add(_btnConnect);

        y += 44;
        _peerLabel.Location = new Point(18, y);
        _peerLabel.AutoSize = true;
        _peerLabel.ForeColor = M3.OnSurfaceVariant;
        Controls.Add(_peerLabel);

        y += 26;
        _diffPanel.Location = new Point(18, y);
        _diffPanel.Size = new Size(ClientSize.Width - 36, ClientSize.Height - y - 150);
        _diffPanel.AutoScroll = true;
        _diffPanel.BackColor = M3.SurfaceContainerLow;
        Controls.Add(_diffPanel);

        var bottom = ClientSize.Height - 118;
        _btnAuto = new M3Button("一键同步（时间新者胜）", M3ButtonKind.Tonal, 190, 40) { Location = new Point(18, bottom) };
        _btnAuto.Click += (_, _) => { AutoFill(); RunTransfer(); };
        _btnRun = new M3Button("开始同步", M3ButtonKind.Filled, 110, 40) { Location = new Point(218, bottom) };
        _btnRun.Click += (_, _) => RunTransfer();
        _btnClose = new M3Button("关闭", M3ButtonKind.Text, 72, 40) { Location = new Point(ClientSize.Width - 92, bottom) };
        _btnClose.Click += (_, _) => Close();
        _status.Location = new Point(340, bottom + 10);
        _status.AutoSize = true;
        Controls.Add(_btnAuto); Controls.Add(_btnRun); Controls.Add(_btnClose); Controls.Add(_status);

        _log.Location = new Point(18, bottom + 48);
        _log.Size = new Size(ClientSize.Width - 36, 62);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = M3.SurfaceContainer;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = M3.Font(8.5f);
        Controls.Add(_log);

        _discovery.Found += OnPeerFound;
        _server.Activity += Log;
        _server.AppsChanged += () => BeginInvoke(() => { Log("本机应用列表已更新"); if (_remote != null) RefreshDiff(); });
        FormClosed += (_, _) => { _discovery.Found -= OnPeerFound; };

        Load += (_, _) =>
        {
            _discovery.Start();
            Log(_allowSync.Checked ? "本机可作为同步接收端（在托盘常驻）" : "接收已关闭，可勾选上方开关开启");
        };
    }

    // ------------------------------------------------------------ 发现

    private void OnPeerFound(Discovery.Peer peer)
    {
        if (InvokeRequired) { BeginInvoke(() => OnPeerFound(peer)); return; }
        bool isNew = !_peerMap.ContainsKey(peer.Key);
        _peerMap[peer.Key] = peer;
        if (isNew)
        {
            _peers.Items.Add(peer.Display);
            Log("发现设备：" + peer.Display);
        }
        else
        {
            int idx = _peers.Items.IndexOf(peer.Display);
            if (idx >= 0) { /* 刷新显示时间可以忽略 */ }
        }
    }

    private void ConnectManual()
    {
        string input = _manual.Text.Trim().Replace('：', ':'); // 全角冒号归一化
        if (input.Length == 0)
        {
            if (_peers.SelectedItem == null) { ToastForm.Show(this, "先选设备或输入 IP:端口"); return; }
            ConnectSelected();
            return;
        }
        string host = input, port = SyncServer.Port.ToString();
        int colon = input.LastIndexOf(':');
        if (colon > 0) { host = input[..colon].Trim('[', ']'); port = input[(colon + 1)..].Trim(']'); }
        Connect(host, port);
    }

    private void ConnectSelected()
    {
        if (_peers.SelectedItem == null) return;
        var peer = _peerMap.Values.FirstOrDefault(p => p.Display == _peers.SelectedItem.ToString());
        if (peer == null) return;
        Connect(peer.Host, peer.TcpPort.ToString());
    }

    // ------------------------------------------------------------ 连接与差异

    private async void Connect(string host, string portStr)
    {
        if (!int.TryParse(portStr, out int port)) { ToastForm.Show(this, "端口不合法"); return; }
        SetBusy(true, "连接 " + host + ":" + portStr + " …");
        try
        {
            var info = await Task.Run(() => SyncClient.GetInfo(host, port));
            _remote = info;
            _remoteHost = host;
            _remotePort = portStr;
            _peerLabel.Text = $"对方：{info.Device}（{host}:{portStr}，{info.Apps.Count} 个应用）";
            BuildDiff();
            Log("已连接 " + info.Device);
        }
        catch (Exception e)
        {
            _peerLabel.Text = "";
            Log("连接失败：" + e.Message);
            ToastForm.Show(this, "连接失败：" + e.Message);
        }
        finally { SetBusy(false, ""); }
    }

    private void BuildDiff()
    {
        _rows.Clear();
        _diffPanel.Controls.Clear();
        if (_remote == null) return;

        var local = AppRepo.List().ToDictionary(a => a.Id);
        var remote = _remote.Apps.ToDictionary(a => a.Id);

        foreach (var kv in local)
        {
            var row = new DiffRow { Local = kv.Value };
            row.Remote = remote.TryGetValue(kv.Key, out var ra) ? ra : null;
            row.State = row.Remote != null ? (row.Remote.StoreHash == AppRepo.StoreHash(kv.Key) ? "两边一致" : "数据不同") : "仅本机";
            row.Combo = MakeCombo(row);
            _rows.Add(row);
        }
        foreach (var kv in remote)
        {
            if (local.ContainsKey(kv.Key)) continue;
            var row = new DiffRow { Remote = kv.Value, State = "仅对方" };
            row.Combo = MakeCombo(row);
            _rows.Add(row);
        }

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        int i = 0;
        foreach (var row in _rows)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            table.Controls.Add(new Label
            {
                Text = Truncate(row.Name, 16),
                AutoSize = false,
                Size = new Size(244, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = M3.Font(9.5f)
            }, 0, i);
            var stateLabel = new Label
            {
                Text = row.State,
                AutoSize = false,
                Size = new Size(104, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = row.State == "数据不同" ? M3.ErrorColor : M3.OnSurfaceVariant
            };
            table.Controls.Add(stateLabel, 1, i);
            row.Combo.DropDownStyle = ComboBoxStyle.DropDownList;
            table.Controls.Add(row.Combo, 2, i);
            i++;
        }
        if (_rows.Count == 0)
        {
            table.Controls.Add(new Label { Text = "两边应用一致，无需同步。", AutoSize = true, ForeColor = M3.OnSurfaceVariant, Padding = new Padding(4, 10, 0, 0) }, 0, 0);
        }
        _diffPanel.Controls.Add(table);
    }

    private ComboBox MakeCombo(DiffRow row)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Font = M3.Font(9f) };
        bool both = row.Local != null && row.Remote != null;
        if (both)
        {
            combo.Items.AddRange(new object[] { "跳过", "推送给对方", "从对方获取" });
            bool localNewer = (row.Local!.InstalledAt >= row.Remote!.InstalledAt);
            combo.SelectedIndex = row.State == "数据不同" ? (localNewer ? 1 : 2) : 0;
        }
        else if (row.Local != null)
        {
            combo.Items.AddRange(new object[] { "跳过", "推送给对方" });
            combo.SelectedIndex = 0;
        }
        else
        {
            combo.Items.AddRange(new object[] { "跳过", "从对方获取" });
            combo.SelectedIndex = 0;
        }
        return combo;
    }

    private void AutoFill()
    {
        foreach (var row in _rows)
        {
            if (row.State == "仅本机") row.Combo.SelectedIndex = 1;                       // 推送
            else if (row.State == "仅对方") row.Combo.SelectedIndex = 1;                  // 拉取
            else if (row.State == "数据不同")
            {
                bool localNewer = row.Local!.InstalledAt >= row.Remote!.InstalledAt;
                row.Combo.SelectedIndex = localNewer ? 1 : 2;
            }
            else row.Combo.SelectedIndex = 0;                                             // 一致→跳过
        }
    }

    // ------------------------------------------------------------ 执行

    private async void RunTransfer()
    {
        if (_remote == null) { ToastForm.Show(this, "先连接一台设备"); return; }
        int push = _rows.Count(r => r.Combo.SelectedIndex == 1 && r.Local != null);
        int pull = _rows.Count(r => r.Combo.SelectedIndex == 1 && r.Local == null);
        if (push == 0 && pull == 0)
        {
            // 兼容"仅对方"行：方向 1 = 从对方获取
            pull = _rows.Count(r => r.Combo.SelectedIndex == 1);
        }
        if (_rows.All(r => r.Combo.SelectedIndex == 0)) { ToastForm.Show(this, "没有需要同步的项"); return; }

        SetBusy(true, "同步中…");
        int ok = 0, fail = 0;
        try
        {
            // 先在 UI 线程取方向快照，避免后台线程触碰控件
            var plan = _rows
                .Select(r => new { Row = r, Dir = r.Combo.SelectedIndex, LocalId = r.Local?.Id, RemoteId = r.Remote?.Id, Name = r.Name })
                .Where(p => p.Dir > 0)
                .ToList();
            int hostPort = int.TryParse(_remotePort, out int hp) ? hp : SyncServer.Port;
            await Task.Run(() =>
            {
                foreach (var p in plan)
                {
                    try
                    {
                        if (p.Dir == 1 && p.LocalId != null)
                        {
                            byte[] zip = ExportZip(p.LocalId);
                            SyncClient.PushApp(_remoteHost, hostPort, p.LocalId, zip);
                            BeginInvoke(new Action(() => Log($"↑ 已推送「{p.Name}」")));
                        }
                        else if (p.Dir >= 1 && p.RemoteId != null && (p.Dir == 2 || p.LocalId == null))
                        {
                            byte[] zip = SyncClient.GetApp(_remoteHost, hostPort, p.RemoteId);
                            string tmp = Path.Combine(Path.GetTempPath(), "miniappbox_pull_" + Guid.NewGuid().ToString("N") + ".zip");
                            File.WriteAllBytes(tmp, zip);
                            try { AppRepo.InstallSynced(tmp, p.RemoteId); }
                            finally { try { File.Delete(tmp); } catch { } }
                            BeginInvoke(new Action(() => Log($"↓ 已获取「{p.Name}」")));
                        }
                        else
                        {
                            continue;
                        }
                        Interlocked.Increment(ref ok);
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref fail);
                        string msg = e.Message;
                        BeginInvoke(new Action(() => Log($"× {p.Name} 失败：{msg}")));
                    }
                }
            });
            BeginInvoke(new Action(() =>
            {
                Log($"同步完成：成功 {ok}，失败 {fail}");
                if (_remote != null) RefreshDiff();
            }));
        }
        finally { SetBusy(false, ""); }
    }

    private void RefreshDiff()
    {
        try
        {
            _remote = SyncClient.GetInfo(_remoteHost, int.Parse(_remotePort));
            BuildDiff();
        }
        catch { /* 刷新失败保持现状 */ }
    }

    private static byte[] ExportZip(string id)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "miniappbox_push_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            AppRepo.Export(id, tmp);
            return File.ReadAllBytes(tmp);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ------------------------------------------------------------ 杂项

    private void SetBusy(bool busy, string statusText)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy, statusText)); return; }
        _btnConnect.Enabled = _btnRun.Enabled = _btnAuto.Enabled = !busy;
        UseWaitCursor = busy;
        _status.Text = statusText;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
    }

    private static Label Hr(int y) => new()
    {
        Location = new Point(18, y),
        Size = new Size(660, 1),
        BackColor = M3.OutlineVariant
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
