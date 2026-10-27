# Hearthbound — Runtime Configuration and Feature Coverage

[Overview](README.md) · [UI layouts](05-UI-Layouts-and-Runtime-Authoring.md) · [Acceptance](06-Opening-Slice-Content-and-Validation.md)

This map is part of [shared conformance](../../30-Profile-Conformance-and-Runtime-Configuration.md). Every named definition in the source catalogs receives its own feature-manifest entry; grouped rows below share an editing workflow, not a single test that excuses untested members. All routes are inside the standalone UI Toolkit application. A retail player sees approved choices/settings through owner commands; author-only fields do not become unrestricted player cheats.

Each form supports typed validation, inherited-value explanations, reference navigation, undo/redo, save/reopen and isolated preview. The normal action editor covers source/slot access, target/LOS, cost/reservation, phase and input timing, claim compatibility, effects, presentation, interruption, cooldown, source loss and save policy. Each effect editor covers clock, stacking, refresh, capacity, projections, pulse, removal and ancestry. Each scene editor covers roles, lines, choices, intent, checkpoints, skip/interruption and consequences. These complete field groups apply to every corresponding row.

## Coverage map

| Feature/content family | State owner and runtime editing route | Required fields or proof |
|---|---|---|
| Four backgrounds, identity and appearance | Core/Narrative/Characters; Project → Profiles → Creation | Point budget, biography, male/female presentation and matching language, prepared appearance/voice, creation ledger |
| Six attributes, all derived resources, Strength damage and Dexterity aiming | Stats/Combat; Characters → Stats; Combat → Aiming | Every formula/coefficient/cap, sampling, regen, current/reserved values; compare S10/S30 and D10/D30 |
| Levels, Steel/Ember/Ways and all 18 nodes | Stats; Characters → Progression | XP thresholds, costs, prerequisite/grant graph, respec, auto-build and loadout compatibility |
| Light Cut, Crescent, Rising, Storm Finish and Staff Light | Abilities/Combat; Combat → Movesets | Contact sweep, base/bonus poise, attack groups, same-life chain, costs and animation transitions |
| Dodge, Guard/parry and Counterstep | Actors/Abilities/Combat; Combat → Defense | Entry/hold/exit, frontal arc, parry/block eligibility, token, guarded budget, break and legal counter exit |
| Bow shot and equipment swap | Abilities/Items/Aiming; Items → Weapons | Real arrows, draw/release, authoritative muzzle/trajectory, hand claims, swap timing |
| Ember Thread, Stormlance, Armed Sigil and Grounding Mark | Abilities/CombatLinks; Combat → Abilities/When this hits | Charge formula, reservation, readiness, auxiliary channel, Wet/Conductive and bounded detonation |
| Hearth Aegis, Brace and other protection | StatusEffects/Combat; Combat → Statuses | Per-recipient capacity, radius/targets, four-layer cap, duration, replacement and damage allocation |
| Spark rune proc | Items/CombatLinks; Items → Affixes | Eligible origin, chance/retained roll, ICD, target, source lease and child action |
| Measured Edge, Patient Guard, Keeper's Rhythm, Patient Aim, Clear Mind and Quiet Passage | Stats/StatusEffects/Abilities; Progression → Grant bundle | Modifier operation, consumer, cap and replacement; no additive duplication of replacement coefficients |
| Read the Ground/Scent and Sign | Narrative/World; Story → Evidence; Combat → Utility | Known-record categories, 8 m/LOS filter, interpretation privileges, no hidden-truth reveal |
| Field Dressing, bandage, draught, salve, preparations and Shared Shelter | Items/Abilities/StatusEffects; Items → Recipes & Services | Recipe inputs/output caps, treatment targets, interruption, safe-station/camp conditions, timers and cost |
| Sword, shield, coat, bow, staff, focus and all consumables | Items; Items → Equipment/Containers | Mass, slot conflicts, finite caps, protected custody, moveset/rig compatibility |
| Crowns, vendors, stock, discounts and contracts | Items/Social/Titles; Items → Economy | Integer rounding, recognized audience, discount cap, stock/quote revision, reward claims and no-loop audit |
| Mara/Ilyan kits, powers, orders and progression | Party/AI/Stats/Items; Story → Companions | Signature grants, real supplies, per-actor builds, role/fallback, safe transfer, departure and knowledge |
| Grounding Pin, Brace, Disrupting Shot and Call the Opening | Abilities/StatusEffects; Companion → Powers | Reuse action/effect forms, target ranges, resource/cooldown, primer/poise result and presentation |
| Broken Crossing, its four outcomes and three side stories | Narrative; Story → Quests | Role alternatives, early credit, milestone XP, physical safety queries, disclosure and delayed inspection |
| Clues, accusations, lore, faction recognition and crime | Narrative/Social; Story → World Bible/Evidence/Factions | Observed/reported/inferred/established, provenance, disclosure audience, standing and witnessed response |
| Mara/Ilyan friendship and optional romance questlines | Relationships/Narrative; Story → Relationships | Explicit interest, male/female language, open eligibility to either player sex, exclusive commitment, playable optional content, equal utility and continuation |
| Bridgekeeper | Titles/Social/Stats; Characters → Titles | Safety/deed predicate, source-owned +10 stamina, recognition discount, display/equip separation, no refill |
| Town/bridge/river/road placements, material hazards, repaired-state changes and routes | World/Navigation; World → Regions/Interactions | Prepared assets/anchors, state variants, physical passability, nav invalidation, heat/Wet and alternate routes |
| Enemies, surrender, guardian/conductor and phases | Encounters/Actors/AI; World → Encounters | Resolved builds, poise/control, part states, pressure, phase tie-breaks, shutdown/surrender and one resolution |
| Rest, treatment, travel, safe save and full retry | World/Persistence/owner participants; World → Travel & Rest | Preconditions, actual resource/status changes, calendar, protected custody, checkpoint consistency |
| Every HUD/menu, evidence board, character screen, controls and audio | UI/Presentation; Interface → Screens/Input; Combat → Presentation | Read-only queries, focus, known-data filtering, reflow, localization, clip/cue mapping, controller/keyboard alternatives |
| Distribution, versions, diagnostics and scope | Content/SDK; Packages; Project → Profiles → Coverage | Feature/field closure, provider/asset errors, generation activation, migrations and target-build evidence |

