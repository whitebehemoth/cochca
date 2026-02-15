# cochca - WebRTC Video Chat

Production-ready WebRTC video calling and chat application built with Blazor Server, SignalR, and coturn.

## ?? Quick Start

```powershell
# 1. Configure parameters
code cicd/main.parameters.json  # Add SSH public key

# 2. Deploy everything
cd cicd
.\deploy.ps1 -TurnDomain "turn.your-domain.com"

# 3. Configure DNS (see installation.md section 3)
# 4. Add SSL certificate (see installation.md section 4)
```

**Done!** Your app is live at `https://call.your-domain.com`

## ?? Features

- ? **WebRTC** peer-to-peer video/audio calls
- ? **TURN server** for NAT traversal (auto-configured)
- ? **SignalR** real-time chat and file sharing
- ? **Security**: Server-side TURN credentials, SSL/TLS, no secrets in code
- ? **Blazor Server** interactive UI with localization (EN/RU)
- ? **Infrastructure as Code** (Bicep) - fully automated deployment
- ? **No Docker required** - uses .NET 10 SDK native containers

## ??? Architecture

```
Browser ?? Container App (Blazor + SignalR) ?? TURN Server (VM)
              ?
         ACR (Private Registry)
              ?
     Azure Resources (automated)
```

## ?? Documentation

- **[Installation Guide](cochca/installation.md)** - Full deployment walkthrough
- **[Installation Guide (RU)](cochca/installation.ru.md)** - Russian version
- **[Troubleshooting](cochca/installation.md#8-troubleshooting)** - Common issues

## ?? Cost

~$20-30/month on Azure (can scale Container App to 0 when not in use)

## ?? Security

- Server-side TURN credential generation (HMAC-SHA1, 1-hour TTL)
- SSL/TLS via Container Apps managed certificates
- SSH key authentication for VM
- Network Security Groups for firewall rules
- Private container registry (ACR)

## ??? Tech Stack

- **Frontend**: Blazor Server (.NET 10), SignalR
- **WebRTC**: Peer-to-peer video/audio
- **TURN**: coturn on Ubuntu VM
- **Infrastructure**: Azure Container Apps, Bicep
- **CI/CD**: PowerShell deployment script

## ?? License

MIT
