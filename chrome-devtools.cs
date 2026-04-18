#:property ExperimentalFileBasedProgramEnableIncludeDirective=true
#:property TargetFramework=net11.0-windows
#:property UseWindowsForms=true
#:property UseWPF=true
#:property PublishAot=false
#:include CdpCommands.cs
#:include CdpConstants.cs
#:include CdpSetup.cs
#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using File = System.IO.File;

await new CdpCli().RunAsync(args);

#pragma warning disable SA1649, SA1402
public partial class CdpCli
{
    private static readonly string ChromeUserDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");
    private static readonly string ActivePortFile = Path.Combine(ChromeUserDataDir, "DevToolsActivePort");

    private ClientWebSocket? WebSocket;
    private string? SessionId;
    private int CommandId = 1;
    private readonly List<JsonNode> EventBuffer = new();

    private const int ServePort = 9333;

    public async Task RunAsync(string[] Argv)
    {
        if (Argv.Length == 0 || Argv[0] is "--help" or "-h" or "help") { PrintHelp(); return; }
        var (Command, ParsedArgs) = ParseArgs(Argv);
        // Local-only commands (no Chrome connection needed)
        if (Command == "allow") { ClickAllowPrompt(ParsedArgs.ContainsKey("debug")); return; }
        if (Command == "screenshot_desktop") { ExecuteScreenshotDesktop(ParsedArgs); return; }
        if (Command == "focus_chrome") { FocusChrome(); return; }
        if (Command == "navigate_address_bar") { NavigateAddressBar(ParsedArgs.TryGetValue(CdpKey.Url, out var NavUrl) ? NavUrl.ToString()! : ""); return; }
        if (Command == "serve") { await RunServeMode(ParsedArgs); return; }
        // If serve is running, forward the command to it
        if (await TryForwardToServe(Argv)) return;
        // Otherwise connect directly
        await ConnectToChrome(ParsedArgs);
        if (ParsedArgs.TryGetValue(CdpArg.PageId, out var GlobalPageId) && Command is not "select_page" and not "close_page")
        {
            var Pages = await GetPageTargets();
            var PageIndex = int.Parse(GlobalPageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
            if (PageIndex >= 0 && PageIndex < Pages.Count) await AttachToTarget(Pages[PageIndex][CdpKey.TargetId]!.ToString());
        }
        try { await DispatchCommand(Command, ParsedArgs); }
        finally { WebSocket?.Dispose(); }
    }

    private static async Task<bool> TryForwardToServe(string[] Argv)
    {
        try
        {
            using var Http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var Payload = string.Join(" ", Argv.Select(A => A.Contains(' ') ? string.Concat("\"", A, "\"") : A));
            var Response = await Http.PostAsync(string.Concat("http://127.0.0.1:", ServePort, "/exec"), new System.Net.Http.StringContent(Payload, Encoding.UTF8));
            Console.Write(await Response.Content.ReadAsStringAsync());
            return true;
        }
        catch { return false; }
    }

    private async Task RunServeMode(Dictionary<string, object> ParsedArgs)
    {
        await ConnectToChrome(ParsedArgs);
        var Listener = new System.Net.HttpListener();
        Listener.Prefixes.Add(string.Concat("http://127.0.0.1:", ServePort, "/"));
        Listener.Start();
        Console.Error.WriteLine(string.Concat("serve: connected, listening on http://127.0.0.1:", ServePort));
        while (true)
        {
            var Ctx = await Listener.GetContextAsync();
            var Req = Ctx.Request;
            var Res = Ctx.Response;
            if (Req.HttpMethod == "OPTIONS") { Res.StatusCode = 204; Res.Close(); continue; }
            // POST /exec with body = full command line (same as CLI args)
            string Line;
            using (var Reader = new System.IO.StreamReader(Req.InputStream)) Line = (await Reader.ReadToEndAsync()).Trim();
            var Output = new StringBuilder();
            var OldOut = Console.Out;
            var OldErr = Console.Error;
            Console.SetOut(new System.IO.StringWriter(Output));
            Console.SetError(new System.IO.StringWriter(Output));
            try
            {
                var LineArgs = SplitArgs(Line);
                var (Cmd, Args) = ParseArgs(LineArgs);
                if (Cmd == "allow") ClickAllowPrompt(Args.ContainsKey("debug"));
                else if (Cmd == "screenshot_desktop") ExecuteScreenshotDesktop(Args);
                else if (Cmd == "focus_chrome") FocusChrome();
                else if (Cmd == "navigate_address_bar") NavigateAddressBar(Args.TryGetValue(CdpKey.Url, out var Nu) ? Nu.ToString()! : "");
                else
                {
                    SessionId = null;
                    if (Args.TryGetValue(CdpArg.PageId, out var Pid) && Cmd is not "select_page" and not "close_page")
                    {
                        var Pages = await GetPageTargets();
                        var Idx = int.Parse(Pid.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
                        if (Idx >= 0 && Idx < Pages.Count) await AttachToTarget(Pages[Idx][CdpKey.TargetId]!.ToString());
                    }
                    await DispatchCommand(Cmd, Args);
                }
            }
            catch (Exception Ex) { Output.AppendLine(string.Concat("Error: ", Ex.Message)); }
            Console.SetOut(OldOut);
            Console.SetError(OldErr);
            var ResponseBytes = Encoding.UTF8.GetBytes(Output.ToString());
            Res.ContentType = "text/plain; charset=utf-8";
            Res.ContentLength64 = ResponseBytes.Length;
            await Res.OutputStream.WriteAsync(ResponseBytes);
            Res.Close();
        }
    }

    private static string[] SplitArgs(string Line)
    {
        var Args = new List<string>();
        var Current = new StringBuilder();
        var InQuote = false;
        var QuoteChar = '"';
        foreach (var Ch in Line)
        {
            if (InQuote) { if (Ch == QuoteChar) InQuote = false; else Current.Append(Ch); }
            else if (Ch is '"' or '\'') { InQuote = true; QuoteChar = Ch; }
            else if (Ch == ' ') { if (Current.Length > 0) { Args.Add(Current.ToString()); Current.Clear(); } }
            else Current.Append(Ch);
        }
        if (Current.Length > 0) Args.Add(Current.ToString());
        return Args.ToArray();
    }

    private async Task DispatchCommand(string Command, Dictionary<string, object> ParsedArgs)
    {
        switch (Command)
        {
            case "list_pages": await ExecuteListPages(); break;
            case "select_page": await ExecuteSelectPage(ParsedArgs); break;
            case "close_page": await ExecuteClosePage(ParsedArgs); break;
            case "new_page": await ExecuteNewPage(ParsedArgs); break;
            case "navigate_page": await ExecuteNavigatePage(ParsedArgs); break;
            case "take_screenshot": await ExecuteTakeScreenshot(ParsedArgs); break;
            case "take_snapshot": await ExecuteTakeSnapshot(ParsedArgs); break;
            case "evaluate_script": await ExecuteEvaluateScript(ParsedArgs); break;
            case "click": await ExecuteClick(ParsedArgs); break;
            case "hover": await ExecuteHover(ParsedArgs); break;
            case "fill": await ExecuteFill(ParsedArgs); break;
            case "type_text": await ExecuteTypeText(ParsedArgs); break;
            case "press_key": await ExecutePressKey(ParsedArgs); break;
            case "list_console_messages": await ExecuteListConsoleMessages(); break;
            case "list_network_requests": await ExecuteListNetworkRequests(); break;
            case "resize_page": await ExecuteResizePage(ParsedArgs); break;
            case "emulate": await ExecuteEmulate(ParsedArgs); break;
            case "handle_dialog": await ExecuteHandleDialog(ParsedArgs); break;
            case "drag": await ExecuteDrag(ParsedArgs); break;
            case "upload_file": await ExecuteUploadFile(ParsedArgs); break;
            default: Console.Error.WriteLine(string.Concat(CdpMsg.UnknownCommand, Command)); Environment.Exit(1); break;
        }
    }

    private static readonly int[] FallbackPorts = [9222, 9223, 9224, 9225, 9229, 9333];
    private const int DesktopDebugPort = 9222;

    private async Task ConnectToChrome(Dictionary<string, object> ParsedArgs)
    {
        var TargetFilter = ParsedArgs.TryGetValue(CdpArg.Target, out var T) ? T.ToString()! : "desktop";
        var ExplicitPort = ParsedArgs.TryGetValue(CdpArg.Port, out var P) ? int.Parse(P.ToString()!, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        if (Process.GetProcessesByName(CdpProto.ChromeProcessName).Length == 0)
        {
            Console.Error.WriteLine(CdpMsg.ChromeNotRunning);
            LaunchChromeWithDebugging(TargetFilter == "desktop" ? ExplicitPort ?? DesktopDebugPort : 0);
            await Task.Delay(8000);
        }
        else if (TargetFilter == "desktop" && await ResolveEndpoint("desktop", ExplicitPort) == null)
        {
            Console.Error.WriteLine("Desktop Chrome has no CDP port. Restarting with --remote-debugging-port...");
            foreach (var Proc in Process.GetProcessesByName(CdpProto.ChromeProcessName)) try { Proc.Kill(); } catch { }
            await Task.Delay(5000);
            LaunchChromeWithDebugging(ExplicitPort ?? DesktopDebugPort);
            await Task.Delay(8000);
        }
        for (var Attempt = 0; Attempt < 6; Attempt++)
        {
            var Endpoint = await ResolveEndpoint(TargetFilter, ExplicitPort);
            if (Endpoint == null) { ClickAllowPrompt(); await Task.Delay(CdpTimeout.RetryDelayMs); continue; }
            Console.Error.WriteLine(string.Concat("Connecting to ", Endpoint));
            WebSocket = new ClientWebSocket();
            using var Timeout = new CancellationTokenSource(CdpTimeout.ConnectTimeoutMs);
            var AllowCts = new CancellationTokenSource();
            var AllowTask = Task.Run(async () => { while (!AllowCts.Token.IsCancellationRequested) { await Task.Delay(500); ClickAllowPrompt(); } });
            try { await WebSocket.ConnectAsync(new Uri(Endpoint), Timeout.Token); AllowCts.Cancel(); if (!DismissInfobar()) { Thread.Sleep(800); DismissInfobar(); } Console.Error.WriteLine("Connected!"); return; }
            catch (Exception Ex) { AllowCts.Cancel(); Console.Error.WriteLine(string.Concat("Connect failed: ", Ex.Message)); await Task.Delay(CdpTimeout.RetryDelayMs); }
        }
        Console.Error.WriteLine("Cannot connect. Enable: chrome://inspect/#remote-debugging");
        Environment.Exit(1);
    }

    private async Task<string?> ResolveEndpoint(string TargetFilter, int? ExplicitPort)
    {
        if (ExplicitPort == null && File.Exists(ActivePortFile))
        {
            var Lines = File.ReadAllLines(ActivePortFile).Where(L => !string.IsNullOrWhiteSpace(L)).ToArray();
            if (Lines.Length >= 2) return string.Concat(CdpProto.WsPrefix, Lines[0].Trim(), Lines[1].Trim());
        }
        using var Http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var Ports = ExplicitPort != null ? new[] { ExplicitPort.Value } : FallbackPorts;
        foreach (var Port in Ports)
        {
            try
            {
                var Json = await Http.GetStringAsync(string.Concat("http://127.0.0.1:", Port, "/json/version"));
                var VersionNode = JsonNode.Parse(Json);
                var UserAgent = VersionNode?["User-Agent"]?.ToString() ?? "";
                var IsMobile = UserAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) || UserAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase);
                if (TargetFilter == "mobile" && !IsMobile) continue;
                if (TargetFilter == "desktop" && IsMobile) continue;
                var WsUrl = VersionNode?["webSocketDebuggerUrl"]?.ToString();
                if (WsUrl != null) return WsUrl;
            }
            catch { }
        }
        return null;
    }

    private static void LaunchChromeWithDebugging(int Port)
    {
        var ChromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
        // Create a .bat launcher in temp to ensure Chrome inherits the interactive desktop session
        var BatPath = Path.Combine(Path.GetTempPath(), "chrome-debug.bat");
        File.WriteAllText(BatPath, $"@start \"\" \"{ChromePath}\" --remote-debugging-port={Port} --remote-allow-origins=*\n");
        Process.Start(new ProcessStartInfo(BatPath) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }

    internal async Task<JsonNode?> SendCommand(string Method, JsonObject? Params = null)
    {
        var Id = CommandId++;
        var Message = new JsonObject { [CdpKey.Id] = Id, [CdpKey.Method] = Method };
        if (Params != null) Message[CdpKey.Params] = Params;
        if (SessionId != null) Message[CdpKey.SessionId] = SessionId;
        await WebSocket!.SendAsync(Encoding.UTF8.GetBytes(Message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        var Buffer = new byte[CdpTimeout.BufferSize];
        var Builder = new StringBuilder();
        while (true)
        {
            var Result = await WebSocket.ReceiveAsync(Buffer, CancellationToken.None);
            Builder.Append(Encoding.UTF8.GetString(Buffer, 0, Result.Count));
            if (Result.EndOfMessage)
            {
                var Parsed = JsonNode.Parse(Builder.ToString());
                if (Parsed?[CdpKey.Id]?.GetValue<int>() == Id) return Parsed?[CdpKey.Result];
                if (Parsed?[CdpKey.Method] != null) EventBuffer.Add(Parsed!);
                Builder.Clear();
            }
        }
    }

    internal async Task<JsonNode?> SendBrowserCommand(string Method, JsonObject? Params = null)
    {
        var SavedSession = SessionId;
        SessionId = null;
        var Result = await SendCommand(Method, Params);
        SessionId = SavedSession;
        return Result;
    }

    internal async Task<List<JsonNode>> GetPageTargets()
    {
        var Targets = await SendBrowserCommand(Cdp.TargetGetTargets);
        return Targets![CdpKey.TargetInfos]!.AsArray()
            .Where(T => T![CdpKey.Type]!.ToString() == CdpKey.Page)
            .Where(T => !T![CdpKey.Url]!.ToString().StartsWith(CdpProto.ChromeScheme, StringComparison.Ordinal) || T![CdpKey.Url]!.ToString() == CdpProto.NewTabUrl)
            .Where(T => !T![CdpKey.Url]!.ToString().StartsWith(CdpProto.ChromeExtensionScheme, StringComparison.Ordinal))
            .Select(T => T!).ToList();
    }

    internal async Task AttachToTarget(string TargetId)
    {
        var Session = await SendBrowserCommand(Cdp.TargetAttachToTarget, new JsonObject { [CdpKey.TargetId] = TargetId, [CdpKey.Flatten] = true });
        SessionId = Session![CdpKey.SessionId]!.ToString();
        await SendCommand(Cdp.PageEnable);
        await SendCommand(Cdp.RuntimeEnable);
        await SendCommand(Cdp.DomEnable);
        await SendCommand(Cdp.NetworkEnable);
    }

    internal async Task EnsurePageAttached()
    {
        if (SessionId != null) return;
        var Pages = await GetPageTargets();
        if (Pages.Count == 0) { Console.Error.WriteLine("No pages open"); Environment.Exit(1); }
        await AttachToTarget(Pages[0][CdpKey.TargetId]!.ToString());
    }

    internal async Task<string> EvaluateExpression(string Expression, bool AwaitPromise = false)
    {
        var Params = new JsonObject { [CdpKey.Expression] = Expression, [CdpKey.ReturnByValue] = true };
        if (AwaitPromise) Params[CdpKey.AwaitPromise] = true;
        var Result = await SendCommand(Cdp.RuntimeEvaluate, Params);
        if (Result?[CdpKey.ExceptionDetails] != null) return string.Concat(CdpProto.ErrorPrefix, Result[CdpKey.ExceptionDetails]![CdpKey.Text]);
        return Result?[CdpKey.Result]?[CdpKey.Value]?.ToString() ?? string.Empty;
    }

    private static string BuildUidSelector(string Uid) => string.Concat(CdpProto.DataUidSelector, Uid, CdpProto.DataUidSelectorEnd);
}
