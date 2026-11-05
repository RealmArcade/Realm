# Steam demo setup

This document lists what a person must do outside the code to publish the demo on Steam. Everything here is
configuration or partner-site work; none of it is committed.

The demo is its own Steam application with its own depot. The base game stays unpublished, which is what keeps a later
Steam Next Fest appearance possible.

The base game is free to play, so the two applications differ in nothing but the id Steam launches the process under: the
same build is uploaded to both, and no feature may branch on which one is running.

## 1. What the build already does

- `Realm.Client/Services/Steam/SteamApplicationIds.cs` holds the demo's application id (`5537420`) and the base game's
  application id (`5372260`) in one place.
- `SteamRuntime` (an autoload in `project.godot`) starts Steamworks once per process, pumps its callbacks every frame and
  shuts it down when the window closes.
- Steamworks initialises against whichever application Steam launched the process as, and the process never relaunches
  itself into a fixed id. One build is shipped as both applications, so a relaunch would drag a retail install back to the
  demo. Running `Realm.exe` directly, outside Steam, leaves Steamworks off and Steam unoffered: those players play unverified
  under their local name and cannot sign in, because a ticket is the only identity the authority accepts.
- Without Steam the game still runs: play, lobbies and offline matches are independent of Steamworks, so `IsAvailable` false
  is a normal state rather than an error. Only sign-in needs it.
- The social popover offers `WISHLIST ON STEAM`, which opens the base game's store page in the overlay. It is shown only
  while Steamworks is initialised and the base game's application id is configured.
- The registration form prefills the DisplayName with the Steam persona name. It is still validated by
  `DisplayNameRules` like any other name, so an unusable persona name is reported instead of being rewritten.
- `INVITE FRIEND` in a lobby room lists this player's Steam friends and invites the ticked ones through a private Steam
  lobby of that player's own, which carries the match lobby's id as metadata. Accepting the invitation in Steam turns
  that id into the ordinary registry join, so the host and every guest can invite and the match keeps its one lobby. A
  Steam lobby belongs to the application that created it, so an invitation between a demo player and a base-game player
  is not guaranteed to work; during the demo that costs nothing, because everyone plays the demo.

## 2. Values

The two application ids are committed in `SteamApplicationIds`, which is where the build states the applications it is
shipped as. The authority accepts both ids from `Steam:AppIds`, so a ticket from either edition verifies. Leaving
`FullGameAppId` at `0` hides the wishlist button rather than breaking a build.

| Value | Where it goes |
| --- | --- |
| `steam_api64.dll` | `ThirdPartyBinaries/steam_api64.dll`, copied from the Steamworks SDK under `redistributable_bin/win64/` |
| Steamworks SDK version | keep `Steamworks.NET` and the native library from the same SDK release (`Steamworks.NET` 2024.8.0 is SDK 1.60) |
| Steam publisher Web API key | `Steam:WebApiKey` on the account authority only, never in the build (see `docs/account-setup.md`) |
| Accepted application ids | `Steam:AppIds` on the account authority, both ids, so a ticket from either application verifies |

`steam_api64.dll` is required only at runtime, so a build without it still compiles; the publish workflow refuses to
produce a Steam build without it. Every build copies it from `ThirdPartyBinaries/` into `Realm.Client/`, which is where
the engine looks for an unmanaged library in a run from the editor. An exported build looks beside `Realm.exe` instead,
which is where the publish job puts it. Placing it only in the assembly output folder is not enough: the game starts
normally and reports Steamworks as unavailable.

The library must also come from the SDK release the managed wrapper was built against. A mismatched pair still loads, so
the failure is not a missing file: a pre-1.60 library under the 2024.8.0 wrapper makes `SteamAPI.Init()` throw
`EntryPointNotFoundException` for `SteamAPI_ISteamClient_GetISteamGameSearch`, which leaves Steamworks off with the
reason visible only in the output log. `steam_appid.txt` and a running Steam client cannot help with that; only the
matching library can.

The 32-bit `steam_api.dll` and the two `.lib` import libraries are not used by this project: the game ships x64 only, and
`Steamworks.NET` reaches the library through P/Invoke rather than linking against it.

## 3. Configure the authority the demo talks to

A Steam player installs the demo, presses Play and must sign in, so the account authority in `servers.json` has to be
public and reachable over TLS. Follow `docs/account-setup.md`, then:

1. Set the repository variable `SERVERS_JSON` to the registry list the demo should contain. The publish workflow writes it
   to `Realm.Client/servers.json`, which is not committed. Include `AccountAuthorityUrl` pointing at the authority so a
   client bootstrapped from any other node still signs in against the right one.
2. Confirm `GET /auth/providers` on that authority reports `steam` as available, and that `AccountAuthority` is `true`
   there. An unavailable provider means `Steam:WebApiKey` or `Steam:AppIds` is missing.
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

For local testing, place `steam_appid.txt` containing the id you are testing under (`5537420` for the demo, `5372260` for
the base game) next to `Realm.exe`, or in the project directory when running from the editor, with the Steam client
running and that application's Developer Comp package on your account. This file is the only thing that tells a direct
executable launch which application it is, which is why it must never be shipped and why an install without it has to be
started through Steam.

## 5. Upload with SteamPipe

SteamPipe upload is the one step that cannot be produced by a GitHub-hosted runner without storing a Steam build
account's credentials, so run it from a trusted machine:

```vdf
"AppBuild"
{
	"AppID" "5537420"
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
steamcmd +login <build account> +run_app_build ..\scripts\app_build_5537420.vdf +quit
```

The build account needs *Upload* rights on the demo's depot and must complete Steam Guard on the machine once.

## 6. Make it playable

1. Demo app → **Builds** → set the uploaded build live on the `default` branch → **Preview Change** → **Set Build Live Now**.
2. Demo app → **Installation** → confirm the launch option runs `Realm.exe` with no arguments.
3. Complete the demo's build and store-presence checklists, submit for review, then choose **Release Demo…** on the demo's
   landing page. The base game remains Coming Soon.
4. Republish the base game's store page so its **Download Demo** button appears.
5. Install the demo from a clean Steam account and check: the account screen prefills the persona name, `SIGN IN WITH STEAM`
   signs in without opening a browser, and a match starts (the authority is spawned from the install directory beside
   `Realm.exe`).

## 7. Next Fest

Opt in from the **base game's** landing page, not the demo's. Registration windows are per edition, a title can take part
once, the demo must be live for the whole festival, and the base game must stay unreleased through it. Submit the demo for
review at least three weeks before the festival (five weeks for Press Preview).
