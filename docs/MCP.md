# Daynote MCP server

`Daynote.Mcp` is a headless **stdio [Model Context Protocol](https://modelcontextprotocol.io) server**
that exposes your local Daynote notes to MCP clients (Claude Desktop, Claude Code, and other MCP
hosts) with read **and** write tools. It reads the same database the app uses —
`%LocalAppData%\Daynote\daynote.db` — so notes created or edited through an AI assistant show up in
the Daynote app, and vice versa.

It is a separate console app (`src/Daynote.Mcp`), not part of the Store package. It uses the official
`ModelContextProtocol` .NET SDK (2.0.0) over stdio.

## Tools

| Tool | Parameters (required\*) | What it does |
|------|--------------------------|--------------|
| `search_notes` | `query`\*, `limit` (default 20) | Full-text search across your notes; returns date, id, title, snippet. |
| `get_notes_for_date` | `date`\* (`YYYY-MM-DD`) | Lists the notes on one day (id, title, body, favorite). |
| `list_recent_notes` | `limit` (default 20) | Most recent notes first (id, date, title, snippet, favorite). |
| `create_note` | `date`\*, `body`\*, `title` | Creates a note on a date; returns the new note id. |
| `update_note` | `noteId`\*, `body`, `title` | Updates a note's body and/or title (unspecified fields keep their value). |
| `delete_note` | `noteId`\* | Deletes a note. |

Dates are ISO `YYYY-MM-DD`. `noteId` is the GUID returned by `create_note` / `search_notes` /
`list_recent_notes`. Invalid input and save conflicts come back as tool errors, not crashes.

## Build

```powershell
# Framework-dependent (needs the .NET 10 runtime on the machine):
dotnet build src\Daynote.Mcp\Daynote.Mcp.csproj -c Release
#   -> src\Daynote.Mcp\bin\Release\net10.0-windows10.0.19041.0\Daynote.Mcp.exe

# Or a self-contained single folder (no runtime dependency):
dotnet publish src\Daynote.Mcp\Daynote.Mcp.csproj -c Release -r win-x64 --self-contained true -o dist\daynote-mcp
#   -> dist\daynote-mcp\Daynote.Mcp.exe
```

## Register with a client

**Claude Desktop** — add to `claude_desktop_config.json`
(`%AppData%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "daynote": {
      "command": "C:\\path\\to\\Daynote.Mcp.exe"
    }
  }
}
```

Restart Claude Desktop; "daynote" appears in the tools list.

**Claude Code** — one command:

```powershell
claude mcp add daynote -- "C:\path\to\Daynote.Mcp.exe"
```

**Run via the SDK host instead of the apphost exe** (if launching the `.exe` is blocked by an endpoint
policy), point the client at `dotnet` with the DLL:

```json
{
  "mcpServers": {
    "daynote": {
      "command": "dotnet",
      "args": ["C:\\path\\to\\Daynote.Mcp.dll"]
    }
  }
}
```

## Notes

- **Data location** — defaults to `%LocalAppData%\Daynote`. Set the `DAYNOTE_DATA_ROOT` environment
  variable (in the client's server config `env`) to point at a different root, e.g. a disposable test
  database.
- **Running alongside the app** — safe. SQLite (WAL mode) allows the MCP server to read while the app
  is open, and serializes writers, so concurrent edits don't corrupt data. A rare write that races the
  app is returned as a tool error; just retry.
- **stdio hygiene** — the server logs only to stderr; stdout carries the JSON-RPC stream, so never add
  `Console.WriteLine` to stdout in this project.
