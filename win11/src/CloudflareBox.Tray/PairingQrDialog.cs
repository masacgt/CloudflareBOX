using System.Diagnostics;
using System.Text.Json;
using QRCoder;

namespace CloudflareBox.Tray;

internal static class PairingQrDialog
{
    public static void Show(IWin32Window owner, string pairingJson, string serviceExe)
    {
        using var doc = JsonDocument.Parse(pairingJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("qrPayload", out var qrPayload)) throw new InvalidDataException("ペアリングQR情報がありません。");
        var pairingId = root.GetProperty("pairingId").GetString() ?? throw new InvalidDataException("pairingId がありません。");
        var code = root.GetProperty("code").GetString() ?? throw new InvalidDataException("pairing code がありません。");
        var expiresAt = root.GetProperty("expiresAt").GetInt64();
        var qrJson = qrPayload.GetRawText();

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(qrJson, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(qrData).GetGraphic(7);
        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);
        using var form = new Form
        {
            Text = "Androidをペアリング",
            Width = 540,
            Height = 700,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var title = new Label { Text = "スマホでQRコードを読み取ってください", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Left = 22, Top = 18 };
        var note = new Label { Text = "有効期限は5分、1回だけ使用できます。読み取り後にこのPCで承認してください。", Left = 22, Top = 48, Width = 480, Height = 40 };
        var picture = new PictureBox { Left = 86, Top = 92, Width = 360, Height = 360, SizeMode = PictureBoxSizeMode.Zoom, Image = new Bitmap(image) };
        var expires = DateTimeOffset.FromUnixTimeSeconds(expiresAt).ToLocalTime();
        var expiry = new Label { Text = $"有効期限: {expires:yyyy/MM/dd HH:mm:ss}", Left = 22, Top = 466, Width = 480, Height = 24 };
        var status = new Label { Text = "Androidで読み取り後、「この端末を承認」を押してください。", Left = 22, Top = 498, Width = 480, Height = 42 };
        var copy = new Button { Text = "JSONをコピー", Left = 22, Top = 554, Width = 120, Height = 34 };
        var approve = new Button { Text = "この端末を承認", Left = 152, Top = 554, Width = 170, Height = 34 };
        var close = new Button { Text = "閉じる", Left = 332, Top = 554, Width = 110, Height = 34, DialogResult = DialogResult.Cancel };
        copy.Click += (_, _) => Clipboard.SetText(pairingJson.Trim());
        approve.Click += async (_, _) =>
        {
            approve.Enabled = false;
            try
            {
                var result = await RunServiceAsync(serviceExe, "--approve-pairing", pairingId, code);
                if (result.ExitCode == 0)
                {
                    status.Text = "ペアリングが完了しました。";
                    MessageBox.Show(form, "Android端末を承認しました。", "CFBox", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    status.Text = result.StdErr.Contains("Scan the QR code", StringComparison.OrdinalIgnoreCase)
                        ? "先にAndroidでQRコードを読み取ってください。"
                        : result.StdErr.Trim();
                }
            }
            finally { approve.Enabled = true; }
        };
        form.Controls.AddRange([title, note, picture, expiry, status, copy, approve, close]);
        form.CancelButton = close;
        form.ShowDialog(owner);
    }

    private static async Task<CommandResult> RunServiceAsync(string serviceExe, params string[] args)
    {
        var start = new ProcessStartInfo(serviceExe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("CFBox サービス操作を開始できませんでした。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
