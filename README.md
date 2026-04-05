# FriendZone

FriendZone adds a new custom zone to RimWorld's Zone architect tab.

## Current feature set

- Draw a FriendZone like a growing zone
- Choose Camp, Farm, Tavern, Inn, or Village
- Spawn allied non player settlers from the map edge
- Plan enclosed housing with beds, walls, and doors
- Place simple shared structures based on settlement type
- Reserve field cells and assign allied settlers to sow and harvest potatoes
- Assign allied settlers to hunt nearby wildlife
- Track settlement resource gains and send 10% to the player's stockpiles as rent
- Build with a GitHub Actions workflow against Krafs.Rimworld.Ref 1.6.4633

## Project layout

- `About` metadata and preview assets
- `Defs` xml patches
- `Languages` keyed translations
- `Textures` UI icons
- `Source/FriendZone` C# source project
- `.github/workflows/build.yml` CI build and packaging

## Notes

This version keeps the settlers separate from the player faction and biases toward allied outlander style behavior. Farming uses field cells managed by the settlement manager rather than vanilla player grow zones so that allied workers can handle the work without becoming player pawns.
