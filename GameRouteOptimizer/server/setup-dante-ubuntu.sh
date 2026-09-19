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

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y dante-server

if ! id "$SOCKS_USER" >/dev/null 2>&1; then
  useradd -M -s /usr/sbin/nologin "$SOCKS_USER"
fi
echo "$SOCKS_USER:$SOCKS_PASS" | chpasswd

IFACE=$(ip route show default | awk '/default/ {print $5; exit}')
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
ufw allow 1080/tcp || true
ufw allow 1080/udp || true

echo "Dante SOCKS5 is running on port 1080."
echo "IMPORTANT: restrict firewall access to your own public IP whenever possible."