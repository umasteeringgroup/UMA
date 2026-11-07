# Wildsong — Runtime Configuration and Feature Coverage

[Overview](README.md) · [UI layouts](05-UI-Layouts-and-Runtime-Authoring.md) · [Acceptance](06-Opening-Slice-Content-and-Validation.md)

This map is part of [shared conformance](../../30-Profile-Conformance-and-Runtime-Configuration.md). Every named definition in the source catalogs receives its own feature-manifest entry; grouped rows below share an editing workflow, not a single test that excuses untested members. All routes are inside the standalone UI Toolkit application. A retail player sees approved choices/settings through owner commands; author-only fields do not become unrestricted player cheats.

Each form supports typed validation, inherited-value explanations, reference navigation, undo/redo, save/reopen and isolated preview. The normal action editor covers source/slot access, target/LOS, cost/reservation, phase and input timing, claim compatibility, effects, presentation, interruption, cooldown, source loss and save policy. Each effect editor covers clock, stacking, refresh, capacity, projections, pulse, removal and ancestry. Each scene editor covers roles, lines, choices, intent, checkpoints, skip/interruption and consequences. These complete field groups apply to every corresponding row.

## Coverage map

| Feature/content family | State owner and runtime editing route | Required fields or proof |
|---|---|---|
| Four backgrounds, traveler identity and appearance | Core/Narrative/Characters; Project → Profiles → Creation | Point budget, biography, male/female presentation and matching language, prepared catalogs, retry-safe grants |
| Six stats, damage/aim, endurance/resonance and carrying | Stats/Combat/Aiming; Characters → Stats; Combat → Aiming | Typed formulas, consumer sampling/caps, ground-only regen and meaningful HUD explanations |
| XP, Way/Weave/Kin and all 18 nodes | Stats; Characters → Progression | Every level/prerequisite/cost/grant, source persistence, auto-build, respec and mandatory-tool protection |
| Staff Tap, Turning Reed, Falling Star and Evade | Abilities/CombatLinks/Actors; Combat → Movesets/Defense | Contacts, poise, same-life hit evidence, exact timing, costs, handoff and actual dodge interval |
| Seed sling, plain/ember projectiles and set swap | Items/Aiming/Abilities; Items → Weapons | Real inventory/caps, projectile/gravity, heat contribution, target/range, claims and shot provenance |
| Wind Pulse and Gentle Spark | World/Abilities/Combat; Combat → Tools; World → Materials | Cone/target filters, mass/impulse clamp, heat, cost, allowed contact and no arbitrary transform writes |
| Leaf Lift, Glider, Open Horizon and prepared climb/jump | Movement/Actors/World; World → Traversal | Provider, mode, support, obstruction, rate/cost, airborne flags, safe recovery and camera mapping |
| Reed Tether and prepared machinery | World/Movement; World → Manipulation/Interactions | Anchor versus object mode, mass/range, collision path, sustained cost, attach/detach and permission |
| Raincall, Wet, moisture decay, heat and Burning | World/StatusEffects/Combat; World → Materials; Combat → Statuses | Exposure identity, abstract units, thresholds, pulse schedule, refresh/dispel and causal caps |
| Rootsnare, Resonant Seed and Living Chorus | Abilities/StatusEffects/CombatLinks; Combat → Abilities | Control resistance, armed reservation/release, aura target/cost scope, duration and source loss |
| Windchime Binding | Items/CombatLinks; Items → Affixes | Chance/ICD, eligible direct origins, retained roll, auxiliary Breeze Dart and bounded child budget |
| Balanced Step, Patient Glide, Sure Landing, Careful Current, Patient Sling and Quiet Footfall | Stats/StatusEffects/Movement; Progression → Grant bundle | Actual consumer, replacement versus multiplier, clamps and no source-toggle refill |
| Read the Signs and notebook experiments | Narrative/World; Story → Evidence; Interface → Notebook | Discovered data only, failed-experiment reasons, observed/inferred distinction and known explanations |
| Shared Task, Field Meal, A Place at the Fire, salve and Chilled | Items/Abilities/StatusEffects; Items → Recipes & Services | Helper consent/claims, solo duration, per-dose custody, treatment/food result, safe camp and interrupt |
| Staff/sling/wraps/toolbelt, glider and consumable supplies | Items/Characters; Items → Equipment/Containers | Protected tool grants, hand/socket compatibility, quantity caps, mass and recoverable overflow |
| Leafmarks, vendors, recipes, caches and title discounts | Items/Titles/Social; Items → Economy | Atomic input/output, integer prices, once-only claims, recognized audience and no money loop |
| Lark/Neri builds, recruitment, orders and traversal follow | Party/AI/Stats/Items; Story → Companions | One optional ally, actual supplies, source powers, low-resource fallback and reached-anchor regroup |
| Mark the Breeze, Steady Line, Bracing Stake and Grounding Seed | Abilities/StatusEffects/CombatLinks; Companion → Powers | Target/LOS, source stats, finite protection, conductive token consumer and bounded spirit blast |
| Missing Morning, four allocations and three side stories | Narrative/World; Story → Quests | Early route credit, access/danger milestones, actual passability, knowledge, recovery and 360-XP budget |
| Lark/Neri friendship and optional romance adventures | Relationships/Narrative; Story → Relationships | Either player sex eligible, explicit interest, exclusive agreement, playable opt-in branches, utility and continuation |
| Wayfriend | Titles/Stats/Social; Characters → Titles | Safe communicated-route predicate, +10 endurance, food discount, history/display separation and no refill |
| Village/vanes/mill/grove/bell/season routes, weight platform and repair projects | World/Navigation/Narrative; World → Regions/Interactions/Seasons | Prepared state machines, weights/decay, story-gated season changes, nav updates and consistent audiovisual states |
| Creatures, guardian/parts and combat/noncombat solutions | Encounters/AI/Actors; World → Encounters | Resolved builds, control resistance, phase priority, shutdown alternative and one reward identity |
| Rest, local fall recovery, regroup, checkpoint and full retry | World/Actors/Persistence; World → Travel & Rest | Reached anchors, 20-HP cost, no token refresh until support, genuine full rollback and unavailable-suspend diagnostics |
| HUD, trajectory, tool wheel, map, People, inputs and presentation | UI/Presentation; Interface → Screens/Input; Combat → Presentation | Authoritative bindings, chord consumption, pause, privacy, reflow, captions, clips/rigs and reduced motion |
| Package/capability dependencies and publication | Content/SDK; Packages; Profiles → Coverage | Required traversal/material provider, field/editor closure, safe generation activation and exact build evidence |

