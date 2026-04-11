#pragma warning disable SA1400, SA1649, SA1402, SA1502, SA1128, SA1501, SA1119, SA1503, SA1513, SA1413
public static class Cdp
{
    public const string TargetGetTargets = "Target.getTargets";
    public const string TargetAttachToTarget = "Target.attachToTarget";
    public const string TargetCloseTarget = "Target.closeTarget";
    public const string TargetCreateTarget = "Target.createTarget";
    public const string PageEnable = "Page.enable";
    public const string PageNavigate = "Page.navigate";
    public const string PageReload = "Page.reload";
    public const string PageGetLayoutMetrics = "Page.getLayoutMetrics";
    public const string PageCaptureScreenshot = "Page.captureScreenshot";
    public const string PageHandleJavaScriptDialog = "Page.handleJavaScriptDialog";
    public const string RuntimeEnable = "Runtime.enable";
    public const string RuntimeEvaluate = "Runtime.evaluate";
    public const string RuntimeConsoleApiCalled = "Runtime.consoleAPICalled";
    public const string RuntimeExceptionThrown = "Runtime.exceptionThrown";
    public const string DomEnable = "DOM.enable";
    public const string DomGetDocument = "DOM.getDocument";
    public const string DomQuerySelector = "DOM.querySelector";
    public const string DomSetFileInputFiles = "DOM.setFileInputFiles";
    public const string NetworkEnable = "Network.enable";
    public const string NetworkRequestWillBeSent = "Network.requestWillBeSent";
    public const string AccessibilityGetFullAxTree = "Accessibility.getFullAXTree";
    public const string InputDispatchKeyEvent = "Input.dispatchKeyEvent";
    public const string EmulationSetDeviceMetrics = "Emulation.setDeviceMetricsOverride";
    public const string EmulationSetUserAgent = "Emulation.setUserAgentOverride";
    public const string EmulationSetGeolocation = "Emulation.setGeolocationOverride";
    public const string EmulationSetMedia = "Emulation.setEmulatedMedia";
}

public static class CdpKey
{
    public const string Id = "id";
    public const string Method = "method";
    public const string Params = "params";
    public const string SessionId = "sessionId";
    public const string Result = "result";
    public const string TargetId = "targetId";
    public const string TargetInfos = "targetInfos";
    public const string Flatten = "flatten";
    public const string Type = "type";
    public const string Page = "page";
    public const string Url = "url";
    public const string Title = "title";
    public const string Expression = "expression";
    public const string ReturnByValue = "returnByValue";
    public const string AwaitPromise = "awaitPromise";
    public const string ExceptionDetails = "exceptionDetails";
    public const string Text = "text";
    public const string Value = "value";
    public const string Name = "name";
    public const string Role = "role";
    public const string NodeId = "nodeId";
    public const string Root = "root";
    public const string Nodes = "nodes";
    public const string Format = "format";
    public const string Quality = "quality";
    public const string Clip = "clip";
    public const string Data = "data";
    public const string X = "x";
    public const string Y = "y";
    public const string Width = "width";
    public const string Height = "height";
    public const string Scale = "scale";
    public const string Selector = "selector";
    public const string Files = "files";
    public const string Key = "key";
    public const string Modifiers = "modifiers";
    public const string Accept = "accept";
    public const string PromptText = "promptText";
    public const string IgnoreCache = "ignoreCache";
    public const string ContentSize = "contentSize";
    public const string Features = "features";
    public const string DeviceScaleFactor = "deviceScaleFactor";
    public const string Mobile = "mobile";
    public const string UserAgent = "userAgent";
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";
    public const string Accuracy = "accuracy";
    public const string Request = "request";
    public const string Args = "args";
    public const string Description = "description";
}

public static class CdpArg
{
    public const string PageId = "pageId";
    public const string Uid = "uid";
    public const string FilePath = "filePath";
    public const string FullPage = "fullPage";
    public const string DblClick = "dblClick";
    public const string SubmitKey = "submitKey";
    public const string FromUid = "from_uid";
    public const string ToUid = "to_uid";
    public const string Action = "action";
    public const string Function = "function";
    public const string ColorScheme = "colorScheme";
    public const string Geolocation = "geolocation";
    public const string NoPrefix = "no-";
    public const string ArgPrefix = "--";
}

public static class CdpShell
{
    public const string PowerShell = "powershell.exe";
    public const string NoProfile = "-NoProfile";
    public const string CommandFlag = "-Command";
    public const string ClickedPrefix = "Clicked";
}

