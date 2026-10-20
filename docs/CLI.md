# TxtConverter CLI

`TxtConverter.exe` is a **dual-mode executable**: it starts the graphical interface when it is launched
without arguments, and switches to a full command line interface as soon as you pass any argument.

```
TxtConverter.exe                 -> graphical interface (unchanged behaviour)
TxtConverter.exe help            -> command line interface
TxtConverter.exe convert MyGame  -> command line interface
TxtConverter.exe gui             -> graphical interface (explicit)
```

The CLI is built from the same project and shipped in the same file as the GUI, so there is nothing extra
to download, install or keep in sync. The whole conversion pipeline (presets, compression, structure maps,
merged files, PDF reports, custom presets, AI Smart Select, settings, update checks) is available from
the command line, and both front ends share the same `settings.json`. A few interactions are intentionally
GUI-only; see [Intentional CLI differences](#intentional-cli-differences).

---

## Table of contents

- [Quick start](#quick-start)
- [Command overview](#command-overview)
- [Global options](#global-options)
- [Output, JSON and exit codes](#output-json-and-exit-codes)
- [`convert`](#convert)
- [`scan`](#scan)
- [`detect`](#detect)
- [`presets`](#presets)
- [`config`](#config)
- [`ai`](#ai)
- [`update`](#update)
- [`test`](#test)
- [Recipes](#recipes)
- [Intentional CLI differences](#intentional-cli-differences)
- [Notes and troubleshooting](#notes-and-troubleshooting)

---

## Quick start

```bat
REM 1. See what would be converted (writes nothing)
TxtConverter.exe scan "C:\Projects\MyGame" --stats

REM 2. Convert the whole project using the auto-detected preset
TxtConverter.exe convert "C:\Projects\MyGame"

REM 3. Only the scripts that matter, nothing else, preview first
TxtConverter.exe convert "C:\Projects\MyGame" --include "scripts/**" --dry-run
TxtConverter.exe convert "C:\Projects\MyGame" --include "scripts/**" --no-pdf
```

The result is always written to `<project>/_ConvertedToTxt` unless `--output` says otherwise.

---

## Command overview

| Command | Description |
|---|---|
| `convert [path]` | Full pipeline: scan → file selection → txt / merged / PDF reports |
| `scan [path]` | List (and count) the files a conversion would pick up |
| `detect [path]` | Show the preset the auto detection selects |
| `presets <list\|show\|save\|delete>` | Manage built-in and custom presets |
| `config <list\|get\|set\|reset\|path>` | Read/write the settings shared with the GUI |
| `ai <select\|models\|test>` | AI Smart Select plus provider checks |
| `update check [--open]` | Check GitHub for a newer release, optionally opening the release page |
| `test` | Self test: verifies the CLI and the whole conversion pipeline |
| `gui` | Start the graphical interface explicitly |
| `help [command]` | Show help (also `--help` on every command) |
| `version` | Show version information (also `--version`) |

If the path is omitted, the CLI uses the last folder converted from the GUI, otherwise the current
directory.

---

## Global options

Accepted by every command:

| Option | Description |
|---|---|
| `-l, --language <en\|ru>` | Language of the **generated report** for this run. The saved GUI language is never overwritten. |
| `--telemetry <on\|off>` | Enable/disable anonymous usage statistics for this run. `on` also overrides a saved `off` for this run. |
| `-y, --yes` | Answer yes to confirmation prompts (required for non-interactive use) |
| `--json` | Print one JSON document on stdout instead of text |
| `-q, --quiet` | Print results and errors only (no progress output) |
| `-v, --verbose` | Print detailed progress and diagnostics on stderr |
| `--no-color` | Disable colored output (also honoured: `NO_COLOR` environment variable) |
| `--pause` / `--no-pause` | Hold the console open with a `Press any key to return to the command prompt...` prompt after the command finished. Default: on. Automatically skipped when the output is redirected/piped or with `--json` / `--quiet`. |
| `-h, --help` | Help for the command |

### After the command finished

Every command leaves the console ready for the next one: once the result (for example the `Done in 3.4s`
summary of `convert`) is printed, the CLI waits for a keypress and then returns to the command prompt,
so nothing is lost when the executable is started from a shortcut or its own console window.

The wait is skipped automatically whenever blocking would break automation - when stdout/stdin is
redirected or piped, with `--json`, and with `--quiet`. To turn it off explicitly (for example in a
batch file that is started from an interactive console) pass `--no-pause`:

```bat
TxtConverter.exe convert "C:\Projects\MyGame" --no-pause
```

`help`, `--help` and `version` never wait - they print and return immediately.

---

## Output, JSON and exit codes

* **stdout** carries results (paths, selections, JSON documents) – pipe it freely.
* **stderr** carries progress, warnings and errors.
* `--json` emits exactly one JSON object on stdout and mutes all human readable text.
* Colors are used only when stdout is a real console; the `NO_COLOR` environment variable is respected.

| Exit code | Meaning |
|---|---|
| `0` | Success |
| `1` | Unexpected runtime failure |
| `2` | Invalid command line (unknown option, bad value, aborted confirmation) |
| `3` | Nothing to convert (the scan matched no files) |
| `4` | Preset, settings key, list file or directory not found |
| `5` | AI provider / network / API error |

```bat
TxtConverter.exe convert MyGame --quiet
if errorlevel 1 echo "conversion failed"
```

---

## `convert`

```
TxtConverter.exe convert [path] [options]
```

Scans the project, compresses every matching file and writes the results into the output folder.
Files that are not selected are still listed in the report, but only as stubs, exactly like in the GUI.

### Project options

| Option | Description |
|---|---|
| `-p, --preset <name>` | Preset to use. Default: auto-detected, otherwise the last used one. |
| `--ext <list>` | File extensions, comma separated (replaces the preset list). Repeatable. |
| `--ignore <list>` | Extra ignored folders, comma separated (added to the preset list). Repeatable. |
| `--exclude <list>` | Files/folders kept as stubs only (added to the preset list). Repeatable. |

> Exclusions use the same syntax as the GUI "Exclusions / Stubs" box: an entry without a slash matches a
> **file name** (`legacy_file.cs`), an entry with a slash matches a **path or folder**
> (`vendor/`, `src/old_api/`). Add the trailing slash for folders.
>
> `--exclude` is **additive**: the values are added to the preset exclusions and the global excluded
> paths, they never replace them.

### File selection (replaces the "Select Files..." window)

| Option | Description |
|---|---|
| `--select <all\|none>` | Start from every scanned file, or from none |
| `-i, --include <glob>` | Merge every matching file in full. Repeatable, comma separated values allowed. |
| `--include-file <list>` | Text file with paths/globs to merge in full |
| `--exclude-file <list>` | Text file with paths/globs to keep as stubs |

**Selection order** (deterministic, so scripts behave exactly the same every time):

1. start from *all scanned files minus the exclusions* (`--exclude` + preset exclusions + global exclusions),
2. reset to empty or full with `--select none` / `--select all`,
3. remove the files listed in `--exclude-file`,
4. add the files matched by `--include` globs,
5. add the files listed in `--include-file`,
6. if `--ai-task` is given, the AI selection replaces the whole set.

Glob syntax: `*` = any character except `/`, `**` = anything including `/`, `?` = single character.
A pattern without `/` (`*.cs`) matches the file name at any depth, a pattern with `/` (`src/**`) is
anchored at the project root. A trailing `/` means "everything inside".

List files contain one entry per line, `#` starts a comment, and entries may be paths or globs.

### Output options

| Option | Description |
|---|---|
| `-c, --compression <none\|smart\|maximum>` | Token compression mode for this run (default: the saved mode, see below) |
| `--structure` / `--no-structure` | Embed the file structure map in the report |
| `--compact-structure` / `--no-compact-structure` | Collapse stub entries in the structure map |
| `--merged` / `--no-merged` | Generate the single merged source file |
| `--pdf` / `--no-pdf` | Generate the PDF report |
| `--pdf-mode <standard\|compact\|extreme>` | PDF layout mode |
| `-o, --output <dir>` | Output directory (default: `<project>/_ConvertedToTxt`), relative paths are resolved against the project |
| `--dry-run` | Print what would be converted and where, write nothing |
| `--ai-task <text>` | Let the AI pick the relevant files before converting |
| `--ai-model <name>` | Override the AI model for this run |
| `--ai-budget <tokens>` | Override the AI thinking budget (Gemini only) |
| `--ai-system-prompt <file>` | Read the AI system prompt from a file |

### Compression modes

| Mode | Effect |
|---|---|
| `none` | Original file contents, verbose report headers (`FILE: path`) |
| `smart` | Removes excessive empty lines, normalizes line endings — safe for any project |
| `maximum` | Removes comments and formatting, flattens indentation and applies engine specific optimizers (Godot `@Repeated(n)` + type aliases, Unity YAML, C# compaction) |

**Three ways to select the mode**, from the most specific to the most general:

```bat
REM 1. per run (highest priority)
TxtConverter.exe convert MyGame --compression maximum

REM 2. for the context packed for the AI
TxtConverter.exe ai select MyGame --task "..." --compression smart

REM 3. stored default - shared with the GUI Conversion Settings dialog
TxtConverter.exe config set compression maximum
TxtConverter.exe config get compression
```

The stored value is only used when no `--compression` option is given, so scripts and one-off runs never
change the global default.

### Examples

```bat
REM Everything, default settings (identical to pressing START CONVERSION in the GUI)
TxtConverter.exe convert "C:\Projects\MyGame"

REM Only the player scripts, max compression, no PDF, everything else as stubs
TxtConverter.exe convert "C:\Projects\MyGame" --include "scripts/player/**" -c maximum --no-pdf

REM Preview first, then really convert into a custom folder
TxtConverter.exe convert "C:\Projects\MyGame" --include "*.cs" --dry-run
TxtConverter.exe convert "C:\Projects\MyGame" --include "*.cs" -o reports/latest

REM Check the plan and get machine readable output
TxtConverter.exe convert "C:\Projects\MyGame" --dry-run --json
```

---

## `scan`

```
TxtConverter.exe scan [path] [options]
```

Applies the preset, extension, ignore and exclusion rules and prints the resulting file list.
`[F]` marks files that would be merged in full, `[E]` marks files kept as stubs.
All project and selection options of `convert` are supported, plus:

| Option | Description |
|---|---|
| `--stats` | Print a summary grouped by extension |
| `--limit <n>` | Print at most `n` paths |
| `--ai-task <text>` | Preview the file set AI Smart Select would choose |
| `--ai-model <name>` | Override the AI model for this preview |
| `--ai-budget <tokens>` | Override the AI thinking budget (Gemini only) |
| `--ai-system-prompt <file>` | Read the AI system prompt from a file |

```bat
TxtConverter.exe scan . --stats
TxtConverter.exe scan . --select none --include "**/*.gd" --quiet
TxtConverter.exe scan . --ai-task "fix player movement"
```

---

## `detect`

```
TxtConverter.exe detect [path]
```

Runs the same heuristic as the GUI (`project.godot`, `Assets` + `ProjectSettings`, `*.sln`, `package.json`,
extension statistics, ...) and prints the preset name that would be selected.

```bat
TxtConverter.exe detect "C:\Projects\MyGame"
```

---

## `presets`

```
TxtConverter.exe presets <list|show|save|delete> [name] [options]
```

| Action | Description |
|---|---|
| `list` | Print every built-in and custom preset |
| `show <name>` | Print extensions, ignored folders and exclusions |
| `save <name>` | Create or update a custom preset |
| `delete <name>` | Delete a custom preset (asks for confirmation, use `--yes` in scripts) |

Options for `save`: `--ext`, `--ignore`, `--exclude`, `--from <preset>` (copy an existing preset first).
Built-in presets are protected exactly like in the GUI.

```bat
TxtConverter.exe presets list
TxtConverter.exe presets save "My Godot" --from "Godot Engine" --ignore "addons"
TxtConverter.exe presets delete "My Godot" --yes
```

---

## `config`

```
TxtConverter.exe config <list|get|set|reset|path> [key] [value]
```

The CLI and the GUI share `%LOCALAPPDATA%\oiuht54\TxtConverter\settings.json`, so a change made here is
visible in the interface immediately.

| Action | Description |
|---|---|
| `list` | Print every setting (API keys are masked) |
| `get <key>` | Print one setting |
| `set <key> <value>` | Change one setting and save it |
| `reset <key>` | Restore a setting to its factory default (currently `ai.system-prompt`) |
| `path` | Print the settings file location |

Keys are grouped in `config list`:

| Group | Keys |
|---|---|
| General | `language`, `telemetry`, `global-ignored-folders`, `global-excluded-paths`, `last-source-dir`, `last-preset`, `ai.enabled`, `ai.provider`, `ai.system-prompt` |
| Conversion settings | `compression` (`none`/`smart`/`maximum`), `generate-structure`, `compact-mode`, `generate-merged`, `generate-pdf`, `pdf-mode` |
| AI: Google Gemini | `ai.api-key`, `ai.model`, `ai.thinking-enabled`, `ai.thinking-budget` |
| AI: NVIDIA NIM | `nvidia.api-key`, `nvidia.model`, `nvidia.max-tokens`, `nvidia.temperature`, `nvidia.top-p`, `nvidia.reasoning-enabled` |
| AI: OpenAI compatible | `custom.endpoint`, `custom.api-key`, `custom.model`, `custom.max-tokens`, `custom.temperature`, `custom.top-p` |

The **Conversion settings** group is the CLI equivalent of the GUI "Conversion Settings" dialog: change a
value once and both front ends use it.

```bat
TxtConverter.exe config set ai.provider GoogleGemini
TxtConverter.exe config set ai.api-key "AIza..."
TxtConverter.exe config set global-ignored-folders "bin, obj, .git, venv"
TxtConverter.exe config set compression maximum
TxtConverter.exe config set language ru
TxtConverter.exe config reset ai.system-prompt
```

`config reset ai.system-prompt` is the CLI twin of the **Reset** button in the AI window: it restores the
factory prompt and prints it. Only `ai.system-prompt` can be reset; other keys are changed with `set`.

---

## `ai`

```
TxtConverter.exe ai <select|models|test> [path] [options]
```

### `ai select`

Describes a task, packs the project context and lets the configured provider pick the relevant files.

| Option | Description |
|---|---|
| `--task <text>` | **Required.** What the code should do |
| `--provider <gemini\|nvidia\|openai>` | Provider override for this call |
| `--api-key <key>` | API key override for this call |
| `--endpoint <url>` | Endpoint override (OpenAI compatible provider only) |
| `--model <name>` | Model override for this call |
| `--budget <tokens>` | Thinking token budget (Gemini only) |
| `-c, --compression <none\|smart\|maximum>` | Compression mode used to pack the project context |
| `--system-prompt <file>` | Read the system prompt from a file instead of the saved one |
| `--save <file>` | Write the selection as a reusable path list |
| `--debug` | Print the request JSON and raw API response JSON (stderr), or include them in `--json` output |
| `-p, --preset`, `--ext`, `--ignore`, `--exclude` | Same project options as `convert` |

Every override applies to **this call only** and never changes the saved settings. Precedence is always:
command line option, then the stored value for the selected provider, then the built-in default.
`--budget` and `--endpoint` are rejected with a usage error when they do not apply to the selected provider.

```bat
TxtConverter.exe ai select "C:\Projects\MyGame" --task "fix the player movement and collisions" --save selection.txt
TxtConverter.exe convert "C:\Projects\MyGame" --select none --include-file selection.txt

REM One-off provider/model override without touching settings.json
TxtConverter.exe ai select . --task "add multiplayer" --provider nvidia --api-key "nvapi-..." --model "meta/llama-3.1-70b-instruct"

REM Inspect the same request/response payloads the GUI debug tabs show
TxtConverter.exe ai select . --task "add multiplayer" --debug
TxtConverter.exe ai select . --task "add multiplayer" --debug --json
```

With `--debug --json` the single JSON document additionally contains `request`, `response` (the model's
text answer) and `rawResponse`. API keys are masked in the request debug text.

### `ai models` / `ai test`

```bat
TxtConverter.exe ai models
TxtConverter.exe ai test --model gemini-2.5-flash
TxtConverter.exe ai models --provider openai --endpoint http://localhost:11434/v1
```

Both accept `--provider <gemini|nvidia|openai>`, `--api-key`, `--endpoint`, `--model` to override the
stored configuration for a single call. `--endpoint` applies to the OpenAI compatible provider only.
Options that belong to `select` (`--task`, `--save`, `--compression`, `--system-prompt`, `--budget`,
`--debug`) are rejected for these actions. `ai test` sends a tiny "Hi" request and prints the answer.

---

## `update`

```
TxtConverter.exe update check [--open]
```

Queries the GitHub releases API (with the same fallbacks as the GUI) and prints the newest release.
Nothing is downloaded automatically. With `--open` the release page is opened in the default browser
when an update is available; when the installed version is already the latest, nothing is opened.

```bat
TxtConverter.exe update check
TxtConverter.exe update check --open
TxtConverter.exe update check --open --json
```

In JSON mode the document is still emitted; `--open` additionally reports `opened` (and `openError` when
the browser could not be started).

---

## `test`

```
TxtConverter.exe test [--keep] [--online]
```

Verifies the installation. The command builds a temporary sample Godot project in the temp folder and
runs the **production code** against it, then checks the results:

| Check | What it verifies |
|---|---|
| `runtime` | .NET runtime and version information |
| `settings` | `settings.json` is readable and the saved language is valid |
| `localization` | English **and** Russian resource dictionaries load (headless pack URIs) |
| `presets` | Built-in and custom presets are loaded |
| `auto-detect` | `project.godot` is detected as the `Godot Engine` preset |
| `scan` | Extension filter, ignored folders (`_ConvertedToTxt`, `.godot`) and the black list |
| `compression` | None/Smart/Maximum levels, including the Godot `@Repeated(n)` scene optimizer |
| `compression-mode` | Selecting a mode really changes the report (`none` vs `maximum` size and format) |
| `glob-matching` | `--include` patterns (`*.gd`, `scripts/**`, `**/*.tscn`, `vendor/`) |
| `ai-path-matching` | AI suggested paths are resolved to real project files |
| `ai-options` | AI overrides (`--model`, `--budget`, `--system-prompt`, provider, key) and the `convert` aliases are resolved; select-only flags are rejected for `models`/`test` |
| `exclusions` | Preset exclusions, `--exclude` and global exclusions are merged (never replaced) |
| `presets-show` | `presets show <unknown>` returns exit code 4 |
| `config-reset-prompt` | `config reset ai.system-prompt` returns the factory prompt |
| `debug-output` | `--debug` JSON contains `request`/`rawResponse`, the normal output does not |
| `update-options` | `update check --open` is accepted |
| `convert-merged` | Structure map, full file contents and stub markers in the merged report |
| `convert-pdf` | PDF is rendered and starts with the `%PDF` header |
| `update-check` | GitHub release query (only with `--online`) |

* The temporary project is deleted afterwards, use `--keep` to inspect it.
* No telemetry is sent and no network request is made unless `--online` is used.
* `--quiet` prints only failures (useful in CI), `--json` returns the full report as one document.
* Exit code `0` means every check passed.

```bat
REM quick check
TxtConverter.exe test

REM what CI runs
TxtConverter.exe test --quiet

REM full report, including the GitHub release check
TxtConverter.exe test --online --json
```

Typical output:

```
  [PASS] runtime           .NET 10.0.9 (win-x64)
  [PASS] localization      en + ru resource dictionaries loaded
  [PASS] compression       gd none 43B / smart 40B / max 39B, tscn 208B -> 50B
  [PASS] compression-mode  none 2 KB vs maximum 1 KB (Godot optimizer applied)
  [PASS] convert-merged    _SelfTest_Full_Source_code.txt (1927 B) + 8 txt files
  [PASS] convert-pdf       _SelfTest_Report.pdf (99 KB)
  [SKIP] update-check      network checks disabled (use --online)
All 18 checks passed (1 skipped).
```

---

## Recipes

### CI (GitHub Actions)

```yaml
- name: Prepare code for the AI reviewer
  shell: cmd
  run: TxtConverter.exe convert "${{ github.workspace }}" --preset "C# (.NET / Visual Studio)" --no-pdf --quiet
```

### PowerShell

```powershell
$files = TxtConverter.exe scan . --json --quiet | ConvertFrom-Json
"TxtConverter will merge $($files.filesMerged) of $($files.filesFound) files"
```

### Prepare only the part of a monorepo you care about

```bat
echo scripts/gameplay/** > keep.txt
echo assets/art/**     >> keep.txt
TxtConverter.exe convert . --select none --include-file keep.txt -c maximum
```

### Reproducible "AI picks the files" run

```bat
TxtConverter.exe ai select . --task "add multiplayer support" --save ai-selection.txt --quiet
TxtConverter.exe convert . --select none --include-file ai-selection.txt --quiet
```

### Watch a folder and re-convert on every change

```bat
:loop
TxtConverter.exe convert "C:\Projects\MyGame" --quiet --no-pause
timeout /t 30
goto loop
```

---

## Intentional CLI differences

These GUI interactions are deliberately not ported to the command line:

* **Folder picker, drag & drop, window chrome, themes** — the CLI takes a path argument.
* **Interactive file-selection tree** — use `--select`, `--include`, `--include-file` and
  `--exclude-file`; they are scriptable and deterministic.
* **Automatic update check on startup / update notification dialog** — automation calls
  `update check` (optionally with `--open`) explicitly.
* **Prewarming files and QuestPDF on app start** — a GUI startup optimization, not a conversion
  feature.
* **GUI UI language switching** — `--language` and `config set language` control the generated
  report; CLI console messages stay English by design.
* **Secret display** — `config list` / `config get` mask API keys; the GUI shows them in password
  fields. This is a deliberate security choice.
* **Window position, log panel, progress bar** — GUI presentation only.
* **`ai.enabled=false`** — the GUI hides AI selection; the CLI only warns and still runs an explicit
  `ai select` / `convert --ai-task` command. The explicit command is treated as the user's consent.
* **`help` and `version`** — always print human readable text, independent of `--json`.

---

## Notes and troubleshooting

* **Nothing is printed?** Results go to stdout, progress to stderr. Use `--quiet` to drop the progress
  noise or `--json` for machine consumption.
* **The folder and preset of the last successful conversion are remembered** (like in the GUI), so a later
  `convert` or `scan` without a path reuses them. Inspect or change them with `config get last-source-dir`
  / `config set last-preset "<name>"`.
* **No files found (exit code 3)?** The preset extension list did not match anything. Check with
  `TxtConverter.exe scan . --stats`, then override with `--ext "gd,tscn,tres"`.
* **Wrong project type?** The CLI picks the same preset as the GUI. Force it with `--preset`,
  or persist it with `TxtConverter.exe config set last-preset "Godot Engine"`.
* **Report language** follows the GUI setting; override for one run with `--language ru`.
  CLI messages themselves are always English.
* **Telemetry** follows the same anonymous statistics setting as the GUI; use `--telemetry off`
  to mute a single run, or `--telemetry on` to record a single run even when the saved setting is off.
* **The console waits after a command?** That is the default: the CLI holds the window open with
  `Press any key to return to the command prompt...` so the result stays readable. Pass `--no-pause`,
  or redirect the output, to get an immediate return to the prompt.
* **PDF rendering** requires no extra tools, the QuestPDF engine is bundled in the executable.
* **The GUI is unaffected**: `TxtConverter.exe` without arguments still opens the window, including the
  drag & drop, AI Smart Select and all settings dialogs.