# CLI ↔ GUI Feature Parity — Gap Analysis and Implementation Plan

Date: 2026-10-20  
Scope: `docs/CLI.md`, `TxtConverter/Cli/**`, and all GUI windows under `TxtConverter/Views/**`.

## 1. Verdict

The CLI already covers the **entire conversion pipeline** and the normal settings/preset workflows:

- scanning, auto-detection, preset application, extension/ignore/exclusion rules;
- file selection with `--select`, `--include`, `--include-file`, `--exclude-file`;
- compression, structure map, compact mode, merged file, PDF + PDF mode, output directory;
- preset listing/creation/deletion, shared settings, AI model listing/connection test, update check;
- JSON output, quiet/verbose modes, exit codes and a self test.

It is **not 100 % feature-equivalent** yet. The main gaps are:

1. **`ai select` ignores several options it advertises.** `--model`, `--budget` and
   `--system-prompt` are accepted but never read; `--provider`, `--api-key` and `--endpoint` are
   accepted but ignored by `select` as well.
2. **The AI debug views are missing.** The GUI has “Request” and “Response” tabs; the CLI only prints
   the model's text response with `--verbose`, not the request JSON or the raw API JSON.
3. **The AI system prompt cannot be reset to the factory default** from the CLI (the GUI has a
   “Reset” button).
4. **The update release page cannot be opened** from the CLI (the GUI's update window has a download
   button that opens `HtmlUrl`).
5. **Two smaller bugs:** `--exclude` replaces preset exclusions instead of adding to them (the docs
   and GUI semantics are additive), and `presets show <unknown>` succeeds with empty data instead of
   returning exit code 4.
6. **Small consistency gaps:** `scan` cannot preview an AI selection, general help does not list
   `gui` / `version`, and `--telemetry on` cannot override a disabled saved telemetry setting.

Some GUI behaviour is intentionally not applicable to a command line tool (folder picker, drag &
drop, interactive file tree, window chrome, automatic update check on startup). Those are not
counted as missing functionality; see §6.

---

## 2. Evidence from the current code

### 2.1 AI selection flags are ignored

`AiCommand` declares these options for every AI action:

- `--provider`, `--api-key`, `--endpoint` (`TxtConverter/Cli/ProjectOptions.cs:62-76`);
- `--task`, `--model`, `--budget`, `--compression`, `--system-prompt`, `--save`
  (`TxtConverter/Cli/Commands/AiCommand.cs:25-32`).

`AiSelectionService` however reads only the `ai-*` names used by `convert`:

```csharp
// TxtConverter/Cli/AiSelectionService.cs:36-37
string model  = args.Value("ai-model")  ?? prefs.GetAiModel();
int    budget = args.IntValue("ai-budget") ?? prefs.GetAiThinkingBudget();

// TxtConverter/Cli/AiSelectionService.cs:81-83
string? promptFile = args.Value("ai-system-prompt");
```

and always creates the client from the saved settings:

```csharp
// TxtConverter/Cli/AiSelectionService.cs:53
IAiClient client = AiClientFactory.CreateClient();
```

Consequences for `ai select`:

| Option | Documented at | Current effect |
|---|---|---|
| `--model` | `docs/CLI.md:355` | silently ignored |
| `--budget` | `docs/CLI.md:356` | silently ignored |
| `--system-prompt` | `docs/CLI.md:358` | silently ignored |
| `--provider` | not advertised for `select`, but accepted | silently ignored |
| `--api-key` | not advertised for `select`, but accepted | silently ignored |
| `--endpoint` | not advertised for `select`, but accepted | silently ignored |

`convert --ai-task` uses `--ai-model`, `--ai-budget` and `--ai-system-prompt`
(`TxtConverter/Cli/Commands/ConvertCommand.cs:33-36`), so those work there — but the same service is
used by two commands with two different option vocabularies.

### 2.2 AI debug output

