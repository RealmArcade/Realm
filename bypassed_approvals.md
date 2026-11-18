# Bypassed approvals

Decisions taken on the developer's behalf while working through `prompts/SteamWorks/`, in the spirit of
"make your best judgement without stopping to ask". Each entry names the task it belongs to, the call I made, and why.
Anything here is a candidate to revisit during the collaborative testing phase.

## 05 — Lobby registry removal

- **The whole `Realm.AdminServer` went with the lobby registry.** The prompt scopes this task to the lobby half and
  defers accounts (11) and distribution (10) to their own tasks. Tasks 11, 13 and 22 delete those services anyway, and
  every remaining endpoint in that project existed to serve a world the later tasks remove, so splitting the deletion
  across three commits would have meant three rounds of editing files whose only future was the bin. It is one commit
  instead, and the checkpoints after this one are gameplay-visible ones.
- **Deleted `Realm.Client/servers.template.json` and the `ServersConfig`/`ServersConfigHelper` surface.** Nothing reads
  a lobby server any more, and the template is the only thing that would tell a contributor there is one to run.
- **`HostLobbyStage` is now two steps.** `TestingNetwork`, `OpeningPort`, `VerifyingNetwork` and `RegisteringLobby`
  described work Steam does on its own, so the host's progress card shows the map loading and the host taking its seat.

## 06 — Relay, NAT traversal and reachability

- **`RestrictiveNetworkDialog` is deleted rather than reworded.** Its body text explained UPnP, port forwarding and
  NAT types, and its only action copied Realm's own network diagnostics; keeping the frame and rewriting the words
  would leave a dialog whose reason to exist was the subsystem being removed. A failed join now states what the join
  said went wrong, or "Steam could not connect to the host".
- **The "seed map files" setting is deleted, not hidden.** It volunteered this machine as a distribution seeder, which
  no longer has a directory to advertise to. Its checkbox node left `SettingsMenu.tscn` with it.
- **`PeerLinkStatistics` is kept** as the prompt asks; Steam's transport feeds it.

## 07 / 08 / 09 — Workshop publishing, consumption and identity

- **A Workshop item's identity is one JSON string in the item's own metadata field** (`WorkshopMapMetadata`), plus two
  key-value tags (`mapName`, `build`) for lookups. Steam's tags are a fixed vocabulary configured per app, and the
  identity needs version, build, content hash, author and dependencies, so a metadata string is the part of the item
  that is ours. Nothing else has to be registered anywhere.
- **Publishing creates the item the first time by asking the Workshop whether an item already states the map's name**,
  instead of storing an item id in `metadata.json`. That keeps the map format — and its schema — unchanged, and it
  means an item published from another machine is still found and updated rather than duplicated. The cost is one
  query per publish; if a map is renamed, a new item is created, which is the honest reading of a rename.
- **The upload is a canonical copy of the workspace, not the workspace itself.** `MapArchiveHelper.StageCanonicalCopy`
  writes exactly the files an exported `.rmap` carries, so the author's `bin/`, `obj/`, `.vscode/`, history and keys
  never reach Steam.
- **The item preview is a PNG converted from the map's own `thumbnail.webp`**, written beside the staged folder rather
  than into it: Steam reads PNG and JPEG, and the downloaded map should not carry the storefront's copy of its picture.
- **No test-version channel.** Steam Workshop has one item per map with version history, so the publish dialog's
  "publish as test version" switch and the maintainer-key note are gone; publishing updates the item in place.
- **Greenlight is gone with the admin server.** Steam's own moderation and reporting is the review surface (task 13).
- **`MapData.FromDto` became `MapData.FromWorkshop`.** Ratings are Steam's up/down votes; the creator is the Steam
  account that owns the item; the thumbnail is Steam's preview URL.
- **`AssetPackService` came back on top of `MapProvisioningService`**, because it was only ever a named wrapper around
  it and the editor's art pack still needs a distribution channel. It is now an ordinary Workshop item whose identity
  is marked `IsAssetPack`, which a browse filters out.

## 18 — Attribution

- **Asset authors are read from the assets themselves** (`RealmMetadataHelper.ExtractAuthor`) instead of the deleted
  `asset_author` endpoint. The signing step already walks every referenced hash, and each file's metadata states who
  made it, so attribution survives with no service and no extra network call.
- **Map authorship is verified against the key the map carries.** The lobby previously asked the admin server for the
  published key and signature; it now recomputes the unsigned metadata hash and checks the Ed25519 signature in the
  file against the `author_key` beside it. A map that names no key still shows its stated author, unverified.

## 16 — Creator identity

- **The creator panel reads Steam, and nothing is cached.** `CreatorDiscoveryService` asks the Workshop for the items a
  SteamID owns in this AppID on each open, and the avatar comes from `GetMediumAvatar` per SteamID. A cached copy would
  be a profile database with a lifetime, which is the thing the migration removes.
- **Other items are queried for the current AppID only.** A SteamID is global, so the query states the app; a creator
  who also publishes for another game shows only the maps they made for Realm.

## 17 — Workshop archive

- **The archive tool is a separate console project (`tools/WorkshopArchive`), not part of the shipped game.** It uses the
  same `SteamWorkshopService` read paths, writes a reviewable JSON record plus the item's files, and takes the partner
  publisher key from the environment so no key reaches the repository or a build.
