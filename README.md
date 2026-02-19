# mcp-f12 — Go-to-Definition for C# via Roslyn

`FindDef` is a command-line tool that resolves C# symbol definitions using Roslyn's Semantic Model — the same analysis that powers F12 ("Go to Definition") in Visual Studio and VS Code. It is designed to be called by an AI agent or wrapped as an MCP tool.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later

## Installation

### Option A: Run from source

Clone this repository and run directly with `dotnet run`:

```bash
git clone <this-repo-url>
cd mcp-f12
dotnet run --project FindDef -- <args>
```

### Option B: Build a self-contained executable

```bash
cd mcp-f12/FindDef
dotnet publish -c Release -r win-x64 --self-contained -o ./publish
```

Replace `win-x64` with your target runtime (`linux-x64`, `osx-x64`, `osx-arm64`, etc.).

The resulting `FindDef.exe` (or `FindDef` on Linux/macOS) in `./publish` can be copied anywhere and run without the .NET SDK installed.

## Usage

```
FindDef <SolutionPath> <Mode> [Mode-specific args]
```

### Mode 1: Exact resolution by position (`-pos`)

Resolves the symbol at a specific file/line/column. This is the **recommended mode** for AI agents — it uses full Roslyn semantics to pick the correct overload or private method, just like pressing F12.

```
FindDef <SolutionPath> -pos <AbsoluteFilePath> <Line> <Column>
```

| Argument           | Description                                      |
|--------------------|--------------------------------------------------|
| `SolutionPath`     | Absolute or relative path to the `.sln` file    |
| `AbsoluteFilePath` | Absolute path to the C# file containing the call site |
| `Line`             | 0-indexed line number of the symbol              |
| `Column`           | 0-indexed column number of the symbol            |

**Example:** Resolve the symbol at line 126, column 16 of `BaseLoader.cs`:

```bash
FindDef ./DevProxy.sln -pos "/abs/path/DevProxy.Abstractions/Plugins/BaseLoader.cs" 126 16
```

**Output:**
```
DEFINITION_FOUND:
Symbol: DevProxy.Abstractions.Plugins.BaseLoader.LoadData(string)
File: C:\...\DevProxy.Abstractions\Plugins\BaseLoader.cs
Line: 58
Code:
protected abstract void LoadData(string fileContents);
```

### Mode 2: Fuzzy search by name (`-name`)

Searches the entire solution for all declarations matching the given name. Prints the containing type for each result so an AI agent can disambiguate private methods that exist in multiple classes.

```
FindDef <SolutionPath> -name <SymbolName>
```

**Example:**

```bash
FindDef ./DevProxy.sln -name LoadData
```

**Output:**
```
Found 12 matching definitions:
---
Context: MockRequestLoader.LoadData (Protected)
File: C:\...\DevProxy.Plugins\Mocking\MockRequestLoader.cs
Line: 23
---
Context: BaseLoader.LoadData (Protected)
File: C:\...\DevProxy.Abstractions\Plugins\BaseLoader.cs
Line: 58
...
```

> **Note:** Line numbers in the output are 0-indexed. Add 1 to match the line number shown in most editors.

## How to use with an AI agent

Add the following to your agent's system prompt or instructions:

```
I have a tool FindDef to navigate C# code by definition.

- Scenario 1: I see a method call and want to know what it does.
  Use the exact location of the call site to find the definition:
    FindDef ./MySolution.sln -pos "/abs/path/CurrentFile.cs" <line> <col>
  (line and col are 0-indexed)

- Scenario 2: I know a name but not where it lives.
    FindDef ./MySolution.sln -name "MyMethod"
  The output shows the containing type (e.g. WorkerA.DoWork vs WorkerB.DoWork),
  so use the Context field to pick the definition relevant to the file you're editing.
```

## Why this exists

Simple string search can't distinguish between two private methods with the same name in different classes. Roslyn's Semantic Model applies full C# language rules, so `-pos` mode returns the **exact** definition the compiler would resolve — not just a name match.
