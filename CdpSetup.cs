#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Runtime.InteropServices;
using System.Windows.Automation;

public partial class CdpCli
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr Handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr Handle, int Command);

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

    private static void PressKey(byte Key)
    {
        KeybdEvent(Key, 0, 0, UIntPtr.Zero);
        KeybdEvent(Key, 0, CdpWin32.KeyEventUp, UIntPtr.Zero);
    }

    private static void ClickAllowPrompt()
    {
        try
        {
            var Root = AutomationElement.RootElement;
            var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
            foreach (AutomationElement Window in Root.FindAll(TreeScope.Children, ChromeCondition))
            {
                var WindowHandle = new IntPtr(Window.Current.NativeWindowHandle);
                var HasAllow = false;
                var ButtonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                foreach (AutomationElement Button in Window.FindAll(TreeScope.Descendants, ButtonCondition))
                {
                    if (Button.Current.Name == CdpProto.AllowButtonName) { HasAllow = true; break; }
                }
                if (!HasAllow) continue;
                SwitchToWindow(WindowHandle);
                Thread.Sleep(CdpTimeout.ForegroundDelayMs);
                PressKey(CdpWin32.VkTab);
                Thread.Sleep(CdpTimeout.ClickDelayMs);
                PressKey(CdpWin32.VkTab);
                Thread.Sleep(CdpTimeout.ClickDelayMs);
                PressKey(CdpWin32.VkReturn);
                Console.Error.WriteLine(string.Concat(CdpShell.ClickedPrefix, " ", CdpProto.AllowButtonName));
                return;
            }
        }
        catch { /* Prompt may not be present or was dismissed */ }
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

    private static void FocusChrome()
    {
        var Root = AutomationElement.RootElement;
        var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
        var Window = Root.FindFirst(TreeScope.Children, ChromeCondition);
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
