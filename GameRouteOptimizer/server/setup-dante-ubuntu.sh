#!/usr/bin/env bash
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run as root: sudo bash setup-dante-ubuntu.sh"
  exit 1
fi

read -rp "SOCKS username [gamer]: " SOCKS_USER
SOCKS_USER=${SOCKS_USER:-gamer}

read -rsp "SOCKS password: " SOCKS_PASS
echo

if [[ -z "$SOCKS_PASS" ]]; then
  echo "Password cannot be empty."
  exit 1
fi

read -rp "Allowed client public IP/CIDR for port 1080 (example 41.45.10.20/32, blank = do not change UFW): " CLIENT_CIDR

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y dante-server

if ! id "$SOCKS_USER" >/dev/null 2>&1; then
  useradd -M -s /usr/sbin/nologin "$SOCKS_USER"
fi

echo "$SOCKS_USER:$SOCKS_PASS" | chpasswd

IFACE=$(ip route show default | awk '/default/ {print $5; exit}')

if [[ -z "$IFACE" ]]; then
  echo "Could not detect the default network interface."
  exit 1
fi

cat >/etc/danted.conf <<EOF
logoutput: syslog
internal: 0.0.0.0 port = 1080
external: $IFACE
socksmethod: username
user.privileged: proxy
user.notprivileged: nobody

client pass {
  from: 0.0.0.0/0 to: 0.0.0.0/0
  log: error
}

socks pass {
  from: 0.0.0.0/0 to: 0.0.0.0/0
  command: connect bind udpassociate
  socksmethod: username
  log: error
}
EOF

systemctl enable --now danted

if command -v ufw >/dev/null 2>&1; then
  if [[ -n "$CLIENT_CIDR" ]]; then
    ufw allow from "$CLIENT_CIDR" to any port 1080 proto tcp
    ufw allow from "$CLIENT_CIDR" to any port 1080 proto udp
    echo "UFW restricted port 1080 to: $CLIENT_CIDR"
  else
    echo "UFW rules were not changed."
    echo "Open TCP/UDP 1080 only for your own public IP in your cloud firewall."
  fi
else
  echo "UFW is not installed. Configure your VPS/cloud firewall manually."
fi

echo
echo "Dante SOCKS5 is running on port 1080."
echo "Interface: $IFACE"
echo "Username: $SOCKS_USER"
echo "Do not expose port 1080 to the whole internet unless absolutely necessary."
