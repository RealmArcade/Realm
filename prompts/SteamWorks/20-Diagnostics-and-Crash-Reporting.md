# 20 — Replace the Diagnostics the Admin Server Provided

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** nothing for the local export and crash capture, which work against the current build and are committed in stage 1 of the migration branch; 22 for closing the gap the admin server leaves when it is deleted, which is when requirement 6 is decided.
**Unblocks:** nothing; it closes the observability gap the migration opens.

**Commit the local half early in the branch.** Requirements 1–5 and 7 are a self-contained export and crash-capture feature, and they are worth most immediately before the transport change in task 02, when a player saying "it felt laggy" or a disconnect reason is the thing you need to see. They are committed with stage 1 of the migration branch, before the networking stage, and only requirement 6 — whether a hosted error service is ever adopted — is left for the end of the branch. This task is not two deliverables to juggle: it is one task whose local half is committed early in the lineage and whose decision is written at the end.

## User story

As the developer, I want a player's crash and the authority's log to reach me without a server I operate, so I can still diagnose a map-script trap or a startup failure on a machine I have never seen.

As a player, I want to report a problem with one button instead of explaining where the log file is, and I want nothing sent anywhere unless I decide to send it.

## Context

The admin server carried whatever the project used to watch a running deployment: map metrics, engagement records and the log paths the portal exposed. Those numbers had no player-facing consumer, and they are deleted with the project (task 22) rather than replaced: a play count nobody reads is not worth a service. Steam exposes no crash-reporting or telemetry API to games, so there is no platform replacement to adopt either: this is the one capability the Steam-first migration genuinely removes without a substitute, and the answer is a local export the player chooses to send.

The game already writes what is needed locally: `GameLog`, the authority's log and console capture, the map-script compile and trap output, and the authority log path handed back to the client when a match starts. What is missing is a way for a player to hand that to the developer, and a record of an unexpected failure.

Do not build a metrics service, a dashboard, a session counter or an analytics pipeline. That is the thing this migration is deleting.

## Requirements

1. **One diagnostics export action.** A control in the settings or help screen opens the log folder, and a second produces one zip of the current session's logs and a small environment summary (game build, platform, GPU and driver, language, whether Steam initialised, the AppID, the map and its Workshop item when one is loaded, the authority PID, and the last match's result line). It writes the zip beside the logs and reveals it. Nothing is uploaded.
2. **Capture an unexpected failure once.** Install a handler for unhandled exceptions in the client and in the authority that appends a short crash file: the exception type and message, the managed stack, the last lines of the log, and the same environment summary. On the next start, if a crash file exists, show one non-blocking line offering to open the folder or export diagnostics. Do not loop the prompt, and do not show a modal.
3. **Surface a map-script failure to the player.** When the authority cannot instantiate a map's script or the script traps, the client shows a readable line naming the map and stating that the map's script failed, with the log path in an advanced field. A trap in somebody else's map must not look like a game crash.
4. **Keep the existing logs, and bound them.** Retain a small fixed number of sessions and cap the total size of the log folder, deleting the oldest. Do not grow without a limit on a player's disk.
5. **No silent network traffic.** There is no automatic upload, no analytics, no crash ping and no unique player identifier in an exported file. The SteamID is included only when the player chooses to include it, and the export screen says so before it writes.
6. **If a hosted error service is ever adopted, decide it as its own task.** A third-party error or crash service would be a new integration with its own account, keys, quotas, data-processing terms and privacy-policy clause. Do not add one here, and do not half-add one behind a setting. The local export is the complete answer for a game of this size; a hosted service is a separate decision taken when the volume justifies it.
7. **Do not log anything the match thread cannot afford.** The existing rules stand: no synchronous disk I/O on a simulation tick, and a bounded producer-consumer buffer drained off-thread. This task adds crash capture and export, not logging inside the tick.

## UX

- Settings or Help has one "Diagnostics" row with "Open log folder" and "Export diagnostics".
- The export writes a zip and reveals it in the file manager; it does not open a mail client and does not ask for an address.
- After a crash, one line on the main menu: "Realm closed unexpectedly last time. Export diagnostics?" with Export and Dismiss.
- A map-script failure in a lobby or a match names the map and links to its Workshop page, because the fix usually belongs to the author.
- Nothing in the primary flow mentions telemetry, metrics or reporting.

## Out of scope

- Analytics, engagement metrics, session counts, funnels or a metrics dashboard.
- A backend, an HTTP endpoint, a database, a queue or an inbox service.
- Automatic crash upload, opt-out telemetry or a vendor SDK.
- Replay capture and automated bug reports.
- Server-side monitoring of a dedicated server (Realm does not ship one).

## Acceptance criteria

- A player can export one zip from the settings, and it contains the session logs and the environment summary.
- The zip is written locally and nothing is transmitted.
- An unhandled exception in the client or the authority produces a crash file, and the next launch offers the export once.
- A map-script trap is reported as a map failure with the map named, not as a game crash.
- The log folder does not grow without a bound across many sessions.
- The export states whether the SteamID is included, and includes it only when the player chooses.
- No new project, endpoint, database, SDK or configuration key exists for metrics.
- The solution builds and the game still starts, hosts, joins and plays with no diagnostics service available.

## Pitfalls

- Do not call this telemetry. It is a file the player chooses to send.
- Do not upload anything automatically, not even once "to see if it works". A silent upload is the behaviour this repository's users would least expect.
- Do not put a SteamID, a session ID or a machine identifier in the export by default.
- Do not write logs from the simulation tick, and do not let crash capture allocate on the match path.
- Do not add a hosted error service "for later" behind a setting nobody can see. Decide it when it is needed, in its own task, with its own privacy clause.
- Do not keep the admin server alive to preserve metrics. It is deleted; this task is what replaces the useful part of it.
- Do not let an export include another creator's Workshop content. Logs and environment only.
