# SmarterSystems Claude Service

An HTTP wrapper around the Claude Code CLI. It lets n8n workflows (or any other
HTTP client) talk to Claude through a Claude subscription instead of a
pay-as-you-go API key, and it exposes the agent loop, so the model can use tools
and keep conversations alive across calls.

## Why this exists

The Anthropic Messages API expects an API key. If you use one, n8n's built-in
Anthropic node already does the job and this service is unnecessary.

This service targets the other case: authenticating with a Claude subscription.
Anthropic permits that through Claude Code and the Agent SDK, which includes the
non-interactive `claude -p` command this service calls. Subscription credentials
used directly against the Messages API are rejected, so the CLI is the supported
path.

## Requirements

- .NET 10
- Claude Code CLI installed on the host and authenticated
- A Claude Pro, Max, Team, or Enterprise plan

## Authentication

The service never handles credentials itself. It shells out to the `claude`
executable, which authenticates on its own. Two ways to set that up on the host:

- Interactive: run `claude` once and log in.
- Headless (servers, containers): run `claude setup-token` on a machine with a
  browser, then set the printed token as an environment variable for the service
  process:

```
CLAUDE_CODE_OAUTH_TOKEN=<token>
```

Do not put credentials in `appsettings.json`. Nothing reads them from there. The
runner deliberately removes `ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN` from
the child process environment, because an API key present in the environment
would silently take precedence over the subscription credential and bill per
token.

## Configuration

All keys are optional.

| Key | Default | Meaning |
| --- | --- | --- |
| `ClaudeCli:ExecutablePath` | `claude` | Path to the CLI, resolved via `PATH` when not set |
| `ClaudeCli:WorkingDirectory` | `agent-workspace` next to the app | Directory the CLI runs in, also where uploads are stored |
| `ClaudeCli:TimeoutSeconds` | `600` | Kills the process tree when a call takes longer |

The working directory matters: Claude Code scopes session lookup to it, so it has
to stay stable across restarts.

## Endpoints

### `POST /Agent/message`

```json
{
  "message": "Summarise this text: ...",
  "model": "claude-haiku-4-5-20251001",
  "sessionId": "00000000-0000-0000-0000-000000000000",
  "fileId": "optional, from /Agent/file",
  "schema": "optional JSON schema as a string"
}
```

`model` is required, and so is at least one of `message` or `fileId`.

```json
{ "message": "...", "stopReason": "end_turn", "hasError": false, "error": null }
```

### `POST /Agent/file`

```json
{ "file": "<base64>", "fileName": "numbers.txt", "mediaType": "optional" }
```

```json
{ "fileId": "60ad5fbeee71475e8ac37274e4f6dc6d.txt", "hasError": false, "error": null }
```

Pass the returned `fileId` to `/Agent/message`. The agent reads the file with the
`Read` tool, which is the only tool enabled, and only when a file is referenced.

## Sessions

The service holds no conversation state. Claude Code stores sessions on disk, so
they survive a restart of this service.

- No `sessionId`: one-shot request.
- Same `sessionId` again: continues that conversation.
- New `sessionId`: starts a new one.

The caller owns the id and it has to be a UUID. Claude Code rejects anything else.
n8n execution ids are numeric, so generate a UUID in the workflow and carry it
through the steps, or store one per entity if a conversation should outlive a run.

## Structured output

With `schema`, the CLI is invoked with `--json-schema` and the answer comes back
as JSON matching that schema. Without it, the answer is plain text. Asking for
JSON in the prompt alone is unreliable, the model tends to wrap it in code fences,
so use `schema` when a workflow has to parse the result.

## Error handling

Failures are returned as HTTP 200 with `hasError: true` and a message in `error`,
so an n8n branch checks the field rather than the status code. Malformed input
that model binding rejects, such as a `sessionId` that is not a UUID, still comes
back as HTTP 400.

## Known limitations

- Uploaded files stay in the working directory, there is no cleanup job.
- Claude Code session files grow over time and are never pruned.
- Quota is bound to the account, not the machine. A workflow shares the plan's
  usage windows with everyday Claude Code work, and there is no overage, so a
  depleted window fails the request outright.
