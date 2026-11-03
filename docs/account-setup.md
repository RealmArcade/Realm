# Account authority setup

This document lists what a person must do outside the code to run accounts. Everything here is configuration; none of it is
committed.

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

1. Generate an Ed25519 key pair with the same tooling used for admin keys (`Realm.Tools.Admin keygen`).
2. Put the **private** key in `AccountAuthoritySigningPrivateKey` on the authority only.
3. Put the **public** key in `AccountAuthorityPublicKeys` on **every** node, including the authority.

A node with no configured authority public keys refuses every account event.

## 3. Create the Discord application

1. Create an application at <https://discord.com/developers/applications>.
2. Add an OAuth2 redirect to exactly the URL the authority will serve, for example
   `https://accounts.example.com/auth/discord/callback`. It must match byte for byte.
3. Set `Discord:ClientId`, `Discord:ClientSecret` and `Discord:RedirectUri` on the authority.

The client secret never leaves the authority and is not stored in the repository.

## 4. Configure Steam OpenID 2.0

Steam does not offer OAuth 2.0, and no API key is involved: its OpenID provider returns the SteamID64 and nothing else, with
no attribute exchange for a display name.

1. Set `Steam:Realm` to the site the sign-in belongs to, for example `https://accounts.example.com`.
2. Set `Steam:ReturnUrl` to `https://accounts.example.com/auth/steam/callback`.

Steam sign-in is offered only to a player whose client has Steamworks loaded, because the registration form is prefilled from
the persona name Steamworks already holds for the running session. A standalone build hides Steam and offers BasicAuth and
Discord only, so no Steam credential is ever needed to sign in from one.

## 5. Terminate TLS in front of AdminServer

Session tokens are bearer secrets and travel from the client to the authority. Terminate TLS at the proxy in front of
AdminServer and never expose the plain HTTP port.

## 6. Confirm what the node reports

`GET /auth/providers` reports which providers are configured and whether this node is the authority. A warning code
`provider_not_configured:discord` or `provider_not_configured:steam` means the corresponding settings are missing.

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

## 9. Keep secrets out of the build

The published `Realm-AdminServer-Windows-x64_<version>.7z` is a public download and must never contain a secret. The build
workflow already behaves that way: it runs a plain `dotnet publish` and ships only `appsettings.template.json`, whose secret
fields are empty. Do not add provider secrets or `AccountAuthoritySigningPrivateKey` to the build.

Set them on the host instead. ASP.NET Core reads environment variables and `appsettings.{Environment}.json` already, so
either works:

* a root-owned `appsettings.Production.json` beside the binary (`chmod 600`), or
* `EnvironmentFile=` under systemd with the same keys, using `__` for nesting:

```
AccountAuthoritySigningPrivateKey=<base64 Ed25519 private key>
AccountAuthorityPublicKeys__0=<base64 public key>
Discord__ClientSecret=...
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

A session does not outlive the proof behind it. `AuthenticatedAtUtc` records when the credentials were last entered, and a change
that outlives the session — changing the username, or linking another provider to it — is refused with `step_up_required` unless
that proof is within `AccountSensitiveActionWindowMinutes` (15 minutes by default, never less than one). The client answers the
code with `POST /auth/verify`, which takes the account password and stamps the same session; an account with no password can only
satisfy it by signing in with its provider again, and is told so instead of being left at a prompt it cannot complete.

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

The authority holds the credential hashes and the replication signing key, so only you and the peers you operate should
reach it.

* The raw collection routes (`/api/data/{collection}`, `/api/players`, `/api/maps`, `/api/admin/bans`, `/api/admin/info`)
  require an admin signature over `admin:{action}`, where the action is `info`, `players`, `maps`, `bans`, or the collection
  name for `/api/data/...`. Send it as `X-Admin-PublicKey` plus `X-Admin-Signature`, or send the timestamped token from the
  same admin key as `X-Admin-Bypass`. `Realm.Tools.Admin info --key <path>` already does this.
* Keep the authority off the public internet: publish a separate mirror node, or allow only peer addresses at the proxy, and
  terminate TLS in front of every node.