The GUI stores and shows both `RequestJson` and `RawResponseJson`
(`TxtConverter/Views/AiTaskWindow.xaml.cs:139-147`; tabs in `AiTaskWindow.xaml:194-210`).

The CLI already captures both values in `AiSelectionOutcome`:

```csharp
// TxtConverter/Cli/AiSelectionService.cs:17-18
public string RawResponse { get; init; } = "";
public string RequestJson { get; init; } = "";
```

but they are never printed or included in the JSON output. Only the text response is shown with
`--verbose` (`AiSelectionService.cs:60-61`).

### 2.3 AI system prompt reset

`SettingsWindow`/`AiTaskWindow` have a reset-to-default action
(`TxtConverter/Views/AiTaskWindow.xaml.cs:94-103`, calling
`PreferenceManager.ResetAiSystemPrompt()`). The CLI can set the prompt through
`config set ai.system-prompt <value>` or a per-run file, but there is no reset action
(`TxtConverter/Cli/Commands/ConfigCommand.cs:44-159`).

### 2.4 Update release page

The GUI's update dialog opens the release page (`TxtConverter/Views/UpdateNotificationWindow.xaml.cs:23-33`).
`update check` only prints the URL and notes (`TxtConverter/Cli/Commands/UpdateCommand.cs:36-58`);
there is no `--open` option.

### 2.5 `--exclude` replaces preset exclusions

Docs say the value is **added** to the preset list (`docs/CLI.md:151`, selection order at
`docs/CLI.md:168`). The resolver uses only the CLI values when the option is present:

```csharp
// TxtConverter/Cli/ProjectResolver.cs:54-58
string localExclusions = args.Has("exclude")
    ? string.Join(",", args.CsvValues("exclude"))
    : PresetManager.Instance.GetExclusionsFor(project.PresetName);
```

This matters for custom presets with exclusions; all built-in presets currently have empty
exclusion lists, which hides the bug in casual testing.

### 2.6 `presets show` does not validate the name

`Show()` calls `RequireName()` and then `Describe()` without checking `HasPreset`
(`TxtConverter/Cli/Commands/PresetsCommand.cs:62-79`). Unknown names therefore print an empty
preset and return success instead of exit code 4 (`ExitCodes.NotFound`).

### 2.7 Smaller consistency issues

- **`scan` cannot preview AI selection.** `scan` supports the project/selection options but not
  `--ai-task` (`TxtConverter/Cli/Commands/ScanCommand.cs:19-22`), so a user cannot preview the file
  set that `convert --ai-task` would produce.
- **General help omits `gui` and `version`.** `HelpText.PrintGeneral` lists only
  `CliLauncher.Commands` (`TxtConverter/Cli/HelpText.cs:26-30`), while `docs/CLI.md:69-71` lists
  both.
- **`--telemetry on` cannot override a saved “off”.** The CLI sets
  `TelemetryService.Suppress = false` (`CliLauncher.cs:120-139`), but `TrackEvent` still checks
  `GetTelemetryEnabled()` and returns when the saved setting is `false`
  (`TxtConverter/Services/TelemetryService.cs:47-50`). The option description says “for this run”.

---

## 3. Coverage matrix

