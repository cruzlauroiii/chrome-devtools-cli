# chrome-devtools-cli

Pure .NET 11 Chrome DevTools CLI. Connects to Chrome via raw CDP WebSocket. No npm, no Playwright, no Selenium — just `dotnet run chrome-devtools.cs`.

## Setup

Chrome must be running with DevTools enabled. The CLI reads `DevToolsActivePort` from Chrome's user data directory and auto-clicks the "Allow remote debugging" prompt (DPI-aware).

```
dotnet run chrome-devtools.cs -- <command> [args] [--options]
```

## Serve Mode (Recommended)

Start serve mode once — it connects to Chrome, clicks Allow once, and accepts commands via HTTP on port 9333. All subsequent CLI calls auto-forward to serve (no reconnection overhead):

```bash
# Terminal 1: start serve (connects once)
dotnet run chrome-devtools.cs -- serve

# Terminal 2: commands are fast (forwarded via HTTP)
dotnet run chrome-devtools.cs -- list_pages
dotnet run chrome-devtools.cs -- take_screenshot --filePath screenshot.png
dotnet run chrome-devtools.cs -- evaluate_script "() => document.title"
```

Without serve mode, each command connects to Chrome independently (slower, clicks Allow each time).

## Commands

### Navigation

```bash
# List all open pages
dotnet run chrome-devtools.cs -- list_pages

# Select a page by number (from list_pages output)
dotnet run chrome-devtools.cs -- select_page 2

# Open a new tab
dotnet run chrome-devtools.cs -- new_page https://example.com

# Navigate current page
dotnet run chrome-devtools.cs -- navigate_page --type url --url https://example.com
dotnet run chrome-devtools.cs -- navigate_page --type back
dotnet run chrome-devtools.cs -- navigate_page --type forward
dotnet run chrome-devtools.cs -- navigate_page --type reload

# Close a page
dotnet run chrome-devtools.cs -- close_page 3
```

### Screenshots & Snapshots

```bash
# Screenshot current page
dotnet run chrome-devtools.cs -- take_screenshot
dotnet run chrome-devtools.cs -- take_screenshot --filePath ./my-screenshot.png
dotnet run chrome-devtools.cs -- take_screenshot --format jpeg --quality 80
dotnet run chrome-devtools.cs -- take_screenshot --fullPage true

# Screenshot with page targeting
dotnet run chrome-devtools.cs -- take_screenshot --pageId 2 --filePath page2.png

# Accessibility snapshot (lists all elements with UIDs for click/fill)
dotnet run chrome-devtools.cs -- take_snapshot
dotnet run chrome-devtools.cs -- take_snapshot --filePath snapshot.txt

# Desktop screenshot (no Chrome connection needed)
dotnet run chrome-devtools.cs -- screenshot_desktop
dotnet run chrome-devtools.cs -- screenshot_desktop --filePath desktop.png
```

### JavaScript Execution

```bash
# Run a function in the page context
dotnet run chrome-devtools.cs -- evaluate_script "() => document.title"
dotnet run chrome-devtools.cs -- evaluate_script "() => document.querySelectorAll('a').length"
dotnet run chrome-devtools.cs -- evaluate_script "async () => { const r = await fetch('/api'); return r.status; }"

# With page targeting
dotnet run chrome-devtools.cs -- evaluate_script --pageId 2 "() => location.href"
```

### Input & Interaction

```bash
# Click an element by UID (from take_snapshot)
dotnet run chrome-devtools.cs -- click <uid>
dotnet run chrome-devtools.cs -- click <uid> --dblClick true

# Hover over an element
dotnet run chrome-devtools.cs -- hover <uid>

# Fill an input or select element
dotnet run chrome-devtools.cs -- fill <uid> "text value"

# Type text character by character (simulates real typing)
dotnet run chrome-devtools.cs -- type_text "hello world"
dotnet run chrome-devtools.cs -- type_text "search query" --submitKey Enter

# Press a key or key combination
dotnet run chrome-devtools.cs -- press_key Enter
dotnet run chrome-devtools.cs -- press_key Control+a
dotnet run chrome-devtools.cs -- press_key Alt+F4

# Drag element to another element
dotnet run chrome-devtools.cs -- drag <fromUid> <toUid>

# Upload a file
dotnet run chrome-devtools.cs -- upload_file <uid> --filePath /path/to/file.png
```

### Debugging

```bash
# Console messages
dotnet run chrome-devtools.cs -- list_console_messages
dotnet run chrome-devtools.cs -- list_console_messages --pageId 2

# Network requests
dotnet run chrome-devtools.cs -- list_network_requests
```

### Page Control

```bash
# Resize page viewport
dotnet run chrome-devtools.cs -- resize_page 1920 1080

# Emulate a device
dotnet run chrome-devtools.cs -- emulate --device "iPhone 12"

# Handle browser dialogs (alert/confirm/prompt)
dotnet run chrome-devtools.cs -- handle_dialog accept
dotnet run chrome-devtools.cs -- handle_dialog dismiss
```

### Local Utilities (No Chrome Connection)

```bash
# Focus Chrome window
dotnet run chrome-devtools.cs -- focus_chrome

# Click the Allow remote debugging prompt
dotnet run chrome-devtools.cs -- allow

# Navigate via address bar (focus + type URL + Enter)
dotnet run chrome-devtools.cs -- navigate_address_bar --url https://example.com
```

## Targeting Pages

Use `--pageId N` with any command to target a specific page (number from `list_pages`):

```bash
dotnet run chrome-devtools.cs -- take_screenshot --pageId 2 --filePath page2.png
dotnet run chrome-devtools.cs -- evaluate_script --pageId 3 "() => document.title"
dotnet run chrome-devtools.cs -- list_console_messages --pageId 1
```

## Example: Full Test Flow

```bash
# Start serve mode
dotnet run chrome-devtools.cs -- serve &

# Open a page, wait, screenshot, check console, interact
dotnet run chrome-devtools.cs -- new_page https://myapp.com
dotnet run chrome-devtools.cs -- take_screenshot --filePath before.png
dotnet run chrome-devtools.cs -- list_console_messages
dotnet run chrome-devtools.cs -- take_snapshot --filePath snap.txt
dotnet run chrome-devtools.cs -- fill <inputUid> "test input"
dotnet run chrome-devtools.cs -- click <buttonUid>
dotnet run chrome-devtools.cs -- take_screenshot --filePath after.png
dotnet run chrome-devtools.cs -- list_console_messages
```

## Architecture

- `chrome-devtools.cs` — Entry point, command routing, serve mode HTTP server
- `CdpCommands.cs` — Command implementations (screenshot, evaluate, click, fill, etc.)
- `CdpSetup.cs` — Chrome connection, CDP WebSocket protocol, Allow prompt auto-click
- `CdpConstants.cs` — Protocol constants, CSS selectors, JS scripts

All communication uses raw Chrome DevTools Protocol over WebSocket. No external dependencies beyond .NET 11.
