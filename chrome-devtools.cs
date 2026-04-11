// Chrome DevTools CLI (.NET 11 native script)
// Wraps the local chrome-devtools-mcp repo as a CLI tool.
//
// Usage:
//   dotnet run chrome-devtools.cs -- <command> [args] [--options]
//   dotnet run chrome-devtools.cs -- list_pages --live
//   dotnet run chrome-devtools.cs -- navigate_page --type url --url "https://example.com"
//   dotnet run chrome-devtools.cs -- take_screenshot --filePath shot.png
//
// Modes:
//   (default)  Launches its own Chrome with your profile (fast, no login sessions)
//   --live     Connects to your running Chrome (preserves logged-in sessions)
//              Auto-clicks the Allow remote debugging prompt via UIAutomation

#pragma warning disable IL2026, IL3050

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var cli = new ChromeDevToolsCli();
await cli.RunAsync(args);

// ── Tool/Arg definitions (mirroring cliDefinitions.ts) ──────────────────────

class ArgDef
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "string";
    public string Description { get; init; } = "";
    public bool Required { get; init; }
    public object? Default { get; init; }
    public string[]? Enum { get; init; }
}

class CommandDef
{
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public Dictionary<string, ArgDef> Args { get; init; } = new();
}

class ChromeDevToolsCli
{
    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    static readonly Dictionary<string, CommandDef> Commands = new()
    {
        ["click"] = new() { Description = "Clicks on the provided element", Category = "Input", Args = new()
        {
            ["uid"] = new() { Name = "uid", Type = "string", Description = "Element uid", Required = true },
            ["dblClick"] = new() { Name = "dblClick", Type = "boolean", Description = "Double click", Required = false },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot in response", Required = false },
        }},
        ["close_page"] = new() { Description = "Closes the page by its index", Category = "Navigation", Args = new()
        {
            ["pageId"] = new() { Name = "pageId", Type = "number", Description = "Page ID to close", Required = true },
        }},
        ["drag"] = new() { Description = "Drag an element onto another", Category = "Input", Args = new()
        {
            ["from_uid"] = new() { Name = "from_uid", Type = "string", Description = "Element to drag", Required = true },
            ["to_uid"] = new() { Name = "to_uid", Type = "string", Description = "Drop target", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
        ["emulate"] = new() { Description = "Emulate device/network features", Category = "Emulation", Args = new()
        {
            ["networkConditions"] = new() { Name = "networkConditions", Type = "string", Description = "Throttle network", Required = false, Enum = new[] { "Offline", "Slow 3G", "Fast 3G", "Slow 4G", "Fast 4G" } },
            ["cpuThrottlingRate"] = new() { Name = "cpuThrottlingRate", Type = "number", Description = "CPU slowdown factor", Required = false },
            ["geolocation"] = new() { Name = "geolocation", Type = "string", Description = "Geolocation (<lat>x<lon>)", Required = false },
            ["userAgent"] = new() { Name = "userAgent", Type = "string", Description = "User agent", Required = false },
            ["colorScheme"] = new() { Name = "colorScheme", Type = "string", Description = "dark, light, or auto", Required = false, Enum = new[] { "dark", "light", "auto" } },
            ["viewport"] = new() { Name = "viewport", Type = "string", Description = "Viewport '<w>x<h>x<dpr>'", Required = false },
        }},
        ["evaluate_script"] = new() { Description = "Evaluate JavaScript in the selected page", Category = "Debugging", Args = new()
        {
            ["function"] = new() { Name = "function", Type = "string", Description = "JavaScript function to execute", Required = true },
            ["args"] = new() { Name = "args", Type = "array", Description = "Arguments for the function", Required = false },
        }},
        ["fill"] = new() { Description = "Type text into an input or select an option", Category = "Input", Args = new()
        {
            ["uid"] = new() { Name = "uid", Type = "string", Description = "Element uid", Required = true },
            ["value"] = new() { Name = "value", Type = "string", Description = "Value to fill", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
        ["fill_form"] = new() { Description = "Fill multiple form fields at once", Category = "Input", Args = new()
        {
            ["fields"] = new() { Name = "fields", Type = "array", Description = "Array of {uid, value}", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
        ["get_console_message"] = new() { Description = "Gets a console message by ID", Category = "Debugging", Args = new()
        {
            ["msgid"] = new() { Name = "msgid", Type = "number", Description = "Console message ID", Required = true },
        }},
        ["get_network_request"] = new() { Description = "Gets a network request by reqid", Category = "Network", Args = new()
        {
            ["reqid"] = new() { Name = "reqid", Type = "number", Description = "Network request ID", Required = false },
            ["requestFilePath"] = new() { Name = "requestFilePath", Type = "string", Description = "Save request body to path", Required = false },
            ["responseFilePath"] = new() { Name = "responseFilePath", Type = "string", Description = "Save response body to path", Required = false },
        }},
        ["handle_dialog"] = new() { Description = "Handle a browser dialog", Category = "Input", Args = new()
        {
            ["action"] = new() { Name = "action", Type = "string", Description = "accept or dismiss", Required = true, Enum = new[] { "accept", "dismiss" } },
            ["promptText"] = new() { Name = "promptText", Type = "string", Description = "Prompt text to enter", Required = false },
        }},
        ["hover"] = new() { Description = "Hover over an element", Category = "Input", Args = new()
        {
            ["uid"] = new() { Name = "uid", Type = "string", Description = "Element uid", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
        ["lighthouse_audit"] = new() { Description = "Lighthouse audit for accessibility, SEO, best practices", Category = "Debugging", Args = new()
        {
            ["mode"] = new() { Name = "mode", Type = "string", Description = "navigation or snapshot", Required = false, Default = "navigation", Enum = new[] { "navigation", "snapshot" } },
            ["device"] = new() { Name = "device", Type = "string", Description = "desktop or mobile", Required = false, Default = "desktop", Enum = new[] { "desktop", "mobile" } },
            ["outputDirPath"] = new() { Name = "outputDirPath", Type = "string", Description = "Directory for reports", Required = false },
        }},
        ["list_console_messages"] = new() { Description = "List console messages for the selected page", Category = "Debugging", Args = new()
        {
            ["pageSize"] = new() { Name = "pageSize", Type = "integer", Description = "Max messages", Required = false },
            ["pageIdx"] = new() { Name = "pageIdx", Type = "integer", Description = "Page number (0-based)", Required = false },
            ["types"] = new() { Name = "types", Type = "array", Description = "Filter by types", Required = false },
            ["includePreservedMessages"] = new() { Name = "includePreservedMessages", Type = "boolean", Description = "Include last 3 navigations", Required = false },
        }},
        ["list_network_requests"] = new() { Description = "List network requests for the selected page", Category = "Network", Args = new()
        {
            ["pageSize"] = new() { Name = "pageSize", Type = "integer", Description = "Max requests", Required = false },
            ["pageIdx"] = new() { Name = "pageIdx", Type = "integer", Description = "Page number (0-based)", Required = false },
            ["resourceTypes"] = new() { Name = "resourceTypes", Type = "array", Description = "Filter by types", Required = false },
            ["includePreservedRequests"] = new() { Name = "includePreservedRequests", Type = "boolean", Description = "Include last 3 navigations", Required = false },
        }},
        ["list_pages"] = new() { Description = "Get a list of pages open in the browser", Category = "Navigation", Args = new() },
        ["navigate_page"] = new() { Description = "Go to a URL, or back, forward, or reload", Category = "Navigation", Args = new()
        {
            ["type"] = new() { Name = "type", Type = "string", Description = "url, back, forward, or reload", Required = false, Enum = new[] { "url", "back", "forward", "reload" } },
            ["url"] = new() { Name = "url", Type = "string", Description = "Target URL", Required = false },
            ["ignoreCache"] = new() { Name = "ignoreCache", Type = "boolean", Description = "Ignore cache on reload", Required = false },
            ["timeout"] = new() { Name = "timeout", Type = "integer", Description = "Max wait time in ms", Required = false },
        }},
        ["new_page"] = new() { Description = "Open a new tab and load a URL", Category = "Navigation", Args = new()
        {
            ["url"] = new() { Name = "url", Type = "string", Description = "URL to load", Required = true },
            ["background"] = new() { Name = "background", Type = "boolean", Description = "Open in background", Required = false },
            ["timeout"] = new() { Name = "timeout", Type = "integer", Description = "Max wait time in ms", Required = false },
        }},
        ["performance_analyze_insight"] = new() { Description = "Analyze a Performance Insight from a trace", Category = "Performance", Args = new()
        {
            ["insightSetId"] = new() { Name = "insightSetId", Type = "string", Description = "Insight set ID", Required = true },
            ["insightName"] = new() { Name = "insightName", Type = "string", Description = "Insight name", Required = true },
        }},
        ["performance_start_trace"] = new() { Description = "Start a performance trace", Category = "Performance", Args = new()
        {
            ["reload"] = new() { Name = "reload", Type = "boolean", Description = "Reload page", Required = false, Default = true },
            ["autoStop"] = new() { Name = "autoStop", Type = "boolean", Description = "Auto-stop", Required = false, Default = true },
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Save trace to path", Required = false },
        }},
        ["performance_stop_trace"] = new() { Description = "Stop the active performance trace", Category = "Performance", Args = new()
        {
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Save trace to path", Required = false },
        }},
        ["press_key"] = new() { Description = "Press a key or key combination", Category = "Input", Args = new()
        {
            ["key"] = new() { Name = "key", Type = "string", Description = "Key or combo (e.g. 'Enter', 'Control+A')", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
        ["resize_page"] = new() { Description = "Resize the selected page's window", Category = "Emulation", Args = new()
        {
            ["width"] = new() { Name = "width", Type = "number", Description = "Page width", Required = true },
            ["height"] = new() { Name = "height", Type = "number", Description = "Page height", Required = true },
        }},
        ["select_page"] = new() { Description = "Select a page as context for future calls", Category = "Navigation", Args = new()
        {
            ["pageId"] = new() { Name = "pageId", Type = "number", Description = "Page ID to select", Required = true },
            ["bringToFront"] = new() { Name = "bringToFront", Type = "boolean", Description = "Focus and bring to top", Required = false },
        }},
        ["take_memory_snapshot"] = new() { Description = "Capture a heap snapshot", Category = "Performance", Args = new()
        {
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Path to .heapsnapshot file", Required = true },
        }},
        ["take_screenshot"] = new() { Description = "Take a screenshot of the page or element", Category = "Debugging", Args = new()
        {
            ["format"] = new() { Name = "format", Type = "string", Description = "png, jpeg, or webp", Required = false, Default = "png", Enum = new[] { "png", "jpeg", "webp" } },
            ["quality"] = new() { Name = "quality", Type = "number", Description = "Quality (0-100)", Required = false },
            ["uid"] = new() { Name = "uid", Type = "string", Description = "Element uid (omit for page)", Required = false },
            ["fullPage"] = new() { Name = "fullPage", Type = "boolean", Description = "Full page screenshot", Required = false },
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Save to path", Required = false },
        }},
        ["take_snapshot"] = new() { Description = "Take a text snapshot of the page (a11y tree)", Category = "Debugging", Args = new()
        {
            ["verbose"] = new() { Name = "verbose", Type = "boolean", Description = "Include all a11y info", Required = false },
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Save to path", Required = false },
        }},
        ["type_text"] = new() { Description = "Type text into a previously focused input", Category = "Input", Args = new()
        {
            ["text"] = new() { Name = "text", Type = "string", Description = "Text to type", Required = true },
            ["submitKey"] = new() { Name = "submitKey", Type = "string", Description = "Key after typing", Required = false },
        }},
        ["upload_file"] = new() { Description = "Upload a file through a file input", Category = "Input", Args = new()
        {
            ["uid"] = new() { Name = "uid", Type = "string", Description = "File input uid", Required = true },
            ["filePath"] = new() { Name = "filePath", Type = "string", Description = "Local file path", Required = true },
            ["includeSnapshot"] = new() { Name = "includeSnapshot", Type = "boolean", Description = "Include snapshot", Required = false },
        }},
    };

    // ── Main entry ──────────────────────────────────────────────────────────

    public async Task RunAsync(string[] argv)
    {
        if (argv.Length == 0 || argv[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return;
        }

        if (argv[0] == "list-commands")
        {
            foreach (var cmd in Commands.OrderBy(c => c.Key))
                Console.WriteLine($"{cmd.Key,-35} {cmd.Value.Description}");
            return;
        }

        // Extract global flags
        bool useLive = argv.Contains("--live");
        var filteredArgv = argv.Where(a => a != "--live").ToArray();

        var (command, cmdArgs) = ParseArgs(filteredArgv);

        if (filteredArgv.Contains("--help") && Commands.TryGetValue(command, out var helpDef))
        {
            PrintCommandHelp(command, helpDef);
            return;
        }

        if (!Commands.ContainsKey(command))
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            Console.Error.WriteLine("Run with --help to see available commands.");
            Environment.Exit(1);
        }

        // Build tool args
        var toolArgs = new JsonObject();
        foreach (var (k, v) in cmdArgs)
        {
            if (v is List<string> list)
                toolArgs[k] = new JsonArray(list.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
            else if (v is bool b) toolArgs[k] = b;
            else if (v is int i) toolArgs[k] = i;
            else if (v is double d) toolArgs[k] = d;
            else toolArgs[k] = v.ToString();
        }

        if (useLive)
        {
            // --live: connect to running Chrome via MCP stdio (logged-in sessions)
            await RunLiveMode(command, toolArgs);
        }
        else
        {
            // Default: use Node CLI (launches its own Chrome)
            await RunNodeCli(command, cmdArgs);
        }
    }

    // ── Live mode (MCP stdio, connects to running Chrome) ───────────────────

    async Task RunLiveMode(string command, JsonObject toolArgs)
    {
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        var serverScript = Path.Combine(repoRoot, "build", "src", "bin", "chrome-devtools-mcp.js");
        if (!File.Exists(serverScript))
        {
            Console.Error.WriteLine($"MCP server not found at {serverScript}. Run 'npm run build' first.");
            Environment.Exit(1);
        }

        var psi = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(serverScript);
        psi.ArgumentList.Add("--auto-connect");
        psi.EnvironmentVariables["CHROME_DEVTOOLS_MCP_NO_USAGE_STATISTICS"] = "true";

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start MCP server");
        int msgId = 1;

        // Background: poll for Allow prompt and click it while MCP server starts
        var allowCts = new CancellationTokenSource();
        var allowTask = Task.Run(async () =>
        {
            for (int i = 0; i < 15 && !allowCts.Token.IsCancellationRequested; i++)
            {
                await Task.Delay(2000, allowCts.Token);
                ClickAllowPrompt();
            }
        }, allowCts.Token);

        try
        {
            // Initialize MCP
            await SendJsonRpc(proc, new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = msgId,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "chrome-devtools-cli", ["version"] = "1.0.0" },
                }
            });
            var initResp = await ReadJsonRpcResponse(proc, msgId++, 30_000);

            // Send initialized notification
            await SendJsonRpc(proc, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });

            // Call the tool
            await SendJsonRpc(proc, new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = msgId,
                ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = command, ["arguments"] = toolArgs },
            });
            var result = await ReadJsonRpcResponse(proc, msgId++, 60_000);

            // Print result
            if (result.TryGetProperty("error", out var error))
            {
                Console.Error.WriteLine($"Error: {error}");
                Environment.Exit(1);
            }
            if (result.TryGetProperty("result", out var res))
                PrintToolResult(res);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            allowCts.Cancel();
            try { await allowTask; } catch { }
            try { proc.Kill(); } catch { }
        }
    }

    static async Task SendJsonRpc(Process proc, JsonObject msg)
    {
        await proc.StandardInput.WriteLineAsync(msg.ToJsonString());
        await proc.StandardInput.FlushAsync();
    }

    static async Task<JsonElement> ReadJsonRpcResponse(Process proc, int id, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await proc.StandardOutput.ReadLineAsync(cts.Token);
            if (line == null) throw new Exception("MCP server closed unexpectedly");
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("id", out var rid) && rid.GetInt32() == id)
                    return doc.RootElement;
            }
            catch { }
        }
        throw new TimeoutException("MCP server response timeout");
    }

    // ── Node CLI mode (launches its own Chrome) ─────────────────────────────

    async Task RunNodeCli(string command, Dictionary<string, object> cmdArgs)
    {
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        var cliScript = Path.Combine(repoRoot, "build", "src", "bin", "chrome-devtools.js");
        if (!File.Exists(cliScript))
        {
            Console.Error.WriteLine($"CLI not found at {cliScript}. Run 'npm run build' first.");
            Environment.Exit(1);
        }

        var cliArgs = new List<string> { cliScript, command };
        var def = Commands[command];
        var requiredArgs = def.Args.Where(a => a.Value.Required).Select(a => a.Key).ToList();

        // Add positional (required) args first
        foreach (var reqArg in requiredArgs)
            if (cmdArgs.TryGetValue(reqArg, out var val))
                cliArgs.Add(val.ToString()!);

        // Add optional args as --flags
        foreach (var (k, v) in cmdArgs)
        {
            if (requiredArgs.Contains(k)) continue;
            if (v is bool b) { if (b) cliArgs.Add($"--{k}"); else cliArgs.Add($"--no-{k}"); }
            else if (v is List<string> list) { foreach (var item in list) { cliArgs.Add($"--{k}"); cliArgs.Add(item); } }
            else { cliArgs.Add($"--{k}"); cliArgs.Add(v.ToString()!); }
        }

        var psi = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in cliArgs) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start CLI");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;

        // Filter noise from output
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (string.IsNullOrEmpty(t)) continue;
            if (t.StartsWith("(node:") || t.StartsWith("(Use `node")) continue;
            if (t.Contains("chrome-devtools-mcp exposes") || t.Contains("Avoid sharing sensitive")) continue;
            if (t.Contains("Performance tools may send") || t.Contains("Google collects usage")) continue;
            if (t.Contains("For more details, visit") || t.Contains("debug, and modify")) continue;
            Console.WriteLine(t);
        }
    }

    // ── Allow prompt auto-clicker (UIAutomation + mouse) ────────────────────

    static void ClickAllowPrompt()
    {
        try
        {
            var script = @"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class ClickHelper {
    [DllImport(""user32.dll"")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport(""user32.dll"")] public static extern void mouse_event(uint f, uint x, uint y, uint d, int e);
    [DllImport(""user32.dll"")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x02, 0, 0, 0, 0);
        mouse_event(0x04, 0, 0, 0, 0);
    }
}
'@
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Chrome_WidgetWin_1')
foreach ($win in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cc)) {
    [ClickHelper]::SetForegroundWindow($win.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 300
    $bc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($btn in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
        $name = $btn.Current.Name
        if ($name -eq 'Allow') {
            $r = $btn.Current.BoundingRectangle
            $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
            [ClickHelper]::Click($x, $y)
            Write-Host ""Clicked Allow at ($x, $y)""
        }
        if ($name -eq 'Restore') {
            $r = $btn.Current.BoundingRectangle
            [ClickHelper]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
            Write-Host ""Clicked Restore""
        }
    }
}
";
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(15_000);
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Clicked"))
                        Console.Error.WriteLine(trimmed);
                }
            }
        }
        catch { }
    }

    // ── Print tool result ───────────────────────────────────────────────────

    static void PrintToolResult(JsonElement result)
    {
        if (result.TryGetProperty("structuredContent", out var structured))
        {
            Console.WriteLine(JsonSerializer.Serialize(structured, JsonOpts));
            return;
        }
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var t)) continue;
                var type = t.GetString();
                if (type == "text" && item.TryGetProperty("text", out var text))
                    Console.WriteLine(text.GetString());
                else if (type == "image" && item.TryGetProperty("data", out var data))
                {
                    var ext = ".png";
                    if (item.TryGetProperty("mimeType", out var mime))
                    {
                        var m = mime.GetString();
                        if (m?.Contains("jpeg") == true) ext = ".jpeg";
                        else if (m?.Contains("webp") == true) ext = ".webp";
                    }
                    var path = Path.Combine(Path.GetTempPath(), $"devtools-{Guid.NewGuid():N}{ext}");
                    File.WriteAllBytes(path, Convert.FromBase64String(data.GetString()!));
                    Console.WriteLine($"Image saved: {path}");
                }
            }
        }
    }

    // ── Arg parsing ─────────────────────────────────────────────────────────

    static (string command, Dictionary<string, object> args) ParseArgs(string[] argv)
    {
        if (argv.Length == 0) return ("help", new());
        var command = argv[0];
        var result = new Dictionary<string, object>();
        if (!Commands.TryGetValue(command, out var def)) return (command, result);

        var requiredArgs = def.Args.Where(kv => kv.Value.Required).Select(kv => kv.Key).ToList();
        int posIdx = 0;

        for (int i = 1; i < argv.Length; i++)
        {
            if (argv[i].StartsWith("--"))
            {
                var key = argv[i][2..];
                if (key.StartsWith("no-") && def.Args.ContainsKey(key[3..])) { result[key[3..]] = false; continue; }
                if (!def.Args.TryGetValue(key, out var argDef)) { Console.Error.WriteLine($"Unknown option: --{key}"); continue; }
                if (argDef.Type == "boolean")
                {
                    if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--") && bool.TryParse(argv[i + 1], out var bv)) { result[key] = bv; i++; }
                    else result[key] = true;
                }
                else if (argDef.Type == "array")
                {
                    var items = new List<string>();
                    while (i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) items.Add(argv[++i]);
                    result[key] = items;
                }
                else if (i + 1 < argv.Length) result[key] = ParseValue(argDef.Type, argv[++i]);
            }
            else if (posIdx < requiredArgs.Count)
            {
                var argName = requiredArgs[posIdx++];
                result[argName] = ParseValue(def.Args[argName].Type, argv[i]);
            }
        }
        return (command, result);
    }

    static object ParseValue(string type, string value) => type switch
    {
        "number" or "integer" => int.TryParse(value, out var n) ? n : double.TryParse(value, out var d) ? d : value,
        "boolean" => bool.Parse(value),
        _ => value,
    };

    static string? FindRepoRoot(string start)
    {
        var dir = start;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "package.json"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    // ── Help ────────────────────────────────────────────────────────────────

    static void PrintHelp()
    {
        Console.WriteLine("chrome-devtools.cs - Chrome DevTools CLI (.NET 11)");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run chrome-devtools.cs -- <command> [args] [--options]");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  (default)    Launches its own Chrome with your profile (fast, no login sessions)");
        Console.WriteLine("  --live       Connects to your running Chrome (preserves logged-in sessions)");
        Console.WriteLine("               Auto-clicks the Allow remote debugging prompt");
        Console.WriteLine();
        Console.WriteLine("Commands:");

        foreach (var cat in Commands.GroupBy(c => c.Value.Category).OrderBy(g => g.Key))
        {
            Console.WriteLine($"\n  [{cat.Key}]");
            foreach (var cmd in cat.OrderBy(c => c.Key))
            {
                var req = cmd.Value.Args.Where(a => a.Value.Required).Select(a => $"<{a.Key}>");
                var opt = cmd.Value.Args.Where(a => !a.Value.Required).Select(a => $"[--{a.Key}]");
                Console.WriteLine($"    {cmd.Key,-35} {cmd.Value.Description}");
                var usage = string.Join(" ", req.Concat(opt));
                if (usage.Length > 0) Console.WriteLine($"      {usage}");
            }
        }
    }

    static void PrintCommandHelp(string command, CommandDef def)
    {
        Console.WriteLine($"{command} - {def.Description}");
        Console.WriteLine($"Category: {def.Category}");
        if (def.Args.Count == 0) { Console.WriteLine("  No arguments."); return; }
        Console.WriteLine();
        foreach (var (name, arg) in def.Args)
        {
            var req = arg.Required ? " (required)" : "";
            Console.WriteLine($"  --{name,-25} {arg.Description}{req}");
            if (arg.Default != null) Console.WriteLine($"    {"",25} Default: {arg.Default}");
            if (arg.Enum != null) Console.WriteLine($"    {"",25} Values: {string.Join(", ", arg.Enum)}");
        }
    }
}
