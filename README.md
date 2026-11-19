`Real`time strategy `M`aps

# 👑 Realm
**Realm** is a highly optimized, non-deterministic, server-authoritative engine designed specifically for building **casual arcade-style RTS maps**. It prioritizes **rapid modding and User-Generated Content (UGC)**.

This repository contains the full source code for the Realm Engine and the Realm Editor, licensed under the **MIT License**. Realm is a Steam game: Steam owns identity, networking, lobbies, map distribution, invites and stats, and this project owns the simulation, the editor and the map format. Nothing here is a service the project operates.

## 🚀 Engine Core Philosophy

The engine is built for simplicity for content creators and is not geared toward esports-level integrity. A match runs on the host's own machine and reaches the other seats over Steam's peer-to-peer networking, so its quality follows the link between the host and its peers; there is no dedicated server to rent and none to run.

* **Architecture Base:** Godot.
* **Primary Languages:** C# for all simulation logic; Godot for rendering and physics.
* **Networking Model:** Non-deterministic, non-lockstep, server-authoritative RTS architecture with **no rollback**, carried over Steam's peer-to-peer networking.
* **Performance Targets (Server):** 30 Hz constant simulation tick rate.
    * Maximum units per player: $\approx 100$ (tunable).
    * Maximum players per map (including AI or neutral creeps): $\approx 12$ (tunable).

## 🧠 Authority Model & Simulation

The engine is server-authoritative and uses latency masking rather than prediction:

