# Installation Guide - Production Ready

This guide walks through **fully automated** deployment of the `cochca` WebRTC app to Azure with security best practices.

---
## 0) Prerequisites

- **.NET SDK 10** (with container support built-in)
- **Azure CLI** (`az`) authenticated (`az login`)
- **NO Docker required** for deployment
- An Azure subscription  
- A domain (e.g. `your-domain.com`) with DNS management access

---
## 1) Local development

1. Trust dev cert (Windows):
   ```powershell
   dotnet dev-certs https --trust
   ```
2. Run the app:
   ```powershell
   dotnet run --project cochca --urls "https://0.0.0.0:7088;http://0.0.0.0:5247"
   ```
3. On your phone (same LAN):
   ```
   https://<PC_IP>:7088
   ```
4. Accept the certificate (required for camera/mic).

**Check:** Main UI loads, connect/chat/call works locally.

---
## 2) One-command deployment

### 2.1 Edit `cicd/main.parameters.json`

Only these basic parameters are needed:

```json
{
  "location": { "value": "eastus2" },
  "appName": { "value": "cochca" },
  "environmentName": { "value": "cochca-env" },
  "logAnalyticsName": { "value": "cochca-logs" },
  "containerPort": { "value": 8080 },
  "vmName": { "value": "cochca-turn" },
  "adminUsername": { "value": "azureuser" },
  "sshPublicKey": { "value": "ssh-ed25519 AAAA..." }
}
```

**Generate SSH key:**
```powershell
ssh-keygen -t ed25519 -C "cochca"
# Copy content from: C:\Users\<you>\.ssh\id_ed25519.pub
```

### 2.2 Run deployment

```powershell
cd cicd
.\deploy.ps1 -TurnDomain "turn.your-domain.com"
```

**What happens:**
1. Creates ACR
2. Builds & pushes container
3. Generates secure TURN password (32-char random)
4. Deploys all infrastructure:
   - Container Apps Environment
   - Log Analytics
   - Container App (with TURN secrets as env vars)
   - VM with **auto-install coturn** (cloud-init)
   - VNet, NSG, Public IP
5. Configures NSG ports (SSH, TURN)

**Duration:** ~10-15 minutes (first time), ~5 minutes (updates)

### 2.3 Get deployment outputs

```bash
az deployment group show -g cochca-rg -n main --query properties.outputs
```

You'll see:
```json
{
  "containerAppFqdn": { "value": "cochca.xxx.eastus2.azurecontainerapps.io" },
  "turnPublicIp": { "value": "20.114.168.104" }
}
```

---
## 3) Configure DNS

Add these records in your DNS provider:

| Type | Name | Data | TTL |
|------|------|------|-----|
| **CNAME** | **call** | **cochca.xxx.eastus2.azurecontainerapps.io** | 1 Hour |
| **A** | **turn** | **20.114.168.104** | 1 Hour |

**Optional:** Set up `www.call.your-domain.com` ? `call.your-domain.com` redirect

---
## 4) Add SSL certificate

```bash
# Add custom domain
az containerapp hostname add -n cochca -g cochca-rg --hostname call.your-domain.com
```

Azure shows TXT record - add it to your DNS provider:
```
Type: TXT
Name: asuid.call  
Data: <long-hash-from-azure>
TTL: 1 Hour
```

Wait 5 minutes, then create certificate:
```bash
az containerapp hostname bind \
  -n cochca -g cochca-rg \
  --hostname call.your-domain.com \
  --environment cochca-env \
  --validation-method CNAME
```

**Check:** `https://call.your-domain.com` works with valid SSL ?

---
## 5) TURN server - automatic! ?

### What's automated:

1. **coturn installation** (cloud-init on VM creation)
2. **TURN password** (generated securely in deploy.ps1)
3. **NSG ports** (TCP 22/3478/5349, UDP 3478)
4. **Server-side credentials API** (`/api/turn-credentials`)

### How it works:

