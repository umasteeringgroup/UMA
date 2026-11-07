# Frontier Company — Runtime Configuration and Feature Coverage

[Overview](README.md) · [UI layouts](05-UI-Layouts-and-Runtime-Authoring.md) · [Acceptance](06-Opening-Slice-Content-and-Validation.md)

This map is part of [shared conformance](../../30-Profile-Conformance-and-Runtime-Configuration.md). Every named definition in the source catalogs receives its own feature-manifest entry; grouped rows below share an editing workflow, not a single test that excuses untested members. All routes are inside the standalone UI Toolkit application. A retail player sees approved choices/settings through owner commands; author-only fields do not become unrestricted player cheats.

Each form supports typed validation, inherited-value explanations, reference navigation, undo/redo, save/reopen and isolated preview. The normal action editor covers source/slot access, target/LOS, cost/reservation, phase and input timing, claim compatibility, effects, presentation, interruption, cooldown, source loss and save policy. Each effect editor covers clock, stacking, refresh, capacity, projections, pulse, removal and ancestry. Each scene editor covers roles, lines, choices, intent, checkpoints, skip/interruption and consequences. These complete field groups apply to every corresponding row.

## Coverage map

| Feature/content family | State owner and runtime editing route | Required fields or proof |
|---|---|---|
| Four backgrounds, commander identity and prepared appearance | Core/Narrative/Characters; Project → Profiles → Creation | Point sum, biography, male/female presentation with matching language, kit ledger and provider compatibility |
| Six attributes, continuous resources, shield conversion and actual handling | Stats/Combat/Aiming; Characters → Stats; Combat → Shield/Aim Profiles | Typed formulas, regen/disruption, budget conservation, fractional values, reticle and preview provenance |
| XP, Assault/Systems/Recon and all 18 nodes | Stats; Characters → Progression | Thresholds, costs/prerequisites, source grants, respec, inactive catch-up and legal slots |
| Carbine, pistol, shotgun, survey rifle, reload and swap | Items/Abilities/Aiming; Items → Weapons | Attack model/range, cadence, per-shot/pellet IDs, spread/recoil, magazine/reserve caps, insertion commit, cancel and no duplicate ledger |
| Jab, Breach Sweep, Anchor Break and timed chain | Abilities/CombatLinks; Combat → Movesets | Same-life evidence, input/perfect/handoff windows, poise, independent costs and transitions |
| Shield Brace, Counterdrive, dodge, sprint and cover/vault | Actors/Combat/World; Combat → Defense/Movement/Cover | Guard target/type, token producer/consumer, protected intervals, geometry/occupancy, legal exits and input reachability |
| Concussive Strike, Shield Pulse, Ion Tag, Arc Detonator, Thermal Marker and Disabling Shot | Abilities/Combat/StatusEffects; Combat → Abilities | All timings, targets, costs, shield restoration, primer consumption, weapon compatibility and same pipeline |
| Remote Brace and Relay Step | World/Actors/Abilities; Combat → Deployables/Traversal | Prepared barrier placement/HP/lifetime/leases; collision-constrained dash and forbidden barrier bypass |
| Controlled Burst, Mobile Guard, Point of Entry, Efficient Loop, Coordinated Surge, Settled Sight, Quiet Approach, Field Triage and Shared Picture | Stats/StatusEffects/AI/Abilities; Progression → Grants | Actual modifier consumers, burst cadence, movement claims, squad scopes/ICDs, bounded knowledge and scan input |
| Armed Discharge and Capacitor Spur | Abilities/CombatLinks/Items; Combat → When this hits; Items → Attachments | Reservation/readiness versus chance proc, cooldown/ICD, source loss, compatible channels and research source |
| Medgel, treatment and revive | Items/Actors/Abilities; Items → Consumables; Characters → Lifecycle | Per-actor count, healing caps, helper inventory, downed timer, interruption and LifeId checks |
| Armor, field shield rig, mass, supplies and quartermaster | Items/Stats; Items → Loadouts/Economy | Actual owned gun/supply sources, deployment mass check, paid/sponsored stock, overflow and finite reserves |
| Research/components/schematics | Items/Stats/Narrative; Items → Research | Recipe cost, unlock source, unique evidence prerequisites, deterministic output and transaction identity |
| Juno/Ishan/Ro roster, powers, inventory and build templates | Party/AI/Items/Stats; Story → Companions | Shared XP/individual state, two deployed allies, grant reconciliation, manual/auto builds and depleted-loadout fallback |
| Bulwark Pulse and Pinning Burst | Abilities/Items/StatusEffects; Companion → Powers | Protection layer, five reserved rounds, shot offsets, one AttackGroup, cancellation and suppression |
| Squad commands and primer coordination | Party/AI/CombatLinks; Combat → Squad Policies | Observed targets, queue replacement, deadline, advisory coordination versus atomic consumption, explain rejected orders |
| Meridian mission, three core allocations and expanded missions | Narrative; Story → Missions, also World shortcut | Lifecycle, roster/custody, 750-XP budget, route/log requirements, extraction/debrief and fail-forward versus full retry |
| Logs, truth, ship bible, factions, reports and commendation audience | Narrative/Social; Story → Evidence/World Bible/Factions | Knowledge/provenance, disclosure, reputation/standing bands and price/custody consequences |
| Juno/Ishan friendship and optional romance adventures | Relationships/Narrative; Story → Relationships | Either player sex eligible, explicit opt-in, exclusive agreement, separate mission inventory, continuation and no forced main-story romance |
| Reach Responder | Titles/Stats/Social; Characters → Titles | Safe-evacuation evidence, +10 Focus source, recognized medical discount and no refill |
| Ship/station placements, doors, hazards, cover and navigation | World/Navigation; World → Regions/Interactions | Prepared state swaps, collision/path updates, safe anchors, actual exposure and region leases |
| Security roles, controller/conduits, reinforcement wave and shutdown | Encounters/AI/Actors; World → Encounters | Builds, pressure, phase priority, recharge shutdown, part identity and one reward outcome |
| Deploy/extract/rest/retry/save | Narrative/World/Persistence and participants; Missions → Transition Policy | Failed load retains prior generation, supplies/custody transfer once, downed transport, safe ship service |
| HUD, reticle, tactical overlay, crew screens, input, cues and localization | UI/Presentation; Interface → Screens/Input; Combat → Presentation | Read-only projection, pause/queued intents, known information, reflow, real shot timing and rig mappings |
| Packages, migration and coverage | Content/SDK; Packages; Profiles → Coverage | Installed versus verified provider state, generation locks, feature/field closure and exact build evidence |

