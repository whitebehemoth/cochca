#!/usr/bin/env bash
set -euo pipefail

TURN_USER=${TURN_USER:-turnuser}
TURN_PASSWORD=${TURN_PASSWORD:-turnpass}
PUBLIC_IP=${PUBLIC_IP:-}
DOMAIN=${DOMAIN:-}

if [[ -z "$PUBLIC_IP" ]]; then
  PUBLIC_IP=$(curl -s https://api.ipify.org)
fi

sudo apt-get update -y
sudo apt-get install -y coturn

sudo bash -c "cat > /etc/turnserver.conf <<EOF
listening-port=3478
tls-listening-port=5349
fingerprint
use-auth-secret
static-auth-secret=${TURN_PASSWORD}
realm=${DOMAIN:-$PUBLIC_IP}
server-name=${DOMAIN:-$PUBLIC_IP}
external-ip=${PUBLIC_IP}
min-port=49160
max-port=49200
no-loopback-peers
no-multicast-peers
EOF"

sudo sed -i 's/#TURNSERVER_ENABLED=0/TURNSERVER_ENABLED=1/' /etc/default/coturn

sudo systemctl enable coturn
sudo systemctl restart coturn

cat <<INFO
coturn installed.
Public IP: $PUBLIC_IP
Realm: ${DOMAIN:-$PUBLIC_IP}
Static secret: $TURN_PASSWORD
INFO
