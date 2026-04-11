#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Runtime.InteropServices;
using System.Windows.Automation;

public partial class CdpCli
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(uint Flags, uint X, uint Y, uint Data, int ExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr Handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr Handle, int Command);

    private static void ClickAtPoint(int X, int Y)
    {
        SetCursorPos(X, Y);
        Thread.Sleep(CdpTimeout.ClickDelayMs);
        MouseEvent(CdpWin32.MouseLeftDown, 0, 0, 0, 0);
        MouseEvent(CdpWin32.MouseLeftUp, 0, 0, 0, 0);
    }

    private static void ClickAllowPrompt()
    {
        try
        {
            SetProcessDPIAware();
            var Root = AutomationElement.RootElement;
            var ChromeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, CdpProto.ChromeWidgetClass);
            foreach (AutomationElement Window in Root.FindAll(TreeScope.Children, ChromeCondition))
            {
                ShowWindow(new IntPtr(Window.Current.NativeWindowHandle), CdpWin32.SwMaximize);
                var ButtonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                foreach (AutomationElement Button in Window.FindAll(TreeScope.Descendants, ButtonCondition))
                {
                    if (Button.Current.Name == CdpProto.AllowButtonName)
                    {
                        SetForegroundWindow(new IntPtr(Window.Current.NativeWindowHandle));
                        Thread.Sleep(CdpTimeout.ForegroundDelayMs);
                        dynamic Bounds = Button.GetCurrentPropertyValue(AutomationElement.BoundingRectangleProperty);
                        var CenterX = (int)(Bounds.X + (Bounds.Width / 2));
                        var CenterY = (int)(Bounds.Y + (Bounds.Height / 2));
                        ClickAtPoint(CenterX, CenterY);
                        Thread.Sleep(CdpTimeout.ClickRepeatDelayMs);
                        ClickAtPoint(CenterX, CenterY);
                        Console.Error.WriteLine(string.Concat(CdpShell.ClickedPrefix, " ", CdpProto.AllowButtonName));
                    }
                }
            }
        }
        catch { /* Prompt may not be present or was dismissed */ }
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
