# Tile authority fix verification

This repository is a code extract. Changes were first reviewed and tested in the
full Unity 6000.3.11f1 source project, where **26 EditMode tests passed**: 6 territory
BFS tests, 13 Bot decision tests, and 7 connection/authority regressions.

Incoming tile claims now require the authenticated, active, exact registered
connection and its colour. A missing controller/registration rejects the request.
Respawning a player refreshes the registry object without changing its colour.
Legacy client force-owner messages, including Neutral, make no changes. Existing
server effects keep `RequestForceSetOwner` as a compatible wrapper around the
explicit `ServerForceSetOwner` API.

All force-owner callers in the full project were reviewed (territory collapse,
mine explosions/hits, tsunami effects, and the meteor's server-only override).
Two-stage normal enemy claims, Bot server claims, batch messages, reset snapshots,
and snapshot retry code retain their existing paths.

Still required before release: live host/remote/Bot matches, delayed mobile join,
disconnect/reconnect and scene respawn, mine/skill effects, batch ordering, and
missed-delta snapshot recovery. The EditMode result does not claim those live
network scenarios were played through. No full Unity build of this extract is claimed.
