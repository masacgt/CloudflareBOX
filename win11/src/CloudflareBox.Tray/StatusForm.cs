using System.Diagnostics;
using System.Text.Json;

namespace CloudflareBox.Tray;

internal sealed class StatusForm : Form
{
    private const string AccountSelectionPrefix = "CLOUDFLAREBOX_ACCOUNT_SELECTION:";
    private readonly Label status = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly Label destination = new() { AutoSize = true };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private static string Root => Environment.GetEnvironmentVariable("CLOUDFLAREBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloudflareBOX");

    public StatusForm()
    {
        Text = "CloudflareBOX";
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(status);
        top.Controls.Add(destination);

        var primary = new FlowLayoutPanel { AutoSize = true };
        primary.Controls.Add(Button("Cloudflareと連携", async () => await ConnectCloudflareAsync()));
        primary.Controls.Add(Button("診断", async () => await RunAndShowAsync("--diagnose")));
        primary.Controls.Add(Button("修復", async () => await RunAndShowAsync("--repair-cloudflare")));
        primary.Controls.Add(Button("Androidを追加", async () => await AddAndroidAsync()));
        primary.Controls.Add(Button("端末一覧・失効", async () => await ManageDevicesAsync()));
        primary.Controls.Add(Button("復旧情報を保存", async () => await ExportRecoveryAsync()));
        top.Controls.Add(primary);

        var secondary = new FlowLayoutPanel { AutoSize = true };
        secondary.Controls.Add(Button("更新", () => { RefreshView(); return Task.CompletedTask; }));
        secondary.Controls.Add(Button("保存先を開く", () => { OpenDestination(); return Task.CompletedTask; }));
        secondary.Controls.Add(Button("初回ペアリングJSONをコピー", () => { CopyInitialPairing(); return Task.CompletedTask; }));
        secondary.Controls.Add(Button("受信 一時停止/再開", () => { TogglePause(); return Task.CompletedTask; }));
        secondary.Controls.Add(Button("Cloudflare連携解除", async () => await UnlinkCloudflareAsync()));
        top.Controls.Add(secondary);

        Controls.Add(details);
        Controls.Add(top);

        var menu = new ContextMenuStrip();
        menu.Items.Add("開く", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("保存先を開く", null, (_, _) => OpenDestination());
        menu.Items.Add("診断", null, async (_, _) => await RunAndShowAsync("--diagnose"));
        menu.Items.Add("受信 一時停止/再開", null, (_, _) => TogglePause());
        menu.Items.Add("終了", null, (_, _) => { tray.Visible = false; Application.Exit(); });
        tray.Text = "CloudflareBOX";
        tray.Icon = SystemIcons.Application;
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        timer.Tick += (_, _) => RefreshView();
        timer.Start();
        RefreshView();
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try { await action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "CloudflareBOX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { button.Enabled = true; }
        };
        return button;
    }

    private async Task ConnectCloudflareAsync()
    {
        var clientId = ResolveOAuthClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            MessageBox.Show(this, "Cloudflare OAuth Client ID が配布物に設定されていません。", "CloudflareBOX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var destinationPath = ReadDestination();
        var result = await RunServiceAsync("--connect-cloudflare", clientId, "", destinationPath);
        if (result.ExitCode != 0)
        {
            ShowCommandResult(result, "Cloudflare連携");
            RefreshView();
            return;
        }

        var accounts = ParseAccountSelection(result.StdOut);
        if (accounts.Count > 0)
        {
            var selected = ChooseCloudflareAccount(accounts);
            if (selected is null)
            {
                details.Text = "Cloudflare認証は完了しています。連携するアカウントが未選択です。もう一度「Cloudflareと連携」を押すと選択をやり直せます。";
                RefreshView();
                return;
            }

            var completed = await RunServiceAsync("--complete-cloudflare", selected.Id);
            ShowCommandResult(completed, "Cloudflare連携");
            RefreshView();
            return;
        }

        ShowCommandResult(result, "Cloudflare連携");
        RefreshView();
    }

    private async Task AddAndroidAsync()
    {
        var result = await RunServiceAsync("--pair-device");
        if (result.ExitCode != 0) { ShowCommandResult(result, "Android追加"); return; }
        details.Text = "Android追加ペアリング\r\n\r\n" + result.StdOut;
        if (!string.IsNullOrWhiteSpace(result.StdOut)) Clipboard.SetText(result.StdOut.Trim());
        MessageBox.Show(this, "追加ペアリング情報を表示し、クリップボードにもコピーしました。", "CloudflareBOX");
    }

    private async Task ManageDevicesAsync()
    {
        var listed = await RunServiceAsync("--list-devices");
        if (listed.ExitCode != 0) { ShowCommandResult(listed, "端末一覧"); return; }
        details.Text = "登録端末\r\n\r\n" + listed.StdOut;
        var deviceId = Prompt("失効させる Android の deviceId を入力してください。\n一覧確認だけの場合は空欄のまま閉じてください。", "端末一覧・失効");
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        var revoked = await RunServiceAsync("--revoke-device", deviceId.Trim());
        ShowCommandResult(revoked, "端末失効");
    }

    private async Task ExportRecoveryAsync()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "CloudflareBOX 復旧ファイルの保存先",
            Filter = "CloudflareBOX Recovery (*.cbxr)|*.cbxr|All files (*.*)|*.*",
            FileName = "cloudflarebox-recovery.cbxr",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var result = await RunServiceAsync("--export-recovery", dialog.FileName);
        ShowCommandResult(result, "復旧情報");
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut)) Clipboard.SetText(result.StdOut.Trim());
    }

    private async Task UnlinkCloudflareAsync()
    {
        var answer = MessageBox.Show(this, "このインストール専用の Worker・D1・R2 を削除し、Cloudflare連携を解除します。転送中データがある場合は自動的に拒否されます。続行しますか？", "CloudflareBOX", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        var result = await RunServiceAsync("--unlink-cloudflare");
        ShowCommandResult(result, "Cloudflare連携解除");
        RefreshView();
    }

    private async Task RunAndShowAsync(params string[] args)
    {
        var result = await RunServiceAsync(args);
        ShowCommandResult(result, string.Join(' ', args));
        RefreshView();
    }

    private void ShowCommandResult(CommandResult result, string title)
    {
        details.Text = result.ExitCode == 0 ? result.StdOut : result.StdErr + (string.IsNullOrWhiteSpace(result.StdOut) ? "" : "\r\n" + result.StdOut);
        MessageBox.Show(this, result.ExitCode == 0 ? "完了しました。" : details.Text, title, MessageBoxButtons.OK, result.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private async Task<CommandResult> RunServiceAsync(params string[] args)
    {
        var serviceExe = ResolveServiceExe();
        if (!File.Exists(serviceExe)) throw new FileNotFoundException("CloudflareBox.Service.exe が見つかりません。", serviceExe);
        var start = new ProcessStartInfo(serviceExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CloudflareBOX サービス操作を開始できませんでした。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static List<AccountChoice> ParseAccountSelection(string stdout)
    {
        var line = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => x.StartsWith(AccountSelectionPrefix, StringComparison.Ordinal));
        if (line is null) return [];

        using var doc = JsonDocument.Parse(line[AccountSelectionPrefix.Length..]);
        var accounts = new List<AccountChoice>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            var name = item.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(id)) accounts.Add(new AccountChoice(id, string.IsNullOrWhiteSpace(name) ? "Cloudflare account" : name));
        }
        return accounts;
    }

    private AccountChoice? ChooseCloudflareAccount(IReadOnlyList<AccountChoice> accounts)
    {
        using var form = new Form
        {
            Text = "Cloudflareアカウントを選択",
            Width = 620,
            Height = 210,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var label = new Label { Left = 12, Top = 14, Width = 580, Height = 42, Text = "CloudflareBOX用のR2・D1・Workerを作成するアカウントを選択してください。" };
        var combo = new ComboBox { Left = 12, Top = 62, Width = 580, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var account in accounts) combo.Items.Add(account);
        combo.SelectedIndex = 0;
        var ok = new Button { Text = "このアカウントを使用", Left = 370, Top = 110, Width = 130, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "キャンセル", Left = 512, Top = 110, Width = 80, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([label, combo, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem as AccountChoice : null;
    }

    private string ResolveOAuthClientId()
    {
        var env = Environment.GetEnvironmentVariable("CLOUDFLAREBOX_OAUTH_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "oauth-client-id.txt"),
            Path.Combine(Path.GetDirectoryName(ResolveServiceExe())!, "oauth-client-id.txt"),
        })
        {
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
        }
        return "";
    }

    private static string ResolveServiceExe()
    {
        var same = Path.Combine(AppContext.BaseDirectory, "CloudflareBox.Service.exe");
        if (File.Exists(same)) return same;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service", "CloudflareBox.Service.exe"));
    }

    private void RefreshView()
    {
        try
        {
            var settingsPath = Path.Combine(Root, "settings.json");
            var statePath = Path.Combine(Root, "state.json");
            var pairingPath = Path.Combine(Root, "pending-pairing.json");
            var settingsJson = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "{}";
            using var settingsDoc = JsonDocument.Parse(settingsJson);
            destination.Text = "保存先: " + (settingsDoc.RootElement.TryGetProperty("destinationDirectory", out var d) ? d.GetString() : "未設定");
            var stateJson = File.Exists(statePath) ? File.ReadAllText(statePath) : "{\"status\":\"not initialized\"}";
            using var stateDoc = JsonDocument.Parse(stateJson);
            var stateText = stateDoc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "unknown";
            if (File.Exists(Path.Combine(Root, "paused.flag"))) stateText = "paused";
            status.Text = "状態: " + stateText;
            var pairingJson = File.Exists(pairingPath) ? File.ReadAllText(pairingPath) : "";
            details.Text = pairingJson.Length > 0 ? "初回ペアリング待機中\r\n\r\n" + pairingJson : "状態詳細\r\n\r\n" + stateJson;
        }
        catch (Exception ex)
        {
            status.Text = "状態: 読み取りエラー";
            details.Text = ex.Message;
        }
    }

    private string ReadDestination()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
        var settingsPath = Path.Combine(Root, "settings.json");
        if (!File.Exists(settingsPath)) return path;
        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (doc.RootElement.TryGetProperty("destinationDirectory", out var d) && !string.IsNullOrWhiteSpace(d.GetString())) return d.GetString()!;
        return path;
    }

    private void OpenDestination()
    {
        try
        {
            var path = ReadDestination();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CloudflareBOX"); }
    }

    private void CopyInitialPairing()
    {
        var path = Path.Combine(Root, "pending-pairing.json");
        if (File.Exists(path)) Clipboard.SetText(File.ReadAllText(path));
        else MessageBox.Show(this, "現在、初回ペアリング待機情報はありません。", "CloudflareBOX");
    }

    private void TogglePause()
    {
        var flag = Path.Combine(Root, "paused.flag");
        try
        {
            if (File.Exists(flag)) File.Delete(flag);
            else
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(flag, DateTimeOffset.UtcNow.ToString("O"));
            }
            RefreshView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CloudflareBOX"); }
    }

    private static string? Prompt(string message, string title)
    {
        using var form = new Form { Text = title, Width = 620, Height = 180, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var label = new Label { Left = 12, Top = 12, Width = 580, Height = 44, Text = message };
        var input = new TextBox { Left = 12, Top = 62, Width = 580 };
        var ok = new Button { Text = "実行", Left = 420, Top = 98, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "閉じる", Left = 512, Top = 98, Width = 80, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([label, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
    private sealed record AccountChoice(string Id, string Name)
    {
        public override string ToString() => $"{Name} ({Id})";
    }
}
