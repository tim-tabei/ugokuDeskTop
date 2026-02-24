using System.Diagnostics;

namespace ugokuDeskTop.Native;

internal static class WallpaperHelper
{
    /// <summary>
    /// 壁紙の埋め込み先ウィンドウを探す。
    /// Windows 11 ではウィンドウ階層が複数パターン存在するため、全パターンを試す。
    /// </summary>
    public static IntPtr FindWallpaperParent()
    {
        IntPtr progman = Win32Api.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Debug.WriteLine("[ugokuDeskTop] Progman not found");
            return IntPtr.Zero;
        }

        Debug.WriteLine($"[ugokuDeskTop] Progman: 0x{progman:X}");

        // 0x052C を送って WorkerW を生成させる
        Win32Api.SendMessageTimeout(
            progman, 0x052C,
            new UIntPtr(0x000D), IntPtr.Zero,
            Win32Api.SMTO_NORMAL, 1000, out _);

        Thread.Sleep(500);

        Win32Api.SendMessageTimeout(
            progman, 0x052C,
            new UIntPtr(0x000D), new IntPtr(1),
            Win32Api.SMTO_NORMAL, 1000, out _);

        Thread.Sleep(1000);

        // リトライ付きで埋め込み先を探す
        for (int attempt = 0; attempt < 15; attempt++)
        {
            IntPtr target = ScanForTarget(progman);
            if (target != IntPtr.Zero)
            {
                Debug.WriteLine($"[ugokuDeskTop] Target found: 0x{target:X} (attempt {attempt + 1})");
                return target;
            }

            Debug.WriteLine($"[ugokuDeskTop] Target not found, retrying... (attempt {attempt + 1})");
            Thread.Sleep(500);
        }

        Debug.WriteLine("[ugokuDeskTop] Target not found after all retries");
        return IntPtr.Zero;
    }

    private static IntPtr ScanForTarget(IntPtr progman)
    {
        // パターン1: Progman の子に SHELLDLL_DefView があり、
        //            その兄弟として WorkerW が Progman の子にいる場合
        //            (Windows 11 Build 26200+ で確認されたパターン)
        IntPtr shellInProgman = Win32Api.FindWindowEx(
            progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellInProgman != IntPtr.Zero)
        {
            IntPtr workerWChild = Win32Api.FindWindowEx(
                progman, IntPtr.Zero, "WorkerW", null);
            if (workerWChild != IntPtr.Zero)
            {
                Debug.WriteLine($"[ugokuDeskTop] Pattern 1: WorkerW as child of Progman: 0x{workerWChild:X}");
                return workerWChild;
            }

            // Progman の子に WorkerW がなければ、トップレベルの兄弟を探す
            IntPtr workerWSibling = Win32Api.FindWindowEx(
                IntPtr.Zero, progman, "WorkerW", null);
            if (workerWSibling != IntPtr.Zero)
            {
                Debug.WriteLine($"[ugokuDeskTop] Pattern 1b: WorkerW as sibling of Progman: 0x{workerWSibling:X}");
                return workerWSibling;
            }
        }

        // パターン2: トップレベルの WorkerW の中に SHELLDLL_DefView がある場合
        //            (従来の Windows 10 / Windows 11 初期パターン)
        IntPtr result = IntPtr.Zero;
        Win32Api.EnumWindows((topHandle, _) =>
        {
            IntPtr shellDefView = Win32Api.FindWindowEx(
                topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shellDefView != IntPtr.Zero)
            {
                // この WorkerW の次の兄弟 WorkerW が埋め込み先
                IntPtr nextWorkerW = Win32Api.FindWindowEx(
                    IntPtr.Zero, topHandle, "WorkerW", null);
                if (nextWorkerW != IntPtr.Zero)
                {
                    Debug.WriteLine($"[ugokuDeskTop] Pattern 2: next sibling WorkerW: 0x{nextWorkerW:X}");
                    result = nextWorkerW;
                }
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// 指定されたウィンドウを親ウィンドウの子にし、壁紙として画面全体に表示する
    /// </summary>
    public static void EmbedAsWallpaper(IntPtr windowHandle, IntPtr parentHandle)
    {
        Win32Api.SetParent(windowHandle, parentHandle);

        Win32Api.GetWindowRect(parentHandle, out Win32Api.RECT rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
        {
            width = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            height = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
        }

        Win32Api.MoveWindow(windowHandle, 0, 0, width, height, true);
        Win32Api.ShowWindow(windowHandle, Win32Api.SW_SHOW);
    }

    /// <summary>
    /// 壁紙の埋め込みを解除する
    /// </summary>
    public static void DetachWallpaper(IntPtr windowHandle)
    {
        Win32Api.SetParent(windowHandle, IntPtr.Zero);
    }
}
