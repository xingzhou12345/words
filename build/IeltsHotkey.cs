// ============================================================
//  IeltsHotkey.exe  ——  雅思划词查词 · 轻量热键守护
//  Ctrl+Alt+D → 复制当前选中文字 → 弹出查词小窗
//  编译：csc.exe /target:winexe /out:IeltsHotkey.exe IeltsHotkey.cs
//  说明：纯 Win32 P/Invoke，不依赖 WinForms，常驻内存约 5MB
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

static class Program
{
    // ---------- Win32 常量 ----------
    const int WM_HOTKEY = 0x0312;
    const int WM_DESTROY = 0x0002;
    const int WM_CLOSE = 0x0010;
    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    const uint MOD_WIN = 0x0008;
    const int VK_CONTROL = 0x11;
    const int VK_MENU = 0x12;
    const uint CF_UNICODETEXT = 13;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;
    const uint GMEM_MOVEABLE = 0x0002;
    // 句柄型剪贴板格式：不能按 HGLOBAL 字节拷贝，快照时跳过
    const uint CF_BITMAP = 2;
    const uint CF_METAFILEPICT = 3;
    const uint CF_PALETTE = 9;
    const uint CF_ENHMETAFILE = 14;
    const uint CF_OWNERDISPLAY = 0x0080;
    const uint CF_DIB = 8;
    const uint CF_DIBV5 = 17;

