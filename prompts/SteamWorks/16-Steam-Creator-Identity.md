# 16 — Steam Creator Identity and Workshop Profiles

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01 (SteamID) and 09 (author identity).
**Related:** 07, 08, 13 and 18 surface the creator.

## User story

As a creator, I want my Steam persona, avatar and Workshop page to be my creator profile, so I never maintain a second profile, bio or link list, and I can edit everything on Steam.

As a player, I want the map details to show who made the map, their Steam avatar, their other Workshop maps and the Discord server they run, with one action to open their full Steam profile, so I can recognise and follow creators without leaving the game for a separate Realm site.

As the developer, I want no creator database, no website route and no second identity system. Steam is the profile. The only creator link beyond Steam is a Discord invite stored in the map itself.

## Context

The earlier plan added `realm-game.com/creator_profile/<steamid64>` with a database, Steam OpenID login and a bio/link editor. That is deleted. Steam already has a public profile page, a Workshop author page and an edit page. The game reads the public Steam identity and links out; it does not host, cache or edit profile data.

Steam exposes persona name, avatar, profile URL and published items. It does **not** expose a structured social-links field, and the profile summary is free text on a web page. Do not scrape the profile page or the Workshop description for URLs. The only creator link the game shows directly is an optional Discord server invite the creator writes into the map's own metadata. Do not generalise it into a link list.

This task does not change the no-DRM policy. The game must keep starting and working without the Steam client for single-player, the editor and local map play; the creator panel simply hides its Steam actions.

## Requirements

1. **No Realm profile service.** Do not build the website route, database, OpenID login, bio, link editor, privacy page or deployment. If the game contains a stub or a link to `realm-game.com/creator_profile`, remove it. No new HTTP endpoint, database or secret. The website repository is not part of this task.
2. **Resolve a creator from Steam.** Given a SteamID64, read the persona name and avatar through the Steamworks client APIs. Use the profile URL Steam reports, or the canonical `https://steamcommunity.com/profiles/<steamid64>`. Cache only in memory for the session; names and avatars change. A missing avatar shows a neutral placeholder, not an error.
3. **Other Workshop items, for this AppID only.** Query the creator's published items **for the current AppID** through the Steamworks UGC query API (`CreateQueryUserUGCRequest` with the current AppID), sorted the way the Workshop author page sorts them. Never include items the creator published for another game or another AppID. Show up to a small fixed number (for example five) with title, thumbnail, positive percentage and vote count. Clicking one opens its Workshop page. An empty result is a normal state: show "No other maps for Realm" and the Workshop page action. Do not build a creator index or a global creator directory.
4. **Surface the creator in map details and credits.** The map details panel, the lobby map card and the in-match credits show the creator's Steam persona and avatar, the other-item strip and these actions:
   - "View Steam Profile" opens the creator's Steam profile.
   - "View Workshop" opens the creator's Workshop page for this AppID.
   - "Other maps by this creator" opens the same list, or the Workshop page when the list is empty.

   Use the Steam overlay when it is enabled; otherwise open the system browser. If neither works, show a copyable URL.
5. **Editor and publish links.** The editor's map panel and the publish dialog offer:
   - "Edit Steam Profile" opens `https://steamcommunity.com/profiles/<steamid64>/edit`.
   - "Edit Workshop Item" opens the item's Workshop edit page, when the map has a published file ID.

   These are links, not embedded forms. The game never asks for a Steam password and never stores profile fields.
6. **The only creator link is the Discord server invite.** Add one optional `DiscordServerInviteLink` string to the map's info metadata, authored in the editor's map properties and included in the generated schema. The map details render it as a single external "Join the creator's Discord" action. It is also included in the generated Workshop description as one line, where Valve's rules permit external links. If it is empty, show only the Steam profile and Workshop actions and no empty row.
   - Accept only an `https` URL on a Discord invite host (`discord.gg`, `discord.com/invite`, or `discordapp.com/invite`). Normalise a pasted `discord.gg/<code>` to `https://discord.gg/<code>`. Reject every other host, `javascript:`, `data:`, `file:` and relative URLs. Do not call Discord's API to validate or resolve the invite, and do not try to infer from the URL whether it expires: an invite URL carries no expiry information, so a lapsed invite is the creator's to correct and the helper label is the only guidance the game gives.
   - The editor shows a persistent helper label beside the input: "In Discord, open Server Settings → Invites → Create Invite, set Expire After to Never and Max Uses to No Limit, then paste the invite link here. Do not use a temporary or single-use invite." The label is translated. Do not generate the invite for the creator and do not require Discord sign-in in the game.
   - The link opens externally in the system browser. Do not embed a Discord client, join a server for the player, or require the player to have Discord installed.
   - Do not add a generic link list, a website field, a donation link, a payment link or a store section. The creator's Steam profile is the place for anything else Steam permits, and the Discord invite is the only channel through which a creator may direct players who want to support their work (task 21). The game shows no purchase control, no price and no support request for a map or a creator.
   - Add a sequential `MapUpgradeService` migration for the new field; no fallback reader.