## Resolved defaults and previously implicit behavior

Authoritative numbers in [stats](02-Character-Stats-and-Progression.md) and [actions](03-Combat-Skills-and-Equipment.md) remain the source of truth. These additional defaults are ordinary editable records:

- Crouch speed 1.8 m/s and footstep noise multiplier 0.50; Quiet Passage multiplies that crouched noise by 0.75. Running/sprinting exits crouch. Enemy reference sight is 22 m/120°, with ordinary-footstep hearing 6 m, sprint hearing 14 m and crouched hearing derived by the noise multiplier; occlusion and declared hearing categories apply. Reaction delay is at least 0.25 s for newly observed threats, not omniscient reaction.
- The reference camera has 60° vertical FOV, 3.5 m follow distance and 0.35 m shoulder offset, adjustable collision clearance with a 0.20 m probe. Prepared interior camera profiles can shorten distance; the aim solver still uses the actual camera/muzzle. These are tuning targets requiring rig/interior tests.
- Ordinary interaction reach is 2 m with line of sight; dialogue needs a safe participant claim. A catalog action with its own range overrides this field explicitly. Mara's Brace targets one ally within 8 m; Ilyan's Call the Opening targets an observed hostile within 12 m. Both revalidate at commit.
- Ember Thread adds 25 abstract heat to a tagged material on impact. For the opening, its compatible materials use the same bounded heat/moisture/exclusive-end Burning rule family as Wildsong, instantiated under hb IDs; World owns exposure and StatusEffects owns actor Burning/Wet. Damp reedbank volumes maintain Wet; they do not grant a global lightning vulnerability without an actual exposure/status source.
- Grounding Pin and Grounding Mark create the same compatible Conductive family. The lightning consumer uses the player's declared detonator rule, never an NPC-private callback. A missing player detonator is an optional-build limitation, not a reason for Mara's AI to spam unusable primers.
- Carry/stock overflow, actual encounter pressure and checkpoint readiness follow the shared owners. The UI explains each unavailable action without revealing undiscovered quest truth.

## Companion builds and nonromantic utility

Mara's recommendation order is the Ember track in prerequisite order, then Ways; Ilyan's is Ways, then Steel. Within a track, use level gate then listed node order, buying only eligible affordable nodes. Signature companion powers are separate starting sources. Unspent points remain visible when no recommended node fits; manual respec can choose another legal build. Recommended slotted powers retain the two signatures until the player changes them; melee/bow, dodge and item treatment are universal weapon/utility groups. All grants still require compatible assets and an actual input/AI path.

Mara's shared friendship/courtship utility is the Clearbreath recipe unlock after the independent-inspection milestone; it can also be taught by the town herbalist for 40 crowns. Ilyan's is a verified route report revealing one optional road-lodge location as reported information; an equivalent public survey contract supplies that report without romance. Each is one stable source grant, not extra power for choosing interest. Romantic quests add substantial optional adventures and personal outcomes as specified in the [story plan](04-Quests-Companions-and-Romance.md).

## Editing walkthrough and scope gate

Create a profile variant; change a background within its 60-point budget; give a prepared staff an armed-hit interaction; adjust Guard's interval; preview an absent-Mara investigation path; edit a romance mission and its opt-in gate; change the HUD layout; then undo, reopen, publish and activate in an isolated session. Test actual combat/quest outcomes, not only previews. Run HB gates plus PARITY-01 through PARITY-12. The opening and expanded pack have separate coverage reports; missing full romance adventures block the expanded scope, not an explicitly smaller opening.

