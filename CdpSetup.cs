#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Path = System.IO.Path;
using File = System.IO.File;

public partial class CdpCli
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr Handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr Handle, int Command);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint Flags, uint X, uint Y, uint Data, int ExtraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte Key, byte Scan, uint Flags, UIntPtr Extra);

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        bool IsWindowOnCurrentVirtualDesktop(IntPtr TopLevelWindow);
        Guid GetWindowDesktopId(IntPtr TopLevelWindow);
        void MoveWindowToDesktop(IntPtr TopLevelWindow, ref Guid DesktopId);
    }

    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    private class VirtualDesktopManager { }

    [DllImport("user32.dll")]
    private static extern bool SwitchToThisWindow(IntPtr Handle, bool AltTab);

    private static void SwitchToWindow(IntPtr Handle)
    {
        try
        {
            var Manager = (IVirtualDesktopManager)new VirtualDesktopManager();
            if (!Manager.IsWindowOnCurrentVirtualDesktop(Handle))
            {
                var TargetDesktop = Manager.GetWindowDesktopId(Handle);
                var ServiceProvider = (IServiceProvider10)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(CdpWin32.ImmersiveShellClsid))!)!;
                var InternalManager = (IVirtualDesktopManagerInternal)ServiceProvider.QueryService(new Guid(CdpWin32.VirtualDesktopManagerInternalClsid), new Guid(CdpWin32.IVirtualDesktopManagerInternalIid));
                InternalManager.SwitchDesktopByDesktopId(ref TargetDesktop);
                Thread.Sleep(CdpTimeout.ForegroundDelayMs);
            }
        }
        catch { /* Virtual desktop API not available */ }
        ShowWindow(Handle, CdpWin32.SwMaximize);
        SetForegroundWindow(Handle);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider10
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid Service, ref Guid Riid);
    }

    [ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManagerInternal
    {
        int GetCount(IntPtr Monitor);
        void MoveViewToDesktop(object View, object Desktop);
        bool CanViewMoveDesktops(object View);
        object GetCurrentDesktop(IntPtr Monitor);
        void GetDesktops(IntPtr Monitor, out object Desktops);
        object GetAdjacentDesktop(object From, int Direction);
        void SwitchDesktop(IntPtr Monitor, object Desktop);
        object CreateDesktop(IntPtr Monitor);
        void MoveDesktop(object Desktop, IntPtr Monitor, int Index);
        void RemoveDesktop(object Remove, object Fallback);
        object FindDesktop(ref Guid DesktopId);
        void GetDesktopSwitchIncludeExcludeViews(object Desktop, out object Views1, out object Views2);
        void SetDesktopName(object Desktop, [MarshalAs(UnmanagedType.HString)] string Name);
        void SetDesktopWallpaper(object Desktop, [MarshalAs(UnmanagedType.HString)] string Path);
        void UpdateWallpaperPathForAllDesktops([MarshalAs(UnmanagedType.HString)] string Path);
        void CopyDesktopState(object Source, object Target);
        void CreateRemoteDesktop([MarshalAs(UnmanagedType.HString)] string Path, out object Desktop);
        void SwitchRemoteDesktop(object Desktop, int SwitchType);
        void SwitchDesktopByDesktopId(ref Guid DesktopId);
    }

    private static void ClickAllowPrompt(bool Debug = false)
    {
        try
        {
            SetProcessDPIAware();
            var Root = AutomationElement.RootElement;
            var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
            if (Root.FindFirst(TreeScope.Children, ChromeCondition) == null)
            {
                if (Debug) Console.Error.WriteLine("Chrome not on current desktop, switching right...");
                SwitchDesktopRight();
                if (Root.FindFirst(TreeScope.Children, ChromeCondition) == null) { SwitchDesktopLeft(); SwitchDesktopLeft(); }
            }
            foreach (AutomationElement Window in Root.FindAll(TreeScope.Children, ChromeCondition))
            {
                var WindowHandle = new IntPtr(Window.Current.NativeWindowHandle);
                AutomationElement? AllowButton = null;
                var ButtonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                if (Debug)
                {
                    Console.Error.WriteLine(string.Concat("Window: ", Window.Current.Name, " hwnd=", WindowHandle));
                    foreach (AutomationElement Button in Window.FindAll(TreeScope.Descendants, ButtonCondition))
                        Console.Error.WriteLine(string.Concat("  Button: '", Button.Current.Name, "' rect=", Button.Current.BoundingRectangle));
                }
                foreach (AutomationElement Button in Window.FindAll(TreeScope.Descendants, ButtonCondition))
                {
                    if (Button.Current.Name == CdpProto.AllowButtonName) { AllowButton = Button; break; }
                }
                if (AllowButton == null) continue;

                // Bring Chrome to foreground
                SwitchToWindow(WindowHandle);
                Thread.Sleep(CdpTimeout.ForegroundDelayMs);

                var Rect = AllowButton.Current.BoundingRectangle;
                if (Debug) Console.Error.WriteLine(string.Concat("Allow button rect: ", Rect, " empty=", Rect.IsEmpty));

                // Click Allow button using mouse at its center coordinates
                if (!Rect.IsEmpty)
                {
                    var X = (int)(Rect.X + Rect.Width / 2);
                    var Y = (int)(Rect.Y + Rect.Height / 2);
                    if (Debug) Console.Error.WriteLine(string.Concat("Mouse click at ", X, ",", Y));
                    SetCursorPos(X, Y);
                    Thread.Sleep(50);
                    MouseEvent(CdpWin32.MouseLeftDown, 0, 0, 0, 0);
                    Thread.Sleep(50);
                    MouseEvent(CdpWin32.MouseLeftUp, 0, 0, 0, 0);
                }
                else
                {
                    // Fallback: keyboard — SetFocus + Tab x2 + Enter
                    try { AllowButton.SetFocus(); } catch { }
                    KeybdEvent(0x09, 0, 0, UIntPtr.Zero); KeybdEvent(0x09, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
                    KeybdEvent(0x09, 0, 0, UIntPtr.Zero); KeybdEvent(0x09, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
                    KeybdEvent(0x0D, 0, 0, UIntPtr.Zero); KeybdEvent(0x0D, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
                }
                Console.Error.WriteLine(string.Concat(CdpShell.ClickedPrefix, " ", CdpProto.AllowButtonName));
                return;
            }
            if (Debug) Console.Error.WriteLine("Allow button not found in any Chrome window");

            // Also dismiss "Chrome is being controlled by automated test software" infobar
            DismissInfobar(Debug);
        }
        catch (Exception Ex) { if (Debug) Console.Error.WriteLine(string.Concat("ClickAllowPrompt error: ", Ex.Message)); }
    }

    private static void DismissInfobar(bool Debug = false)
    {
        try
        {
            var Root = AutomationElement.RootElement;
            var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
            foreach (AutomationElement Window in Root.FindAll(TreeScope.Children, ChromeCondition))
            {
                // The infobar dismiss button is "×" in the infobar row (y ~140-200, below address bar, above page content)
                var ButtonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                foreach (AutomationElement Button in Window.FindAll(TreeScope.Descendants, ButtonCondition))
                {
                    var Name = Button.Current.Name;
                    var Rect = Button.Current.BoundingRectangle;
                    if (Rect.IsEmpty) continue;
                    // The × button on the infobar: name is "×", y between 140 and 220 (below toolbar, above page)
                    if (Name == "×" && Rect.Y > 140 && Rect.Y < 220 && Rect.X > 0)
                    {
                        var X = (int)(Rect.X + Rect.Width / 2);
                        var Y = (int)(Rect.Y + Rect.Height / 2);
                        if (Debug) Console.Error.WriteLine(string.Concat("Infobar × at ", X, ",", Y, " rect=", Rect));
                        SetCursorPos(X, Y);
                        Thread.Sleep(50);
                        MouseEvent(CdpWin32.MouseLeftDown, 0, 0, 0, 0);
                        Thread.Sleep(50);
                        MouseEvent(CdpWin32.MouseLeftUp, 0, 0, 0, 0);
                        Console.Error.WriteLine("Dismissed infobar");
                        return;
                    }
                }
            }
            if (Debug) Console.Error.WriteLine("Infobar × not found");
        }
        catch (Exception Ex) { if (Debug) Console.Error.WriteLine(string.Concat("DismissInfobar error: ", Ex.Message)); }
    }

    private static void ExecuteScreenshotDesktop(Dictionary<string, object> Args)
    {
        var Bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var Bitmap = new System.Drawing.Bitmap(Bounds.Width, Bounds.Height);
        using var Graphics = System.Drawing.Graphics.FromImage(Bitmap);
        Graphics.CopyFromScreen(Bounds.Location, System.Drawing.Point.Empty, Bounds.Size);
        var OutputPath = Args.TryGetValue(CdpArg.FilePath, out var FilePath) ? FilePath.ToString()! : Path.Combine(Path.GetTempPath(), string.Concat(CdpProto.ScreenshotPrefix, CdpProto.DesktopScreenshotFile));
        Bitmap.Save(OutputPath);
        Console.WriteLine(string.Concat(CdpMsg.ScreenshotSaved, OutputPath));
    }

    private static void SwitchDesktopRight()
    {
        KeybdEvent(CdpWin32.VkLWin, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkRight, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkRight, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkLWin, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Thread.Sleep(CdpTimeout.ForegroundDelayMs);
    }

    private static void SwitchDesktopLeft()
    {
        KeybdEvent(CdpWin32.VkLWin, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkLeft, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkLeft, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkLWin, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Thread.Sleep(CdpTimeout.ForegroundDelayMs);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint Count, INPUT[] Inputs, int Size);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT Ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }

    private static void TypeString(string Text)
    {
        foreach (var Ch in Text)
        {
            var Inputs = new INPUT[2];
            Inputs[0] = new INPUT { Type = 1, U = new INPUTUNION { Ki = new KEYBDINPUT { Vk = 0, Scan = Ch, Flags = 4 } } }; // KEYEVENTF_UNICODE
            Inputs[1] = new INPUT { Type = 1, U = new INPUTUNION { Ki = new KEYBDINPUT { Vk = 0, Scan = Ch, Flags = 4 | 2 } } }; // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
            SendInput(2, Inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static void NavigateAddressBar(string Url)
    {
        var Root = AutomationElement.RootElement;
        var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
        var Window = Root.FindFirst(TreeScope.Children, ChromeCondition);
        if (Window == null) { Console.Error.WriteLine("Chrome not found"); return; }
        SwitchToWindow(new IntPtr(Window.Current.NativeWindowHandle));
        Thread.Sleep(200);
        // Copy URL to clipboard, Ctrl+L to focus address bar, Ctrl+V to paste, Enter
        var Thread2 = new Thread(() => { System.Windows.Forms.Clipboard.SetText(Url); });
        Thread2.SetApartmentState(ApartmentState.STA);
        Thread2.Start();
        Thread2.Join();
        // Escape to dismiss any dropdown, then Ctrl+L to focus address bar
        KeybdEvent(CdpWin32.VkEscape, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkEscape, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Thread.Sleep(100);
        KeybdEvent(CdpWin32.VkControl, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x4C, 0, 0, UIntPtr.Zero); // L
        KeybdEvent(0x4C, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Thread.Sleep(300);
        // Ctrl+V to paste URL from clipboard
        KeybdEvent(CdpWin32.VkControl, 0, 0, UIntPtr.Zero);
        KeybdEvent(0x56, 0, 0, UIntPtr.Zero); // V
        KeybdEvent(0x56, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkControl, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Thread.Sleep(200);
        // Enter to navigate
        KeybdEvent(CdpWin32.VkReturn, 0, 0, UIntPtr.Zero);
        KeybdEvent(CdpWin32.VkReturn, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
        Console.WriteLine(string.Concat("Navigated to ", Url));
    }

    private static void FocusChrome()
    {
        var Root = AutomationElement.RootElement;
        var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
        var Window = Root.FindFirst(TreeScope.Children, ChromeCondition);
        if (Window != null)
        {
            SwitchToWindow(new IntPtr(Window.Current.NativeWindowHandle));
            Console.WriteLine("Chrome focused");
            return;
        }
        SwitchDesktopRight();
        Window = AutomationElement.RootElement.FindFirst(TreeScope.Children, ChromeCondition);
        if (Window != null)
        {
            SwitchToWindow(new IntPtr(Window.Current.NativeWindowHandle));
            Console.WriteLine("Chrome focused");
            return;
        }
        SwitchDesktopLeft();
        SwitchDesktopLeft();
        Window = AutomationElement.RootElement.FindFirst(TreeScope.Children, ChromeCondition);
        if (Window != null)
        {
            SwitchToWindow(new IntPtr(Window.Current.NativeWindowHandle));
            Console.WriteLine("Chrome focused");
        }
        else Console.Error.WriteLine("Chrome not found");
    }

    private static (string Command, Dictionary<string, object> Args) ParseArgs(string[] Argv)
    {
        var Command = Argv[0];
        var Result = new Dictionary<string, object>(StringComparer.Ordinal);
        for (var Index = 1; Index < Argv.Length; Index++)
        {
            if (Argv[Index].StartsWith(CdpArg.ArgPrefix, StringComparison.Ordinal))
            {
                var Key = Argv[Index][2..];
                if (Key.StartsWith(CdpArg.NoPrefix, StringComparison.Ordinal)) { Result[Key[3..]] = false; continue; }
                if (Index + 1 < Argv.Length && !Argv[Index + 1].StartsWith(CdpArg.ArgPrefix, StringComparison.Ordinal)) { var Value = Argv[++Index]; if (bool.TryParse(Value, out var BoolValue)) Result[Key] = BoolValue; else if (int.TryParse(Value, System.Globalization.CultureInfo.InvariantCulture, out var IntValue)) Result[Key] = IntValue; else if (double.TryParse(Value, System.Globalization.CultureInfo.InvariantCulture, out var DoubleValue)) Result[Key] = DoubleValue; else Result[Key] = Value; }
                else Result[Key] = true;
            }
            else
            {
                switch (Command)
                {
                    case "click" or "hover": if (!Result.ContainsKey(CdpArg.Uid)) Result[CdpArg.Uid] = Argv[Index]; break;
                    case "fill": if (!Result.ContainsKey(CdpArg.Uid)) Result[CdpArg.Uid] = Argv[Index]; else if (!Result.ContainsKey(CdpKey.Value)) Result[CdpKey.Value] = Argv[Index]; break;
                    case "select_page" or "close_page": if (!Result.ContainsKey(CdpArg.PageId)) Result[CdpArg.PageId] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "new_page": if (!Result.ContainsKey(CdpKey.Url)) Result[CdpKey.Url] = Argv[Index]; break;
                    case "evaluate_script": if (!Result.ContainsKey(CdpArg.Function)) Result[CdpArg.Function] = Argv[Index]; break;
                    case "press_key": if (!Result.ContainsKey(CdpKey.Key)) Result[CdpKey.Key] = Argv[Index]; break;
                    case "type_text": if (!Result.ContainsKey(CdpKey.Text)) Result[CdpKey.Text] = Argv[Index]; break;
                    case "resize_page": if (!Result.ContainsKey(CdpKey.Width)) Result[CdpKey.Width] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); else if (!Result.ContainsKey(CdpKey.Height)) Result[CdpKey.Height] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "handle_dialog": if (!Result.ContainsKey(CdpArg.Action)) Result[CdpArg.Action] = Argv[Index]; break;
                    case "drag": if (!Result.ContainsKey(CdpArg.FromUid)) Result[CdpArg.FromUid] = Argv[Index]; else if (!Result.ContainsKey(CdpArg.ToUid)) Result[CdpArg.ToUid] = Argv[Index]; break;
                }
            }
        }
        return (Command, Result);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"chrome-devtools.cs — Pure .NET 11 Chrome DevTools CLI

Usage: dotnet run -- <command> [args] [--options]

Connects via raw CDP WebSocket. Auto-clicks Allow prompt (DPI-aware).

Navigation:
  list_pages                              List open pages
  select_page <pageId>                    Select page
  close_page <pageId>                     Close page
  new_page <url>                          Open new tab
  navigate_page --type url --url <url>    Navigate (url|back|forward|reload)

Debugging:
  take_screenshot [--filePath] [--format] [--fullPage] [--quality]
  take_snapshot [--filePath]
  evaluate_script <function>              Run JS
  list_console_messages                   Console output
  list_network_requests                   Network requests

Input:
  click <uid> [--dblClick]                Click element
  hover <uid>                             Hover element
  fill <uid> <value>                      Fill input/select
  type_text <text> [--submitKey Enter]    Type text
  press_key <key>                         Key combo (Control+A)
  drag <from_uid> <to_uid>               Drag and drop
  handle_dialog <accept|dismiss>          Handle dialog
  upload_file --uid <uid> --filePath <p>  Upload file

Emulation:
  resize_page <width> <height>            Resize viewport
  emulate [--userAgent] [--geolocation] [--colorScheme]

Utility:
  allow                                   Click Allow prompt");
    }
}