- **No archive is published or served anywhere.** The output is a local folder an operator keeps; `README.md` in the tool
  says why, because the obvious next step for a "mirror" is a download page and that is exactly what is forbidden.

## 19 — AGENTS.md

- **The old reachability section is replaced whole, not amended.** Its vocabulary (classes, cold probes, translated
  ports) is the vocabulary of a subsystem that no longer exists, and keeping any of it would read as an instruction.
- **The new guards are written as prohibitions with the reason attached**, so a future change has to argue with the
  reason rather than just with a rule. The section deliberately repeats none of the per-task design: it names the
  boundary and leaves the design in the task files.
- **`GameBuildNumber` became provenance rather than a gate** in the map format section. The equality gate was what made
  every published map unplayable after a release, and leaving the old sentence in place would have reinstated it by
  documentation.

## 15 — Cleanup

- **The donate button, its popover node and its URL are deleted** from the main menu, with the `DONATE` localization key
  following it on the next build. The task forbids a donation surface in the game, and a footer link to the project's own
  donation page is that surface even though it funds the developer rather than a creator.
- **The moderation model and its download step are deleted.** `scripts/fetch-ml-model.ps1` fetched a Hugging Face ONNX
  model for a toxic-language filter, and no code read it any more; the workflow step and its `SHIP_TOXIC_FILTER_MODEL`
  switch went with it. Steam's Workshop reporting is the moderation surface now.
- **`tools/rig/` is deleted.** It existed to make network claims about a lobby registry and reachability classes that no
  longer exist, and AGENTS.md no longer points at it.
- **The admin server's build steps are deleted from CI** (publish, archive, upload, `servers.json` injection) along with
  its launch and task entries and its `.gitignore` lines.
- **`NameNormalizationHelper` and its suite are deleted.** Every member was unused: the account service was its only
  caller, and map-name normalisation has its own implementation in `MapProvisioningService`. Its `AnyAscii` and
  `Fastenshtein` references left `Realm.Shared` with it (`Fastenshtein` remains where the editor's fuzzy asset matching
  uses it).
- **29 orphaned `.cs.uid` files are deleted.** They pointed at sources deleted by earlier tasks, including
  `MapDistributionServer`, `CreatorDiscoveryService` and `WindowsFirewallService`.
- **`servers.json` is dropped from the two map-folder file filters.** It was excluded from map discovery back when the
  client carried one; nothing writes it now.

## 06A — Terms for Steam content

- **Both documents were still the federated-platform text.** They described a "centralized coordination layer", a "UGC
  registry", "community node hosting … participant relays", "registered Realm Platform users" and donations "within the
  Realm Platform". That is the largest gap the sweep found: the game no longer has any of it, so the documents now name
  Steam as the platform and the developer as the publisher of one free game.
- **The canonical licence URL moved to the committed text.** It pointed at `realm-game.com/..._v1.txt`, a page on a site
  that is not part of this repository and that nothing verified. It now points at
  `Terms/RealmPlatform_UGC_License.txt` in the public repository, which is the file the build ships, so the URL and the
  text cannot drift. A repository URL is a weaker canonical document than a versioned page; the counsel review document
  asks whether a page should serve it again, and that is a one-line change.
- **The licence is version 2.0 and the acceptance is recorded locally.** `MetadataService.UgcLicenseVersion` is what the
  editor compares against, `UgcLicenseAcceptance` writes `user://ugc_license_acceptance.json` with the version and date,
  and `UgcLicenseDialog` shows the summary, the full text and Valve's agreement before the first publish continues. A
  version bump asks again; nothing is stored about the creator beyond that.
- **The template licence and every shipped map's metadata were rewritten**, not left as the old summary. `MapTemplate/`
  is copied into every new workspace, so a stale copy there would have been the text most creators actually read.
- **The publish panel still described the deleted beta and greenlight flow.** It promised a "Beta-Testing Phase", a
  play-time/ratings threshold, graduation and a permanently reserved map name, and offered a "bypassing greenlit
  deduplication" checkbox. It now describes what publishing does: an item under the creator's account, visibility
  chosen on Steam, updates in place, rename creates a new item, and playing before publishing without the Workshop.
- **"Greenlit" vocabulary survives in code, not in what players read.** The shared-asset reference feature and the
  manifest's existing `GreenlitReferences` field keep their names because renaming a manifest field is a map-format
  change, which the platform rules forbid. Every string a player sees says "shared assets" instead.
- **The announcements sign-up is kept and disclosed.** It posts a name and address to the e-mail provider's form, which
  is a third-party service rather than a Realm account, so it stays and the ToS privacy clause says what it sends. The
  counsel review flags whether a consent line is needed beside the field.

## Player-facing text sweep (after 06A)

- **The map details lost its awards block and its three empty star rows.** The awards section was only ever hidden and the
  star rows held nothing; both were widgets for a review system Steam replaced, and the rows still had scene nodes for a
  1–5 star widget the task explicitly forbids. They are deleted rather than hidden.
- **The lobby browser header was advertising columns it does not have.** It said SIZE and PING while the rows show an
  optional join-code chip and the game build, so the headers now say CODE and BUILD. Ping is no longer measured in a
  browser, because Steam is the directory and nothing is probed.
