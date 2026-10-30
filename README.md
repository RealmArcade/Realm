`Real`time strategy `M`aps

# 👑 Realm
**Realm** is a highly optimized, non-deterministic, server-authoritative engine designed specifically for building **casual arcade-style RTS maps**. It prioritizes **rapid modding and User-Generated Content (UGC)**.

This repository contains the full source code for the Realm Engine, the Realm Editor, and all associated platform/website code, both licensed under the **MIT License**.

## 🚀 Engine Core Philosophy

The engine is built for simplicity for content creators and is not geared toward esports-level integrity, however community hosted events could support it through dedicated private servers with verified low-ping connection to all players.

* **Architecture Base:** Godot.
* **Primary Languages:** C# for all simulation logic; Godot for rendering and physics.
* **Networking Model:** Non-deterministic, non-lockstep, server-authoritative RTS architecture with **no rollback**.
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

The Editor is a pre-configured **Godot** editor with specific plugins to simplify the interface and support Realm-specific map editing.

* **Map Package Output:** The editor outputs a map package (`.7z`) containing Unit metadata (JSON), Art assets, Trigger scripts, and map configuration (like max units/players).
* **Hard Limits:** To facillitate average internet capabilities, maps are currently limited to $\approx 1200$ total units (including AI waves), but will be increased over time as technology improves (fiber/etc). This could be overridden for private LAN events lobbies where all players are on ethernet with low ping fiber connections to the host (or possibly public internet events where users are okay with potential instability).
* **Modding Features:** Includes hot-reloading  and a button to open trigger and JSON files directly in a pre-configured VSCode instance for debugging and performance profiling. The editor also provides links to open URLs in an external browser for free online AI code and art generation.
* **Licensing & Signing:** By default, there is no source protection of map file assets. Map authors may optionally obfuscate their trigger code and encrypt their asset files using 3rd party tools, however it is not recommended as it reduces collaboration in the community. However, it is recommended that derivative map creators contact the original author if possible to determine if collaboration is possible before forking. Care should be taken to retain the same quality standards as the original map before publishing to avoid tarnishing the player's perspective of the map. To aid in this goal, a signature on each 7z map archive verifies the publisher of that version so players know if they're playing an original or modded version of the map. Full chronological authorship history is required to be maintained in all derivative maps so prior authors never lose credit for their work.

## 🌐 Networking and Distribution

Realm uses a server-authoritative UDP model.

* **Snapshotting:** A custom snapshot protocol is used, leveraging Godot's native ENet.
    * Client commands are sent at a throttled rate of 20 Hz.
    * The server broadcasts a separate snapshot at the end of every 30 Hz tick.
    * Snapshots use **delta compression** based on unit visibility/proximity to the camera. Fog-of-war units are **never sent** in any snapshots.
    * Deltas travel **unreliable and ordered**, so a lost one costs only the change it carried until the acknowledgement reports it missing; baselines, orders and every other frame the match cannot proceed without travel reliably.
* **Hosting:** No official dedicated servers. Any player can host if their firewall allows UDP hole punching (~ 90% of home internet users, but not most corporate networks). Anyone could choose to host their own community server if desired.
* **Map Distribution:** Maps are decentralized, stored as `.7z` packages, and downloaded from the host upon joining a lobby.

## 📦 Repository Structure

This is the main, public, MIT-licensed repository for the entire project.

| Repository Name | Status | Purpose |
| :--- | :--- | :--- |
| **Realm** (You Are Here) | **Public (MIT)** | **The Complete Project:** Contains the core engine/editor source code, platform integrations (e.g., Steam), website files, and all documentation (Wiki). **Note:** Authentication keys for publishing are kept private and are excluded from this repository. |
| **Realm.Assets** | **Private** | Stores the default asset bundle (artwork, sounds, etc.) distributed with the editor. Their license only allows use within the Realm game itself (to maintain brand identity). Standalone games which use the Realm engine will need to provide their own assets |

The editor also supports importing of custom assets if you do not wish to use the default asset bundle.

### 🔒 Note on Assets and Licensing

The `Realm.Assets` repository is **private for trusted maintainers** and is governed by a separate **Realm ASSET LICENSE AGREEMENT**  which is restrictive and must be accepted before the editor will download them to your machine.
Those who wish to contribute assets to be embedded within the editor should contact the Realm admins.

Maps contain many different pieces of intellectual property in one 7z archive (art, sounds, code snippets, etc). The original author of each IP maintains copyright of their own work. The owners of all IP within a map grant a non-exclusive permanent license to all Realm players and modders to use that IP and create derivative works of it as long as it is only distributed and used within the Realm game, even if it requires removing intentional protection mechanisms such as obfuscation or encryption. Content can only be distributed outside the Realm game (including forked versions) by obtaining permission from all original author(s) of each work. To be a friendly contributor, it is recommended that you ask permission and review your changes with the original author before modding, but it is not strictly required. The map metadata contains a field for 'contributors' with their respective 'donation' links, derivative authors may not remove any past contributor info from the metadata file and if they choose to add their own info it must be appended at the bottom of the list to maintain accurate chronology.

## 🛠️ Contribution Guidelines

We encourage contributions! Please submit a pull request. Reach out on discord if you have questions on how to get started.
AI-generated "vibe" code is acceptable only if it has been thoroughly reviewed by a human before submission. "Slop" will not be accepted.

## ⚙️ Setting up the Project

1.  **Clone this repository** recursively to include all submodules (including the required third-party packages).
2.  **Follow the Godot build steps** specific to your platform (as we use a modded version).
3.  **Run the editor** and accept the Asset License agreement to securely download the default asset bundle.