## Resolved shooting, supply and world defaults

The numerical sources remain [stats](02-Character-Stats-and-Progression.md) and [combat](03-Combat-Skills-and-Equipment.md). Additional fields are configurable, not special-case code:

- Starting firearm delivery is hitscan: carbine 60 m, pistol 30 m, shotgun 15 m and survey rifle 100 m maximum query range. Damage is constant inside that range, with no opening distance-falloff curve. Shotgun pellets use the shared disk sampler within a 4° base half-angle before declared stance/movement penalties; one shell spends one round. Other guns use the documented ADS/hip spread profile.
- Controlled Burst selects a three-shot carbine mode at the existing 6 rounds/s. Reserve up to three available rounds, consume one at offsets 0/10/20 ticks and release unfired reservations on interruption; a new trigger press starts another burst after the cadence permits it. Each bullet has a ShotId/AttackGroup as an ordinary shot. This differs from Ro's one powered-burst group by explicit definition, not UI event frequency. The talent's recoil impulse multiplier remains 0.90.
- Supply minimum for departure is one equipped firearm with at least one full loaded magazine and two reserve magazines per deployed actor, plus one medgel per squad in owned custody. A sponsored first-mission issue tops up only the missing amount once through a claim; later resupply is quoted and paid or sponsored explicitly. Failure shows the shortage and safe correction. After deployment, running out does not softlock extraction or technical mission solutions.
- Ionized/EMP by itself does not invent recharge disruption. Arc Detonator additionally applies a 2 s RechargeDisrupted status to electronic targets with positive actual EMP-budget absorption/loss; recharge resumes only after both that status and the ordinary 4 s no-damage delay permit it. The status cannot stack duplicate timers into a hidden permanent shutdown.
- Crouch speed 1.6 m/s, footstep noise multiplier 0.50; Quiet Approach multiplies crouched noise by 0.70. Reference enemy sight 25 m/120°, ordinary-footstep hearing 6 m and sprint hearing 14 m, with occlusion and source confidence. Newly perceived attacks retain the minimum 0.25 s reaction delay.
- Default camera uses 60° vertical FOV, 3 m distance, 0.45 m shoulder offset and 0.20 m collision probe; ADS uses 45° FOV over a 0.15 s presentation blend. Actual camera/muzzle samples drive reticle and shot tests. Ordinary interactions are within 2 m/LOS; explicit ability ranges override this.

## Companion build and utility closure

Juno recommends Recon then Systems; Ishan recommends Systems then Recon; Ro recommends Assault then Recon. Buy eligible affordable nodes in level/list order and retain unspent points. Each actor gets the same profile point budget, independent allocations and signature grants. Juno's two power slots start with Thermal Marker and Shield Pulse; universal Medgel/Revive remains outside those slots. Ishan starts Ion Tag/Arc Detonator. Ro's slots are Bulwark/Pinning Burst, with Pinning Burst visibly unavailable on the shotgun until a carbine is equipped. Universal melee/retreat/treatment remains a legal fallback. No recommendation grants a free player-only power merely because the UI lists it.

Juno's shared friendship/courtship utility is a source-owned 5% ship medgel discount after the accountability milestone, combined under the existing total 20% discount cap. Ishan's is the Capacitor Spur schematic after his diagnostic-accountability milestone; the same schematic can be earned from the optional Sable Yard salvage contract. Fabrication costs do not change and a second source does not duplicate an item. Romantic missions have their own optional playable content and cosmetic/story rewards, detailed in [crew arcs](04-Quests-Companions-and-Romance.md).

## Runtime workflow and proof

Duplicate the shield profile; edit conversion and protection priority; preview a mixed hit; change a weapon cadence/round cap; configure a squad power and required input; build a new mission extraction branch and its custody preview; author an optional romance mission; change the tactical screen; save/reopen and activate an isolated candidate. Test depleted companions, two primer consumers, empty shields, missing cover, failed extraction and decline-all-romance histories. FC gates and PARITY-01 through PARITY-12 apply to each advertised release scope.

