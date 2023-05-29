using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace DcsAutopilot;

public static class DcsWindow
{
    private static void sendScancode(ushort scan, bool down)
    {
        // Sending key combos like Shift+R doesn't work if it's one big array of down/up events. We must make multiple calls to SendInput.
        var inputs = new INPUT[1];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = 0;
        inputs[0].Anonymous.ki.wScan = scan;
        inputs[0].Anonymous.ki.dwFlags = down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
    }

    // this is easier with a direct import because of https://github.com/microsoft/CsWin32/issues/614
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private static HWND findDcsWindow()
    {
        var candidates = new List<HWND>();
        PInvoke.EnumWindows((HWND param0, LPARAM param1) =>
        {
            var name = new StringBuilder(256);
            GetClassName(param0, name, name.Capacity);
            if (name.ToString() == "DCS")
                candidates.Add(param0);
            return true;
        }, default);
        if (candidates.Count == 0)
            throw new Exception($"Unable to find DCS window");
        if (candidates.Count > 1)
            throw new Exception($"Found multiple DCS windows"); // later: not sure if this is even possible, if so we'll figure out how to deal with it then
        return candidates.Single();
    }

    public static bool HaveDcsWindow()
    {
        try { findDcsWindow(); return true; }
        catch { return false; }
    }

    private static void focusDcs()
    {
        var wnd = findDcsWindow();
        while (PInvoke.GetForegroundWindow() != wnd)
        {
            PInvoke.SetForegroundWindow(wnd);
            Thread.Sleep(1000);
        }
    }

    public static void RestartMission()
    {
        focusDcs();
        // Restart mission
        sendScancode(42, true); // LShift
        Thread.Sleep(100);
        sendScancode(19, true); // R
        Thread.Sleep(100);
        sendScancode(19, false); // R
        Thread.Sleep(100);
        sendScancode(42, false); // LShift
        // Wait for restart to begin
        Thread.Sleep(1000);
        // Post Escape and call it a day; DCS buffers that and processes it the moment the mission is ready.
        sendScancode(1, true); // Esc
        Thread.Sleep(100);
        sendScancode(1, false); // Esc
    }

    public static void SpeedUp()
    {
        focusDcs();
        sendScancode(29, true); // LCtrl
        Thread.Sleep(100);
        sendScancode(45, true); // X
        Thread.Sleep(100);
        sendScancode(45, false); // X
        Thread.Sleep(100);
        sendScancode(29, false); // LCtrl
    }
}
