# Changelog

## 2.1.1

- Changed: needs KydlCore 1.1.1 or newer (the KY hub shows every settings category of the mod as its own tab).
- Changed: all the **Enable** switches are now together in `[General]` and have new names: `Enable Mod` (was `EnableAll`) and `Enable Prefabs`, `Enable Offsets`, `Enable Stacking` (each was `Enable` in its own section). The values you had set are taken over; the old entries are removed from your config file.
- Changed: `Enable Mod` is now the first setting of `[General]`, in the F1 menu and in the F6 hub (your value is taken over).
- Changed: the console commands exist only when the developer's own KydlTools mod is loaded in that world; players do not get any console command from this mod (use the config file).

### Testing
- Unit tests: 40 passed. Integration tests: 38 passed.
- Property-based tests (FsCheck, included in the unit tests above): 8 property checks with generated inputs.
- Coverage of the testable code: 100%. Mutation testing (Stryker): 100% of the mutants are caught by the tests.
- Performance benchmarks (BenchmarkDotNet): 7.
- Static analysis (SonarQube): no Medium or High issues.
- In game with DrakesTestLab: the full suite of 21 tests per run, run 3 times in a row on the release build, all green (the release requirement).

## 2.1.0
- New: chests can be placed on top of (or against) other chests without planks or floors. Vanilla refuses it because a chest does not count as a building support; the new `[Stacking] Enable` switch (also needs `[General] EnableAll`) lets a chest aimed at a chest pass that one check. Chests never become real supports, so building stability is unchanged. Checked with the TestLab suite (the game's own placement test says Valid for a chest on a chest, Invalid with the switch off).
- Fix: a chest placed on a chest was destroyed by the game about 30 seconds later (vanilla gives no building support from a chest). A chest under another chest now counts as support in the game's support check; chests still support nothing else.
- New `[Prefabs] SnapRange` (default 1 m, vanilla 0.5): how close a snap point of the chest you hold has to be to another snap point before it locks on, so chests find their snap point sooner.
- Fix: the side of a chest was refused as a "tilted surface" when you aimed at it, so a chest next to another chest (side by side, front to back) showed a red ghost. Aiming at a chest side is now accepted when you hold a chest.
- Fix: chests that only touch along an edge or a corner showed a red ghost because the ghost was a few degrees off the placed chest. The chest you hold now takes the rotation of the chest it snaps to (the nearest quarter turn, so a turn you chose yourself is kept). New `[Prefabs] AlignRotation` (default on) switches it off.
- Fix: the Grausten (ashlands) chest has a collider much smaller than the chest you see (the lid is not in it), so chests snapped into each other, stacked ones sank into the one below and a chest on top was destroyed. The snap box now also covers the closed, visible meshes (not the open lid), and a chest whose collider is smaller than its body gets one extra collider for the visible body. New `[Prefabs] VisibleBounds` (default on) switches this off.
- New: the Grausten chest, the wardrobe, the barrel and the three pots (with their cracked and red variants) are built-in: they get snap points and stack like the other chests.
- Fix: pots snapped about 0.2 m too high (staggered, floating, diagonal rows) because the game only looks for snap points on pieces whose colliders are on its piece layers, and pots keep theirs on the default layer. Pots now get the extra body collider on the piece layer, so their snap points are found like any chest.
- New API for other mods: `ChestSnappingApi.RegisterChest(prefab)` makes their storage chests known (snap points and stacking). See the README.
- Tested in game with the TestLab suite: for every chest type a stack of 3, 3 side by side, a row of 3 front to back and front to front, placed for real on wood floors on cleared ground (the game's support check runs; nothing is destroyed).

## 2.0.0
- Tested in game with the full TestLab suite (6 tests): 3 green runs in a row on 2026-11-05, plus the ecosystem run with all 12 KydlDev mods loaded together.
- Breaking: ChestSnapping now needs the KydlCore mod (hard dependency). Install KydlCore as well.
- Ported to the KydlDev layout (feature switches, KY hub settings tab, TestLab suites). The snap points and console commands work as before. Checked in game on 2026-11-04 (clean start and quit with all mods loaded, no errors from this mod).
- New config switches: `[General] EnableAll`, `[Prefabs] Enable` (snap points on chests) and `[Offsets] Enable` (apply the saved `PrefabAdjustments`). Switching them changes the chests that are already built, no restart needed.
- The settings are a tab in the KY hub window of KydlCore (F6).
- `[DebugLog] Enable` writes detailed lines to the BepInEx log.
- Still client-side: the network check is `VersionCheckOnly` with exact version match when both sides have the mod (a player without it can still join).

## 1.0.0

First release.

- Chests get 14 snap points that work with Valheim's built-in snap system: 4 bottom corners, 4 top corners and 6 face centers. Press **Q / E** while placing to cycle through them; the vanilla "Snapping: ..." message shows the active one.
- Built-in support for the wood chest, reinforced chest, private chest and blackmetal chest.
- Snap points are placed on the chest's real collider bounds, so chests snap flush against each other.
- **Modded chests:** add any prefab name with the `ExtraPrefabs` config entry or the `chestsnap_add` console command (`chestsnap_remove` takes it out again, `chestsnap_list` shows candidates). `ExcludedPrefabs` opts prefabs out, and `AutoDetectContainers` can give every buildable container snap points automatically.
- **Adjustable snap points:** per-prefab `shift`, `grow` and single-point offsets, saved in the `PrefabAdjustments` config entry. Console commands: `chestsnap_shift`, `chestsnap_grow`, `chestsnap_point`, `chestsnap_reset`, `chestsnap_info`.
- `chestsnap_show` draws a sphere on every snap point so you can see what an adjustment does.
- Changes to the config or the console commands apply to chests that are already built right away, no restart needed.
- Client-side only: other players and servers do not need the mod.
- Config file: `BepInEx/config/com.kydldev.ChestSnapping.cfg`.