```
[Browser] ? GET /api/turn-credentials ? [Blazor API]
              ? (generates HMAC-SHA1)
  { username: "timestamp:cochca",
    credential: "base64-hash",
    urls: ["turn:turn.your-domain.com:3478"],
    expiresAt: 1234567890 }
              ? (cached for 1 hour)
         [webrtc.js]
              ?
    [RTCPeerConnection with TURN]
              ?
         [coturn on VM]
```

### Verify TURN installation

```bash
# SSH to VM (wait 5-10 min for cloud-init)
ssh azureuser@<turnPublicIp>

# Check coturn
sudo systemctl status coturn
sudo journalctl -u coturn -n 50

# Test credentials API
curl https://call.your-domain.com/api/turn-credentials
```

---
## 6) Final checks

1. Open `https://call.your-domain.com` on two devices
2. Click **Connect** on both
3. Switch to **Call** tab ? verify video/audio
4. Send message/file in **Chat** tab

**Test TURN (restrictive network):**
- Disable UDP in firewall ? video should still work (TURN relay)
- Check browser console ? see ICE candidates type: `relay`

---
## 7) Architecture

```
???????????????????????????????????????????????????????????????
?                     INTERNET (your-domain.com)                      ?
???????????????????????????????????????????????????????????????
               ?                         ?
       ????????????????????      ?????????????????
       ?  GoDaddy DNS     ?      ?  Azure Portal ?
       ?  call ? CNAME    ?      ?  SSL Cert Mgt ?
       ?  turn ? A        ?      ?????????????????
       ????????????????????
               ?
       ???????????????????????????????????????????????
       ?     Azure Container Apps Environment        ?
       ?  ????????????????????????????????????????   ?
       ?  ?  Container App (cochca)              ?   ?
       ?  ?  - Blazor Server                     ?   ?
       ?  ?  - SignalR Hubs (WebRTC, Chat)       ?   ?
       ?  ?  - API: /api/turn-credentials        ?   ?
       ?  ?  - Env: TurnServer__Password (secret)?   ?
       ?  ????????????????????????????????????????   ?
       ?  ????????????????????????????????????????   ?
       ?  ?  ACR (cochcaacr.azurecr.io)          ?   ?
       ?  ?  - Image: cochca:latest              ?   ?
       ?  ????????????????????????????????????????   ?
       ???????????????????????????????????????????????
               ?
       ???????????????????????????????????
       ?  VM (TURN Server)               ?
       ?  - coturn (auto-installed)      ?
       ?  - static-auth-secret           ?
       ?  - Ports: 3478, 5349            ?
       ?  - NSG: allow from Internet     ?
       ???????????????????????????????????
```

---
## 8) Troubleshooting

### Container App not starting
```bash
az containerapp logs show -n cochca -g cochca-rg --follow
```

### TURN not working
```bash
ssh azureuser@<turnIP>
sudo systemctl status coturn
sudo cat /var/log/turnserver.log
```

### Test TURN credentials
```bash
curl https://call.your-domain.com/api/turn-credentials
# Should return JSON with username, credential, urls
```

### Cloud-init logs (if coturn didn't install)
```bash
ssh azureuser@<turnIP>
sudo cat /var/log/cloud-init-output.log
```

---
## 9) Cost optimization

| Resource | Monthly Cost | Optimization |
|----------|--------------|--------------|
| Container Apps | ~$5-15 | Scale to 0 (min replicas = 0) |
| ACR Basic | $5 | Required |
| VM (B1s) | ~$7 | Deallocate when not in use |
| Log Analytics | ~$2 | Delete old logs |
| **Total** | **~$20-30** | |

### Deallocate VM when not needed:
```bash
az vm deallocate -g cochca-rg -n cochca-turn
az vm start -g cochca-rg -n cochca-turn
```

---
## 10) Update workflow

### Update code:
```powershell
# Make changes to cochca code
cd cicd
.\deploy.ps1  # Rebuilds container, redeploys
```

### Update infrastructure:
```powershell
# Edit main.bicep or main.parameters.json
cd cicd
.\deploy.ps1  # Updates resources
```

### Update coturn config:
```bash
ssh azureuser@<turnIP>
sudo nano /etc/turnserver.conf
sudo systemctl restart coturn
```


**Total setup time:** ~30 minutes (first time), ~5 minutes (updates)