public static class CdpEscape
{
    public const string Backslash = "\\";
    public const string BackslashEscaped = "\\\\";
    public const string SingleQuote = "'";
    public const string SingleQuoteEscaped = "\\'";
    public const string Undefined = "undefined";
}

public static class CdpMsg
{
    public const string UnknownCommand = "Unknown command: ";
    public const string SelectedPage = "Selected page ";
    public const string ClosedPage = "Closed page ";
    public const string Opened = "Opened: ";
    public const string NavigatedTo = "Navigated to ";
    public const string ScreenshotSaved = "Screenshot saved: ";
    public const string SnapshotSaved = "Snapshot saved: ";
    public const string Typed = "Typed: ";
    public const string Pressed = "Pressed: ";
    public const string Uploaded = "Uploaded: ";
    public const string ResizedTo = "Resized to ";
    public const string DialogPrefix = "Dialog ";
    public const string DialogSuffix = "ed";
    public const string ErrorLabel = "Error: ";
    public const string ErrorLog = "[error] ";
    public const string InvalidPageIdRange = "Invalid pageId. Range: 1-";
    public const string InvokeWrapper = ")()";
    public const string Separator = "x";
    public const string ColonSpace = ": ";
    public const string DotSpace = ". ";
    public const string BracketOpen = "  [";
    public const string BracketClose = "] ";
    public const string ParenOpen = "  (";
    public const string ParenClose = ")";
    public const string QuoteOpen = " \"";
    public const string QuoteClose = "\"";
    public const string SquareOpen = "[";
}

public static class CdpProto
{
    public const string ChromeScheme = "chrome://";
    public const string ChromeExtensionScheme = "chrome-extension://";
    public const string NewTabUrl = "chrome://newtab/";
    public const string AboutBlank = "about:blank";
    public const string WsPrefix = "ws://127.0.0.1:";
    public const string DataUidSelector = "[data-uid=\"";
    public const string DataUidSelectorEnd = "\"]";
    public const string PrefersColorScheme = "prefers-color-scheme";
    public const string ScreenshotPrefix = "screenshot-";
    public const string PngFormat = "png";
    public const string ErrorPrefix = "ERROR: ";
    public const string KeyDown = "keyDown";
    public const string KeyUp = "keyUp";
    public const string Char = "char";
}

public static class CdpJs
{
    public const string ClickScript = "(()=>{{const E=document.querySelector('{0}');if(!E)return 'Not found: {1}';E.scrollIntoView({{block:'center'}});E.click();{2}return 'Clicked '+E.tagName+' '+(E.textContent||'').substring(0,50)}})()";
    public const string ClickAgain = "E.click();";
    public const string HoverScript = "(()=>{{const E=document.querySelector('{0}');if(!E)return 'Not found';E.dispatchEvent(new MouseEvent('mouseover',{{bubbles:true}}));E.dispatchEvent(new MouseEvent('mouseenter',{{bubbles:true}}));return 'Hovered: '+E.tagName}})()";
    public const string FillScript = "(()=>{{const E=document.querySelector('{0}');if(!E)return 'Not found';if(E.tagName==='SELECT'){{E.value='{1}';E.dispatchEvent(new Event('change',{{bubbles:true}}));return 'Selected: '+E.value}}E.focus();E.value='{1}';E.dispatchEvent(new Event('input',{{bubbles:true}}));E.dispatchEvent(new Event('change',{{bubbles:true}}));return 'Filled: '+E.value}})()";
    public const string DragScript = "(()=>{{const S=document.querySelector('{0}'),D=document.querySelector('{1}');if(!S||!D)return 'Not found';const Sr=S.getBoundingClientRect(),Dr=D.getBoundingClientRect();S.dispatchEvent(new DragEvent('dragstart',{{bubbles:true,clientX:Sr.x+Sr.width/2,clientY:Sr.y+Sr.height/2}}));D.dispatchEvent(new DragEvent('drop',{{bubbles:true,clientX:Dr.x+Dr.width/2,clientY:Dr.y+Dr.height/2}}));S.dispatchEvent(new DragEvent('dragend',{{bubbles:true}}));return 'Dragged'}})()";
    public const string HistoryBack = "history.back()";
    public const string HistoryForward = "history.forward()";
}

public static class CdpTimeout
{
    public const int RetryDelayMs = 3000;
    public const int ConnectTimeoutMs = 10000;
    public const int NavigationDelayMs = 3000;
    public const int PageLoadDelayMs = 2000;
    public const int ProcessWaitMs = 15000;
    public const int BufferSize = 1024 * 1024;
}

public static class CdpModifier
{
    public const int Alt = 1;
    public const int Control = 2;
    public const int Meta = 4;
    public const int Shift = 8;
}
