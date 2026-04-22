# OnStepX Hub Pipe Protocol

**Transport**: Windows named pipe `\\.\pipe\OnStepXHub`, `PipeDirection.InOut`, `PipeTransmissionMode.Byte`, UTF-8, `\n`-terminated lines.

**Direction**: driver = client, hub = server.

**Contract**: tab-delimited envelope carrying raw LX200 strings. The hub does **not** parse LX200 payloads on the routing path — it only looks at the envelope verb and hands the payload to `MountSession.SendAndReceiveRaw` / `SendBlindRaw`. This is the explicit non-goal: no per-command opcodes ever.

## Frames

Client → Hub:

| Line                        | Meaning                                    |
| --------------------------- | ------------------------------------------ |
| `CMD\t<lx200>`              | Send LX200 with reply                      |
| `BLIND\t<lx200>`            | Send LX200 fire-and-forget                 |
| `IPC:ISCONNECTED`           | Mount-link health probe                    |
| `IPC:SHOWHUB`               | Pop hub window to foreground               |
| `IPC:VERSION\t<ver>`        | Optional version handshake                 |

Hub → Client:

| Line                        | Meaning                                    |
| --------------------------- | ------------------------------------------ |
| `OK`                        | BLIND or IPC:SHOWHUB succeeded             |
| `OK\t<reply>`               | CMD succeeded; reply is raw LX200 (trailing `#` preserved) |
| `ERR\t<msg>`                | Any failure; msg is single-line, tabs/CR/LF stripped |
| `IPC:ISCONNECTED:TRUE`      | Hub is connected to mount                  |
| `IPC:ISCONNECTED:FALSE`     | Hub running but mount offline              |

## Handshake

Driver `PipeTransport.Open()`:

1. `NamedPipeClientStream.Connect(timeoutMs)` — fails fast if hub not running; driver's `HubLauncher` then spawns `OnStepX.Hub.exe --tray` and retries with exponential backoff.
2. Write `IPC:ISCONNECTED`.
3. Read one line. Accept `IPC:ISCONNECTED:TRUE`; anything else → close pipe, throw.

## Example

```
> IPC:ISCONNECTED
< IPC:ISCONNECTED:TRUE
> CMD<TAB>:GVP#
< OK<TAB>On-StepX#
> CMD<TAB>:GRH#
< OK<TAB>18:23:45.1234#
> BLIND<TAB>:Mn#
< OK
> IPC:SHOWHUB
< OK
```

## Stability

Adding new commands does **not** change this spec. New LX200 strings ride the existing `CMD\t` / `BLIND\t` envelope. The only time this file should change is when a new IPC verb is added (e.g., a future `IPC:SUBSCRIBE` for server-pushed state events).
