# Account authority setup

This document lists what a person must do outside the code to run accounts. Everything here is configuration; none of it is
committed.

Steam is the only provider. A player's own machine mints a Steam auth session ticket through Steamworks, the authority
exchanges it with Valve for the SteamID64 it links, and nobody opens a browser, registers a password or answers a redirect
URL. A player who cannot or will not do that still plays, unverified, under the local name from their settings.

## 1. Pick the account authority

Exactly one node owns account and DisplayName writes, because global name uniqueness cannot be produced by an
eventually-consistent peer cluster.

1. On that node set `AccountAuthority` to `true` and leave `AccountAuthorityUrl` empty (or set it to the node's own URL).
2. On every other node set `AccountAuthority` to `false` and set `AccountAuthorityUrl` to the authority's base URL.
3. Non-authority nodes refuse account mutations locally and answer `account_not_authority`.

Authority is never inferred on a node with configured peers: a standalone node becomes the authority without any setting,
and any node that lists `Peers` must state `AccountAuthority` explicitly. A node that is a non-authority with an empty
`AccountAuthorityUrl` refuses every account request and says so in its startup log, rather than quietly serving a second,
diverging account database. A node that infers authority while its server list names other nodes logs a warning, because a
cluster configured only through that list cannot be told apart from a single node.

## 2. Generate the replication key pair

The authority signs every account and rename event, and peers verify the signature before applying it.

`Realm.Tools.Admin keygen` is the admin key tool and is not usable here: it writes an encrypted `.rkey` and never reveals
the plaintext private key, while the authority reads `AccountAuthoritySigningPrivateKey` as a raw base64 Ed25519 key.
Generate the pair with OpenSSL, which gives the raw bytes directly:

```bash
openssl genpkey -algorithm ED25519 -out account_authority.pem

# private key: the raw 32-byte seed, at the end of the PKCS#8 DER
openssl pkey -in account_authority.pem -outform DER | tail -c 32 | base64 -w0

# public key: the raw 32-byte key, at the end of the SPKI DER
openssl pkey -in account_authority.pem -pubout -outform DER | tail -c 32 | base64 -w0
```

1. Put the **private** key in `AccountAuthoritySigningPrivateKey` on the authority only.
2. Put the **public** key in `AccountAuthorityPublicKeys` on **every** node, including the authority.

A node with no configured authority public keys refuses every account event. An authority with an empty
`AccountAuthoritySigningPrivateKey` still serves accounts locally but publishes nothing, and says so once in its log.

Neither value ever reaches a player, so the public key does not belong in `servers.json`, in a GitHub variable or in any
artifact: it is a cluster-internal trust anchor, unrelated to the admin keys that authorize admin API calls and to the
author keys that sign maps.

To rotate the pair, add the new public key everywhere first (the list holds more than one), then swap the private key.

## 3. Create the Steam publisher Web API key

1. Open <https://partner.steamgames.com/apps/webapi> while signed in to the Steamworks partner account that owns the
   applications.
2. Create a key for the application, or for a group that contains it. A publisher key works for every application in the
   group, which is what lets one authority accept both the demo and the base game.
3. Set `Steam:WebApiKey` on the authority and `Steam:AppIds` to the applications whose tickets it accepts.

`Steam:AppIds` is a list, and it is what stops a ticket minted for some unrelated game from being exchanged here. Ship both
ids, because the same build is published as both the demo (`5537420`) and the base game (`5372260`), and a ticket is minted
for whichever of them Steam launched the process as:

```json
"Steam": {
  "WebApiKey": "",
  "AppIds": [5372260, 5537420]
}
```

The key is a server secret. It is only ever used by the authority to call
`ISteamUserAuth/AuthenticateUserTicket`, never sent to a client, and never stored in the repository, the published
AdminServer archive or a map workspace. A key that leaks lets someone else verify tickets for your applications; it cannot
sign anyone into a Realm account by itself, because the SteamID64 still has to match a link the authority recorded.

## 4. How the ticket sign-in works

1. The client asks Steamworks for a web API auth ticket, which is bound to the identity string `realm-game`
   (`SteamAuthTicket.Identity`).
2. Steamworks returns the ticket through a callback, and only once the client is running under Steam with the Steam client
   present. A build without `steam_api64.dll`, a direct executable launch outside Steam, or a missing Steam client all leave
   the player unverified instead of failing the game.
3. The client posts the ticket, its application id and the name the player chose to `POST /auth/steam/ticket`.
4. The authority calls Valve with its own key and accepts only a ticket Valve confirms for one of `Steam:AppIds`. A refusal
   is the same `credentials_invalid` for every reason, so the endpoint cannot be used to probe which tickets are real.
5. A SteamID64 already linked signs into its account. A SteamID64 nothing has seen registers an account under the supplied
   name, which has to pass `DisplayNameRules` like any other name. A refused name costs one more ticket, so the client asks
   for a new one and tries again.

Because the ticket is minted and spent locally, there is no `Realm` URL, no return URL, no callback host and **no DNS work**:
sign-in never touches a browser and never resolves a name you own. That is also what makes sign-in reachable from networks
where Valve's community web pages are blocked but the Steam client and its CDN still work, which is the ordinary situation
for players in China.

A standalone build that does not ship Steamworks cannot sign in at all, so the account screen hides the Steam button there
and says why. Those players use their local name and reach Tier 0 and Tier 1 features only.

The client sends the identity and the application id it was launched as; nothing else about the ticket is client-supplied,
and the authority trusts only Valve's answer.

## 5. Give the authority a public hostname and terminate TLS in front of it

Session tokens are bearer secrets and travel from the client to the authority. Terminate TLS at the proxy in front of
AdminServer and never expose the plain HTTP port.

The hostname must be one you control at your own DNS provider and can repoint, never a hosting-specific or tunnel-specific
name. `AccountAuthorityUrl` is compiled into `servers.json` inside each published client, so players carry whatever host was
current when their build was made: moving the authority from a home connection to a cloud host later has to be a DNS change,
or every player needs a new build. Pick the name before the first release and keep it.

Bind Kestrel to loopback (`ASPNETCORE_URLS=http://127.0.0.1:5000`) and let the proxy be the only listener on 443, so the
internal routes are not independently reachable even if the machine's firewall is loosened.

AdminServer does not read `X-Forwarded-For` or `X-Forwarded-Proto`, so behind a proxy every request appears to come from the
proxy's address. `/lobbies` sorts by distance using it, and a loopback address geolocates to nothing; lobby and seeder
registration fall back to the address the client reports about itself. Add `UseForwardedHeaders` with the proxy in
`KnownProxies` and `ForwardLimit = 1` before exposing the node to players.

## 6. Confirm what the node reports

`GET /auth/providers` reports whether Steam is configured and whether this node is the authority. A warning code
`provider_not_configured:steam:Steam:WebApiKey` or `provider_not_configured:steam:Steam:AppIds` means that setting is
missing, and `account_node_is_not_the_authority` means this node cannot issue sessions.

## 7. Admin renames

An administrator renames any account by signing
`account_rename:{accountId}:{canonicalDisplayName}:{nonce}:{timestampUnixSeconds}` with a key listed in `AdminPublicKeys`,
then posting the proof to `POST /auth/admin/rename`. The nonce is consumed once and the timestamp must be within five minutes.
The canonical DisplayName is produced by the same validation the rest of the game uses; sign the canonical form.

## 8. Point clients at the authority

A client signs in against one fixed node, not against whichever bootstrap server it happened to pick. Add
`AccountAuthorityUrl` to the `servers.json` the client ships:

```json
{
  "AccountAuthorityUrl": "https://accounts.example.com",
  "servers": ["https://node1.example.com", "https://node2.example.com"]
}
```

When the field is empty the client falls back to its first server, which is only correct on a single-node network.

Only the authority answers `POST /auth/steam/ticket`; every other node refuses it with `account_not_authority`. Redundancy
therefore comes from a stable hostname in front of the authority rather than from handing out several account authorities,
because two of them would fork DisplayName uniqueness.

## 9. Keep secrets out of the build

The published `Realm-AdminServer-Windows-x64_<version>.7z` is a public download and must never contain a secret. The build
workflow already behaves that way: it runs a plain `dotnet publish` and ships only `appsettings.template.json`, whose secret
fields are empty. Do not add `Steam:WebApiKey` or `AccountAuthoritySigningPrivateKey` to the build.

Set them on the host instead. ASP.NET Core reads environment variables and `appsettings.{Environment}.json` already, so
either works:

* a root-owned `appsettings.Production.json` beside the binary (`chmod 600`), or
* `EnvironmentFile=` under systemd with the same keys, using `__` for nesting:

```
AccountAuthoritySigningPrivateKey=<base64 Ed25519 private key>
AccountAuthorityPublicKeys__0=<base64 public key>
Steam__WebApiKey=<publisher Web API key>
```

GitHub Secrets are only appropriate when the workflow *deploys* the node: a deploy step can write those values onto the
host, and they still never enter the artifact. Keep `SERVERS_JSON` a repository variable, because it ships to every player
and holds only public URLs and admin public keys.

## 10. Sessions stay signed in

`AccountSessionLifetimeHours` is `0` by default, which means a session never expires on its own. The client stores the token and
the account it describes in its user data directory and adopts both at launch without asking the authority, so a player signs in
once and stays signed in until they sign out or the authority revokes the token, and an authority that is unreachable never
leaves them signed out of an online game. Any other value caps a session at that many hours, up to a year.

Nothing is lost by not asking at launch: a session says only who the player is, and everything that could be done with a stale one
is either harmless or needs a fresh proof anyway. The authority is consulted when the player opens the account screen, which is
where a rename made elsewhere or a session revoked elsewhere becomes visible, and the first answer that reports the session gone
drops it.

A session does not outlive the proof behind it. `AuthenticatedAtUtc` records when a Steam ticket was last accepted, and a change
that outlives the session — changing the username — is refused with `step_up_required` unless that proof is within
`AccountSensitiveActionWindowMinutes` (15 minutes by default, never less than one). The client answers the code by minting one
more ticket and signing the same account in again, which stamps the new session; the player clicks once and is never asked for
something they no longer have.

Revocation is the only thing that ends a session early: `POST /auth/logout` revokes the token the caller presents, and a rename
revokes the token it replaces.

## 11. Keep downloads off the authority

A node that owns accounts is usually a small instance, while map and asset traffic is what costs bandwidth. `ServeClientDownloads`
(default `true`) lets the same binary decline that traffic:

* `false` refuses `GET /api/assets/{hash}` and `POST /api/assets/bundle` with `503` and `content_not_served_by_this_node`, and
  says so in its startup log. Manifests, discovery, lobbies and the account routes keep answering, because they are small.
* A client already treats a refusing node as a failed candidate and moves on to the next server or seeder in its list, and the
  bytes it does receive are verified against the expected Blake3 hash before they are stored, so a node that refuses costs a
  client nothing but one unanswered request.

That is how the authority stays a small private instance while the community nodes carry the bandwidth.

## 12. Lock down the internal HTTP surface

The authority holds the Steam publisher key and the replication signing key, so only you and the peers you operate should
reach it.

* The raw collection routes (`/api/data/{collection}`, `/api/players`, `/api/maps`, `/api/admin/bans`, `/api/admin/info`)
  require an admin signature over `admin:{action}`, where the action is `info`, `players`, `maps`, `bans`, or the collection
  name for `/api/data/...`. Send it as `X-Admin-PublicKey` plus `X-Admin-Signature`, or send the timestamped token from the
  same admin key as `X-Admin-Bypass`. `Realm.Tools.Admin info --key <path>` already does this.
* Keep the authority off the public internet: publish a separate mirror node, or allow only peer addresses at the proxy, and
  terminate TLS in front of every node.
