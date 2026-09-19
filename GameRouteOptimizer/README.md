# Game Route Optimizer

Open-source Windows gaming route optimizer prototype built with .NET 8 WPF and sing-box.

## What it does

- Loads multiple relay/VPS nodes from `nodes.json`.
- Tests every node several times using real TCP connection timing.
- Calculates:
  - latency
  - jitter
  - packet/connect loss
  - weighted route score
- Automatically selects the best reachable node.
- Downloads the latest Windows amd64 `sing-box` binary automatically.
- Creates a Windows TUN interface.
- Routes only the selected game process through the chosen SOCKS5 relay.
- Leaves other Windows traffic on the normal ISP route.
- Includes an Ubuntu Dante SOCKS5 setup script for relay servers.

## Important limitation

This is not a magical ping reducer. It helps only when the ISP route to the game is worse than the route through your relay. A poor or geographically distant VPS can increase ping.

The current version performs **best-node selection/failover style routing**, not packet duplication over multiple simultaneous paths. True ExitLag-style multipath requires a controlled relay network and a custom protocol on both client and relay sides.

## Requirements

- Windows 10/11 x64
- Administrator privileges
- .NET 8 Desktop Runtime or .NET 8 SDK
- One or more VPS servers with SOCKS5 support
- For best results, place VPS nodes near good peering points between your ISP and the game provider.

## Build

```powershell
cd GameRouteOptimizer
dotnet build -c Release
dotnet run
```

The application requests Administrator permissions because TUN/route changes require elevation.

## Configure relay nodes

Edit `nodes.json` next to the executable:

```json
[
  {
    "name": "Marseille VPS",
    "host": "203.0.113.10",
    "port": 1080,
    "username": "gamer",
    "password": "your-strong-password"
  }
]
```

Do **not** commit real passwords to a public repository.

## Prepare Ubuntu VPS

Copy `server/setup-dante-ubuntu.sh` to the VPS and run:

```bash
sudo bash setup-dante-ubuntu.sh
```

Then put the VPS IP and credentials in `nodes.json`.

For security, restrict TCP/UDP 1080 in your cloud firewall to your own public IP rather than exposing SOCKS5 to the entire internet.

## Game process routing

Type the actual executable name in the app, for example:

```text
GameClient.exe
```

Do not enter the full path.

The generated sing-box rule routes that process through the chosen relay. Everything else uses `direct`.

## Route score

The current score is:

```text
score = latency + (jitter * 2) + (lossPercent * 12)
```

Packet loss is deliberately penalized heavily because a slightly lower ping with unstable loss is usually worse for real-time games.

## Architecture

```text
Game.exe
   |
Windows TUN (sing-box)
   |
process_name rule
   |
Selected SOCKS5 VPS
   |
Internet / Game server

Other apps -----------------> Direct ISP connection
```

## Next upgrades

- automatic periodic retesting while gaming
- hysteresis to avoid route flapping
- multiple game profiles
- encrypted relay protocol instead of raw SOCKS credentials
- WireGuard/Hysteria2 relay support
- path history and graphs
- Windows tray mode
- optional automatic failover
- controlled multipath protocol with relay-side agent