| GUI capability | CLI equivalent | Status |
|---|---|---|
| Choose folder / last folder | `[path]`, `last-source-dir` | Covered |
| Drag & drop | — | N/A (GUI-only interaction) |
| Auto-detect preset | `detect`, automatic resolver | Covered |
| Preset list / inspect | `presets list`, `presets show` | Covered (show bug §2.6) |
| Create / edit / copy preset | `presets save [--from]` | Covered |
| Delete preset + confirmation + built-in protection | `presets delete`, `--yes` | Covered |
| Per-run extensions | `--ext` | Covered |
| Per-run ignored folders | `--ignore` | Covered |
| Per-run exclusions / stubs | `--exclude` | **Partial** (§2.5) |
| Global ignored folders / exclusions | `config set global-…` | Covered |
| Rescan / scan | `scan` | Covered |
| Select all / none | `--select all\|none` | Covered |
| Interactive selection tree | `--include`, `--include-file`, `--exclude-file` | Functional equivalent |
| Compression mode | `-c/--compression`, `config set compression` | Covered |
| Structure map + compact mode | `--structure`, `--compact-structure`, config keys | Covered |
| Merged file | `--merged`, config key | Covered |
| PDF + PDF mode | `--pdf`, `--pdf-mode`, config keys | Covered |
| Output folder | `-o/--output` | Covered (CLI also supports custom paths) |
| AI Smart Select | `ai select`, `convert --ai-task` | **Partial** (§2.1) |
| AI provider / key / endpoint / model | `config set ai.…`, `ai models/test` | Covered; **select does not honour per-call overrides** |
| AI model list | `ai models` | Covered |
| AI connection test | `ai test` | Covered |
| AI system prompt edit/persist | `config set ai.system-prompt`, `--system-prompt` | **Partial** (no reset, per-run flag broken for `ai select`) |
| AI request/response debugging | `--verbose` shows text response only | **Missing** (§2.2) |
| Apply AI selection | `ai select --save` + `convert --include-file`, or `convert --ai-task` | Covered |
| AI enabled toggle | `config set ai.enabled` | Covered (CLI only warns if disabled) |
| Report language | `--language`, `config set language` | Covered; CLI console messages are English by design |
| Telemetry | `config set telemetry`, `--telemetry` | **Partial** (§2.7) |
| Update check | `update check` | Covered |
| Release notes / download page | `update check` notes + URL | **Partial** (no `--open`) |
| Version / about | `version` | Covered |
| Self test | `test` | Covered (CLI extra) |
| Machine-readable output / CI | `--json`, `--quiet`, exit codes | Covered (CLI extra) |

---

## 4. Implementation plan

### Phase 0 — Fix advertised AI behaviour and correctness (highest priority)

#### 0.1 Unify AI request option resolution

**Files:** `TxtConverter/Cli/AiSelectionService.cs`,
`TxtConverter/Cli/Commands/AiCommand.cs`,
`TxtConverter/Cli/Commands/ConvertCommand.cs`,
`TxtConverter/Services/Ai/AiClientFactory.cs`,
`TxtConverter/Cli/ProjectOptions.cs`.

Introduce one internal resolver (for example `AiRequestOptions.Resolve(args, prefs)`) that returns:

- provider (`--provider` or saved `ai.provider`);
- API key (`--api-key` or the configured key for the selected provider);
- model (`--model` for `ai select`, `--ai-model` for `convert`, or the configured model);
- endpoint (`--endpoint` for OpenAI-compatible runs, or configured endpoint);
- thinking budget (`--budget` / `--ai-budget` or saved budget);
- system prompt (`--system-prompt` / `--ai-system-prompt` file, or saved default);
- compression for the packed context.

Then use it from both `AiSelectionService` and `AiCommand.CreateClient` so all AI actions share the
same precedence rules.

Acceptance criteria:

- `ai select --model …` changes the model sent to the provider.
- `ai select --budget …` changes the Gemini thinking/max-token setting.
- `ai select --system-prompt file.txt` uses the file contents.
- `ai select --provider nvidia --api-key … --model …` uses that provider/key/model for this call
  without changing saved settings.
- `convert --ai-task` continues to honour `--ai-model`, `--ai-budget`, `--ai-system-prompt`.
- If a per-call provider/key/endpoint override is intentionally not supported for a command, the
  parser must reject it with a clear usage error rather than accept and ignore it.

#### 0.2 Add a provider-aware client factory for selection

`AiClientFactory.CreateSpecific` is fine for “models” and “test connection”, but it uses neutral
Gemini/NVIDIA parameters (`AiClientFactory.cs:39-66`). A selection run that overrides the provider
must still use the configured thinking/reasoning/temperature/top-p settings of that provider.

Add a method such as:

```csharp
CreateForSelection(
    AiProvider provider,
    string apiKey,
    string model,
    string? endpoint,
    bool thinkingEnabled,
    int thinkingBudget)
```

Use the stored provider-specific values for the selected provider, with the per-call key/model/
endpoint taking precedence. Keep `CreateSpecific` unchanged for model listing and connection tests.

#### 0.3 Fix `--exclude` to be additive

**File:** `TxtConverter/Cli/ProjectResolver.cs`.

Start from the preset exclusions and union the CLI values, then union global exclusions:

```text
preset exclusions ∪ --exclude values ∪ global excluded paths
```

Add a regression check to `test` (see Phase 3).

#### 0.4 Validate `presets show`

**File:** `TxtConverter/Cli/Commands/PresetsCommand.cs`.

Check `HasPreset(preset)` before `Describe` and throw
`CliException.NotFound($"Unknown preset '{preset}'.")`.

### Phase 1 — Complete the AI and update parity

#### 1.1 AI debug output

**Files:** `AiSelectionService.cs`, `AiCommand.cs`, optionally `ConvertCommand.cs`,
`docs/CLI.md`.

- With `--verbose`, print the request JSON and raw API response JSON captured in
  `AiSelectionOutcome` (to stderr, with the existing secret masking).
- Add `--debug` (or `--show-request` / `--show-response`) for users who want the debug blocks
  without full verbose progress. Prefer one `--debug` switch to keep the option surface small.
- In `--json --debug`, include `request` and `rawResponse` fields in the single JSON document.
- Never print an unmasked API key; the clients already mask keys in their debug strings.

Acceptance criteria:

- `ai select … --verbose` shows the same information as the GUI request/response tabs.
- `ai select … --json --debug` contains both debug payloads.
- Without `--debug`/`--verbose`, the normal output stays unchanged.

#### 1.2 Reset the AI system prompt

**Files:** `ConfigCommand.cs`, `docs/CLI.md`.

Add an explicit action, for example:

```bat
TxtConverter.exe config reset ai.system-prompt
```

It should call `PreferenceManager.ResetAiSystemPrompt()` and return the default prompt in text/JSON
output. Reject other keys with a clear message unless a generic reset is intentionally implemented.
Optionally add an alias `TxtConverter.exe ai prompt reset` if the command grouping feels better; one
canonical form is enough.

#### 1.3 Open the release page

**Files:** `UpdateCommand.cs`, `docs/CLI.md`.

Add `--open` to `update check`:

```bat
TxtConverter.exe update check --open
```

- If an update is available and `Release.HtmlUrl` is set, open it with
  `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`.
- If no update is available, print the normal “latest version” result and do not open anything.
- In `--json` mode still emit the JSON document; `--open` is an explicit side effect and should be
  honoured (or documented as ignored in JSON mode, but honouring is more predictable).

#### 1.4 Optional: AI selection preview with `scan`

If previewing AI selection is important for automation, add the same AI options to `scan` and run
`AiSelectionService` before printing. This lets users run:

```bat
TxtConverter.exe scan . --ai-task "fix player movement" --dry-run
```

This is lower priority because `convert --dry-run --ai-task …` already provides the same preview
without writing.

### Phase 2 — Consistency and polish

- **General help:** list `gui` and `version` alongside the other commands in
  `HelpText.PrintGeneral`, or add them as help-only pseudo-commands. Match `docs/CLI.md`.
- **`--telemetry on`:** decide the contract. To match the option description (“for this run”),
  introduce an explicit per-run override in `TelemetryService` so `--telemetry on` can re-enable
  events even when the saved setting is off; alternatively change the docs to say the saved setting
  always wins. Prefer the explicit override.
- **Warn on unused AI options:** if `--ai-model`, `--ai-budget` or `--ai-system-prompt` is passed
  to `convert` without `--ai-task`, emit a warning instead of silently ignoring it.
- **JSON consistency:** consideration for `version`/`help` (currently plain text even with
  `--json`) and for mutating commands that report success only on stderr (`presets save`,
  `config set`). Not required for GUI parity, but useful for scripts.