    // ---------- 状态 ----------
    static string gDir = "";
    static string gLog = "";
    static Mutex gMutex = null;
    static uint gMods = MOD_CONTROL | MOD_ALT;
    static uint gVk = 0x44; // D
    static string gHotkeyName = "Ctrl+Alt+D";
    static int gBusy = 0;
    static bool gWarnedNonText = false;
    static IntPtr gHwnd = IntPtr.Zero;
    static WndProcDelegate gWndProc = null;

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ---------- P/Invoke ----------
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]
    static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    static extern int CountClipboardFormats();
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    static extern uint EnumClipboardFormats(uint format);
    [DllImport("kernel32.dll")]
    static extern UIntPtr GlobalSize(IntPtr hMem);
    [DllImport("user32.dll")]
    static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    static extern void PostQuitMessage(int nExitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    // ---------- 入口 ----------
    [STAThread]
    static void Main(string[] args)
    {
        try { gDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        catch { gDir = Environment.CurrentDirectory; }
        gLog = Path.Combine(gDir, "hotkey.log");
        Log("boot" + (args.Length > 0 ? ", mode=" + args[0] : ""));

        try { Run(args); }
        catch (Exception ex) { Log("fatal: " + ex.GetType().Name + " " + ex.Message); }
    }

    static void Run(string[] args)
    {
        // 命令行自检：IeltsHotkey.exe --test delve
        if (args.Length >= 2 && args[0] == "--test")
        {
            Launch(args[1]);
            return;
        }
        // 命令行自检：IeltsHotkey.exe --clipboard-test
        // 只验证全格式快照能否原样写回，日志里只有通过/失败，不记录任何剪贴板内容
        if (args.Length >= 1 && args[0] == "--clipboard-test")
        {
            CreateHiddenWindow();
            ClipSnapshot before = CaptureClipboard();
            string beforeText = GetClipboardText();
            if (!before.Complete)
            {
                Log("clipboard-test: skipped=incomplete formats=" + before.Formats.Count);
                return;
            }
            // 每次用不同哨兵，避免和上一次或别人的剪贴板内容混淆
            string sentinel = "SENTINEL-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            bool okWrite = SetClipboardText(sentinel);
            bool sentinelVisible = GetClipboardText() == sentinel;
            // 恢复时会再次核对内容是否仍是这个哨兵，不是就放弃覆盖
            uint sentinelSeq = GetClipboardSequenceNumber();
            bool okRestore = RestoreClipboardIfText(before, sentinel, sentinelSeq);
            ClipSnapshot after = CaptureClipboard();

            bool sameFormats = SameFormats(before, after);
            bool sameBytes = SameBytes(before, after);
            bool textMatch = (GetClipboardText() == beforeText);
            Log("clipboard-test: formats=" + before.Formats.Count
                + " complete=" + before.Complete
                + " write=" + okWrite
                + " sentinel=" + sentinelVisible
                + " restore=" + okRestore
                + " formatsMatch=" + sameFormats
                + " bytesMatch=" + sameBytes
                + " textMatch=" + textMatch);
            return;
        }
        // 命令行自检：哨兵被别的程序抢走时，恢复必须拒绝。会临时替换剪贴板，跑完还回去
        if (args.Length >= 1 && args[0] == "--clipboard-guard-test")
        {
            CreateHiddenWindow();
            ClipSnapshot before = CaptureClipboard();
            string beforeText = GetClipboardText();
            string sentinel = "SENTINEL-" + Guid.NewGuid().ToString("N").Substring(0, 10);

            bool wroteSentinel = SetClipboardText(sentinel);
            bool wroteOther = SetClipboardText("OTHER-APP-CONTENT");     // 模拟别人抢先写入

            bool restored = RestoreClipboardIfText(before, sentinel, 0);
            string now = GetClipboardText();
            bool keptOther = (now == "OTHER-APP-CONTENT");
            Log("clipboard-guard-test: writeSentinel=" + wroteSentinel
                + " writeOther=" + wroteOther
                + " restoreRefused=" + (!restored)
                + " keptOther=" + keptOther);

            // 第二个场景：同样的文字又被写了一次（内容一样，但序列号变了）
            bool wroteAgain = SetClipboardText(sentinel);
            uint seqBeforeRewrite = GetClipboardSequenceNumber();
            bool rewrote = SetClipboardText(sentinel);              // 同文本，再写一次
            bool restored2 = RestoreClipboardIfText(before, sentinel, seqBeforeRewrite);
            Log("clipboard-guard-test: sameTextRewrite"
                + " wrote=" + (wroteAgain && rewrote)
                + " restoreRefused=" + (!restored2)
                + " keptSentinel=" + (GetClipboardText() == sentinel));

            // 收尾：把原来的剪贴板还回去（此时内容是我们自己写的哨兵，覆盖是安全的）
            bool cleaned = RestoreClipboardIfText(before, sentinel, 0);
            Log("clipboard-guard-test: cleanup=" + cleaned
                + " restoredOriginal=" + (GetClipboardText() == beforeText));
            return;
        }
        // 命令行自检：模拟"没选中任何东西"，验证不会动剪贴板
        if (args.Length >= 1 && args[0] == "--nocopy-test")
        {
            CreateHiddenWindow();
            uint seqBefore = GetClipboardSequenceNumber();
            ClipSnapshot before = CaptureClipboard();
            LookupCore(false);
            uint seqAfter = GetClipboardSequenceNumber();
            ClipSnapshot after = CaptureClipboard();
            Log("nocopy-test: seqChanged=" + (seqAfter != seqBefore)
                + " formatsMatch=" + SameFormats(before, after)
                + " bytesMatch=" + SameBytes(before, after));
            return;
        }
        // 命令行自检：用剪贴板内容弹窗
        if (args.Length >= 1 && args[0] == "--simulate")
        {
            string c = GetClipboardText();
            Log("simulate, clipboard length=" + (c == null ? 0 : c.Length));
            if (!string.IsNullOrEmpty(c)) Launch(c);
            return;
        }

        bool isNew = false;
        try
        {
            gMutex = new Mutex(true, "Local\\IeltsHotkeyV2", out isNew);
            if (!isNew) { Log("another instance is running, exit"); return; }
        }
        catch (Exception ex) { Log("mutex error: " + ex.Message); }

        try { SetProcessDPIAware(); } catch { }
        LoadConfig();

        string html = Path.Combine(gDir, "index.html");
        if (!File.Exists(html)) { Log("html missing: " + html); return; }
        if (FindBrowser() == null) { Log("Chrome / Edge not found"); return; }

        if (!CreateHiddenWindow()) { Log("create window failed"); return; }

        if (!RegisterHotKey(gHwnd, 1, gMods, gVk))
        {
            Log("hotkey registration failed (" + gHotkeyName + ")");
            MessageBoxW(IntPtr.Zero, "热键 " + gHotkeyName + " 已被其它程序占用。\r\n请修改 hotkey.ini 里的配置，或关掉占用它的软件。",
                "雅思划词查词", 0x30);
            return;
        }

        Log("started, hotkey = " + gHotkeyName);
        Log("browser: " + FindBrowser());

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnregisterHotKey(gHwnd, 1);
        Log("stopped");
    }

    // ---------- 窗口 ----------
    static bool CreateHiddenWindow()
    {
        IntPtr hInst = GetModuleHandle(null);
        gWndProc = new WndProcDelegate(WndProcImpl);

        WNDCLASSEX wc = new WNDCLASSEX();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
        wc.style = 0;
        wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(gWndProc);
        wc.hInstance = hInst;
        wc.lpszClassName = "IeltsHotkeyWindowClass";
        if (RegisterClassEx(ref wc) == 0) return false;

        // 不显示：style = 0，且不带 WS_VISIBLE
        gHwnd = CreateWindowEx(0, "IeltsHotkeyWindowClass", "IeltsHotkey", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        return gHwnd != IntPtr.Zero;
    }

    static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            ThreadPool.QueueUserWorkItem(delegate { DoLookup(); });
            return IntPtr.Zero;
        }
        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // ---------- 核心流程 ----------
    static void DoLookup()
    {
        if (Interlocked.Exchange(ref gBusy, 1) == 1) return;
        try { LookupCore(); }
        catch (Exception ex) { Log("error: " + ex.Message); }
        finally { Interlocked.Exchange(ref gBusy, 0); }
    }

    static void LookupCore(bool sendCopy = true)
    {
        // 等 Ctrl / Alt 松开，否则 Ctrl+C 会变成 Alt+C
        for (int i = 0; i < 40; i++)
        {
            if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0 &&
                (GetAsyncKeyState(VK_MENU) & 0x8000) == 0) break;
            Thread.Sleep(20);
        }

        // 全格式快照（图片/文件/富文本都按字节备份；句柄型格式会标记 complete=false）
        ClipSnapshot snap = CaptureClipboard();
        if (!snap.Complete)
        {
            Log("lookup skipped: clipboard holds a format that cannot be snapshotted");
            if (!gWarnedNonText)
            {
                gWarnedNonText = true;
                MessageBoxW(IntPtr.Zero,
                    "剪贴板里有无法备份的内容（例如老式图片句柄格式）。\r\n这次没有划词，以免弄丢它。\r\n\r\n换成文字后再按热键即可。",
                    "雅思划词查词", 0x40);
            }
            return;
        }
        string backupText = GetClipboardText();

        uint seq0 = GetClipboardSequenceNumber();
        if (sendCopy) SendCtrlC();

        string text = null;
        bool copied = false;
        uint copiedSeq = 0;
        for (int i = 0; i < 14; i++)
        {
            Thread.Sleep(60);
            uint now = GetClipboardSequenceNumber();
            if (now == seq0) continue;          // 还没复制成功
            copied = true;
            copiedSeq = now;
            text = GetClipboardText();
            if (!string.IsNullOrEmpty(text)) break;
        }

        // 还原前在持有剪贴板的状态下核对内容：还是我们刚复制的这段，才写回快照
        if (copied && !string.IsNullOrEmpty(text))
        {
            if (!RestoreClipboardIfText(snap, text, copiedSeq)) Log("warning: clipboard restore skipped or incomplete");
        }

        if (string.IsNullOrEmpty(text)) { Log("nothing selected (clipboard unchanged)"); return; }
        text = text.Trim();
        if (text.Length == 0) { Log("nothing selected"); return; }
        if (text.Length > 1200) text = text.Substring(0, 1200);

        Launch(text);
    }

    // ---------- 剪贴板全格式快照 ----------
    class ClipSnapshot
    {
        public List<uint> Formats = new List<uint>();
        public List<byte[]> Blobs = new List<byte[]>();
        public bool Complete = true;
    }

    static bool IsHandleFormat(uint fmt)
    {
        return fmt == CF_BITMAP || fmt == CF_METAFILEPICT || fmt == CF_PALETTE
            || fmt == CF_ENHMETAFILE || fmt == CF_OWNERDISPLAY;
    }

    static ClipSnapshot CaptureClipboard()
    {
        ClipSnapshot snap = new ClipSnapshot();
        if (!OpenClipboard(gHwnd)) { snap.Complete = false; return snap; }
        try
        {
            uint fmt = 0;
            while ((fmt = EnumClipboardFormats(fmt)) != 0)
            {
                if (IsHandleFormat(fmt))
                {
                    // 截图时 Windows 会额外合成一个 CF_BITMAP，只要 DIB 在就能重建，跳过它没关系；
                    // 其它句柄格式（CF_OWNERDISPLAY 等）没法从别的格式还原，算快照不完整
                    // CF_BITMAP / CF_PALETTE 都是截图时由 DIB 合成的，只要 DIB 在就能重建
                    bool hasDib = IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_DIBV5);
                    bool rebuildable = hasDib && (fmt == CF_BITMAP || fmt == CF_PALETTE);
                    if (rebuildable) continue;
                    snap.Complete = false;
                    continue;
                }
                IntPtr h = GetClipboardData(fmt);
                if (h == IntPtr.Zero) { snap.Complete = false; continue; }
                UIntPtr size = GlobalSize(h);
                if (size == UIntPtr.Zero) { snap.Complete = false; continue; }
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) { snap.Complete = false; continue; }
                try
                {
                    int n = (int)size;
                    byte[] buf = new byte[n];
                    Marshal.Copy(p, buf, 0, n);
                    snap.Formats.Add(fmt);
                    snap.Blobs.Add(buf);
                }
                finally { GlobalUnlock(h); }
            }
        }
        finally { CloseClipboard(); }
        return snap;
    }

    // 只有当前剪贴板里仍然是「我们自己刚放进去的那段文字」时，才把快照写回去。
    // 内容核对和写回都在同一次 OpenClipboard 里完成，中间没有可被别人插进来的空档。
    static bool RestoreClipboardIfText(ClipSnapshot snap, string expectedText, uint expectedSeq)
    {
        if (snap == null || expectedText == null) return false;
        if (!OpenClipboard(gHwnd)) return false;
        try
        {
            // 序列号能发现"同样的文字、之后又被写过一次"（格式可能已经变了）
            if (expectedSeq != 0 && GetClipboardSequenceNumber() != expectedSeq)
            {
                Log("restore skipped: clipboard was written again after we read it");
                return false;
            }
            string current = GetClipboardTextLocked();
            if (current != expectedText)
            {
                Log("restore skipped: clipboard no longer holds our text (someone else wrote)");
                return false;
            }
            if (!EmptyClipboard()) { Log("restore failed: EmptyClipboard"); return false; }
            bool allOk = true;
            for (int i = 0; i < snap.Formats.Count; i++)
            {
                byte[] buf = snap.Blobs[i];
                IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)buf.Length);
                if (h == IntPtr.Zero) { allOk = false; continue; }
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) { GlobalFree(h); allOk = false; continue; }
                try { Marshal.Copy(buf, 0, p, buf.Length); }
                finally { GlobalUnlock(h); }
                if (SetClipboardData(snap.Formats[i], h) == IntPtr.Zero) { GlobalFree(h); allOk = false; }
            }
            return allOk;
        }
        finally { CloseClipboard(); }
    }

    // 读取时假定剪贴板已经打开
    static string GetClipboardTextLocked()
    {
        IntPtr h = GetClipboardData(CF_UNICODETEXT);
        if (h == IntPtr.Zero) return null;
        IntPtr p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { GlobalUnlock(h); }
    }

    static bool SameFormats(ClipSnapshot a, ClipSnapshot b)
    {
        if (a.Formats.Count != b.Formats.Count) return false;
        for (int i = 0; i < a.Formats.Count; i++)
            if (a.Formats[i] != b.Formats[i]) return false;
        return true;
    }

    static bool SameBytes(ClipSnapshot a, ClipSnapshot b)
    {
        if (a.Blobs.Count != b.Blobs.Count) return false;
        for (int i = 0; i < a.Blobs.Count; i++)
        {
            if (a.Blobs[i].Length != b.Blobs[i].Length) return false;
            for (int k = 0; k < a.Blobs[i].Length; k++)
                if (a.Blobs[i][k] != b.Blobs[i][k]) return false;
        }
        return true;
    }

    static void SendCtrlC()
    {
        keybd_event((byte)VK_CONTROL, 0, 0, UIntPtr.Zero);
        Thread.Sleep(10);
        keybd_event((byte)0x43, 0, 0, UIntPtr.Zero);           // C down
        Thread.Sleep(10);
        keybd_event((byte)0x43, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(10);
        keybd_event((byte)VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    static void CloseOldPopup()
    {
        IntPtr old = FindWindow(null, "雅思查词弹窗");
        if (old != IntPtr.Zero)
        {
            PostMessage(old, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(120);
        }
    }

    static void Launch(string text)
    {
        string html = Path.Combine(gDir, "index.html");
        if (!File.Exists(html)) { Log("html missing: " + html); return; }
        string browser = FindBrowser();
        if (browser == null) { Log("no browser"); return; }

        CloseOldPopup();

        string url = new Uri(html).AbsoluteUri + "?q=" + Uri.EscapeDataString(text) + "&popup=1";

        POINT pt;
        GetCursorPos(out pt);
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int w = 470, h = 640;
        int x = pt.X - 30, y = pt.Y + 18;
        if (x + w > vx + vw) x = vx + vw - w - 8;
        if (x < vx) x = vx + 8;
        if (y + h > vy + vh) y = pt.Y - h - 18;
        if (y < vy) y = vy + 8;

        string arguments = "--app=" + url + " --window-size=" + w + "," + h + " --window-position=" + x + "," + y;
        try
        {
            Process.Start(new ProcessStartInfo(browser, arguments) { UseShellExecute = false });
            Log("popup -> " + text.Length + " chars, " + CountWords(text) + " words");
        }
        catch (Exception ex) { Log("launch failed: " + ex.Message); }
    }

    // ---------- 浏览器 ----------
    static string FindBrowser()
    {
        string progId = "";
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
            {
                if (k != null) progId = (string)k.GetValue("ProgId", "");
            }
        }
        catch { }

        List<string> chrome = new List<string>();
        List<string> edge = new List<string>();
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (pf.Length > 0)
        {
            chrome.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            edge.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
        }
        if (pf86.Length > 0)
        {
            chrome.Add(Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
            edge.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
        }
        if (lad.Length > 0) chrome.Add(Path.Combine(lad, @"Google\Chrome\Application\chrome.exe"));

        List<string> order = new List<string>();
        if (progId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase)) { order.AddRange(edge); order.AddRange(chrome); }
        else { order.AddRange(chrome); order.AddRange(edge); }

        foreach (string p in order)
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
        return null;
    }

    // ---------- 剪贴板 ----------
    static string GetClipboardText()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(30); continue; }
            try
            {
                IntPtr h = GetClipboardData(CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(p); }
                finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }
        return null;
    }


    static bool SetClipboardText(string text)
    {
        if (text == null) return false;
        // 按 MSDN 要求用 GlobalAlloc(GMEM_MOVEABLE) 分配，系统接管所有权
        UIntPtr bytes = (UIntPtr)((text.Length + 1) * 2);
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (hMem == IntPtr.Zero) return false;

        IntPtr target = GlobalLock(hMem);
        if (target == IntPtr.Zero) { GlobalFree(hMem); return false; }
        try
        {
            char[] chars = text.ToCharArray();
            Marshal.Copy(chars, 0, target, chars.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);   // 结尾 \0
        }
        finally { GlobalUnlock(hMem); }

        IntPtr owner = gHwnd != IntPtr.Zero ? gHwnd : IntPtr.Zero;
        if (!OpenClipboard(owner)) { GlobalFree(hMem); return false; }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }
            // 趁还持有剪贴板，记下这次写入的序列号，供自检核对
            return true;   // 成功后所有权归系统，不能再 Free
        }
        finally { CloseClipboard(); }
    }

    static int CountWords(string s)
    {
        int n = 0;
        bool inWord = false;
        foreach (char c in s)
        {
            bool isW = char.IsLetter(c);
            if (isW && !inWord) n++;
            inWord = isW;
        }
        return n;
    }

    // ---------- 配置 ----------
    static void LoadConfig()
    {
        string ini = Path.Combine(gDir, "hotkey.ini");
        if (!File.Exists(ini)) return;
        try
        {
            foreach (string raw in File.ReadAllLines(ini, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "mods")
                {
                    uint m = 0;
                    string low = val.ToLowerInvariant();
                    if (low.Contains("ctrl")) m |= MOD_CONTROL;
                    if (low.Contains("alt")) m |= MOD_ALT;
                    if (low.Contains("shift")) m |= MOD_SHIFT;
                    if (low.Contains("win")) m |= MOD_WIN;
                    if (m != 0) gMods = m;
                }
                else if (key == "key")
                {
                    uint vk = ParseVk(val);
                    if (vk != 0) gVk = vk;
                }
            }
            gHotkeyName = ModsName(gMods) + "+" + KeyName(gVk);
        }
        catch (Exception ex) { Log("config error: " + ex.Message); }
    }

    static uint ParseVk(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Trim().ToUpperInvariant();
        if (s.Length == 1)
        {
            char c = s[0];
            if (c >= 'A' && c <= 'Z') return (uint)c;
            if (c >= '0' && c <= '9') return (uint)c;
        }
        if (s.StartsWith("F"))
        {
            int n;
            if (int.TryParse(s.Substring(1), out n) && n >= 1 && n <= 12) return (uint)(0x70 + n - 1);
        }
        if (s == "SPACE") return 0x20;
        return 0;
    }

    static string ModsName(uint m)
    {
        List<string> parts = new List<string>();
        if ((m & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((m & MOD_ALT) != 0) parts.Add("Alt");
        if ((m & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((m & MOD_WIN) != 0) parts.Add("Win");
        return string.Join("+", parts.ToArray());
    }

    static string KeyName(uint vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
        if (vk == 0x20) return "Space";
        return "0x" + vk.ToString("X2");
    }

    // ---------- 日志 ----------
    static void Log(string s)
    {
        try
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine;
            File.AppendAllText(gLog, line, new UTF8Encoding(false));
        }
        catch { }
    }
}