| Component | Responsibility |
| :--- | :--- |
| **Server (Authoritative)** | Runs triggers, damage, AI, pathfinding, authoritative unit state, and ability resolution. Runs the ECS-style simulation written in C#. |
| **Client (Latency-masked)** | Renders HP bars, VFX, UI, and sends player commands. Draws every unit, its own included, from the authority's own timeline by interpolating between buffered descriptions at a presentation delay it measures; never simulates movement or combat, and shows an order as a route preview until the authority's destination replaces it. |
| **Interop (C#/Godot)** | Uses a single P/Invoke per tick to send a minimal state snapshot from Godot to C#, and modified state back to Godot. C# heavily uses performance features like `Span<>`, `structs`, and object pooling to avoid excessive GC pressure. |

## 🛠️ Realm Editor (UGC Tooling)

The Editor is a screen inside the game itself - `MapEditorHUD`, opened from the main menu - not the Godot editor and not a Godot plugin. It runs on the **standard Godot .NET (C#) build**, with no custom engine to install, and the map-authoring tools below are Realm's own UI drawn by the same client that will play the map.

* **Map Package Output:** The editor outputs a map folder, and a `.rmap` package for sharing a map outside Steam, containing unit metadata (JSON), art assets, the compiled map script, and map configuration (like max units/players).
* **Hard Limits:** To facillitate average internet capabilities, maps are currently limited to $\approx 1200$ total units (including AI waves), but will be increased over time as technology improves (fiber/etc). This could be overridden for private LAN events lobbies where all players are on ethernet with low ping fiber connections to the host (or possibly public internet events where users are okay with potential instability).
* **Modding Features:** Includes hot-reloading  and a button to open trigger and JSON files directly in a pre-configured VSCode instance for debugging and performance profiling. The editor also provides links to open URLs in an external browser for free online AI code and art generation.
* **Attribution:** By default, there is no source protection of map file assets. Map authors may optionally obfuscate their trigger code and encrypt their asset files using 3rd party tools, however it is not recommended as it reduces collaboration in the community. It is recommended that derivative map creators contact the original author if possible before forking, and that they retain the quality standards of the original before publishing. Authorship is recorded by Steam: the Workshop item's owner is the author of record, references and imports are listed in the manifest, and credits name the creators whose work a map carries. Full chronological authorship history is expected in derivative maps so prior authors never lose credit for their work.

## 🌐 Networking and Distribution

Realm is a host-authoritative Steam game. Steam provides the identity, the lobby directory, the peer-to-peer transport and the Workshop, so a player never forwards a port, runs a relay or configures a router.

* **Identity:** the Steam account is the player. There is no Realm account, password, e-mail or profile service; the persona name, avatar and Workshop page are Steam's.
* **Transport:** Steam networking carries both streams between the host's authority and each screen, and Steam decides whether the traffic goes direct or through its relay, so the same lobby works on two home networks and on a corporate one.
* **Snapshotting:** a custom protocol on top of that transport.
    * Client commands are sent at a throttled rate of 20 Hz.
    * The authority broadcasts a separate snapshot at the end of every 30 Hz tick.
    * Snapshots use **delta compression** based on unit visibility and proximity to the camera. Fog-of-war units are **never sent** in any snapshot.
    * Deltas travel **unreliable and ordered**, so a lost one costs only the change it carried until the acknowledgement reports it missing; baselines, orders and every other frame the match cannot proceed without travel reliably.
* **Hosting:** any player can host. The host's machine runs the authority for its own seat and every other seat; there is no dedicated server and no hosting service of ours.
* **Map Distribution:** maps are Steam Workshop items. A lobby names the item it runs and players subscribe to it in Steam. A map that was never published still plays locally, and a host can send it to the players who join.
* **Without Steam:** single player, the editor, local map play and `.rmap` import all work with no Steam client. Multiplayer, the Workshop, invites and stats are hidden with the reason, and the game is not wrapped in Steam DRM.

### Platform documentation

- [Steam platform](docs/steam-platform.md): the two AppIDs, the Steamworks partner-site configuration, Workshop tags, lobby limits, what a release build ships, and the two-account test a release is checked against.
- [Map authoring](docs/map-authoring.md): the map format, the Workshop item as a map's identity, publishing, and how a map keeps playing across engine updates.
- [Steam test plan](docs/steam-test-plan.md): the ordered checks a Steam-first build is played against, from a local session to two accounts on two networks, and the decisions a first real test is meant to settle.
- [Steam branch readiness](docs/steam-readiness.md): what the Steam-first branch is verified against statically, what is still unproven, the order to check it in, and what is most likely to be wrong.

## 📦 Repository Structure

This is the main, public, MIT-licensed repository for the entire project.

| Repository Name | Status | Purpose |
| :--- | :--- | :--- |
| **Realm** (You Are Here) | **Public (MIT)** | The complete game: engine, editor, map format, map API and the Steam integration. No service, database or deployment of ours is part of it. |
| **Realm.Assets** | **Private** | Stores the default asset bundle (artwork, sounds, etc.) distributed with the editor. Their license only allows use within the Realm game itself (to maintain brand identity). Standalone games which use the Realm engine will need to provide their own assets |

The editor also supports importing of custom assets if you do not wish to use the default asset bundle.

### 🔒 Note on Assets and Licensing

The `Realm.Assets` repository is **private for trusted maintainers** and is governed by a separate **Realm ASSET LICENSE AGREEMENT**  which is restrictive and must be accepted before the editor will download them to your machine.
Those who wish to contribute assets to be embedded within the editor should contact the Realm admins.

Maps contain many different pieces of intellectual property in one 7z archive (art, sounds, code snippets, etc). The original author of each IP maintains copyright of their own work. The owners of all IP within a map grant a non-exclusive permanent license to all Realm players and modders to use that IP and create derivative works of it as long as it is only distributed and used within the Realm game, even if it requires removing intentional protection mechanisms such as obfuscation or encryption. Content can only be distributed outside the Realm game (including forked versions) by obtaining permission from all original author(s) of each work. To be a friendly contributor, it is recommended that you ask permission and review your changes with the original author before modding, but it is not strictly required. Signing a map writes a 'Contributors' list into its metadata naming everybody whose work it carries. Derivative authors may not remove a past contributor from that list, and a name they add is appended at the bottom so the chronology stays accurate. Support links are not part of the map: a creator's Steam profile and the Discord server in their map's metadata are where those belong.

A map records where its content came from: the manifest lists the maps it was built from, the credits panel names their creators, and a creator's Steam profile and Discord server are where support links belong. An authorship key may sign the map, and the signature proves which files that key wrote, but Steam's item ownership is the attribution and the access control, so no publish, install or join path depends on a signature being present or valid.

## 🛠️ Contribution Guidelines

We encourage contributions! Please submit a pull request. Reach out on discord if you have questions on how to get started.
AI-generated "vibe" code is acceptable only if it has been thoroughly reviewed by a human before submission. "Slop" will not be accepted.

## ⚙️ Setting up the Project

1.  **Clone this repository** recursively to include all submodules (including the required third-party packages).
2.  **Install the standard Godot .NET (C#) build** for your platform.
3.  **Create a `steam_appid.txt` in `Realm.Client`**
4.  **Open Godot, import `Realm.Client\project.godot` and click Run**
