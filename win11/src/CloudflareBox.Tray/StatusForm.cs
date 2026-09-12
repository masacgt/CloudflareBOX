using System.Text.Json;

namespace CloudflareBox.Tray;

internal sealed class StatusForm : Form
{
    private readonly Label status = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly Label destination = new() { AutoSize = true };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private static string Root => Environment.GetEnvironmentVariable("CLOUDFLAREBOX_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloudflareBOX");

    public StatusForm()
    {
        Text = "CloudflareBOX";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(16), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        top.Controls.Add(status);
        top.Controls.Add(destination);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var refresh = new Button { Text = "更新", AutoSize = true };
        var open = new Button { Text = "保存先を開く", AutoSize = true };
        var copy = new Button { Text = "ペアリングJSONをコピー", AutoSize = true };
        var pause = new Button { Text = "受信 一時停止/再開", AutoSize = true };
        refresh.Click += (_, _) => RefreshView();
        open.Click += (_, _) => OpenDestination();
        copy.Click += (_, _) => CopyPairing();
        pause.Click += (_, _) => TogglePause();
        buttons.Controls.Add(refresh); buttons.Controls.Add(open); buttons.Controls.Add(copy); buttons.Controls.Add(pause);
        top.Controls.Add(buttons);
        Controls.Add(details); Controls.Add(top);

        var menu = new ContextMenuStrip();
        menu.Items.Add("開く", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("保存先を開く", null, (_, _) => OpenDestination());
        menu.Items.Add("受信 一時停止/再開", null, (_, _) => TogglePause());
        menu.Items.Add("終了", null, (_, _) => { tray.Visible = false; Application.Exit(); });
        tray.Text = "CloudflareBOX"; tray.Icon = SystemIcons.Application; tray.ContextMenuStrip = menu; tray.Visible = true;
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        timer.Tick += (_, _) => RefreshView();
        timer.Start();
        RefreshView();
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
            details.Text = pairingJson.Length > 0 ? "ペアリング待機中\r\n\r\n" + pairingJson : "状態詳細\r\n\r\n" + stateJson;
        }
        catch (Exception ex)
        {
            status.Text = "状態: 読み取りエラー";
            details.Text = ex.Message;
        }
    }

    private void OpenDestination()
    {
        try
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var settingsPath = Path.Combine(Root, "settings.json");
            if (File.Exists(settingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("destinationDirectory", out var d) && !string.IsNullOrWhiteSpace(d.GetString())) path = d.GetString()!;
            }
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CloudflareBOX"); }
    }

    private void CopyPairing()
    {
        var path = Path.Combine(Root, "pending-pairing.json");
        if (File.Exists(path)) Clipboard.SetText(File.ReadAllText(path));
        else MessageBox.Show(this, "現在ペアリング待機情報はありません。", "CloudflareBOX");
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
}

