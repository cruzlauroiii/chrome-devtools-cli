#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public async Task RunAsync(string[] Argv)
    {
        if (Argv.Length == 0 || Argv[0] is "--help" or "-h" or "help") { PrintHelp(); return; }
        var (Command, ParsedArgs) = ParseArgs(Argv);
        if (Command == "allow") { ClickAllowPrompt(); return; }
        if (Command == "screenshot_desktop") { ExecuteScreenshotDesktop(ParsedArgs); return; }
        if (Command == "focus_chrome") { FocusChrome(); return; }
        await ConnectToChrome();
        if (ParsedArgs.TryGetValue(CdpArg.PageId, out var GlobalPageId) && Command is not "select_page" and not "close_page")
        {
            var Pages = await GetPageTargets();
            var PageIndex = int.Parse(GlobalPageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
            if (PageIndex >= 0 && PageIndex < Pages.Count) await AttachToTarget(Pages[PageIndex][CdpKey.TargetId]!.ToString());
        }
        try { await DispatchCommand(Command, ParsedArgs); }
        finally { WebSocket?.Dispose(); }
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

    private async Task ConnectToChrome()
    {
        var AllowClicked = false;
        for (var Attempt = 0; Attempt < 3; Attempt++)
        {
            if (!File.Exists(ActivePortFile)) { if (!AllowClicked) { ClickAllowPrompt(); AllowClicked = true; } await Task.Delay(CdpTimeout.RetryDelayMs); if (!File.Exists(ActivePortFile)) continue; }
            var Lines = File.ReadAllLines(ActivePortFile).Where(L => !string.IsNullOrWhiteSpace(L)).ToArray();
            if (Lines.Length < 2) continue;
            var Endpoint = string.Concat(CdpProto.WsPrefix, Lines[0].Trim(), Lines[1].Trim());
            WebSocket = new ClientWebSocket();
            using var Timeout = new CancellationTokenSource(CdpTimeout.ConnectTimeoutMs);
            if (!AllowClicked) { _ = Task.Run(async () => { await Task.Delay(CdpTimeout.PageLoadDelayMs); ClickAllowPrompt(); }); AllowClicked = true; }
            try { await WebSocket.ConnectAsync(new Uri(Endpoint), Timeout.Token); return; }
            catch { await Task.Delay(CdpTimeout.RetryDelayMs); }
        }
        Console.Error.WriteLine("Cannot connect. Enable: chrome://inspect/#remote-debugging");
        Environment.Exit(1);
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
