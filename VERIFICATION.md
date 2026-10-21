# Tile authority fix verification

## Folder and comment synchronization — 2026-10-22

The public extract matches all 202 runtime C# source files in the full project,
including file paths under `LGH/Scripts` (137 files) and `CSE` (65 files).
Editor sources, tests, Unity metadata, and project assets are excluded from this
extract. The existing server tile-authority fixes are preserved in both copies.

The synchronized full project was checked with Unity 6000.3.11f1 EditMode tests:
all 26 passed, with no failures or skipped tests. This confirms compilation and
the focused automated regressions; no new live multiplayer run is claimed.

See [the script folder guide](LGH/Scripts/README.md) for the functional layout.

## Authority regression coverage

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