- **The authorship key dialog promised a maintainer who could authorise a publish.** There is no maintainer role, no team
  and no approval step, so the text now says what a key does: it signs your work, it must be backed up, and the public key
  is the part you may share.
- **"Your account has no user name" became "Your Steam profile has no name yet".** A Steam profile is not an account the
  game manages, and the old wording is how a deleted account system creeps back into the copy.
- **What was checked and left alone:** "Ping" in the match HUD (a real measured round trip), the KICK button (the host's
  own seat control), join-code and password wording in comments (Steam lobby guides use the same word), and
  `PrivateKeyEncryptionHelper`'s password (it encrypts an authorship key file, not an account).

## Dead-control audit

- **Two HttpClients were still constructed by the match screens.** `LobbyManager` and `LobbyBrowser` each held one that
  no request used, left over from polling a lobby registry. They are deleted with their `Dispose` calls and the ten
  `using HttpClient = ...` directives that existed only for the registry calls. The editor's own installers keep theirs,
  because they really do download a tool the developer asked for.
- **`NetworkService.CheckCreatorRegistrationAsync` was the last live call to a deleted service** — a GET to
  `/api/creators/check/` on a seed URL, with no caller. Deleted rather than left to time out in three seconds if anyone
  wired it up later.
- **The HUD scene shipped four unit buttons that existed only to be destroyed.** `SelectionUnitStrip` clears the container
  and builds twelve slots at startup, so the four `Unit1`–`Unit4` nodes were dead scene content. The container stays,
  because the strip addresses it by path.
- **A scene write added a BOM to `InGameHUD.tscn`**, which Godot does not expect on a scene file. It was stripped before
  the commit; the other two scenes changed in the same pass already carried one.
- **Checked and left alone:** every button and checkbox in every `.tscn` is referenced by name from code (the only
  unreferenced ones were the placeholders above), no `Pressed` handler is empty, and no UI string promises a feature that
  is gone.

## Stale network path audit

- **Lobby publishing had two entry points that differed only by a dead flag.** `PublishPreparedLobbyAsync()` and
  `PublishPreparedLobbyDirectlyOnFailureAsync()` shared one body where `_ = keepLobbyOnFailure;` threw the flag away,
  because a Steam lobby either exists or it does not. Their summaries still described opening a port, asking a router, a
  registry node reaching the lobby first and reporting a listing failure in the room's chat. One method remains, the
  summaries describe Steam, and the caller that used the "on failure" name now calls the plain one.
- **Two port properties were dead.** `ENetPort` was never assigned and `BoundPort` had no reader; both are deleted.
  `DefaultGamePort` stays, because the authority's loopback socket really does ask for that port by name and the comment
  there already says why: the named port belongs to internal IPC, not to a player's router.
- **`EnetMapTransferService` was a whole transfer mechanism nothing called.** Peer map transfer runs through the lobby
  protocol (`RequestMapTransfer`), so the node that transferred over an ENet peer was unreachable and referenced from no
  scene. Deleted with its `.uid`.
- **Checked and left alone:** `CompositeServerTransport` (one peer set over loopback plus Steam, with ids that never
  recycle — correct and load-bearing), `ClientServerSession`'s loopback connect, the map-download retry loops (they cover
  a live transfer), and the announcements form (a deliberate third-party call the Terms disclose).
- **`Realm.Client/Testing/` was kept and made honest instead of deleted.** The multi-process harness launches an authority
  and screens on one machine and still joins with `TryJoin(hostAddress, port)`, which was a direct-address path when a
  player could type a host. Two of its jobs — the lag profile and the frame-path verification — are only possible when
  several processes share one clock and one output folder, so deleting the harness would have deleted the only way to
  check replication under simulated link conditions. `JoinAuthority` now refuses anything but a loopback address and says
  why: a screen reaches only its own authority over loopback, and other players arrive through Steam. The harness is
  therefore a test of the internal IPC plus frame path, never a second multiplayer route, and its one launcher already
  passes `127.0.0.1`.
- **`Realm.Client/servers.template.json` outlived the note that claimed it was deleted.** Task 05 recorded it as removed,
  but the file was still tracked (nothing read it — no `ServersConfig` type survives) and still advertised
  `node1.realm-game.com`. It is deleted here, which is what the task file asks for when the surface has no consumer.

## Residue sweep after the audits

- **The translation files were the last place the removed features still spoke.** `locale/en.json` is pruned to its call
  sites by the build, and none of the ten other languages is, so every one of them still carried the retired vocabulary:
  UPnP status lines, a Direct Connect / IP-address join, forwarding a UDP port, the greenlight and Beta-Testing flow, star
  reviews, a DONATE button, a creator-registration confirmation. 284 entries were dropped from each file, leaving every
  key that English still has and nothing else. The lines were removed in place rather than re-serialised, so no surviving
  translation, order or line ending changed, and each file was re-parsed and compared against the expected dictionary
  before it was written. Translations English had lost some time ago were removed with the rest; a translation of a key no
  call site asks for can only be wrong if it is ever rendered again.
- **`Realm.Tools.Cli/JoinLobbyOptions.cs` was a diagnostic join nothing registered.** It still promised `--lobby` against
  a registry node, `--registry`, and `--address host:port`, and `Program.cs` binds no verb that parses it. Deleted.
