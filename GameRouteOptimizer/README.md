# Game Route Optimizer

Open-source Windows gaming route optimizer built with .NET 8 WPF and sing-box.

## Version 2: multi-relay engine

The application no longer locks the game to one manually selected VPS.

It now builds a sing-box **URLTest relay group** containing every configured VPS. sing-box tests the relays continuously and routes the selected game process through the best healthy member of that group.

The desktop application also measures every relay itself and displays latency, jitter, loss, score, the currently active relay reported by sing-box, and the best measured backup.

## Architecture

```text
                         +--> Relay 1 / Frankfurt --+
Game.exe --> Windows TUN +--> Relay 2 / Marseille -+--> Game server
      |                  +--> Relay 3 / Milan ------+
      |                              ^
      |                              |
      +---- process-only routing ----+
                                     |
                         URLTest every 5 seconds
                         automatic path switching
                         active + backup monitoring

Other Windows traffic ---------------------------> Direct ISP route
```

## What is implemented

- Windows 10/11 x64 WPF client.
- Per-process TUN routing.
- Multiple SOCKS5 relay/VPS nodes.
- sing-box URLTest automatic relay selection.
- 5-second relay retesting inside sing-box.
- 15 ms switching tolerance to reduce route flapping.
- Existing-connection interruption when the URLTest group changes route.
- Live active-relay discovery through the local sing-box Clash API.
- Parallel client-side TCP measurements for latency, jitter and connect loss.
- Weighted route score.
- Active and backup relay display.
- Automatic sing-box download.
- Ubuntu Dante SOCKS5 relay installer.
- GitHub Actions Windows build and artifact publishing.

## Important technical truth

This is now a real **multi-relay automatic-routing system**, but it is not packet duplication across several routes at the same instant.

URLTest chooses one relay for a connection/session and automatically changes the selected relay when another measured path becomes better.

ExitLag-style packet-level redundancy/FEC would require a custom authenticated client and relay protocol on both sides. That is intentionally a separate layer because implementing an unauthenticated generic UDP forwarder would be unsafe.

## Requirements

- Windows 10/11 x64.
- Administrator privileges.
- .NET 8 Desktop Runtime or SDK.
- One or more VPS servers.
- SOCKS5 on every VPS with UDP support enabled.

## Build

```powershell
cd GameRouteOptimizer
dotnet build -c Release
dotnet run
```

## Relay configuration

Edit `nodes.json` beside the executable.

```json
[
  {
    "name": "Marseille VPS",
    "host": "203.0.113.10",
    "port": 1080,
    "username": "gamer",
    "password": "use-a-strong-password"
  },
  {
    "name": "Frankfurt VPS",
    "host": "198.51.100.20",
    "port": 1080,
    "username": "gamer",
    "password": "use-a-different-strong-password"
  }
]
```

Never commit real relay passwords to a public repository.

## Ubuntu relay

Copy:

```text
server/setup-dante-ubuntu.sh
```

to the VPS and run:

```bash
sudo bash setup-dante-ubuntu.sh
```

Restrict port 1080 in the VPS/cloud firewall to your own IP whenever possible.

## Route score shown in the UI

The application's independent health score is:

```text
score = latency + (jitter * 2) + (lossPercent * 12)
```

This score is used for the UI health ranking and backup recommendation.

The actual active data path is selected by sing-box URLTest, whose result is read from the local Clash-compatible API at `127.0.0.1:9097`.

## How path switching works

The generated URLTest group uses:

```text
interval: 5 seconds
tolerance: 15 ms
interrupt existing connections: true
```

The tolerance avoids changing routes for tiny timing differences.

For real-time UDP games, switching can recover quickly from a bad relay. TCP sessions may reconnect when a path switch interrupts an existing connection.

## Game process

Enter only the executable name, for example:

```text
GameClient.exe
```

The process rule sends that game to the multi-relay group. Other applications use the direct ISP route.

## Troubleshooting

If Start Multi-Route fails, the application surfaces sing-box startup stderr. Common causes are:

- invalid VPS hostname/IP
- wrong SOCKS credentials
- another program already using local port 9097
- missing Administrator permission
- VPS firewall blocking TCP/UDP 1080

## Next protocol layer

A future packet-redundancy mode can be added as a separate authenticated relay protocol with:

- UDP sequence numbers
- HMAC authentication
- replay protection
- optional duplicate transmission to two paths
- deduplication at the relay
- optional FEC
- per-destination allowlists and rate limits

That layer should not be exposed as an open generic relay.
