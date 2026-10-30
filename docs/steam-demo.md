# Steam demo setup

This document lists what a person must do outside the code to publish the demo on Steam. Everything here is
configuration or partner-site work; none of it is committed.

The demo is its own Steam application with its own depot. The base game stays unpublished, which is what keeps a later
Steam Next Fest appearance possible.

## 1. What the build already does

- `Realm.Client/Services/Steam/SteamApplicationIds.cs` holds the demo's application id and the base game's application id
  in one place.
- `SteamRuntime` (an autoload in `project.godot`) starts Steamworks once per process, pumps its callbacks every frame and
  shuts it down when the window closes.
- A published build asks Steam to restart it under the demo's id, then initialises against that id. A development run skips
  the restart and initialises directly, so pressing Play in the editor works when the Steam client is running.
- Without Steam the game still runs: every account feature is independent of Steamworks, so `IsAvailable` false is a normal
  state rather than an error.
- The main menu and the post-match screen show `WISHLIST ON STEAM` and open the base game's store page in the overlay,
  but only when the base game's application id is configured and Steamworks initialised.
- The registration form prefills the DisplayName with the Steam persona name. It is still validated by
  `DisplayNameRules` like any other name, so an unusable persona name is reported instead of being rewritten.

## 2. Values still to be supplied

| Value | Where it goes |
| --- | --- |
| Demo application id (expected `1416294`) | `SteamApplicationIds.DemoAppId` — confirm it on the demo's App Admin page |
| Base game application id | `SteamApplicationIds.FullGameAppId` — `0` hides the wishlist buttons |
| `steam_api64.dll` | `ThirdPartyBinaries/steam_api64.dll`, copied from the Steamworks SDK under `redistributable_bin/win64/` |
| Steamworks SDK version | keep `Steamworks.NET` and the native library from the same SDK release |

`steam_api64.dll` is required only at runtime, so a build without it still compiles; the publish workflow refuses to
produce a Steam build without it.

## 3. Configure the authority the demo talks to

A Steam player installs the demo, presses Play and must sign in, so the account authority in `servers.json` has to be
public and reachable over TLS. Follow `docs/account-setup.md`, then:

1. Set the repository variable `SERVERS_JSON` to the registry list the demo should contain. The publish workflow writes it
   to `Realm.Client/servers.json`, which is not committed.
2. Confirm `GET /auth/providers` on that authority reports `discord` and/or `steam` as available, and that
   `AccountAuthority` is `true` there.
3. Set a session lifetime long enough for a play session; sessions are restored on launch and rejected silently when they
   expire.

## 4. Build the depot content in Actions

Run the `Build` workflow with `publish` enabled and a version number. It produces:

- `Realm-Windows-x64_<version>.7z` — the archive players could download from a website.
- `Realm-SteamPipe-Windows-x64` — the same `publish/` tree, zipped with its folders intact. This is the artifact to give
  SteamPipe; extract it and upload the extracted folder as the depot content root.
- `Realm-AdminServer-Windows-x64_<version>.7z` — the authority, when you also want to host it.

The publish job copies `steam_api64.dll` beside `Realm.exe` and fails if it is missing or if `steam_appid.txt` would ship.
`steam_appid.txt` is gitignored and must never appear in a depot: it makes every install behave like a development run.

For local testing, place `steam_appid.txt` containing `1416294` next to `Realm.exe` (or in the project directory when
running from the editor) with the Steam client running and the demo's Developer Comp package on your account.

## 5. Upload with SteamPipe

SteamPipe upload is the one step that cannot be produced by a GitHub-hosted runner without storing a Steam build
account's credentials, so run it from a trusted machine:

```vdf
"AppBuild"
{
	"AppID" "1416294"
	"Desc" "Realm Demo <version>"
	"BuildOutput" ".\build_output"
	"ContentRoot" ".\content"
	"SetLive" ""

	"Depots"
	{
		"<demo depot id>" "depot_build_<demo depot id>.vdf"
	}
}
```

`depot_build_<id>.vdf` maps the extracted artifact to the depot root:

```vdf
"DepotBuildConfig"
{
	"DepotID" "<demo depot id>"
	"ContentRoot" ".\content"
	"FileMapping"
	{
		"LocalPath" "*"
		"DepotPath" "."
		"recursive" "1"
	}
	"FileExclusion" "steam_appid.txt"
	"FileExclusion" "*.pdb"
}
```

Then:

```
steamcmd +login <build account> +run_app_build ..\scripts\app_build_1416294.vdf +quit
```

The build account needs *Upload* rights on the demo's depot and must complete Steam Guard on the machine once.

## 6. Make it playable

1. Demo app → **Builds** → set the uploaded build live on the `default` branch → **Preview Change** → **Set Build Live Now**.
2. Demo app → **Installation** → confirm the launch option runs `Realm.exe` with no arguments.
3. Complete the demo's build and store-presence checklists, submit for review, then choose **Release Demo…** on the demo's
   landing page. The base game remains Coming Soon.
4. Republish the base game's store page so its **Download Demo** button appears.
5. Install the demo from a clean Steam account and check: the overlay opens from the wishlist button, the registration form
   prefills the persona name, sign-in reaches the authority, and a match starts (the authority is spawned from the install
   directory beside `Realm.exe`).

## 7. Next Fest

Opt in from the **base game's** landing page, not the demo's. Registration windows are per edition, a title can take part
once, the demo must be live for the whole festival, and the base game must stay unreleased through it. Submit the demo for
review at least three weeks before the festival (five weeks for Press Preview).
