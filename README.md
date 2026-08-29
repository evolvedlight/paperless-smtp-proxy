# 📬 Home SMTP Mail Listener for Paperless-ngx

An inbound SMTP email receiver and debugging dashboard built in .NET 10 with **SignalR** real-time updates, designed for home hosting on a NAS with **Portainer** / Docker.

When emails are sent to `steve@paperless.brown.bg` (or any configured address), this server intercepts the message, parses the MIME parts and attachments in real time, displays them on a live SignalR web dashboard, and can automatically push attachments to a **Paperless-ngx** instance running in Portainer.

---

## 🌟 Key Features

- **Inbound SMTP Server**: Built using high-performance `SmtpServer` and `MimeKit` to receive and parse incoming email streams.
- **SignalR Real-Time Web UI**:
  - Live animated feed of incoming emails as they arrive without refreshing.
  - Full email inspector: envelope headers, formatted HTML render (sandboxed iframe), and plain text body.
  - Attachment browser: inspect filenames, MIME types, file sizes, download attachments, and 1-click push to Paperless.
  - Built-in **"Send Test Email"** modal with simulated invoice PDF generation for zero-friction local testing.
  - Real-time activity console logging all SMTP events and Paperless API calls.
- **Paperless-ngx Integration**:
  - Fully implemented REST client targeting Paperless `POST /api/documents/post_document/`.
  - Configurable auto-upload or manual on-demand upload.
  - Customizable document naming (e.g. `{Subject} - {FileName}`).
- **Portainer & Docker Ready**:
  - Multi-stage `Dockerfile` and ready-to-use `docker-compose.yml`.
  - Zero external CDN dependencies (SignalR JS is bundled locally for isolated LAN / air-gapped home setups).

---

## 🏗️ Architecture

```
                                    +-----------------------------------------+
                                    |              Your Home Router           |
                                    |       (WAN Port 25 -> Port 25 on NAS)   |
                                    +--------------------+--------------------+
                                                         |
                                                         v
+-----------------------+     Inbound SMTP     +----------------------------------+
| External Mail Servers | -------------------> |    Home SMTP Listener Container   |
| (Gmail, Outlook, etc) |     (Port 25)        |  - SmtpServer (Port 25)          |
+-----------------------+                      |  - MimeKit Parser                |
                                               |  - SignalR Hub (/mailhub)        |
                                               |  - Web UI (Port 8080)            |
                                               +--------+------------------+------+
                                                        |                  |
                                         Real-Time Push |                  | REST API POST
                                         via WebSockets |                  | (post_document)
                                                        v                  v
                                               +------------------+  +--------------------+
                                               |  Browser Client  |  |   Paperless-ngx    |
                                               |  (Debug UI)      |  |  (In Portainer)    |
                                               +------------------+  +--------------------+
```

---

## 🚀 Quick Start (Local Run)

To run the application locally on your machine:

```powershell
dotnet run
```

By default in development mode:
- **Web UI & SignalR**: Open [http://localhost:8080](http://localhost:8080) (or the URL shown in console).
- **SMTP Server**: Listens on port `2525` (in Development) or port `25` (in Production).

Click **"🧪 Send Test Email"** in the top right corner of the web page to simulate receiving an email with a sample PDF attachment!

---

## 🐳 Deployment to Portainer / Docker on Home NAS

### Option 1: Deploy as a Portainer Stack

1. Open **Portainer** on your NAS.
2. Navigate to **Stacks** &rarr; **Add stack**.
3. Name the stack: `home-smtp-listener`.
4. Paste the contents of `docker-compose.yml`:

```yaml
services:
  home-smtp-listener:
    image: home-smtp-listener:latest
    build:
      context: https://github.com/YOUR_REPO/home-smtp-server.git
      dockerfile: Dockerfile
    container_name: home-smtp-listener
    restart: unless-stopped
    ports:
      - "25:25"      # Inbound SMTP
      - "8080:8080"  # Web Dashboard
    environment:
      - SmtpServer__Port=25
      - SmtpServer__ServerName=paperless.brown.bg
      - SmtpServer__AllowAnyRecipient=true
      - Paperless__Enabled=false
      - Paperless__BaseUrl=http://paperless-web:8000
      - Paperless__ApiToken=YOUR_PAPERLESS_API_TOKEN
      - Paperless__AutoUploadAttachments=true
```

5. Click **Deploy the stack**.

---

## 🌐 DNS & Routing Setup for `steve@paperless.brown.bg`

To have real emails sent from any outside email provider reach your listener at home:

### 1. DNS Records (at your domain registrar or Cloudflare)
| Record Type | Name / Host | Value / Target | Priority | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **A** or **CNAME** | `paperless.brown.bg` | Your Home Public IP or DDNS hostname | - | Resolves host to your router |
| **MX** | `paperless.brown.bg` | `paperless.brown.bg` | `10` | Tells mail servers where to route mail for this domain |

### 2. Router Port Forwarding
- Forward **Port 25 (TCP)** on your WAN / Router to your **NAS Local IP** (e.g. `192.168.1.100:25`).
- Ensure no firewall on the NAS is blocking inbound port 25.

> [!NOTE]
> Some residential Internet Service Providers (ISPs) block inbound Port 25 to prevent residential botnet spam. If your ISP blocks inbound port 25, you can either:
> 1. Request your ISP unblock port 25 for your IP/account.
> 2. Use a free/cheap Cloudflare Email Routing or VPS (e.g., small $3/mo Hetzner/DigitalOcean node running Postfix or Nginx stream proxy) that forwards port 25 traffic to an alternate port (e.g. port 2525 or via WireGuard tunnel) on your NAS.

---

## 📄 Connecting to Paperless-ngx

When you are ready to have attachments automatically uploaded into Paperless:

1. In Paperless-ngx, log in as an administrator.
2. Go to **My Profile** (or **Settings**) &rarr; **API Tokens** and generate a new token.
3. In Portainer / `docker-compose.yml`, update:
   - `Paperless__Enabled`: `true`
   - `Paperless__BaseUrl`: `http://paperless-web:8000` (or your internal Paperless container URL)
   - `Paperless__ApiToken`: `<paste-your-token>`
   - `Paperless__AutoUploadAttachments`: `true`
4. Make sure both containers are connected to the same Docker network in Portainer.

---

## ⚙️ Configuration Reference (`appsettings.json`)

```json
{
  "SmtpServer": {
    "Port": 25,
    "ServerName": "paperless.brown.bg",
    "AllowAnyRecipient": true,
    "AllowedRecipients": [
      "steve@paperless.brown.bg"
    ],
    "AllowedDomains": [
      "paperless.brown.bg"
    ]
  },
  "Paperless": {
    "Enabled": false,
    "BaseUrl": "http://paperless-web:8000",
    "ApiToken": "",
    "AutoUploadAttachments": true,
    "TitleFormat": "{Subject} - {FileName}",
    "DefaultTags": [
      "email-import"
    ]
  }
}
```