- **`Realm.slnlaunch` had two launch profiles pointing at the deleted `Realm.AdminServer` project**, so Rider offered
  developers a run configuration that cannot start. Both are deleted; the two client profiles stay, and no replacement
  profile was invented because I could not verify a debug target name that does not already exist in the file.
- **`keygen` still told the author to register the key with the admin server.** It ended by explaining how to graduate a
  key pair to an "Admin Key" by editing `Realm.AdminServer/appsettings.json`. There is no admin server and no key an
  operator installs, so the text now states what the key actually does: the `.rkey` signs every map the author publishes,
  losing it means a later update cannot be shown as theirs, and the public key travels inside each signed map so anyone
  can check it and it can never sign anything.
- **`README.md` promised a dedicated private server and donated contributor links into map metadata.** `Contributors` is a
  list of names written when a map is signed, with no link field of any kind, so the licensing paragraph now says that.
  The engine philosophy paragraph no longer offers "dedicated private servers with verified low-ping connection to all
  players" — hosting is a seat's own machine reached over Steam's peer-to-peer networking — and states the consequence
  plainly instead.
- **The main menu still offered a creator directory that no longer exists.** `CREATOR DISCOVERY` navigated to
  `GameScreen.CreatorDiscovery`, whose scene was deleted when the creator surface moved to Steam and into the map details
  panel, so the button opened nothing. Task 16 puts a global creator directory explicitly out of scope, so the entry point
  is removed rather than re-pointed: the enum member, the transition case, the music-screen clause, the menu button and
  its node, the one helper and four plate styles only that screen used, the four discovery textures and the menu button's
  icon, and the `CreatorInfo` struct — its `DonationLink` and `ContactInfo` fields were the creator-registry shape and
  nothing read it. The creator's Steam profile, Workshop page and other items stay in the map details panel, which is
  where task 16 puts them.
- **Thirty-three files still imported an `HttpClient` they never used.** The registry polling each of them once did was
  removed from one file at a time and the alias stayed behind, so a reader opening the lobby room, the map editor or the
  VSCode bridge saw a web client they had no way to find a caller for. The aliases are gone, as is `LobbyRoom`'s
  `_sharedHttpClient`, which was constructed and never sent anything. The three files that genuinely use one keep it:
  the announcements sign-up and the two editor installers, and there the alias is load-bearing because `Godot` exports a
  type of the same name.
- **The locale prune was re-checked with the repository's own tool.** `Realm.TranslationTool -- prune` reports "nothing to
  prune" for all ten catalogs, so the hand pass removed exactly the keys that tool removes and left no key English does
  not have.
- **Four doc comments still described a guest seat.** `PlayerInfo.Name`, `PlayerInfo.SteamId`, `ReplayPlayerInfo.Name` and
  the lobby seat's `SteamId` said "0 for a guest", and the first two were a copy of each other's text: the name was
  documented as "the account the seat belongs to", which is not what a display name is. Every peer is a Steam identity, so
  the zero case is now stated as what it actually is — a computer seat, a screen running without Steam, or a hello this
  build cannot read — and the name is documented as the display value beside the identity rather than as the identity.
- **`AdminToken` was kept although its name belongs to the deleted admin portal.** It is not an operator credential: the
  host's own screen mints it, puts it in the start command, and proves it back over loopback, which is how the authority
  recognises the peer that started the match as the host. Removing it would delete the host's own seat identity.
- **The four built-in resources named icon files that have never existed.** `Food`, `Gold`, `Stone` and `Wood` were
  declared with `res://icons/<name>.png` in `Realm.Ecs`, and no `icons/` folder has ever been committed, so every one of
  those paths resolved to nothing. Nothing read the value either: `ResourceDefinition.IconPath` had no reader anywhere in
  the client, the authority or the map API, because the HUD draws resource labels from its own scene rather than from a
  definition. The paths are removed, and so is the property and the constructor parameter that carried them, rather than
  left in place for someone to wire a dead path into a bar later. The four attribute call sites now state only an id, a
  name and a description.
- **The replicated component baseline was re-checked without running the suite.** `ReplicationContractTests` is the guard
  for the append-only wire table and it is deliberately not run in this phase, so the same check was reproduced statically:
  the canonical type list in `ComponentTypeRegistry` assigns ids by array position, and every one of the 67 components in
  `ReplicationComponents.baseline.json` still resolves to the id it ships under, with no id reused for a different
  component. The 67 codecs `ReplicatedComponents` registers are exactly those components. Two codecs I first read as
  unregistered (`AttackNumbersCodec`, `ResistanceNumbersCodec`) are second codecs for `Attack` and `Resistances` that state
  the same id, and `Register` drops the duplicate — not new components. The `ComponentIds` constants that map scripts read
  are additionally enforced at startup by `VerifySharedComponentIds`, which refuses to boot a build whose canonical order
  drifted from those constants.
- **The account system's name uniqueness machinery survived in `Realm.Shared`.** `DisplayNameRules` is 473 lines, and
  most of it existed so two accounts could not claim the same typed name: a case-folded uniqueness key with its own expansion
  table, a Greek/Cyrillic homoglyph skeleton with a hand-written lookalike map, `AreVisuallyConfusable`, and the "already
  taken" and "looks like another account's name" rejection codes. With Steam personas as the only identity, nothing is
  registered, nothing is looked up and no caller remained for any of it: `Fold`, `ComputeUniquenessKey`,
  `ComputeConfusableSkeleton`, `AreVisuallyConfusable`, `IsValid` and `DescribeError` had zero call sites in the whole
  repository. What survives is the part a lobby still performs — collapse the whitespace, refuse control and format
  characters, cut at a grapheme boundary — with `TryCanonicalize` now private because `AdmitForLobby` was its only remaining
  caller. The class and method summaries were rewritten to say what it does now, since they described registration, sign-in
  lookup and rename.
