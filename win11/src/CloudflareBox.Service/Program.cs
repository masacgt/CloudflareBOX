using System.Text.Json;

namespace CloudflareBox.Service;

internal static class Program
{
    private const string AccountSelectionPrefix = "CLOUDFLAREBOX_ACCOUNT_SELECTION:";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0)
            {
                var command = args[0].ToLowerInvariant();
                if (command == "--connect-cloudflare")
                {
                    var clientId = Arg(args, 1) ?? Environment.GetEnvironmentVariable("CLOUDFLAREBOX_OAUTH_CLIENT_ID");
                    if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("Cloudflare OAuth client ID is required.");
                    var accountId = Arg(args, 2) ?? Environment.GetEnvironmentVariable("CLOUDFLAREBOX_ACCOUNT_ID");
                    var destination = Arg(args, 3) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
                    var bundle = Arg(args, 4) ?? DefaultWorkerBundlePath();
                    var accounts = await ReceiverHost.ConnectCloudflareAsync(clientId, accountId, bundle, destination, CancellationToken.None);
                    if (accounts.Count > 0)
                    {
                        var payload = accounts.Select(a => new { id = a.Id, name = a.Name }).ToArray();
                        Console.WriteLine(AccountSelectionPrefix + JsonSerializer.Serialize(payload, SettingsStore.Options));
                    }
                    return 0;
                }
                if (command == "--complete-cloudflare")
                {
                    var accountId = Arg(args, 1) ?? throw new ArgumentException("Cloudflare account ID is required.");
                    await ReceiverHost.CompleteCloudflareConnectionAsync(accountId, CancellationToken.None);
                    return 0;
                }
                if (command == "--repair-cloudflare")
                {
                    await ReceiverHost.RepairCloudflareAsync(Arg(args, 1), CancellationToken.None);
                    return 0;
                }
                if (command == "--unlink-cloudflare")
                {
                    await ReceiverHost.UnlinkCloudflareAsync(CancellationToken.None);
                    return 0;
                }
                if (command == "--diagnose")
                {
                    Console.WriteLine(JsonSerializer.Serialize(await ReceiverHost.DiagnoseAsync(CancellationToken.None), SettingsStore.Options));
                    return 0;
                }
                if (command == "--pair-device")
                {
                    var pairing = await ReceiverHost.CreateAdditionalPairingAsync(CancellationToken.None);
                    Console.WriteLine(JsonSerializer.Serialize(pairing, SettingsStore.Options));
                    return 0;
                }
                if (command == "--list-devices")
                {
                    Console.WriteLine(JsonSerializer.Serialize(await ReceiverHost.ListDevicesAsync(CancellationToken.None), SettingsStore.Options));
                    return 0;
                }
                if (command == "--revoke-device")
                {
                    var deviceId = Arg(args, 1) ?? throw new ArgumentException("Device ID is required.");
                    await ReceiverHost.RevokeDeviceAsync(deviceId, CancellationToken.None);
                    return 0;
                }
                if (command == "--export-recovery")
                {
                    var path = Arg(args, 1) ?? AppPaths.DefaultRecoveryBundle;
                    var code = ReceiverHost.ExportRecovery(path, Arg(args, 2));
                    Console.WriteLine($"Recovery bundle: {Path.GetFullPath(path)}");
                    Console.WriteLine($"Recovery code: {code}");
                    return 0;
                }
                if (command == "--import-recovery")
                {
                    var path = Arg(args, 1) ?? throw new ArgumentException("Recovery bundle path is required.");
                    var code = Arg(args, 2) ?? throw new ArgumentException("Recovery code is required.");
                    ReceiverHost.ImportRecovery(path, code);
                    return 0;
                }
                if (command == "--init")
                {
                    if (args.Length < 3) throw new ArgumentException("Usage: --init <api-base> <setup-token> [destination]");
                    var destination = Arg(args, 3) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
                    await ReceiverHost.InitializeAsync(args[1], args[2], destination, CancellationToken.None);
                    return 0;
                }
            }

            if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
            {
                NativeService.Run(ct => ReceiverHost.RunAsync(false, ct));
                return 0;
            }

            if (!File.Exists(AppPaths.Settings))
            {
                Console.Error.WriteLine("CloudflareBOX is not initialized. Use --connect-cloudflare first.");
                return 2;
            }

            var once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await ReceiverHost.RunAsync(once, cts.Token);
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? Arg(string[] args, int index) => args.Length > index && !string.IsNullOrWhiteSpace(args[index]) ? args[index] : null;

    private static string DefaultWorkerBundlePath()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "worker", "cloudflarebox-worker.mjs");
        if (File.Exists(packaged)) return packaged;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cloudflare", "dist", "cloudflarebox-worker.mjs"));
    }
}