- **AI enabled semantics:** the GUI hides AI selection when `ai.enabled=false`; the CLI only warns
  and proceeds. Document this as an intentional override, or add `--force` and fail by default.
  Document whichever behaviour is chosen.

### Phase 3 — Tests and documentation

#### 3.1 Self-test additions

Extend `TxtConverter/Cli/Commands/TestCommand.cs` with checks that do not need the network:

- `ai-options`: parse `ai select --model … --budget … --system-prompt … --provider …` and assert
  the resolved request options; this catches the current silent-ignore bug.
- `exclusions`: verify that preset exclusions and `--exclude` are both applied.
- `presets-show`: unknown preset returns exit code 4.
- `config-reset-prompt`: reset returns `ProjectConstants.DefaultAiSystemPrompt`.
- `debug-output`: with a fake/offline result, verify that `--debug`/`--json` includes request and
  response fields; do not call a real provider in the default self test.
- `update-options`: parse `update check --open` and verify the command option exists.

Keep the self test network-free by default, as it is today.

#### 3.2 Documentation

Update `docs/CLI.md`:

- AI select table: `--model`, `--budget`, `--system-prompt`, and (if supported) provider/key/endpoint
  overrides.
- AI select examples with per-call overrides and `--debug`.
- `config reset ai.system-prompt`.
- `update check --open`.
- Clarify `--exclude` is additive.
- Add a short “Intentional CLI differences” section with the items from §6.
- Update the command overview/help examples for `gui` and `version` if the help output changes.

Update `README.md` only for user-visible additions (per-call AI overrides, debug output, `--open`),
and keep the existing “same file, same features” claim qualified by the intentional differences.

---

## 5. Suggested commit sequence

1. **fix(cli): honour ai select model/budget/system-prompt and provider overrides**
   - resolver + client factory + tests.
2. **fix(cli): make --exclude additive and validate presets show**
   - resolver + presets command + self-test checks.
3. **feat(cli): ai debug output and system prompt reset**
   - service/command/config + docs.
4. **feat(cli): update check --open**
   - update command + docs.
5. **chore(cli): help/telemetry/scan polish and documentation**
   - optional items, final docs and self-test pass.

Keeping the first two commits separate makes it easy to verify the behavioural fixes before adding
new options.

---

## 6. Intentional differences (not missing features)

These are GUI-only interactions or deliberate design decisions; document them rather than porting
them:

- **Folder picker / drag & drop / window chrome / themes** — the CLI takes a path argument.
- **Interactive file-selection tree** — the CLI uses `--select`, `--include`, `--include-file` and
  `--exclude-file`, which are scriptable and deterministic.
- **Automatic update check on startup / update notification dialog** — automation should call
  `update check` explicitly.
- **Prewarming files and QuestPDF on app start** — a startup performance optimization, not a
  user-facing conversion feature.
- **GUI UI language switching** — `--language` and `config set language` control the generated
  report; CLI console messages remain English by design, as documented.
- **Secret display** — `config list/get` masks API keys, while the GUI shows them in password
  fields. This is a deliberate security choice; add a reveal option only if a concrete need exists.
- **Window position / log panel / progress bar** — GUI presentation only.

---

## 7. Definition of done

- Every option shown by `TxtConverter.exe help ai` and `docs/CLI.md` has an observable effect, or is
  rejected with a clear usage error.
- `ai select` supports per-call model, budget, system prompt, provider, key and endpoint overrides.
- AI debug information is available from the CLI, with secrets masked.
- `config reset ai.system-prompt` and `update check --open` work.
- `--exclude` is additive and `presets show <unknown>` returns exit code 4.
- `TxtConverter.exe test --json` passes all existing checks plus the new ones without network access.
- `docs/CLI.md` and `README.md` describe the exact behaviour, including intentional differences.
- No changes to GUI behaviour and no change to the shared `settings.json` format.
