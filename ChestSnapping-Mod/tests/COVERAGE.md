# ChestSnapping - test coverage

**Automated** = covered by a test that ran green; **Manual** = needs a person; **Gap** = not covered. Never mark an untested behaviour as covered.

| Feature / requirement | Unit | Integration | TestLab | Manual | Status |
|---|---|---|---|---|---|
| Loads with exact KydlCore, no errors | - | ManifestTests, ArchitectureRulesTests | selftest | - | Automated |
| Prefab-name lists (parse / add / remove) | PrefabNamesTests | - | - | - | Automated |
| Feature rule EnableAll AND own switch | FunctionsTests | - | feature-toggle | - | Automated |
| KY hub Settings tab, open / close | - | - | hub-page | hub-look | Automated + Manual |
| Switches live (EnableAll, Prefabs, Offsets) | - | - | feature-toggle | - | Automated |
| 14 snap points on the built-in chests (ghost) | - | - | snap-points | snap-feel | Automated + Manual |
| ExcludedPrefabs live | - | - | excluded-prefab | - | Automated |
| Saved shift moves the points; Offsets off ignores it | - | - | adjustment-applies | - | Automated |
| Chest on chest without planks: decision rule, switches, a chest never stays a support | StackingRulesTests | - | stacking, stack-placement (the game's own Player.UpdatePlacementGhost: on Valid, off Invalid, plank Valid) | stack-feel (feel only) | Automated + Manual |
| Every chest type (wood, reinforced, private, blackmetal, grausten): stack of 3, 3 side by side (aligned and ghost turned 22.5 degrees), row of 3 front to back, front to front: ghosts Valid, nothing destroyed, the visible meshes of the placed chests do not overlap (placedVisibleOverlap). Chest side faces accepted; rotation aligned | ChestAlignmentTests | - | chests-wood, chests-reinforced, chests-private, chests-blackmetal, chests-grausten, snap-edge-floor | stack-feel | Automated + Manual |
| A really placed stacked chest survives the game's support check (control: collapses with the switch off) | - | - | stack-survives | stack-feel | Automated |
| Public API for other mods (RegisterChest / UnregisterChest) | - | - | api-register | - | Automated |
| Config layout, patch target, suite rules | - | SettingsTests, PatchTargetsTests, SuiteRulesTests | - | - | Automated |
| Q / E cycling, flush snapping to another chest | | | | snap-feel | Gap |
| `grow` and single-point offsets, `chestsnap_info` output | | | | in-game | Gap |
| `chestsnap_add / remove / list` commands | | | | in-game | Gap |
| `chestsnap_show` markers | | | | snap-feel | Gap |
| ExtraPrefabs / AutoDetectContainers with a modded chest | | | | in-game | Gap |
| Placed (not ghost) chests in a loaded world | | | | in-game | Gap |