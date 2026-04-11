#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Diagnostics;

public partial class CdpCli
{
    private static readonly string AllowClickScript = @"
Add-Type -AssemblyName UIAutomationClient;Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;using System.Runtime.InteropServices;
public class CA{
[DllImport(""user32.dll"")]public static extern bool SetProcessDPIAware();
[DllImport(""user32.dll"")]public static extern bool SetCursorPos(int X,int Y);
[DllImport(""user32.dll"")]public static extern void mouse_event(uint F,uint X,uint Y,uint D,int E);
[DllImport(""user32.dll"")]public static extern bool SetForegroundWindow(IntPtr H);
public static void Click(int X,int Y){SetCursorPos(X,Y);System.Threading.Thread.Sleep(150);mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);}
[DllImport(""user32.dll"")]public static extern bool ShowWindow(IntPtr H,int C);
}
'@
[CA]::SetProcessDPIAware()|Out-Null
$R=[System.Windows.Automation.AutomationElement]::RootElement
$C=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty,'Chrome_WidgetWin_1')
foreach($W in $R.FindAll([System.Windows.Automation.TreeScope]::Children,$C)){
[CA]::ShowWindow($W.Current.NativeWindowHandle,3)|Out-Null
$B=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
foreach($N in $W.FindAll([System.Windows.Automation.TreeScope]::Descendants,$B)){
if($N.Current.Name -eq 'Allow'){[CA]::SetForegroundWindow($W.Current.NativeWindowHandle);Start-Sleep -Milliseconds 500;$P=$N.Current.BoundingRectangle;[CA]::Click([int]($P.X+$P.Width/2),[int]($P.Y+$P.Height/2));Start-Sleep -Milliseconds 200;[CA]::Click([int]($P.X+$P.Width/2),[int]($P.Y+$P.Height/2));Write-Host 'Clicked Allow'}
}}";

    private static void ClickAllowPrompt()
    {
        try
        {
            var StartInfo = new ProcessStartInfo(CdpShell.PowerShell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            StartInfo.ArgumentList.Add(CdpShell.NoProfile);
            StartInfo.ArgumentList.Add(CdpShell.CommandFlag);
            StartInfo.ArgumentList.Add(AllowClickScript);
            var Process = System.Diagnostics.Process.Start(StartInfo);
            if (Process != null) { var Output = Process.StandardOutput.ReadToEnd(); Process.WaitForExit(CdpTimeout.ProcessWaitMs); foreach (var Line in Output.Split('\n')) { var Trimmed = Line.Trim(); if (Trimmed.StartsWith(CdpShell.ClickedPrefix, StringComparison.Ordinal)) Console.Error.WriteLine(Trimmed); } }
        }
        catch { /* Prompt may not be present */ }
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
