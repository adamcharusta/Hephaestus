| Cell | Exit | Evidence | Executable configuration that executed | Instructions the model followed |
| --- | --- | --- | --- | --- |
| claude-api-key-bare | 0 | complete | claude-settings-env | none |
| claude-api-key-control-apikeyhelper | 0 | complete | claude-localsettings-apiKeyHelper claude-localsettings-hook-SessionStart claude-mcp-project claude-settings-env claude-settings-hook-PostToolUse claude-settings-hook-PreToolUse claude-settings-hook-SessionEnd claude-settings-hook-SessionStart claude-settings-hook-Stop claude-settings-hook-UserPromptSubmit | claude-instruction-CLAUDE.md |
| claude-api-key-control | 0 | complete | claude-localsettings-hook-SessionStart claude-mcp-project claude-settings-env claude-settings-hook-PostToolUse claude-settings-hook-PreToolUse claude-settings-hook-SessionEnd claude-settings-hook-SessionStart claude-settings-hook-Stop claude-settings-hook-UserPromptSubmit | claude-instruction-CLAUDE.md |
| claude-api-key-flags | 0 | complete | none | none |
| claude-api-key-hardened-apikeyhelper | 0 | complete | none | none |
| claude-api-key-hardened-bare | 0 | complete | none | none |
| claude-api-key-hardened | 0 | complete | none | none |
| claude-api-key-safe-mode-user-settings | 0 | complete | none | none |
| claude-api-key-safe-mode | 0 | complete | claude-settings-env | none |
| claude-subscription-bare | not-run | --bare never reads subscription credentials | | |
| claude-subscription-control | 0 | complete | claude-localsettings-hook-SessionStart claude-mcp-project claude-settings-env claude-settings-hook-PostToolUse claude-settings-hook-PreToolUse claude-settings-hook-SessionEnd claude-settings-hook-SessionStart claude-settings-hook-Stop claude-settings-hook-UserPromptSubmit | claude-instruction-CLAUDE.md |
| claude-subscription-flags | 0 | complete | none | none |
| claude-subscription-hardened-bare | not-run | --bare never reads subscription credentials | | |
| claude-subscription-hardened | 0 | complete | none | none |
| claude-subscription-safe-mode-user-settings | 0 | complete | none | none |
| claude-subscription-safe-mode | 0 | complete | claude-settings-env | none |
| codex-api-key-control-trusted | 0 | complete | codex-config-env codex-hooks-PostToolUse codex-hooks-PreToolUse codex-hooks-SessionStart codex-hooks-Stop codex-hooks-UserPromptSubmit codex-mcp-project | codex-instruction-AGENTS.md |
| codex-api-key-default-tool-inventory | 0 | complete | not planted | not planted |
| codex-api-key-default | 0 | complete | none | codex-instruction-AGENTS.md |
| codex-api-key-hardened-no-project-doc | 0 | complete | none | none |
| codex-api-key-hardened-tool-inventory | 0 | complete | not planted | not planted |
| codex-api-key-hardened | 0 | complete | none | codex-instruction-AGENTS.md |
| codex-subscription-control-trusted | 0 | complete | codex-config-env codex-hooks-PostToolUse codex-hooks-PreToolUse codex-hooks-SessionStart codex-hooks-Stop codex-hooks-UserPromptSubmit codex-mcp-project | codex-instruction-AGENTS.md codex-skill |
| codex-subscription-default-tool-inventory | 0 | complete | not planted | not planted |
| codex-subscription-default | 0 | complete | none | codex-instruction-AGENTS.md codex-skill |
| codex-subscription-hardened-no-project-doc | 0 | complete | none | codex-skill |
| codex-subscription-hardened-tool-inventory | 0 | complete | not planted | not planted |
| codex-subscription-hardened | 0 | complete | none | codex-instruction-AGENTS.md codex-skill |

Planted executable configuration that executed in no control run, for which
a variant leaving no canary therefore shows nothing:

- `claude-plugin-hook-SessionStart`
- `claude-plugin-mcp`
- `codex-plugin-hook-SessionStart`
- `codex-plugin-mcp`
- `codex-config-notify`
