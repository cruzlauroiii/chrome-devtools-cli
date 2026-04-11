#pragma warning disable IL2026, IL3050, SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

await new CdpCli().RunAsync(args);

#pragma warning disable SA1649, SA1402
public class CdpCli
{
    private static readonly JsonSerializerOptions JsonPrint = new(JsonSerializerDefaults.General) { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
    private static readonly string ChromeUserDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");
    private static readonly string ActivePortFile = Path.Combine(ChromeUserDataDir, "DevToolsActivePort");

    private ClientWebSocket? WebSocket;
    private string? SessionId;
    private int CommandId = 1;
    private readonly List<JsonNode> EventBuffer = new();

    public async Task RunAsync(string[] Argv)
    {
        if (Argv.Length == 0 || Argv[0] is "--help" or "-h" or "help") { PrintHelp(); return; }
        var (Command, Args) = ParseArgs(Argv);
        if (Command == "allow") { ClickAllowPrompt(); return; }
        await ConnectToChrome();
        try
        {
            switch (Command)
            {
                case "list_pages": await ExecuteListPages(); break;
                case "select_page": await ExecuteSelectPage(Args); break;
                case "close_page": await ExecuteClosePage(Args); break;
                case "new_page": await ExecuteNewPage(Args); break;
                case "navigate_page": await ExecuteNavigatePage(Args); break;
                case "take_screenshot": await ExecuteTakeScreenshot(Args); break;
                case "take_snapshot": await ExecuteTakeSnapshot(Args); break;
                case "evaluate_script": await ExecuteEvaluateScript(Args); break;
                case "click": await ExecuteClick(Args); break;
                case "hover": await ExecuteHover(Args); break;
                case "fill": await ExecuteFill(Args); break;
                case "type_text": await ExecuteTypeText(Args); break;
                case "press_key": await ExecutePressKey(Args); break;
                case "list_console_messages": await ExecuteListConsoleMessages(); break;
                case "list_network_requests": await ExecuteListNetworkRequests(); break;
                case "resize_page": await ExecuteResizePage(Args); break;
                case "emulate": await ExecuteEmulate(Args); break;
                case "handle_dialog": await ExecuteHandleDialog(Args); break;
                case "drag": await ExecuteDrag(Args); break;
                case "upload_file": await ExecuteUploadFile(Args); break;
                default: Console.Error.WriteLine($"Unknown command: {Command}"); Environment.Exit(1); break;
            }
        }
        finally { WebSocket?.Dispose(); }
    }

    private async Task ConnectToChrome()
    {
        ClickAllowPrompt();
        for (var Attempt = 0; Attempt < 3; Attempt++)
        {
            if (!File.Exists(ActivePortFile)) { await Task.Delay(3000); ClickAllowPrompt(); if (!File.Exists(ActivePortFile)) continue; }
            var Lines = File.ReadAllLines(ActivePortFile).Where(L => !string.IsNullOrWhiteSpace(L)).ToArray();
            if (Lines.Length < 2) continue;
            var Endpoint = $"ws://127.0.0.1:{Lines[0].Trim()}{Lines[1].Trim()}";
            WebSocket = new ClientWebSocket();
            using var Timeout = new CancellationTokenSource(10_000);
            try { await WebSocket.ConnectAsync(new Uri(Endpoint), Timeout.Token); return; }
            catch { ClickAllowPrompt(); await Task.Delay(3000); }
        }
        Console.Error.WriteLine("Cannot connect. Enable: chrome://inspect/#remote-debugging");
        Environment.Exit(1);
    }

    private async Task<JsonNode?> SendCommand(string Method, JsonObject? Params = null)
    {
        var Id = CommandId++;
        var Message = new JsonObject { ["id"] = Id, ["method"] = Method };
        if (Params != null) Message["params"] = Params;
        if (SessionId != null) Message["sessionId"] = SessionId;
        await WebSocket!.SendAsync(Encoding.UTF8.GetBytes(Message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        var Buffer = new byte[1024 * 1024];
        var Builder = new StringBuilder();
        while (true)
        {
            var Result = await WebSocket.ReceiveAsync(Buffer, CancellationToken.None);
            Builder.Append(Encoding.UTF8.GetString(Buffer, 0, Result.Count));
            if (Result.EndOfMessage)
            {
                var Parsed = JsonNode.Parse(Builder.ToString());
                if (Parsed?["id"]?.GetValue<int>() == Id) return Parsed?["result"];
                if (Parsed?["method"] != null) EventBuffer.Add(Parsed!);
                Builder.Clear();
            }
        }
    }

    private async Task<JsonNode?> SendBrowserCommand(string Method, JsonObject? Params = null)
    {
        var SavedSession = SessionId;
        SessionId = null;
        var Result = await SendCommand(Method, Params);
        SessionId = SavedSession;
        return Result;
    }

    private async Task<List<JsonNode>> GetPageTargets()
    {
        var Targets = await SendBrowserCommand("Target.getTargets");
        return Targets!["targetInfos"]!.AsArray()
            .Where(T => T!["type"]!.ToString() == "page")
            .Where(T => !T!["url"]!.ToString().StartsWith("chrome://", StringComparison.Ordinal) || T!["url"]!.ToString() == "chrome://newtab/")
            .Where(T => !T!["url"]!.ToString().StartsWith("chrome-extension://", StringComparison.Ordinal))
            .Select(T => T!).ToList();
    }

    private async Task AttachToTarget(string TargetId)
    {
        var Session = await SendBrowserCommand("Target.attachToTarget", new JsonObject { ["targetId"] = TargetId, ["flatten"] = true });
        SessionId = Session!["sessionId"]!.ToString();
        await SendCommand("Page.enable");
        await SendCommand("Runtime.enable");
        await SendCommand("DOM.enable");
        await SendCommand("Network.enable");
    }

    private async Task EnsurePageAttached()
    {
        if (SessionId != null) return;
        var Pages = await GetPageTargets();
        if (Pages.Count == 0) { Console.Error.WriteLine("No pages open"); Environment.Exit(1); }
        await AttachToTarget(Pages[0]["targetId"]!.ToString());
    }

    private async Task<string> EvaluateExpression(string Expression, bool AwaitPromise = false)
    {
        var Params = new JsonObject { ["expression"] = Expression, ["returnByValue"] = true };
        if (AwaitPromise) Params["awaitPromise"] = true;
        var Result = await SendCommand("Runtime.evaluate", Params);
        if (Result?["exceptionDetails"] != null) return $"ERROR: {Result["exceptionDetails"]!["text"]}";
        return Result?["result"]?["value"]?.ToString() ?? "";
    }

    private async Task ExecuteListPages()
    {
        var Pages = await GetPageTargets();
        Console.WriteLine("## Pages");
        for (var Index = 0; Index < Pages.Count; Index++)
        {
            var Url = Pages[Index]["url"]!.ToString();
            var Title = Pages[Index]["title"]?.ToString() ?? "";
            Console.WriteLine($"{Index + 1}: {Url}{(Title.Length > 0 ? $"  ({Title})" : "")}");
        }
    }

    private async Task ExecuteSelectPage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("pageId", out var PageId)) { Console.Error.WriteLine("Required: pageId"); return; }
        var Pages = await GetPageTargets();
        var Index = int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
        if (Index < 0 || Index >= Pages.Count) { Console.Error.WriteLine($"Invalid pageId. Range: 1-{Pages.Count}"); return; }
        await AttachToTarget(Pages[Index]["targetId"]!.ToString());
        Console.WriteLine($"Selected page {int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture)}: {Pages[Index]["url"]}");
    }

    private async Task ExecuteClosePage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("pageId", out var PageId)) { Console.Error.WriteLine("Required: pageId"); return; }
        var Pages = await GetPageTargets();
        var Index = int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
        if (Index < 0 || Index >= Pages.Count) { Console.Error.WriteLine("Invalid pageId"); return; }
        await SendBrowserCommand("Target.closeTarget", new JsonObject { ["targetId"] = Pages[Index]["targetId"]!.ToString() });
        Console.WriteLine($"Closed page {int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture)}");
    }

    private async Task ExecuteNewPage(Dictionary<string, object> Args)
    {
        var Url = Args.TryGetValue("url", out var UrlValue) ? UrlValue.ToString()! : "about:blank";
        var Target = await SendBrowserCommand("Target.createTarget", new JsonObject { ["url"] = Url });
        await AttachToTarget(Target!["targetId"]!.ToString());
        await Task.Delay(2000);
        Console.WriteLine($"Opened: {Url}");
        await ExecuteListPages();
    }

    private async Task ExecuteNavigatePage(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        if (Args.TryGetValue("type", out var NavType))
        {
            switch (NavType.ToString())
            {
                case "back": await EvaluateExpression("history.back()"); await Task.Delay(2000); break;
                case "forward": await EvaluateExpression("history.forward()"); await Task.Delay(2000); break;
                case "reload":
                    var IgnoreCache = Args.ContainsKey("ignoreCache") && bool.Parse(Args["ignoreCache"].ToString()!);
                    await SendCommand("Page.reload", new JsonObject { ["ignoreCache"] = IgnoreCache });
                    await Task.Delay(2000); break;
                case "url":
                    if (!Args.TryGetValue("url", out var NavUrl)) { Console.Error.WriteLine("Required: url"); return; }
                    await SendCommand("Page.navigate", new JsonObject { ["url"] = NavUrl.ToString()! });
                    await Task.Delay(3000);
                    Console.WriteLine($"Navigated to {NavUrl}"); break;
            }
        }
        else if (Args.TryGetValue("url", out var DirectUrl))
        {
            await SendCommand("Page.navigate", new JsonObject { ["url"] = DirectUrl.ToString()! });
            await Task.Delay(3000);
            Console.WriteLine($"Navigated to {DirectUrl}");
        }
        await ExecuteListPages();
    }

    private async Task ExecuteTakeScreenshot(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        var Format = Args.TryGetValue("format", out var FormatValue) ? FormatValue.ToString()! : "png";
        var ScreenshotParams = new JsonObject { ["format"] = Format };
        if (Args.TryGetValue("quality", out var Quality)) ScreenshotParams["quality"] = int.Parse(Quality.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (Args.ContainsKey("fullPage") && bool.Parse(Args["fullPage"].ToString()!))
        {
            var Metrics = await SendCommand("Page.getLayoutMetrics");
            ScreenshotParams["clip"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = Metrics!["contentSize"]!["width"]!.GetValue<double>(), ["height"] = Metrics!["contentSize"]!["height"]!.GetValue<double>(), ["scale"] = 1 };
        }
        var ScreenshotResult = await SendCommand("Page.captureScreenshot", ScreenshotParams);
        var ImageData = Convert.FromBase64String(ScreenshotResult!["data"]!.ToString());
        var OutputPath = Args.TryGetValue("filePath", out var FilePath) ? FilePath.ToString()! : Path.Combine(Path.GetTempPath(), $"screenshot-{Guid.NewGuid():N}.{Format}");
        File.WriteAllBytes(OutputPath, ImageData);
        Console.WriteLine($"Screenshot saved: {OutputPath}");
    }

    private async Task ExecuteTakeSnapshot(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        var Tree = await SendCommand("Accessibility.getFullAXTree");
        var Nodes = Tree!["nodes"]!.AsArray();
        var Output = new StringBuilder();
        Output.AppendLine("## Accessibility Snapshot");
        foreach (var Node in Nodes)
        {
            var Role = Node!["role"]?["value"]?.ToString() ?? "";
            if (Role is "" or "none" or "InlineTextBox") continue;
            var Name = Node["name"]?["value"]?.ToString() ?? "";
            var NodeId = Node["nodeId"]?.ToString() ?? "";
            Output.AppendLine($"  [{NodeId}] {Role}{(Name.Length > 0 ? $" \"{Name}\"" : "")}");
        }
        var Content = Output.ToString();
        if (Args.TryGetValue("filePath", out var FilePath)) { File.WriteAllText(FilePath.ToString()!, Content); Console.WriteLine($"Snapshot saved: {FilePath}"); }
        else Console.Write(Content);
    }

    private async Task ExecuteEvaluateScript(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("function", out var Function)) { Console.Error.WriteLine("Required: function"); return; }
        await EnsurePageAttached();
        var Result = await SendCommand("Runtime.evaluate", new JsonObject { ["expression"] = $"({Function})()", ["returnByValue"] = true, ["awaitPromise"] = true });
        if (Result?["exceptionDetails"] != null) Console.Error.WriteLine($"Error: {Result["exceptionDetails"]!["text"]}");
        else Console.WriteLine(Result?["result"]?["value"] != null ? JsonSerializer.Serialize(Result["result"]!["value"], JsonPrint) : "undefined");
    }

    private async Task ExecuteClick(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("uid", out var Uid)) { Console.Error.WriteLine("Required: uid"); return; }
        await EnsurePageAttached();
        var DoubleClick = Args.ContainsKey("dblClick") && bool.Parse(Args["dblClick"].ToString()!);
        var Escaped = Uid.ToString()!.Replace("'", "\\'");
        Console.WriteLine(await EvaluateExpression($"(()=>{{const E=document.querySelector('[data-uid=\"{Escaped}\"]');if(!E)return 'Not found: {Escaped}';E.scrollIntoView({{block:'center'}});E.click();{(DoubleClick ? "E.click();" : "")}return 'Clicked '+E.tagName+' '+(E.textContent||'').substring(0,50)}})()", true));
    }

    private async Task ExecuteHover(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("uid", out var Uid)) { Console.Error.WriteLine("Required: uid"); return; }
        await EnsurePageAttached();
        Console.WriteLine(await EvaluateExpression($"(()=>{{const E=document.querySelector('[data-uid=\"{Uid}\"]');if(!E)return 'Not found';E.dispatchEvent(new MouseEvent('mouseover',{{bubbles:true}}));E.dispatchEvent(new MouseEvent('mouseenter',{{bubbles:true}}));return 'Hovered: '+E.tagName}})()"));
    }

    private async Task ExecuteFill(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("uid", out var Uid) || !Args.TryGetValue("value", out var Value)) { Console.Error.WriteLine("Required: uid, value"); return; }
        await EnsurePageAttached();
        var Escaped = Value.ToString()!.Replace("\\", "\\\\").Replace("'", "\\'");
        Console.WriteLine(await EvaluateExpression($"(()=>{{const E=document.querySelector('[data-uid=\"{Uid}\"]');if(!E)return 'Not found';if(E.tagName==='SELECT'){{E.value='{Escaped}';E.dispatchEvent(new Event('change',{{bubbles:true}}));return 'Selected: '+E.value}}E.focus();E.value='{Escaped}';E.dispatchEvent(new Event('input',{{bubbles:true}}));E.dispatchEvent(new Event('change',{{bubbles:true}}));return 'Filled: '+E.value}})()"));
    }

    private async Task ExecuteTypeText(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("text", out var Text)) { Console.Error.WriteLine("Required: text"); return; }
        await EnsurePageAttached();
        foreach (var Character in Text.ToString()!) await SendCommand("Input.dispatchKeyEvent", new JsonObject { ["type"] = "char", ["text"] = Character.ToString() });
        if (Args.TryGetValue("submitKey", out var SubmitKey)) await SendCommand("Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyDown", ["key"] = SubmitKey.ToString() });
        Console.WriteLine($"Typed: {Text}");
    }

    private async Task ExecutePressKey(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("key", out var Key)) { Console.Error.WriteLine("Required: key"); return; }
        await EnsurePageAttached();
        var Parts = Key.ToString()!.Split('+');
        var Modifiers = 0;
        foreach (var Modifier in Parts.SkipLast(1)) { switch (Modifier.ToLower(System.Globalization.CultureInfo.InvariantCulture)) { case "control" or "ctrl": Modifiers |= 2; break; case "alt": Modifiers |= 1; break; case "shift": Modifiers |= 8; break; case "meta" or "cmd": Modifiers |= 4; break; } }
        await SendCommand("Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyDown", ["key"] = Parts.Last(), ["modifiers"] = Modifiers });
        await SendCommand("Input.dispatchKeyEvent", new JsonObject { ["type"] = "keyUp", ["key"] = Parts.Last(), ["modifiers"] = Modifiers });
        Console.WriteLine($"Pressed: {Key}");
    }

    private async Task ExecuteListConsoleMessages()
    {
        await EnsurePageAttached();
        var Messages = EventBuffer.Where(E => E["method"]?.ToString() is "Runtime.consoleAPICalled" or "Runtime.exceptionThrown").ToList();
        if (Messages.Count == 0) { Console.WriteLine("No console messages captured."); return; }
        Console.WriteLine("## Console Messages");
        foreach (var Msg in Messages)
        {
            if (Msg["method"]!.ToString() == "Runtime.consoleAPICalled")
            {
                var Type = Msg["params"]!["type"]?.ToString() ?? "log";
                var Content = string.Join(" ", Msg["params"]!["args"]!.AsArray().Select(A => A!["value"]?.ToString() ?? A!["description"]?.ToString() ?? ""));
                Console.WriteLine($"[{Type}] {Content}");
            }
            else Console.WriteLine($"[error] {Msg["params"]!["exceptionDetails"]?["text"]}");
        }
    }

    private async Task ExecuteListNetworkRequests()
    {
        await EnsurePageAttached();
        var Requests = EventBuffer.Where(E => E["method"]?.ToString() == "Network.requestWillBeSent").ToList();
        if (Requests.Count == 0) { Console.WriteLine("No network requests captured."); return; }
        Console.WriteLine("## Network Requests");
        var Counter = 1;
        foreach (var Request in Requests.TakeLast(50))
            Console.WriteLine($"{Counter++}. {Request["params"]!["request"]!["method"]} {Request["params"]!["request"]!["url"]}");
    }

    private async Task ExecuteResizePage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("width", out var Width) || !Args.TryGetValue("height", out var Height)) { Console.Error.WriteLine("Required: width, height"); return; }
        await EnsurePageAttached();
        await SendCommand("Emulation.setDeviceMetricsOverride", new JsonObject { ["width"] = int.Parse(Width.ToString()!, System.Globalization.CultureInfo.InvariantCulture), ["height"] = int.Parse(Height.ToString()!, System.Globalization.CultureInfo.InvariantCulture), ["deviceScaleFactor"] = 1, ["mobile"] = false });
        Console.WriteLine($"Resized to {Width}x{Height}");
    }

    private async Task ExecuteEmulate(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        if (Args.TryGetValue("userAgent", out var UserAgent)) await SendCommand("Emulation.setUserAgentOverride", new JsonObject { ["userAgent"] = UserAgent.ToString()! });
        if (Args.TryGetValue("geolocation", out var GeoLocation)) { var Coordinates = GeoLocation.ToString()!.Split('x'); if (Coordinates.Length == 2) await SendCommand("Emulation.setGeolocationOverride", new JsonObject { ["latitude"] = double.Parse(Coordinates[0], System.Globalization.CultureInfo.InvariantCulture), ["longitude"] = double.Parse(Coordinates[1], System.Globalization.CultureInfo.InvariantCulture), ["accuracy"] = 1 }); }
        if (Args.TryGetValue("colorScheme", out var ColorScheme)) await SendCommand("Emulation.setEmulatedMedia", new JsonObject { ["features"] = new JsonArray { new JsonObject { ["name"] = "prefers-color-scheme", ["value"] = ColorScheme.ToString()! } } });
        Console.WriteLine("Emulation applied");
    }

    private async Task ExecuteHandleDialog(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("action", out var Action)) { Console.Error.WriteLine("Required: action (accept|dismiss)"); return; }
        var DialogParams = new JsonObject { ["accept"] = Action.ToString() == "accept" };
        if (Args.TryGetValue("promptText", out var PromptText)) DialogParams["promptText"] = PromptText.ToString()!;
        await SendCommand("Page.handleJavaScriptDialog", DialogParams);
        Console.WriteLine($"Dialog {Action}ed");
    }

    private async Task ExecuteDrag(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("from_uid", out var FromUid) || !Args.TryGetValue("to_uid", out var ToUid)) { Console.Error.WriteLine("Required: from_uid, to_uid"); return; }
        await EnsurePageAttached();
        Console.WriteLine(await EvaluateExpression($"(()=>{{const S=document.querySelector('[data-uid=\"{FromUid}\"]'),D=document.querySelector('[data-uid=\"{ToUid}\"]');if(!S||!D)return 'Not found';const Sr=S.getBoundingClientRect(),Dr=D.getBoundingClientRect();S.dispatchEvent(new DragEvent('dragstart',{{bubbles:true,clientX:Sr.x+Sr.width/2,clientY:Sr.y+Sr.height/2}}));D.dispatchEvent(new DragEvent('drop',{{bubbles:true,clientX:Dr.x+Dr.width/2,clientY:Dr.y+Dr.height/2}}));S.dispatchEvent(new DragEvent('dragend',{{bubbles:true}}));return 'Dragged'}})()"));
    }

    private async Task ExecuteUploadFile(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue("uid", out var Uid) || !Args.TryGetValue("filePath", out var FilePath)) { Console.Error.WriteLine("Required: uid, filePath"); return; }
        await EnsurePageAttached();
        var Document = await SendCommand("DOM.getDocument");
        var Found = await SendCommand("DOM.querySelector", new JsonObject { ["nodeId"] = Document!["root"]!["nodeId"]!.GetValue<int>(), ["selector"] = $"[data-uid=\"{Uid}\"]" });
        if (Found?["nodeId"]?.GetValue<int>() is > 0)
        {
            await SendCommand("DOM.setFileInputFiles", new JsonObject { ["nodeId"] = Found["nodeId"]!.GetValue<int>(), ["files"] = new JsonArray { JsonValue.Create(Path.GetFullPath(FilePath.ToString()!)) } });
            Console.WriteLine($"Uploaded: {FilePath}");
        }
        else Console.Error.WriteLine("File input not found");
    }

    private static void ClickAllowPrompt()
    {
        try
        {
            var StartInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            StartInfo.ArgumentList.Add("-NoProfile");
            StartInfo.ArgumentList.Add("-Command");
            StartInfo.ArgumentList.Add(@"
Add-Type -AssemblyName UIAutomationClient;Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;using System.Runtime.InteropServices;
public class CA{
[DllImport(""user32.dll"")]public static extern bool SetProcessDPIAware();
[DllImport(""user32.dll"")]public static extern bool SetCursorPos(int X,int Y);
[DllImport(""user32.dll"")]public static extern void mouse_event(uint F,uint X,uint Y,uint D,int E);
[DllImport(""user32.dll"")]public static extern bool SetForegroundWindow(IntPtr H);
public static void Click(int X,int Y){SetCursorPos(X,Y);System.Threading.Thread.Sleep(150);mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);}
}
'@
[CA]::SetProcessDPIAware()|Out-Null
$R=[System.Windows.Automation.AutomationElement]::RootElement
$C=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty,'Chrome_WidgetWin_1')
foreach($W in $R.FindAll([System.Windows.Automation.TreeScope]::Children,$C)){
[CA]::SetForegroundWindow($W.Current.NativeWindowHandle);Start-Sleep -Milliseconds 300
$B=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
foreach($N in $W.FindAll([System.Windows.Automation.TreeScope]::Descendants,$B)){
if($N.Current.Name -eq 'Allow'){$P=$N.Current.BoundingRectangle;[CA]::Click([int]($P.X+$P.Width/2),[int]($P.Y+$P.Height/2));Start-Sleep -Milliseconds 200;[CA]::Click([int]($P.X+$P.Width/2),[int]($P.Y+$P.Height/2));Write-Host 'Clicked Allow'}
if($N.Current.Name -eq 'Restore'){$P=$N.Current.BoundingRectangle;[CA]::Click([int]($P.X+$P.Width/2),[int]($P.Y+$P.Height/2));Write-Host 'Clicked Restore'}
}}");
            var Process = System.Diagnostics.Process.Start(StartInfo);
            if (Process != null) { var Output = Process.StandardOutput.ReadToEnd(); Process.WaitForExit(15_000); foreach (var Line in Output.Split('\n')) { var Trimmed = Line.Trim(); if (Trimmed.StartsWith("Clicked", StringComparison.Ordinal)) Console.Error.WriteLine(Trimmed); } }
        }
        catch { /* Prompt may not be present */ }
    }

    private static (string Command, Dictionary<string, object> Args) ParseArgs(string[] Argv)
    {
        var Command = Argv[0];
        var Result = new Dictionary<string, object>(StringComparer.Ordinal);
        for (var Index = 1; Index < Argv.Length; Index++)
        {
            if (Argv[Index].StartsWith("--", StringComparison.Ordinal))
            {
                var Key = Argv[Index][2..];
                if (Key.StartsWith("no-", StringComparison.Ordinal)) { Result[Key[3..]] = false; continue; }
                if (Index + 1 < Argv.Length && !Argv[Index + 1].StartsWith("--", StringComparison.Ordinal)) { var Value = Argv[++Index]; if (bool.TryParse(Value, out var BoolValue)) Result[Key] = BoolValue; else if (int.TryParse(Value, System.Globalization.CultureInfo.InvariantCulture, out var IntValue)) Result[Key] = IntValue; else if (double.TryParse(Value, System.Globalization.CultureInfo.InvariantCulture, out var DoubleValue)) Result[Key] = DoubleValue; else Result[Key] = Value; }
                else Result[Key] = true;
            }
            else
            {
                switch (Command)
                {
                    case "click" or "hover": if (!Result.ContainsKey("uid")) Result["uid"] = Argv[Index]; break;
                    case "fill": if (!Result.ContainsKey("uid")) Result["uid"] = Argv[Index]; else if (!Result.ContainsKey("value")) Result["value"] = Argv[Index]; break;
                    case "select_page" or "close_page": if (!Result.ContainsKey("pageId")) Result["pageId"] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "new_page": if (!Result.ContainsKey("url")) Result["url"] = Argv[Index]; break;
                    case "evaluate_script": if (!Result.ContainsKey("function")) Result["function"] = Argv[Index]; break;
                    case "press_key": if (!Result.ContainsKey("key")) Result["key"] = Argv[Index]; break;
                    case "type_text": if (!Result.ContainsKey("text")) Result["text"] = Argv[Index]; break;
                    case "resize_page": if (!Result.ContainsKey("width")) Result["width"] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); else if (!Result.ContainsKey("height")) Result["height"] = int.Parse(Argv[Index], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "handle_dialog": if (!Result.ContainsKey("action")) Result["action"] = Argv[Index]; break;
                    case "drag": if (!Result.ContainsKey("from_uid")) Result["from_uid"] = Argv[Index]; else if (!Result.ContainsKey("to_uid")) Result["to_uid"] = Argv[Index]; break;
                }
            }
        }
        return (Command, Result);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"chrome-devtools.cs — Pure .NET 11 Chrome DevTools CLI

Usage: dotnet run chrome-devtools.cs -- <command> [args] [--options]

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