7. **Offline and local maps.** With no Steam session, show the map metadata author name and the `DiscordServerInviteLink`; hide the Steam profile, Workshop and other-item actions. A local map with no author SteamID shows the metadata author and the Discord invite only. The invite still opens in the browser without Steam.
8. **Privacy.** The game stores only the SteamID64 already present in the map package, the attribution tags and the optional Discord invite. It does not store a bio, a link list, an email or a profile copy. Steam's privacy settings decide what the profile and Workshop pages show; respect a private or unavailable profile with a neutral message.
9. **Localization.** Every label and error is a `TranslationServer.Translate("literal")` call site. The Discord helper label is a literal at its call site, not text returned from a method.
10. **Loading and failure.** Show a small inline loading state while the other-item query runs, an empty state when the creator has no other public items for this AppID, and a neutral "Steam profile unavailable" state when Steam is offline. Do not block the map details on the query.

## UX

- The creator row in map details shows the avatar, persona name, a small "Steam" badge, then the actions.
- Other items are a horizontal strip of cards with the thumbnail, title and "Positive 87% - 124 votes". A "See all" action opens the Workshop author page. The strip is explicitly scoped to Realm items; never show the creator's items from another game.
- The Discord row sits below the creator row: one external-link action labelled "Join the creator's Discord". Do not label it "Links" or show a list.
- The editor's map properties has one "Discord server invite" field with the helper label beside it. Validation appears on save, not as a popup on every keystroke.
- The publish dialog shows "Edit Steam Profile" and, after the first publish, "Edit Workshop Item".
- Map details keep the Workshop title, author, votes, subscribers and last updated; the creator panel is an addition, not a replacement for the map's own page.

## Out of scope

- A Realm-hosted creator profile, bio, link editor, OpenID login, database or website route.
- A generic social-link list, donation processing or displaying donation links.
- Scraping Steam profile pages or Workshop descriptions for links.
- Querying or displaying the creator's Workshop items for any other AppID.
- A global creator directory, search or ranking.
- Changing the map author identity rules (task 09) or attribution tags (task 18).
- Any Steam DRM or startup check that makes Steam mandatory.

## Acceptance criteria

- No `realm-game.com/creator_profile` route, database, OpenID flow or creator profile secret is added to the game, and no Realm-hosted profile exists.
- Map details show the creator's Steam persona and avatar, up to five other Workshop items **for the current AppID**, and the map's `DiscordServerInviteLink` when set.
- The other-item query passes the current AppID; an item from another AppID is never returned or rendered.
- "View Steam Profile" and "View Workshop" open the correct Steam pages; a disabled overlay falls back to the browser.
- The editor and publish dialog open the Steam profile edit page and the Workshop item edit page.
- The editor shows the non-expiring-invite helper label beside the Discord field.
- A non-Discord host, a non-`https` URL, `javascript:`, `data:`, `file:` and a relative URL are rejected by schema validation; a pasted `discord.gg/<code>` is normalised.
- An invite's expiry is never validated, and the acceptance criteria do not depend on detecting one.
- A local map with no SteamID shows the metadata author and the Discord invite, and hides the Steam actions.
- No donation, payment, generic link-list or website field is rendered by the game or accepted by the schema.
- The other-item query has loading, empty and error states and never blocks the panel.
- The new metadata field round-trips, and the `MapUpgradeService` migration builds.
- The game still starts, and single-player, the editor and local map play still work with no Steam client and with no Steam DRM.

## Pitfalls

- Do not build a creator database because "other Workshop items" looks like one. Query Steam each session and cache in memory.
- Do not query the creator's items without the AppID filter. The SteamID is global, so an unfiltered query will show their maps for other games.
- Do not scrape the Steam profile page for social links. Steam has no structured field; use the one Discord invite in map metadata.
- Do not turn the Discord invite into a link list, a website field or a donation surface. The developer explicitly wants one Discord server link and nothing else.
- Do not generate a Discord invite or ask for a Discord token. The creator creates a non-expiring invite in Discord and pastes it.
- Do not claim the game validates that an invite still works. It validates the host and the scheme, teaches the creator to make a permanent invite, and leaves the rest to them.
- Do not accept `discord.gg` short links without normalising them, and do not follow redirects to validate them.
- Do not store avatars on disk or in the map. They are session UI.
- Do not use a Steam Web API key in the client. Use the Steamworks client APIs.
- Do not make the creator panel a gate for playing, joining or publishing.
- Do not show donation links. The developer explicitly does not want them in the game.
- Do not trust a creator's SteamID from a URL or a map's display name; use the Workshop item owner or the attribution tag.
- Do not call the Steam overlay from a background thread.
- Do not let a private Steam profile look like a broken panel. Show the persona the game already has and a neutral note that the full profile is not public.
- Do not keep the website alive "for later". The decision is no Realm-hosted profile.
- Do not add Steam DRM or a mandatory Steam startup check to make the creator panel work. The panel degrades when Steam is unavailable; the rest of the game does not.
