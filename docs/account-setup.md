# Account authority setup

This document lists what a person must do outside the code to run accounts. Everything here is configuration; none of it is
committed.

## 1. Pick the account authority

Exactly one node owns account and DisplayName writes, because global name uniqueness cannot be produced by an
eventually-consistent peer cluster.

1. On that node set `AccountAuthority` to `true` and leave `AccountAuthorityUrl` empty (or set it to the node's own URL).
2. On every other node set `AccountAuthority` to `false` and set `AccountAuthorityUrl` to the authority's base URL.
3. Non-authority nodes refuse account mutations locally and answer `account_not_authority`.

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

Steam does not offer OAuth 2.0.

1. Set `Steam:Realm` to the site the sign-in belongs to, for example `https://accounts.example.com`.
2. Set `Steam:ReturnUrl` to `https://accounts.example.com/auth/steam/callback`.
3. Set `Steam:WebApiKey` so the registration form can be prefilled with the player's current Steam persona name. The key is
   only used to read that name; the identity is the SteamID64 in the verified assertion.

If `Steam:WebApiKey` is empty, Steam sign-in still verifies, and the registration form simply starts without a prefilled name.

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