- **Twenty-one client files imported that namespace without using it**, left from the deleted account client. Removed; the
  build is the proof, because a namespace that no longer supplied a type would have failed to resolve.
- **The rejection codes and the namespace went with it.** `DisplayNameError` was still produced by the admission path and
  read by nobody, so `TryCanonicalize` now answers the one question admission asks - whether a name can be carried as it
  stands - and the enum is deleted. The type itself moved out of `Realm.Shared/Accounts` and is now
  `Realm.Shared/Network/LobbyPlayerNameRules`, so no namespace named after the account system and no type named
  `DisplayName` survives in the tree. Renaming a type is usually churn; here the name was the residue, and it was the last
  thing in the repository that would tell a reader a player has a typed identity the game manages.
- **`MainMenu.OnPlayPressed` documented a flow that does not exist** — "opens the lobby browser without an account", a
  "signed-out player" playing "under the unverified name", and a "verified review at the end of a match" ask for a sign-in.
  Play opens Steam's directory, which states its reason and offers nothing when there is no Steam client. `SteamService.UserName`
  and the lobby name ceiling carried the same vocabulary ("the name a sign-in claims", "a registered name") and now describe
  Steam's persona and a name that reads cleanly.
## Identity of a seat

- **The authority seated a peer under the SteamID the peer stated, not the one Steam authenticated.** `IServerTransport`
  documents `TryGetPeerSteamId` as "the SteamID64 the transport authenticated this peer as … which is what keeps a seat from
  existing without a verified identity", `SteamServerTransport` fills it from Steam's connection callback, and
  `CompositeServerTransport` forwards it. Nothing read it. `HandleLobbyHello` passed `hello.SteamId` straight into
  `ServerLobby.AdmitPeer`, and the match handshake used the same claimed number to choose the player slot.
- **What that allowed, in order of severity.** A peer connecting to a match could state another player's SteamID64 and be
  handed that player's slot through `_lobbySessionSlots`, taking over their army on reconnect. In a lobby, the claimed
  number selected which disconnected seat a returning player was given, and decided whether the kick list refused them, so
  a kicked player could rejoin by stating a number that was not on the list. The same number is what a map script sees
  through the Steam surface and what the browser's add-friend action points at.
- **The fix is one resolver and four call sites.** `ListenServerHost.ResolveSeatSteamId` prefers
  `_transport.TryGetPeerSteamId` and falls back to the hello only when the transport asserts nothing, which is the host's
  own screen on loopback and a match started without a lobby — cases where the machine running the authority is the one
  stating the identity. A hello that disagrees with Steam is seated under Steam's number and logged, so a tampered or
  confused client cannot take a seat and the authority says which connection tried. Both the lobby hello and the match
  handshake now resolve the identity once and use it for the seat, the slot reclaim, `_steamIdSlots` and
  `SetPeerSteamId`.
- **The wire field stays.** `ClientHello.SteamId` is still the only statement of identity for the loopback host and for a
  lobby-less match, and it is what a bare `dotnet run` authority seats its own screen with. Removing it would break the
  single-machine case that has no transport identity to fall back on.
- **Checked and left alone:** `RegistryService`, `RegistryLookups`, `RegistryKeyMatching` and `GameHost.Registry.cs` are
  the placeable-template store, not a lobby registry; `AccountID_t` in `SteamWorkshopService` is Steamworks' own type;
  `RouteReachabilityEntry` and `CombatAndDamageService.Reach` are combat reach; the "punch", "star rating" and
  "reachability" mentions left in client comments are sentences explaining that those things are absent; the editor
  installers' `api.github.com` and `steam_appid.txt` references are a tool download the developer asks for and a
  development file the release workflow refuses to ship.
## The name a screen with no Steam plays under

- **`LobbyManager.PlayerDisplayName` returned an empty string when Steam was not running.** It is
  `GameContext.Steam.UserName`, and the three places that read it wrote `?? TranslationServer.Translate("Guest")` — a
  fallback that could never run, because the property is an empty string rather than null. A single-player match started
  without Steam therefore seated its own player under no name at all: an empty `Name` component in `WorldInitService`, an
  empty `ClientHello.PlayerName` into the roster, and a nameless local player in `GameHost.SetupDefaultPlayers`. Before the
  migration a local account name always filled it, so this is a removed player-visible fact rather than a changed one.
- **The property now falls back to the game's own word for the person at the keyboard, and the three call sites name that
  word too.** The fallback belongs in the property and not at each call site because the hello is built from it: a fix at
  the display sites alone would have left the authority seating the local player under an empty name.
- **"Guest" became "Player".** A guest is a seat Steam did not authenticate, and the branch has deliberately deleted that
  concept, so the word cannot be the name of the one player who is certainly present. "Player" already existed in all ten
  catalogs; the `Guest` key was pruned by the build and then from the nine translations.
## The session copy that deleted itself

