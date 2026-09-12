namespace CloudflareBox.Service;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--init", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: CloudflareBox.Service --init <https://...workers.dev/api/v1/> <setup-token> [destination]");
                return 2;
            }
            var destination = args.Length >= 4 ? args[3] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CloudflareBOX");
            await ReceiverHost.InitializeAsync(args[1], args[2], destination, CancellationToken.None);
            return 0;
        }

        if (!File.Exists(AppPaths.Settings))
        {
            Console.Error.WriteLine("CloudflareBOX is not initialized. Run with --init first.");
            return 2;
        }

        if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            NativeService.Run(ct => ReceiverHost.RunAsync(false, ct));
            return 0;
        }

        var once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            await ReceiverHost.RunAsync(once, cts.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
