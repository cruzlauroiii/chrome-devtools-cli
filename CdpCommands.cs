#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413, S6608
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class CdpCli
{
    private static readonly JsonSerializerOptions JsonIndented = new(JsonSerializerDefaults.General) { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    internal async Task ExecuteListPages()
    {
        var Pages = await GetPageTargets();
        Console.WriteLine("## Pages");
        for (var Index = 0; Index < Pages.Count; Index++)
        {
            var Url = Pages[Index][CdpKey.Url]!.ToString();
            var Title = Pages[Index][CdpKey.Title]?.ToString() ?? string.Empty;
            Console.WriteLine(string.Concat(Index + 1, CdpMsg.ColonSpace, Url, Title.Length > 0 ? string.Concat(CdpMsg.ParenOpen, Title, CdpMsg.ParenClose) : string.Empty));
        }
    }

    internal async Task ExecuteSelectPage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.PageId, out var PageId)) { Console.Error.WriteLine("Required: pageId"); return; }
        var Pages = await GetPageTargets();
        var Index = int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
        if (Index < 0 || Index >= Pages.Count) { Console.Error.WriteLine(string.Concat(CdpMsg.InvalidPageIdRange, Pages.Count)); return; }
        await AttachToTarget(Pages[Index][CdpKey.TargetId]!.ToString());
        Console.WriteLine(string.Concat(CdpMsg.SelectedPage, int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture), CdpMsg.ColonSpace, Pages[Index][CdpKey.Url]));
    }

    internal async Task ExecuteClosePage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.PageId, out var PageId)) { Console.Error.WriteLine("Required: pageId"); return; }
        var Pages = await GetPageTargets();
        var Index = int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture) - 1;
        if (Index < 0 || Index >= Pages.Count) { Console.Error.WriteLine("Invalid pageId"); return; }
        await SendBrowserCommand(Cdp.TargetCloseTarget, new JsonObject { [CdpKey.TargetId] = Pages[Index][CdpKey.TargetId]!.ToString() });
        Console.WriteLine(string.Concat(CdpMsg.ClosedPage, int.Parse(PageId.ToString()!, System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal async Task ExecuteNewPage(Dictionary<string, object> Args)
    {
        var Url = Args.TryGetValue(CdpKey.Url, out var UrlValue) ? UrlValue.ToString()! : CdpProto.AboutBlank;
        var Target = await SendBrowserCommand(Cdp.TargetCreateTarget, new JsonObject { [CdpKey.Url] = Url });
        await AttachToTarget(Target![CdpKey.TargetId]!.ToString());
        await Task.Delay(CdpTimeout.PageLoadDelayMs);
        Console.WriteLine(string.Concat(CdpMsg.Opened, Url));
        await ExecuteListPages();
    }

    internal async Task ExecuteNavigatePage(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        if (Args.TryGetValue(CdpKey.Type, out var NavType))
        {
            switch (NavType.ToString())
            {
                case "back": await EvaluateExpression(CdpJs.HistoryBack); await Task.Delay(CdpTimeout.PageLoadDelayMs); break;
                case "forward": await EvaluateExpression(CdpJs.HistoryForward); await Task.Delay(CdpTimeout.PageLoadDelayMs); break;
                case "reload":
                    var IgnoreCache = Args.ContainsKey(CdpKey.IgnoreCache) && bool.Parse(Args[CdpKey.IgnoreCache].ToString()!);
                    await SendCommand(Cdp.PageReload, new JsonObject { [CdpKey.IgnoreCache] = IgnoreCache });
                    await Task.Delay(CdpTimeout.PageLoadDelayMs); break;
                case "url":
                    if (!Args.TryGetValue(CdpKey.Url, out var NavUrl)) { Console.Error.WriteLine("Required: url"); return; }
                    await SendCommand(Cdp.PageNavigate, new JsonObject { [CdpKey.Url] = NavUrl.ToString()! });
                    await Task.Delay(CdpTimeout.NavigationDelayMs);
                    Console.WriteLine(string.Concat(CdpMsg.NavigatedTo, NavUrl)); break;
            }
        }
        else if (Args.TryGetValue(CdpKey.Url, out var DirectUrl))
        {
            await SendCommand(Cdp.PageNavigate, new JsonObject { [CdpKey.Url] = DirectUrl.ToString()! });
            await Task.Delay(CdpTimeout.NavigationDelayMs);
            Console.WriteLine(string.Concat(CdpMsg.NavigatedTo, DirectUrl));
        }
        await ExecuteListPages();
    }

    internal async Task ExecuteTakeScreenshot(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        var Format = Args.TryGetValue(CdpKey.Format, out var FormatValue) ? FormatValue.ToString()! : CdpProto.PngFormat;
        var ScreenshotParams = new JsonObject { [CdpKey.Format] = Format };
        if (Args.TryGetValue(CdpKey.Quality, out var Quality)) ScreenshotParams[CdpKey.Quality] = int.Parse(Quality.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (Args.ContainsKey(CdpArg.FullPage) && bool.Parse(Args[CdpArg.FullPage].ToString()!))
        {
            var Metrics = await SendCommand(Cdp.PageGetLayoutMetrics);
            ScreenshotParams[CdpKey.Clip] = new JsonObject { [CdpKey.X] = 0, [CdpKey.Y] = 0, [CdpKey.Width] = Metrics![CdpKey.ContentSize]![CdpKey.Width]!.GetValue<double>(), [CdpKey.Height] = Metrics![CdpKey.ContentSize]![CdpKey.Height]!.GetValue<double>(), [CdpKey.Scale] = 1 };
        }
        var ScreenshotResult = await SendCommand(Cdp.PageCaptureScreenshot, ScreenshotParams);
        var ImageData = Convert.FromBase64String(ScreenshotResult![CdpKey.Data]!.ToString());
        var OutputPath = Args.TryGetValue(CdpArg.FilePath, out var FilePath) ? FilePath.ToString()! : Path.Combine(Path.GetTempPath(), string.Concat(CdpProto.ScreenshotPrefix, Guid.NewGuid().ToString("N"), ".", Format));
        File.WriteAllBytes(OutputPath, ImageData);
        Console.WriteLine(string.Concat(CdpMsg.ScreenshotSaved, OutputPath));
    }

    internal async Task ExecuteTakeSnapshot(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        var Tree = await SendCommand(Cdp.AccessibilityGetFullAxTree);
        var Nodes = Tree![CdpKey.Nodes]!.AsArray();
        var Output = new StringBuilder();
        Output.AppendLine("## Accessibility Snapshot");
        foreach (var Node in Nodes)
        {
            var Role = Node![CdpKey.Role]?[CdpKey.Value]?.ToString() ?? string.Empty;
            if (Role is "" or "none" or "InlineTextBox") continue;
            var Name = Node[CdpKey.Name]?[CdpKey.Value]?.ToString() ?? string.Empty;
            var NodeIdVal = Node[CdpKey.NodeId]?.ToString() ?? string.Empty;
            Output.AppendLine(string.Concat(CdpMsg.BracketOpen, NodeIdVal, CdpMsg.BracketClose, Role, Name.Length > 0 ? string.Concat(CdpMsg.QuoteOpen, Name, CdpMsg.QuoteClose) : string.Empty));
        }
        var Content = Output.ToString();
        if (Args.TryGetValue(CdpArg.FilePath, out var FilePath)) { File.WriteAllText(FilePath.ToString()!, Content); Console.WriteLine(string.Concat(CdpMsg.SnapshotSaved, FilePath)); }
        else Console.Write(Content);
    }

    internal async Task ExecuteEvaluateScript(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Function, out var Function)) { Console.Error.WriteLine("Required: function"); return; }
        await EnsurePageAttached();
        var Result = await SendCommand(Cdp.RuntimeEvaluate, new JsonObject { [CdpKey.Expression] = string.Concat("(", Function, CdpMsg.InvokeWrapper), [CdpKey.ReturnByValue] = true, [CdpKey.AwaitPromise] = true });
        if (Result?[CdpKey.ExceptionDetails] != null) Console.Error.WriteLine(string.Concat(CdpMsg.ErrorLabel, Result[CdpKey.ExceptionDetails]![CdpKey.Text]));
        else Console.WriteLine(Result?[CdpKey.Result]?[CdpKey.Value] != null ? JsonSerializer.Serialize(Result[CdpKey.Result]![CdpKey.Value], JsonIndented) : CdpEscape.Undefined);
    }

    internal async Task ExecuteClick(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Uid, out var Uid)) { Console.Error.WriteLine("Required: uid"); return; }
        await EnsurePageAttached();
        var DoubleClick = Args.ContainsKey(CdpArg.DblClick) && bool.Parse(Args[CdpArg.DblClick].ToString()!);
        var Escaped = Uid.ToString()!.Replace(CdpEscape.SingleQuote, CdpEscape.SingleQuoteEscaped);
        var Sel = BuildUidSelector(Escaped);
        Console.WriteLine(await EvaluateExpression(string.Format(System.Globalization.CultureInfo.InvariantCulture, CdpJs.ClickScript, Sel, Escaped, DoubleClick ? CdpJs.ClickAgain : string.Empty), true));
    }

    internal async Task ExecuteHover(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Uid, out var Uid)) { Console.Error.WriteLine("Required: uid"); return; }
        await EnsurePageAttached();
        Console.WriteLine(await EvaluateExpression(string.Format(System.Globalization.CultureInfo.InvariantCulture, CdpJs.HoverScript, BuildUidSelector(Uid.ToString()!))));
    }

    internal async Task ExecuteFill(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Uid, out var Uid) || !Args.TryGetValue(CdpKey.Value, out var Value)) { Console.Error.WriteLine("Required: uid, value"); return; }
        await EnsurePageAttached();
        var Escaped = Value.ToString()!.Replace(CdpEscape.Backslash, CdpEscape.BackslashEscaped).Replace(CdpEscape.SingleQuote, CdpEscape.SingleQuoteEscaped);
        Console.WriteLine(await EvaluateExpression(string.Format(System.Globalization.CultureInfo.InvariantCulture, CdpJs.FillScript, BuildUidSelector(Uid.ToString()!), Escaped)));
    }

    internal async Task ExecuteTypeText(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpKey.Text, out var Text)) { Console.Error.WriteLine("Required: text"); return; }
        await EnsurePageAttached();
        foreach (var Character in Text.ToString()!) await SendCommand(Cdp.InputDispatchKeyEvent, new JsonObject { [CdpKey.Type] = CdpProto.Char, [CdpKey.Text] = Character.ToString() });
        if (Args.TryGetValue(CdpArg.SubmitKey, out var SubmitKey)) await SendCommand(Cdp.InputDispatchKeyEvent, new JsonObject { [CdpKey.Type] = CdpProto.KeyDown, [CdpKey.Key] = SubmitKey.ToString() });
        Console.WriteLine(string.Concat(CdpMsg.Typed, Text));
    }

    internal async Task ExecutePressKey(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpKey.Key, out var Key)) { Console.Error.WriteLine("Required: key"); return; }
        await EnsurePageAttached();
        var Parts = Key.ToString()!.Split('+');
        var Mods = 0;
        foreach (var Modifier in Parts.SkipLast(1)) { switch (Modifier.ToLower(System.Globalization.CultureInfo.InvariantCulture)) { case "control" or "ctrl": Mods |= CdpModifier.Control; break; case "alt": Mods |= CdpModifier.Alt; break; case "shift": Mods |= CdpModifier.Shift; break; case "meta" or "cmd": Mods |= CdpModifier.Meta; break; } }
        await SendCommand(Cdp.InputDispatchKeyEvent, new JsonObject { [CdpKey.Type] = CdpProto.KeyDown, [CdpKey.Key] = Parts.Last(), [CdpKey.Modifiers] = Mods });
        await SendCommand(Cdp.InputDispatchKeyEvent, new JsonObject { [CdpKey.Type] = CdpProto.KeyUp, [CdpKey.Key] = Parts.Last(), [CdpKey.Modifiers] = Mods });
        Console.WriteLine(string.Concat(CdpMsg.Pressed, Key));
    }

    internal async Task ExecuteListConsoleMessages()
    {
        await EnsurePageAttached();
        var Messages = EventBuffer.Where(E => E[CdpKey.Method]?.ToString() is Cdp.RuntimeConsoleApiCalled or Cdp.RuntimeExceptionThrown).ToList();
        if (Messages.Count == 0) { Console.WriteLine("No console messages captured."); return; }
        Console.WriteLine("## Console Messages");
        foreach (var Msg in Messages)
        {
            if (Msg[CdpKey.Method]!.ToString() == Cdp.RuntimeConsoleApiCalled)
            {
                var MsgType = Msg[CdpKey.Params]![CdpKey.Type]?.ToString() ?? "log";
                var Content = string.Join(" ", Msg[CdpKey.Params]![CdpKey.Args]!.AsArray().Select(A => A![CdpKey.Value]?.ToString() ?? A![CdpKey.Description]?.ToString() ?? string.Empty));
                Console.WriteLine(string.Concat(CdpMsg.SquareOpen, MsgType, CdpMsg.BracketClose, Content));
            }
            else Console.WriteLine(string.Concat(CdpMsg.ErrorLog, Msg[CdpKey.Params]![CdpKey.ExceptionDetails]?[CdpKey.Text]));
        }
    }

    internal async Task ExecuteListNetworkRequests()
    {
        await EnsurePageAttached();
        var Requests = EventBuffer.Where(E => E[CdpKey.Method]?.ToString() == Cdp.NetworkRequestWillBeSent).ToList();
        if (Requests.Count == 0) { Console.WriteLine("No network requests captured."); return; }
        Console.WriteLine("## Network Requests");
        var Counter = 1;
        foreach (var Request in Requests.TakeLast(50))
            Console.WriteLine(string.Concat(Counter++, CdpMsg.DotSpace, Request[CdpKey.Params]![CdpKey.Request]![CdpKey.Method], " ", Request[CdpKey.Params]![CdpKey.Request]![CdpKey.Url]));
    }

    internal async Task ExecuteResizePage(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpKey.Width, out var Width) || !Args.TryGetValue(CdpKey.Height, out var Height)) { Console.Error.WriteLine("Required: width, height"); return; }
        await EnsurePageAttached();
        await SendCommand(Cdp.EmulationSetDeviceMetrics, new JsonObject { [CdpKey.Width] = int.Parse(Width.ToString()!, System.Globalization.CultureInfo.InvariantCulture), [CdpKey.Height] = int.Parse(Height.ToString()!, System.Globalization.CultureInfo.InvariantCulture), [CdpKey.DeviceScaleFactor] = 1, [CdpKey.Mobile] = false });
        Console.WriteLine(string.Concat(CdpMsg.ResizedTo, Width, CdpMsg.Separator, Height));
    }

    internal async Task ExecuteEmulate(Dictionary<string, object> Args)
    {
        await EnsurePageAttached();
        if (Args.TryGetValue(CdpKey.UserAgent, out var UserAgent)) await SendCommand(Cdp.EmulationSetUserAgent, new JsonObject { [CdpKey.UserAgent] = UserAgent.ToString()! });
        if (Args.TryGetValue(CdpArg.Geolocation, out var GeoLocation)) { var Coordinates = GeoLocation.ToString()!.Split('x'); if (Coordinates.Length == 2) await SendCommand(Cdp.EmulationSetGeolocation, new JsonObject { [CdpKey.Latitude] = double.Parse(Coordinates[0], System.Globalization.CultureInfo.InvariantCulture), [CdpKey.Longitude] = double.Parse(Coordinates[1], System.Globalization.CultureInfo.InvariantCulture), [CdpKey.Accuracy] = 1 }); }
        if (Args.TryGetValue(CdpArg.ColorScheme, out var ColorScheme)) await SendCommand(Cdp.EmulationSetMedia, new JsonObject { [CdpKey.Features] = new JsonArray { new JsonObject { [CdpKey.Name] = CdpProto.PrefersColorScheme, [CdpKey.Value] = ColorScheme.ToString()! } } });
        Console.WriteLine("Emulation applied");
    }

    internal async Task ExecuteHandleDialog(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Action, out var Action)) { Console.Error.WriteLine("Required: action (accept|dismiss)"); return; }
        var DialogParams = new JsonObject { [CdpKey.Accept] = Action.ToString() == "accept" };
        if (Args.TryGetValue(CdpKey.PromptText, out var PromptTextVal)) DialogParams[CdpKey.PromptText] = PromptTextVal.ToString()!;
        await SendCommand(Cdp.PageHandleJavaScriptDialog, DialogParams);
        Console.WriteLine(string.Concat(CdpMsg.DialogPrefix, Action, CdpMsg.DialogSuffix));
    }

    internal async Task ExecuteDrag(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.FromUid, out var FromUid) || !Args.TryGetValue(CdpArg.ToUid, out var ToUid)) { Console.Error.WriteLine("Required: from_uid, to_uid"); return; }
        await EnsurePageAttached();
        Console.WriteLine(await EvaluateExpression(string.Format(System.Globalization.CultureInfo.InvariantCulture, CdpJs.DragScript, BuildUidSelector(FromUid.ToString()!), BuildUidSelector(ToUid.ToString()!))));
    }

    internal async Task ExecuteUploadFile(Dictionary<string, object> Args)
    {
        if (!Args.TryGetValue(CdpArg.Uid, out var Uid) || !Args.TryGetValue(CdpArg.FilePath, out var FilePath)) { Console.Error.WriteLine("Required: uid, filePath"); return; }
        await EnsurePageAttached();
        var Document = await SendCommand(Cdp.DomGetDocument);
        var Found = await SendCommand(Cdp.DomQuerySelector, new JsonObject { [CdpKey.NodeId] = Document![CdpKey.Root]![CdpKey.NodeId]!.GetValue<int>(), [CdpKey.Selector] = BuildUidSelector(Uid.ToString()!) });
        if (Found?[CdpKey.NodeId]?.GetValue<int>() is > 0)
        {
            await SendCommand(Cdp.DomSetFileInputFiles, new JsonObject { [CdpKey.NodeId] = Found[CdpKey.NodeId]!.GetValue<int>(), [CdpKey.Files] = new JsonArray { JsonValue.Create(Path.GetFullPath(FilePath.ToString()!)) } });
            Console.WriteLine(string.Concat(CdpMsg.Uploaded, FilePath));
        }
        else Console.Error.WriteLine("File input not found");
    }
}
