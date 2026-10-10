# 🌐 Realm Seed Node Setup Guide

This guide describes how to host a community **Lobby & Discovery Server** (also known as a **Seed Node**). 

---

### ⚠️ IMPORTANT DISCLAIMER
* **For Advanced Users Only:** Setting up and maintaining a seed node requires basic knowledge of networking, command-line interfaces, and router port-forwarding.
* **No Pressure:** The Realm network is decentralized and only requires a small handful of stable, high-uptime seed nodes globally to support the entire player base. You do not need to host one unless you want to support the community.
* **Uptime & Network Requirements:** To be accepted as a public seed node, your server **must maintain high uptime** and have a **stable internet connection**.
* **Static IP Required:** Public seed nodes **must have a static IP address** or a persistent dynamic DNS domain name. Dynamic/frequently-changing IPs will not be accepted.
* **Security Notice:** Running a seed node exposes port `5000` to the internet. While the server does not execute arbitrary code, hosting any public-facing server carries default network security considerations.

---

## 🧠 What is a Seed Node?
When a player hosts a game, the client contacts a registry server configured in [servers.json](file:///C:/temp/Realm/Realm.Clientservers.json). The registry server:
1. Records the host's public IP, port, and NAT type.
2. Relays connection requests and coordinates UDP hole punching via WebSockets to enable direct peer-to-peer gameplay.

The server logic is implemented in [Program.cs](file:///C:/temp/Realm/Realm.AdminServer/Program.cs).

In addition to acting as a lobby connection registry, the Seed Node handles NoSQL-based data persistence and APIs across the community network to support various features outside the game (ranked ladders, etc).

---

## 🛠️ Prerequisites
* **Runtime:** [.NET 10.0 SDK or Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
* **Port Forwarding:** Access to your router settings to forward port **5000 TCP** to your hosting machine.
* **Network:** A stable internet connection with a static IP address.

---

## 🚀 How to Run the Seed Node

### Option A: From Source Code (For Developers & Advanced Users)
1. Clone the repository and navigate to the project directory.
2. Run the admin server project with the `--urls` flag to listen on all network interfaces:
   ```powershell
   dotnet run --project Realm.AdminServer/Realm.AdminServer.csproj --urls "http://0.0.0.0:5000"
   ```
   *File Reference: [Realm.AdminServer.csproj](file:///C:/temp/Realm/Realm.AdminServer/Realm.AdminServer.csproj)*

### Option B: Using Pre-compiled Executables (User-Friendly Setup)
1. Download the latest `Realm.AdminServer` release build for your operating system.
2. Open a terminal (cmd or terminal app) in the folder where the files were extracted.
3. Run the executable:
   * **Windows:**
     ```powershell
     .\Realm.AdminServer.exe --urls "http://0.0.0.0:5000"
     ```
   * **Linux/macOS:**
     ```bash
     chmod +x ./Realm.AdminServer
     ./Realm.AdminServer --urls "http://0.0.0.0:5000"
     ```

---

## 🔒 Router Configuration (Port Forwarding)
To allow players to reach your seed node:
1. Find your hosting computer's local IP address (e.g. `192.168.1.100`) via `ipconfig` (Windows) or `ifconfig`/`ip a` (Linux).
2. Access your router's administration panel (usually at `192.168.1.1` or `192.168.0.1`).
3. Set up a port forwarding rule:
   * **Service Name:** Realm Seed Node
   * **Protocol:** TCP
   * **External/Internal Port:** 5000
   * **Internal IP:** Your computer's local IP address.
4. Verify accessibility using external tools (like [CanYouSeeMe.org](https://canyouseeme.org/) on port 5000).

## 🌐 Registering Your Seed Node in the Network

Once your seed node is running and port forwarding is confirmed working:

### 1. Find Your Public IP Address
Your public IP address is needed for players to connect. You can retrieve it using one of the following methods:
* **Via Web Browser:** Visit [icanhazip.com](https://icanhazip.com/) or [ifconfig.me](https://ifconfig.me/).
* **Via PowerShell (Windows):**
  ```powershell
  (Invoke-WebRequest -Uri "https://icanhazip.com").Content.Trim()
  ```
* **Via Terminal (Linux/macOS):**
  ```bash
  curl icanhazip.com
  ```

### 2. Request Seed Node Verification
To protect the network from malicious tampering and rogue IP injection, official registry endpoints and admin keys are managed securely via GitHub repository variables embedded during official release builds.

To have your seed node registered in the official registry:
1. Open an Issue or Registration Request on the project repository with:
   - Your static public IP or persistent domain name (e.g. `http://<YOUR_STATIC_PUBLIC_IP>:5000`)
   - Your server region / hosting location (e.g., `us-east`, `eu-central`)
   - Proof of uptime and reachability on port 5000
2. Maintainers will perform connectivity checks and add your node to the official `SERVERS_JSON` registry variable.
3. Once verified, your node will be included in official release builds and peer-to-peer discovery.


---

## 📈 Maintaining High Uptime

If you want your node to be a reliable bootstrap server for the community, consider the following uptime best practices:

### 1. Run as a Background Service
To prevent the application from closing when you close the terminal window:
* **Windows (using NSSM):**
  Use the [Non-Sucking Service Manager (NSSM)](https://nssm.cc/) to install it as a Windows service:
  ```powershell
  nssm install RealmAdminServerService "C:\path\to\Realm.AdminServer.exe" "--urls http://0.0.0.0:5000"
  nssm start RealmAdminServerService
  ```
* **Linux (using systemd):**
  Create a systemd unit file at `/etc/systemd/system/realm-adminserver.service`:
  ```ini
  [Unit]
  Description=Realm Admin & Discovery Server
  After=network.target

  [Service]
  ExecStart=/usr/bin/dotnet /path/to/Realm.AdminServer.dll --urls "http://0.0.0.0:5000"
  WorkingDirectory=/path/to/
  Restart=always
  RestartSec=10
  SyslogIdentifier=realm-adminserver
  User=nobody

  [Install]
  WantedBy=multi-user.target
  ```
  Enable and start it:
  ```bash
  sudo systemctl enable realm-adminserver.service
  sudo systemctl start realm-adminserver.service
  ```

### 2. Configure Auto-Restart on Crash
Operating systems or unexpected network interruptions can occasionally crash applications. Running the node via systemd (`Restart=always`) or an process manager like **PM2** ensures the server immediately spins back up.

---

## 🌐 P2P Map Seeding & Discovery Protocol

The Realm Seed Node also acts as a decentralized directory indexer for P2P map seeding. If the player enables **"Seed map files to other players"** in their settings, their client registers with the Seed Node as an active peer for the maps stored locally on their disk.