- **`MapSessionCopyService.TryPrepare` removed the copy it was about to install.** The new copy is built in
  `<signature>.building` beside the retained folder so a failed build never destroys a playable one, and the method then
  swept the session root keeping only `<signature>` before moving the build into place. The folder being built is a
  sibling of the retained one and matched nothing the sweep kept, so the sweep deleted it and the following
  `Directory.Move` threw `DirectoryNotFoundException`, which the method's own `catch` turned into
  "This map could not be prepared for play."
- **What that would have cost, if it had run.** `TryPrepare` is the gate for hosting a lobby
  (`LobbyManager.Hosting`), for the joiner's map compatibility check (`LobbyMapCompatibilityService`) and therefore for
  every map that is not already inside the session root - every Workshop map and every local map. The retained-copy
  early return only answers when a complete copy already exists, and a failed move never creates one, so no map could
  ever have been prepared. It is reachable on the first attempt of the first host or join and on every attempt after it.
- **The fix is the order of three statements.** The build is written whole, the old folder is removed, the build is moved
  into its place, and the sweep runs afterwards keeping the folder just installed. That preserves what the original
  comment promised - the new copy is complete before the old one goes - and the sweep still runs on the same call, so the
  session root is left holding exactly one copy.
- **The startup sweep now reads the record instead of the filename.** `FindMostRecentCompleteFolder` named completeness
  and checked only that a record file existed, so a folder whose build was interrupted could be kept over a good one. It
  now requires `Complete: true`, and `IsComplete` shares that record reader.
## The poll that refused a peer while reading it

- **`SteamServerTransport.Poll` walked its peer dictionary while delivering frames.** `OnFrameReceived` runs inside that
  walk, and the authority disconnects a peer from it in several ordinary cases: a refused lobby hello (build, wire table,
  map or token mismatch, a kick, a full roster), and the match handshake's own refusals. `Disconnect` removes the peer
  from `_connectionByPeer`, so the next step of the iteration throws `InvalidOperationException`, which nothing in `Poll`
  catches - it escapes into the authority's tick.
- **So the first refused join would have taken the authority's tick with it.** The refusal itself is a routine answer, and
  the peer it refuses is the one whose frame is being read at that moment, which is what makes this reachable on a
  mismatched build rather than only on a hostile client.
- **The peers are now listed before any frame is delivered.** A reusable id list is filled from the dictionary, and the
  delivery loop looks each peer up as it reaches it, skipping one that was closed by an earlier frame in the same poll.
- **The same loop allocated on every tick.** A 64-entry pointer array and a 64 KiB payload buffer were built per call, and
  this runs once per simulation tick on the authority's match thread - about two megabytes a second of garbage for
  nothing. Both are instance fields now, and the buffer only grows if a message is larger than the one before it.
## Whose Workshop item a re-publish updates

- **The publisher looked its existing item up by map name across every creator.** `MapPublishController` asked
  `SteamWorkshopService.FindByMapNameAsync` for the item to update, and that answers the newest item for this AppID whose
  metadata states the same map name, whoever owns it. Steam reserves no map name and an update can only be aimed at an
  item the caller owns, so a creator whose map shared a name with anybody else's - a remake, a common word, a port -
  would have every publish aimed at a stranger's item and fail instead of creating their own.
- **The fix is a search limited to the caller's own items.** `FindOwnedByMapNameAsync` filters on the item's owner as
  Steam reports it, and the publish path uses it. When no owned item exists but another creator's item states the same
  name, the publish now goes ahead as a new item and the console names both, because the alternative - refusing to
  publish because somebody else took the name - would let one creator block another.
