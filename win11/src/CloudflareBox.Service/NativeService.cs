using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CloudflareBox.Service;

internal static class NativeService
{
    private const int ServiceWin32OwnProcess = 0x10;
    private const int ServiceStartPending = 0x2;
    private const int ServiceStopPending = 0x3;
    private const int ServiceRunning = 0x4;
    private const int ServiceStopped = 0x1;
    private const int AcceptStop = 0x1;
    private const int AcceptShutdown = 0x4;
    private const int ControlStop = 0x1;
    private const int ControlShutdown = 0x5;

    private delegate void ServiceMainDelegate(int argc, IntPtr argv);
    private delegate int HandlerDelegate(int control, int eventType, IntPtr eventData, IntPtr context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public ServiceMainDelegate? Main;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher(ServiceTableEntry[] table);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerDelegate handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);

    private static Func<CancellationToken, Task>? work;
    private static CancellationTokenSource? cancellation;
    private static IntPtr statusHandle;
    private static readonly ServiceMainDelegate MainDelegate = ServiceMain;
    private static readonly HandlerDelegate Handler = HandleControl;

    public static void Run(Func<CancellationToken, Task> worker)
    {
        work = worker;
        var table = new[]
        {
            new ServiceTableEntry { Name = "CloudflareBOX", Main = MainDelegate },
            new ServiceTableEntry { Name = null, Main = null },
        };
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        statusHandle = RegisterServiceCtrlHandlerEx("CloudflareBOX", Handler, IntPtr.Zero);
        if (statusHandle == IntPtr.Zero) return;
        SetState(ServiceStartPending, 0, 15_000);
        cancellation = new CancellationTokenSource();
        SetState(ServiceRunning, AcceptStop | AcceptShutdown, 0);
        var exitCode = 0;
        try { work!(cancellation.Token).GetAwaiter().GetResult(); }
        catch { exitCode = 1; }
        SetState(ServiceStopped, 0, 0, exitCode);
    }

    private static int HandleControl(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        if (control is ControlStop or ControlShutdown)
        {
            SetState(ServiceStopPending, 0, 15_000);
            cancellation?.Cancel();
        }
        return 0;
    }

    private static void SetState(int state, int accepted, int waitHint, int exitCode = 0)
    {
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = accepted,
            Win32ExitCode = exitCode,
            WaitHint = waitHint,
        };
        SetServiceStatus(statusHandle, ref status);
    }
}
