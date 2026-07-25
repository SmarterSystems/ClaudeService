# SmarterSystems Claude Service

An HTTP wrapper around the Claude Code CLI, so n8n workflows or any other HTTP
client can talk to Claude using a Claude subscription instead of a pay-as-you-go
API key.

## When to use it

Use it if you want a Claude subscription to pay for the calls, or if you need the
agent loop: tool use and conversations that continue across separate HTTP calls.

Do not use it if you have an Anthropic API key. n8n's built-in Anthropic node
already covers that case and this service adds nothing.

A subscription credential is not accepted by the Anthropic Messages API directly.
Claude Code is the supported way to use one, which is why this service drives the
CLI instead of calling the API.

## Requirements

- .NET 10
- Claude Code CLI installed on the host and authenticated
- A Claude Pro, Max, Team, or Enterprise plan

## Quick start

Authenticate the CLI once on the host by running `claude` and logging in.

Start the service:

```bash
dotnet run --project SmarterSystems.Claude.Service --urls http://localhost:5220
```

It refuses every request until an API key is configured. Put one in
`appsettings.json`:

```json
{
  "Auth": {
    "ApiKey": "YOUR_API_KEY"
  }
}
```

Replace `YOUR_API_KEY` with a long random string of your own, then call it:

```bash
curl -X POST http://localhost:5220/Agent/message -H "Content-Type: application/json" -H "X-API-Key: YOUR_API_KEY" -d '{"message":"Name the capital of Switzerland.","model":"claude-haiku-4-5-20251001"}'
```

```json
{ "message": "Bern.", "stopReason": "end_turn", "hasError": false, "error": null }
```

## Authenticating Claude

The service holds no Claude credentials. It runs the `claude` executable, which
authenticates on its own, so the host it runs on has to be logged in.

- **Interactive host:** run `claude` once and log in.
- **Server or container:** run `claude setup-token` on a machine that has a
  browser, then hand the printed token to the service process as the
  `CLAUDE_CODE_OAUTH_TOKEN` environment variable.

If an `ANTHROPIC_API_KEY` exists in the environment, the CLI would prefer it over
the subscription and bill per token. The service therefore removes
`ANTHROPIC_API_KEY` and `ANTHROPIC_AUTH_TOKEN` before starting the CLI. Setting
either of them has no effect here.

## Securing the service

Every request needs an `X-API-Key` header matching `Auth:ApiKey`. A missing or
wrong key returns HTTP 401, and the comparison runs in constant time.

With no key configured, every request returns HTTP 500 with an explanation. That
is deliberate. This endpoint runs an agent with tool access on its host, so a
deployment where the key was forgotten must not be a working deployment.

If the service is reachable only from localhost or an internal container network,
set `Auth:Disabled` to `true` to skip the check.

Isolation remains the stronger control. Bind to loopback with
`ASPNETCORE_URLS=http://127.0.0.1:5220`, or in Docker leave the port unpublished
and let n8n reach the service over the internal network. Use the key when a
network boundary has to be crossed, not as a substitute for keeping the endpoint
off the public internet.

In n8n, a Header Auth credential named `X-API-Key` covers this.

## Configuration

Every setting lives in `appsettings.json` and can be overridden by an environment
variable. A colon in the file becomes a double underscore in the environment, so
`Auth:ApiKey` is `Auth__ApiKey`, and the environment wins.

| Key | Default | Meaning |
| --- | --- | --- |
| `Auth:ApiKey` | none | Expected value of the `X-API-Key` header. Required unless `Auth:Disabled` is set |
| `Auth:Disabled` | `false` | Skips the key check, for deployments that are already isolated |
| `ClaudeCli:ExecutablePath` | `claude` | Path to the CLI, resolved through `PATH` when left empty |
| `ClaudeCli:WorkingDirectory` | `agent-workspace` next to the app | Where the CLI runs and where uploads are stored |
| `ClaudeCli:TimeoutSeconds` | `600` | Kills the process tree when a call takes longer |

Keep the working directory stable. Claude Code scopes session lookup to it, so
changing it makes existing sessions unreachable.

## Endpoints

### `POST /Agent/message`

| Field | Required | Meaning |
| --- | --- | --- |
| `model` | yes | A model id your plan can use, for example `claude-haiku-4-5-20251001` |
| `message` | yes, unless `fileId` is set | The prompt |
| `sessionId` | no | A UUID. Continues that conversation, or starts it |
| `fileId` | no | An id returned by `/Agent/file` |
| `schema` | no | A JSON schema, as a string, that the answer must match |

```json
{
  "message": "Bern.",
  "stopReason": "end_turn",
  "hasError": false,
  "error": null
}
```

### `POST /Agent/file`

```json
{ "file": "SGFsbG8gV2VsdA==", "fileName": "numbers.txt", "mediaType": "text/plain" }
```

`file` is the content as base64. `mediaType` is optional and is derived from the
file name or the content when omitted.

```json
{ "fileId": "60ad5fbeee71475e8ac37274e4f6dc6d.txt", "hasError": false, "error": null }
```

Pass the returned `fileId` to `/Agent/message`. The agent then reads that file
with the `Read` tool, which is the only tool enabled, and only for requests that
reference a file.

## Sessions

The service keeps no conversation state. Claude Code stores sessions on disk, so
they survive a restart of the service.

- No `sessionId`: a one-shot request.
- A `sessionId` seen before: continues that conversation.
- A new `sessionId`: starts a new one.

The caller owns the id and it has to be a UUID, anything else is rejected with
HTTP 400. n8n execution ids are numbers, so generate a UUID in the workflow and
carry it through the steps, or store one per customer or topic if a conversation
should outlive a single run.

### Compacting a long session

Send `/compact` as the `message` of an existing session and Claude Code summarises
the conversation so far, then continues from that summary. Useful for sessions
that live for weeks, where every call would otherwise carry a growing history.
One-shot requests never need it.

Three things to expect:

- The reply is empty. `hasError: false` is the only success signal.
- Compaction is itself a model call, so on short conversations it costs more than
  it saves.
- It is not a documented feature of the CLI's non-interactive mode. It works, but
  it may stop working in a future CLI release.

## Structured output

Send a JSON schema in `schema` and the answer comes back as JSON matching it.
Without a schema the answer is plain text.

Use `schema` whenever a workflow has to parse the result. Asking for JSON in the
prompt alone is unreliable, answers often arrive wrapped in markdown code fences.

## Error handling

Failures during a call return HTTP 200 with `hasError: true` and a message in
`error`, so a workflow branches on the field rather than the status code.

Requests that are rejected before reaching that stage use real status codes: 400
for malformed input such as a `sessionId` that is not a UUID, 401 for a missing or
wrong API key, 500 when no API key is configured.

## Known limitations

- Uploaded files stay in the working directory. There is no cleanup job.
