# CNC Sync Expansion Roadmap

## Core Idea

CNC Sync already does something more general than CNC file transfer:

1. detect that something is ready
2. optionally process it
3. deliver it somewhere
4. recover later if delivery failed

That pattern applies to CNC jobs, 3D printer jobs, reports, labels, CAM exports, generated payloads, and many other file-driven workflows.

## Product Direction

The current product language is still CNC- and FTP-shaped. That is fine for the current release, but the architecture should leave room for a broader automation tool.

The most useful long-term model is:

- triggers
- processing setups
- destinations

Today the only real trigger is a watched local folder, so `Watch Folders` is still a sensible user-facing label. Internally, though, we should treat it as the first trigger type, not the only trigger type.

## Trigger Model

### Current Trigger

- watched local folder

### Future Trigger Types

- watched local folder
- scheduled folder scan
- remote file pickup
- webhook-triggered submission
- manual/API submission
- cloud or web export pickup

This keeps the current app simple while leaving room for workflows where a file appears somewhere other than a local watch directory.

## Destination Model

### Current Destination

- FTP

### High-Value Next Destinations

1. SFTP
   - closest expansion from FTP
   - widely requested and more acceptable than plain FTP in many environments

2. Local / Network Folder
   - useful for local routing, handoff folders, SMB shares, and internal workflows
   - probably the single broadest non-FTP destination

3. Webhook / HTTP Upload
   - useful for modern systems and SaaS/API workflows
   - enables file delivery into custom services without building a bespoke connector for each one

### Later Candidates

- WebDAV
- S3-compatible storage
- email/drop-folder style integrations
- destination script execution for custom delivery

## Processing Model

The current processing model is already strong:

- default upload
- external script processing

That should remain the main extension point. If the product broadens, the best path is not to hard-code every workflow into the app. It is to keep processing reusable and scriptable.

Useful future improvements:

- richer script presets
- destination-specific script hooks
- more guided built-in transforms
- better validation and runner detection

## Recovery Model

The recovery story is already one of the most valuable parts of the app:

- staging
- catch-up
- retry through prepared output

That should remain a first-class concept as the app expands. A broader product should still answer:

- what was detected
- what was prepared
- what was delivered
- what failed
- how to retry safely

## UI / Terminology Recommendations

### Near Term

Keep the current user-facing labels where they are still accurate:

- Watch Folders
- Processing Setups
- FTP Servers

### Medium Term

Move toward a more general model:

- Triggers
- Processing Setups
- Destinations

That gives the app room to grow without forcing abstraction on users before the extra trigger and destination types actually exist.

## Recommended Implementation Order

1. SFTP destination
2. Local / Network Folder destination
3. Webhook / HTTP upload destination
4. Trigger abstraction in the core model
5. Additional trigger types

## Architectural Recommendation

As the product expands, the code should be shaped around:

- `ITrigger`
- `IProcessor`
- `IDestination`

The current FTP path can then become one destination implementation rather than the main identity of the whole app.

## Product Positioning

Today, CNC Sync is a good name for the current audience.

If the app grows into a broader output-automation tool, the branding may eventually need to widen. The implementation should be flexible enough for that future even if the product name stays focused for now.