## Resolved tool and movement defaults

[Stats](02-Character-Stats-and-Progression.md) and [tools](03-Combat-Skills-and-Equipment.md) remain the numerical sources. The following fields complete their ordinary runtime forms:

- Crouch speed 1.8 m/s and footstep noise multiplier 0.50; Quiet Footfall multiplies that noise by 0.75. Reference hostile sight is 20 m/120°, ordinary-footstep hearing 6 m and sprint hearing 14 m, with occlusion and a minimum 0.25 s reaction delay to newly observed threats. Wildlife uses its separately authored nonhostile policy.
- Camera baseline is 60° vertical FOV, 3.5 m follow distance, 0.35 m shoulder offset and 0.20 m collision probe. Glide framing may move to 4.5 m without changing actual trajectory. Ordinary interactions are 2 m/LOS; tool ranges override explicitly.
- Wind Pulse contributes 15 poise to eligible enemies and may request at most 1 m collision-constrained push over 12 ticks for small/ordinary movable actors. Large/anchored targets reject that movement while reporting permitted poise separately. The small-prop impulse formula does not directly move a character motor.
- Reed Tether accepts the same 0.5–20 mass-unit movable-prop class as Wind Pulse. It detaches on release, blocked path, source/target loss, invalid anchor or insufficient available endurance, releasing only uncommitted claims/reservations. The current paid interval is never refunded. On controller the tool selector exposes Attach, Retrieve and Detach; a traversal link uses its registered motion mode, not prop dragging.
- Living Chorus applies a single 0.90 multiplier to Wind Pulse, Leaf Lift, Reed Tether's initial resonance cost, Gentle Spark and Raincall. It does not reduce passive glide/climb drains, its own cost, ally revive supplies or ancestry budgets. Duplicate auras use the strongest equivalent multiplier, not repeated multiplication. Costs reserve the effective fractional amount; whole health-damage rounding does not round them.
- Lark's Mark the Breeze range is 15 m/LOS; Steady Line targets one ally within 8 m. Neri's Bracing Stake targets one ally within 8 m; Grounding Seed targets a hostile within 10 m/LOS. Tokens, protection and modifiers follow the common source/lifetime rules.

## Companion builds and equal utility

Lark recommends Kin then Way; Neri recommends Weave then Kin. Spend only eligible affordable nodes in level/list order, keep remaining points visible and permit manual respec. The two signature power slots remain separate from weapon movesets, permanent story tools and item treatment. Recommendations cannot grant a missing traversal capability. Solo routes remain valid when an ally waits at a safe anchor.

Lark's friendship/courtship utility is an annotated optional meadow route marked as reported until personally traversed; a public survey task offers the same information without romance. Neri's is the salve recipe unlock after resolving her independent-work milestone; the village cook can teach the same recipe for 20 leafmarks. The actual recipe and its ingredient costs stay unchanged. Multiple sources preserve one learned recipe/report and never create duplicate supplies. Optional romance adventures add distinct playable objectives and memories, described in [relationships](04-Quests-Companions-and-Romance.md).

## Runtime authoring and physical proof

Create a material variant, edit decay/ignition and inspect actual pulses; place a prepared vane/weight platform and its navigation state; edit a glide profile and compare a fully deployed fixture with real deployment; configure a tool and input; create an optional relationship quest; alter notebook/HUD layouts; save/reopen and activate an isolated candidate. Validate low stats, no consumables, absent companion, destroyed prop, no romance, fall recovery and unsupported suspend. WS gates and PARITY-01 through PARITY-12 apply. Physical clearance, animation and accessibility require actual player-build evidence; a green topology diagram is insufficient.