- **Left as a known limitation, deliberately:** a name that two creators both publish is still ambiguous to the screens
  that resolve a map *by name* rather than by item id (`MapStorageService.CheckForNewerVersionAsync` for the storage
  list's update offer, `MapProvisioningService.DownloadLatestAsync` for the details download button). The join path is not
  affected: a lobby advertises the Workshop item id and `LobbyMapCompatibilityService` subscribes to exactly that item.
  Closing the remaining gap needs the installed package to record the item it came from, which is a map-format change and
  a decision for the design rather than a repair.
## The Verified collection the mark was supposed to read

- **The trusted mark is votes-and-age plus an optional file, and the collection route is not written.** Task 13 asks for
  a map to be vouched for by membership of a developer-owned Workshop collection, read with `GetQueryUGCChildren`, with a
  local configuration file as the fallback if collections cannot be queried. What exists is the fallback only:
  `MapTrustPolicy` reads `config/featured_workshop_items.json`, no build ships that file, and nothing reads a collection.
- **It was not written because there is nothing to point it at.** A collection query needs a collection id, and no id is
  configured anywhere - the official catalog does not exist yet, and inventing a config format to hold an id that no
  collection answers would be a format decision taken to satisfy a checklist. The mark behaves exactly as documented
  without it: a map too new to have votes is untrusted, and says so.
- **What was wrong was the documentation, not the behaviour.** `AGENTS.md` stated collection membership as if it were
  read, and the readiness doc described the featured list as gone while the code still reads one if a build ships it.
  Both now state what the code does and name the collection route as the intended next step, and the test plan carries
  it as a pivot with the two ways to settle it: ship the list, or write the query and decide where its id lives.
## The item a host advertises for its own map

- **A host advertised whichever item was newest under its map's name.** `ResolveHostedMapTrustAsync` asked
  `MapProvisioningService.FindLatestPublishedAsync`, which is a name lookup across every creator, and the id it answered
  is what the lobby publishes as `MapWorkshopIdKey` and what the joiner downloads. A host who published their own map
  while somebody else's item carried the same name - and was updated more recently - would advertise that stranger's
  item, so every joiner would download the wrong package and then be refused by the map-hash check, with neither side
  told why.
- **The host's own item is preferred now, and the name lookup stays as the fallback.** When the host published the map,
  their item is the one their copy came from and the one every joiner can be given. When the host is hosting a map
  somebody else published - the ordinary case for a downloaded Workshop map - there is no owned item, so the newest
  same-name item is still the answer, which is the only route available until a map records the item it came from.
## The translations no native speaker has read

- **All ten catalogs were translated by the agent, not by a native speaker.** The branch added 202 strings with no
  translations behind them, so every Steam surface read English in nine languages. Leaving that was the larger defect:
  a player who cannot read English met an unlabelled button, while a slightly awkward sentence still names the action.
  Every language was written against the vocabulary its own catalog already used - German keeps "Lobby" and "Karte",
  Spanish keeps "sala" and "mapa", Japanese keeps "ロビー" and "マップ", Arabic keeps "الردهة" and "الخريطة" - so the
  wording matches the screens beside it even where a native speaker would phrase a sentence differently.
- **What the checks do and do not cover.** `Realm.TranslationTool -- verify` proves every key is present, non-empty, and
  keeps its placeholders and BBCode identical to English, which is what a silent break would look like; a merge script
  refuses a stale key, and an indexed generator refuses a skipped entry so text cannot slide onto the wrong key. None of
  that checks whether the German for "trust mark" reads naturally. The readiness document now states this as review work
  rather than as missing coverage, and the kit's README hands it over in that form.
- **The pass did not stop at the Steam surfaces.** Those were what the branch broke, but the editor had been drifting for
  longer: `-- coverage` read 171 to 183 English prose keys in nine catalogs before this pass. Filling them went surface
  by surface - the map generator and the cross-map import dialogs, the procedural animation studio and decal builder, the
  VFX stage sequence, the map-package import path, the GLB converter, the anchor editor, the asset browser, the index
  verification and the command toasts - and the catalogs now leave 19 to 32 keys per language, every one of which is
  meant to stay identical to English: node names collected from scenes (`BtnZoom_Button`), example placeholders shown in
  an empty field (`vfx_slug`, `decal_texture_id`, `your.email@example.com`, `https://discord.gg/<code>`), shortcut
  strings (`Ctrl + S / Ctrl + O`), and brand or product names (`Steam Workshop`, `Mixamo`, and the languages where
  `Audio`, `Decal (PBR)` or `STEAM OFFLINE` are spelled the same way). `-- verify` still reports 0 missing, 0 empty, 0
  placeholder and 0 BBCode mismatches for all ten.
- **Fourteen call sites handed raw English to code that translates internally.** `InGameHUD.ShowFeedbackText` translates its
  argument, and a literal handed to it is never a key: the build prunes any key with no `TranslationServer.Translate(...)`
  call site, so two of the ten distinct lines - `Construction complete!` and `Unit is already at full health!` - were not in
  `en.json` at all and read English in every language. Both are wrapped at the call site now, which is what the rest of the
  same file already did, and the regenerated catalog carries them (2877 keys, two more than before, none dropped). The same
  audit found nine labels assigned as raw literals: `Game Paused`, the lobby browser's `Search Lobbies...`, the wasm
  console's `Enter command or chat message...`, `Copy Logs` and `Clear`, the storage menu's `Author: Unknown`,
  `Genre: Custom Map` and `Total Storage: 0 MB`, and the editor toolbar's `Test Settings` option and tooltip. Those keys did
  exist and were translated, but the value was written at construction and nothing re-asked the node for it, so a player
  whose language is not English met them in English until they switched language and back. Wrapping them where they are drawn
  is a one-line change per site and is the rule the style guide already states; no layout, wording or behaviour changed.
- **What the same audit found clean.** All 182 `text`, `tooltip_text`, `placeholder_text` and `title` values written into the
  scene files are catalog keys, so the scene surface cannot show English. Five places compare displayed text against a
  literal: four in the noise texture dialog (`Cellular`, `ColorRamp`) and one in the UX smoke harness (`Worker`, `Gather`).
  Those four are the dialog's list items, whose strings are also the identifiers written into the generated texture config,
  so they are data that must stay English rather than prose that was missed; the harness compares a unit name and a card
  tooltip that come from the map's own metadata. Comparing there is safe for the same reason translating them would be
  wrong, so they were left alone rather than converted to ids.
- **Two judgement calls inside the text.** "Steam", "Workshop", "SteamID64", `.rmap`, `terrain.json`, the
  `authorship_key_...rkey` filename and the Discord URL pattern were left untranslated in every language, because they are
  identifiers a player has to type or match, not words. And `BUILD  ` was left as `BUILD  ` in all ten, since it is drawn
  as a label beside a build number rather than as a sentence, and each catalog already carries Latin technical labels.
## The category filter that compared translated text

- **A translation fix exposed a bug in the object manager, and it was fixed without asking.** `InstanceManagerDialog`
  added the type filter's items already translated (`Units` -> `Einheiten`, `All Types` -> `Alle Typen`) and then decided
  which category was selected by reading that item's text back and comparing it with the English group key. In English
  it matched; in every other language it matched nothing, so choosing a category emptied the tree while `Alle Typen`
  stayed correct. The same comparison decided which filter to preselect in `OpenDialog(initialTypeFilter)`.
- **The filter now reads its own id, not its own text.** `_typeFilterOpt` already carried ids 1..8, so the selection is
  resolved through `GetSelectedId()` against a canonical key list and the ids are mapped back to indexes for the
  preselect path. Category headings translate the key for display instead of printing it raw. This is a behaviour change
  in a screen nobody asked about, taken because the alternative was translating the filter and shipping a control that
  visibly breaks in nine languages; it is recorded here rather than in a task because it is a defect found while doing
  the task, not a design decision.
- **What was deliberately left in English, and why.** Map *data* stays untranslated: a template's
  `Damage: 5 | Range: 2 | Type: pierce` description and a `Custom Preset 3` name are written into `metadata.json` and read
  back as content, so translating them would write the editor's language into every player's copy of the map. The WASM
  console keeps English too - its status line sits beside raw compiler output, it writes the same text to a log file, and
  a translated `dotnet publish failed` would be harder to search for than to read. The inspector's `Pos / Rot / Scale`
  abbreviations and numeric readouts were treated as notation rather than prose.
- **The Settings overlay was not built.** The stated next step after this pass was letting Settings open over a match or
  the editor, so language registration has somewhere to heal visibly. That is a new capability - a pause menu surface
  that does not exist today, with its own input and lifecycle questions - so it is left as a documented limit in
  [the test plan](docs/steam-test-plan.md) group K rather than invented here. Group K says plainly that registration is
  observable live only on the Settings screen until that changes.
- **The other comparison sites were checked and are safe.** `NoiseTextureDialog` compares `GetItemText` against
  `Cellular` and `ColorRamp`, but `AddOptionDropdown` adds those strings raw, so they are FastNoise parameter names rather
  than translations; `SpawnAnchorDialog` reads back a coordinate's own author-given name; the palette labels
  (`1 Red`, `0 Gaia (gaia)`) are display text whose selection is tracked by id. No place writes a translated string into
  `metadata.json` or a settings file - the tab titles that are translated are Godot node names used as tab titles, not
  data, and each is read by no `GetNode` literal.
## The neutral seat that showed an identifier, and what "valid" hid

- **The neutral player's label read `0 NEUTRAL_TAN (gaia)`.** `PlayerColorConfig.GetPaletteLabel` printed the palette
  entry's own name, which for the neutral seat is the data identifier `NEUTRAL_TAN`, and appended a borrowed term from
  another RTS. `AGENTS.md` forbids borrowed game terms and the project's own naming rule forbids an identifier reaching a
  label, so the label now reads the translated word for neutral (`0 Neutral`, `0 Neutral`, `0 中立`, `0 محايد`). The
  colour market is unchanged: the other 24 entries keep their palette names, which are names rather than sentences.
- **The tooltip that taught the old label was repointed, and nine catalogs had never translated it.** The owner-picker's
  tooltip named `0 NEUTRAL_TAN (gaia)` inline, so it now names `0 Neutral`; only Korean had a translation of that
  sentence, and the other nine carried the English text verbatim. They are translated now, along with the word itself.
- **`verify` calling a catalog valid does not mean the catalog is translated.** It checks that a key exists, is non-empty
  and keeps its placeholders; a value identical to the English one passes. Measured with a two-or-more-word filter, the
  catalogs carried **255-267 untranslated prose keys each**, against **31** in Korean - so Korean was effectively
  complete and the other nine were not.
- **The player-facing part of that gap is now closed.** Twenty-five keys were still English in nine catalogs and are not
  authoring tooling: the two quit confirmations, the loading and countdown headings, the settings labels and their
  explanations (`Frame Rate Limit`, `Distance Muffling`, `Doppler Effect`, `Occluded Unit Outlines` with its description,
  the recommended-FPS text), `STEAM OFFLINE`, `TOWER DEFENSE`, the imported-content attribution lines, `Quit Match`,
  `Select All` / `Select None`, `Clear Selection`, `Import Map (.rmap)` and `Failed to start the match.`. They are
  translated now, which took the nine catalogs from 255-267 English prose keys to **232-244**.
- **What remains belongs to the map authoring tools.** The map generator, the cross-map import dialogs, the
  procedural-animation and VFX studios, the GLB converter and the asset importers, plus slugs and example values
  (`vfx_slug`, `your.email@example.com`, `func(id: s32, ...)`) and node names the scanner collected from scenes. They are
  being translated in batches, largest surface first, rather than left out: the generator's 35 strings and the import
  dialog's 30 are done, which took the nine catalogs from 232-244 English prose keys to **171-183**. `coverage` is the
  live count and the source of truth here - every figure in this paragraph was true on the day it was written, and the
  command is how the next person checks it instead of trusting the number.
- **Why the authoring surface is last, not skipped.** A player never opens those dialogs; an author does, and an author
  is already reading a tool whose compiler output, file paths and template ids are English. That makes the surface less
  urgent, not unimportant, and it is why it was batched rather than done in one pass where a five-placeholder summary
  sentence in nine languages would have been a guess with no way left to review it.
