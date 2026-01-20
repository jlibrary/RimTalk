# RimTalk Scriban Examples (Known Working Variables)

{{- # ===============================
    # 1) System / Context
    # =============================== -}}
Language: {{ lang }}
Current hour (in-game): {{ hour }}
JSON format instructions:
{{ json.format }}

Chat history:
{{ chat.history }}

Dialogue type: {{ ctx.DialogueType }}
Dialogue status: {{ ctx.DialogueStatus }}
Is monologue: {{ ctx.IsMonologue }}
User raw input (player dialogue only): {{ ctx.UserPrompt }}

Decorated prompt:
{{ prompt }}

Pawn context (short):
{{ context }}

{{- # ===============================
    # 2) Primary pawn (initiator) shorthands
    # =============================== -}}
Pawn: {{ pawn.name }}
Role: {{ pawn.role }}
Current activity: {{ pawn.job }}
Mood: {{ pawn.mood }}
Personality: {{ pawn.personality }}
Social: {{ pawn.social }}
Location: {{ pawn.location }}
Beauty: {{ pawn.beauty }}
Cleanliness: {{ pawn.cleanliness }}
Surroundings: {{ pawn.surroundings }}

{{- # ===============================
    # 3) Recipient
    # =============================== -}}
Recipient: {{ recipient.name }}
Recipient role: {{ recipient.role }}

{{- # ===============================
    # 4) All pawns (pawns)
    # =============================== -}}
{{- for p in pawns -}}
- {{ p.name }} ({{ p.role }}) / {{ p.job }} / {{ p.mood }}
  Location: {{ p.location }}
  Social: {{ p.social }}
{{- end -}}

{{- # ===============================
    # 5) Map
    # =============================== -}}
Current weather: {{ map.weather }}
Current temperature: {{ map.temperature }}

{{- # ===============================
    # 6) Utility functions (PawnUtil / CommonUtil)
    # =============================== -}}
{{- if (IsTalkEligible pawn) -}}
Talk eligible: yes
{{- else -}}
Talk eligible: no
{{- end -}}
Role (utility): {{ GetRole pawn }}

{{- # ===============================
    # 7) Static classes (use with care)
    # =============================== -}}
Absolute ticks: {{ Find.TickManager.TicksAbs }}
Current hour (GenDate): {{ GenDate.HourOfDay(Find.TickManager.TicksAbs, 0) }}
Map count (all maps): {{ PawnsFinder.AllMaps.Count }}

# Pawn Scriban fields (level 1, prompt-ready)
Notes:
- Level 1 uses `pawn.<field>` and must render as text/number/bool.
- Only prompt-safe fields are listed.

| Field | Example | Notes |
| --- | --- | --- |
| name | {{ pawn.name }} | magic: LabelShort |
| job | {{ pawn.job }} | magic |
| role | {{ pawn.role }} | magic |
| mood | {{ pawn.mood }} | magic |
| personality | {{ pawn.personality }} | magic |
| social | {{ pawn.social }} | magic |
| location | {{ pawn.location }} | magic |
| beauty | {{ pawn.beauty }} | magic |
| cleanliness | {{ pawn.cleanliness }} | magic |
| surroundings | {{ pawn.surroundings }} | magic |
| KindLabel | {{ pawn.KindLabel }} | string |
| LabelShort | {{ pawn.LabelShort }} | string |
| LabelCap | {{ pawn.LabelCap }} | string |
| Downed | {{ pawn.Downed }} | bool |
| Dead | {{ pawn.Dead }} | bool |
| DeadOrDowned | {{ pawn.DeadOrDowned }} | bool |
| InMentalState | {{ pawn.InMentalState }} | bool |
| InAggroMentalState | {{ pawn.InAggroMentalState }} | bool |
| IsColonist | {{ pawn.IsColonist }} | bool |
| IsFreeColonist | {{ pawn.IsFreeColonist }} | bool |
| IsPrisoner | {{ pawn.IsPrisoner }} | bool |
| IsSlave | {{ pawn.IsSlave }} | bool |
| IsColonistPlayerControlled | {{ pawn.IsColonistPlayerControlled }} | bool |
| IsColonyAnimal | {{ pawn.IsColonyAnimal }} | bool |
| IsColonyMech | {{ pawn.IsColonyMech }} | bool |
| IsMutant | {{ pawn.IsMutant }} | bool |
| IsGhoul | {{ pawn.IsGhoul }} | bool |
| IsShambler | {{ pawn.IsShambler }} | bool |
| IsSubhuman | {{ pawn.IsSubhuman }} | bool |
| IsCreepJoiner | {{ pawn.IsCreepJoiner }} | bool |
| BodySize | {{ pawn.BodySize }} | float |
| HealthScale | {{ pawn.HealthScale }} | float |

# Pawn Scriban fields (level 2, prompt-ready)
Notes:
- Level 2 uses `pawn.<field>.<subfield>`.
- Manager-only or non-text fields are excluded.

| Field | Example | Notes |
| --- | --- | --- |
| RaceProps.Humanlike | {{ pawn.RaceProps.Humanlike }} | bool |
| RaceProps.IsMechanoid | {{ pawn.RaceProps.IsMechanoid }} | bool |
| RaceProps.intelligence | {{ pawn.RaceProps.intelligence }} | enum |
| ageTracker.AgeBiologicalYears | {{ pawn.ageTracker.AgeBiologicalYears }} | int |
| ageTracker.AgeBiologicalYearsFloat | {{ pawn.ageTracker.AgeBiologicalYearsFloat }} | float |
| ageTracker.AgeChronologicalYears | {{ pawn.ageTracker.AgeChronologicalYears }} | int |
| ageTracker.AgeChronologicalYearsFloat | {{ pawn.ageTracker.AgeChronologicalYearsFloat }} | float |
| ageTracker.AgeNumberString | {{ pawn.ageTracker.AgeNumberString }} | string |
| ageTracker.BirthYear | {{ pawn.ageTracker.BirthYear }} | int |
| ageTracker.BirthDayOfYear | {{ pawn.ageTracker.BirthDayOfYear }} | int |
| ageTracker.BirthQuadrum | {{ pawn.ageTracker.BirthQuadrum }} | enum |
| ageTracker.CurLifeStage | {{ pawn.ageTracker.CurLifeStage }} | LifeStageDef |
| ageTracker.Adult | {{ pawn.ageTracker.Adult }} | bool |
| ageTracker.Growth | {{ pawn.ageTracker.Growth }} | float |
| mindState.IsIdle | {{ pawn.mindState.IsIdle }} | bool |
| mindState.InRoamingCooldown | {{ pawn.mindState.InRoamingCooldown }} | bool |
| mindState.WillJoinColonyIfRescued | {{ pawn.mindState.WillJoinColonyIfRescued }} | bool |
| mindState.AnythingPreventsJoiningColonyIfRescued | {{ pawn.mindState.AnythingPreventsJoiningColonyIfRescued }} | bool |
| mindState.CombatantRecently | {{ pawn.mindState.CombatantRecently }} | bool |
| mindState.anyCloseHostilesRecently | {{ pawn.mindState.anyCloseHostilesRecently }} | bool |
| needs.PrefersOutdoors | {{ pawn.needs.PrefersOutdoors }} | bool |
| needs.PrefersIndoors | {{ pawn.needs.PrefersIndoors }} | bool |
| needs.mood.CurLevelPercentage | {{ pawn.needs.mood.CurLevelPercentage }} | float |
| needs.mood.MoodString | {{ pawn.needs.mood.MoodString }} | string |
| needs.food.CurLevelPercentage | {{ pawn.needs.food.CurLevelPercentage }} | float |
| needs.rest.CurLevelPercentage | {{ pawn.needs.rest.CurLevelPercentage }} | float |
| needs.joy.CurLevelPercentage | {{ pawn.needs.joy.CurLevelPercentage }} | float |
| needs.comfort.CurLevelPercentage | {{ pawn.needs.comfort.CurLevelPercentage }} | float |
| needs.energy.CurLevelPercentage | {{ pawn.needs.energy.CurLevelPercentage }} | float |
| story.Title | {{ pawn.story.Title }} | string |
| story.TitleCap | {{ pawn.story.TitleCap }} | string |
| story.TitleShort | {{ pawn.story.TitleShort }} | string |
| story.TitleShortCap | {{ pawn.story.TitleShortCap }} | string |
| story.CaresAboutOthersAppearance | {{ pawn.story.CaresAboutOthersAppearance }} | bool |
| guest.GuestStatus | {{ pawn.guest.GuestStatus }} | enum |
| guest.IsPrisoner | {{ pawn.guest.IsPrisoner }} | bool |
| guest.IsSlave | {{ pawn.guest.IsSlave }} | bool |
| guest.Resistance | {{ pawn.guest.Resistance }} | float |
| guest.will | {{ pawn.guest.will }} | float |
| guest.Recruitable | {{ pawn.guest.Recruitable }} | bool |
| guest.EverEnslaved | {{ pawn.guest.EverEnslaved }} | bool |
| guest.Released | {{ pawn.guest.Released }} | bool |
| relations.ChildrenCount | {{ pawn.relations.ChildrenCount }} | int |
| relations.RelatedToAnyoneOrAnyoneRelatedToMe | {{ pawn.relations.RelatedToAnyoneOrAnyoneRelatedToMe }} | bool |
| relations.IsTryRomanceOnCooldown | {{ pawn.relations.IsTryRomanceOnCooldown }} | bool |
| ideo.Certainty | {{ pawn.ideo.Certainty }} | float |

# Map Scriban fields (level 1, prompt-ready)
Notes:
- Level 1 uses `map.<field>` and must render as text/number/bool.
- Only prompt-safe fields are listed.

| Field | Example | Notes |
| --- | --- | --- |
| weather | {{ map.weather }} | magic |
| temperature | {{ map.temperature }} | magic |
| IsPlayerHome | {{ map.IsPlayerHome }} | bool |
| IsStartingMap | {{ map.IsStartingMap }} | bool |
| IsPocketMap | {{ map.IsPocketMap }} | bool |
| AgeInDays | {{ map.AgeInDays }} | float |
| PlayerWealthForStoryteller | {{ map.PlayerWealthForStoryteller }} | float |
| Tile | {{ map.Tile }} | PlanetTile |

# Map Scriban fields (level 2, prompt-ready)
Notes:
- Level 2 uses `map.<field>.<subfield>`.
- Manager-only or non-text fields are excluded.

| Field | Example | Notes |
| --- | --- | --- |
| info.Size | {{ map.info.Size }} | IntVec3 |
| info.NumCells | {{ map.info.NumCells }} | int |
| info.isPocketMap | {{ map.info.isPocketMap }} | bool |
| TileInfo.temperature | {{ map.TileInfo.temperature }} | float |
| TileInfo.rainfall | {{ map.TileInfo.rainfall }} | float |
| TileInfo.elevation | {{ map.TileInfo.elevation }} | float |
| TileInfo.hilliness | {{ map.TileInfo.hilliness }} | enum |
| TileInfo.swampiness | {{ map.TileInfo.swampiness }} | float |
| TileInfo.pollution | {{ map.TileInfo.pollution }} | float |
| TileInfo.IsCoastal | {{ map.TileInfo.IsCoastal }} | bool |
| TileInfo.AnimalDensity | {{ map.TileInfo.AnimalDensity }} | float |
| TileInfo.PlantDensityFactor | {{ map.TileInfo.PlantDensityFactor }} | float |
| TileInfo.MaxTemperature | {{ map.TileInfo.MaxTemperature }} | float |
| TileInfo.MinTemperature | {{ map.TileInfo.MinTemperature }} | float |
| Biome.label | {{ map.Biome.label }} | string |
| Biome.defName | {{ map.Biome.defName }} | string |
| mapTemperature.OutdoorTemp | {{ map.mapTemperature.OutdoorTemp }} | float |
| mapTemperature.SeasonalTemp | {{ map.mapTemperature.SeasonalTemp }} | float |
| wealthWatcher.WealthTotal | {{ map.wealthWatcher.WealthTotal }} | float |
| wealthWatcher.WealthItems | {{ map.wealthWatcher.WealthItems }} | float |
| wealthWatcher.WealthBuildings | {{ map.wealthWatcher.WealthBuildings }} | float |
| wealthWatcher.WealthPawns | {{ map.wealthWatcher.WealthPawns }} | float |
| wealthWatcher.WealthFloorsOnly | {{ map.wealthWatcher.WealthFloorsOnly }} | float |
| wealthWatcher.HealthTotal | {{ map.wealthWatcher.HealthTotal }} | int |
| mapPawns.AllPawnsCount | {{ map.mapPawns.AllPawnsCount }} | int |
| mapPawns.AllPawnsUnspawnedCount | {{ map.mapPawns.AllPawnsUnspawnedCount }} | int |
| mapPawns.FreeColonistsCount | {{ map.mapPawns.FreeColonistsCount }} | int |
| mapPawns.PrisonersOfColonyCount | {{ map.mapPawns.PrisonersOfColonyCount }} | int |
| mapPawns.FreeColonistsAndPrisonersCount | {{ map.mapPawns.FreeColonistsAndPrisonersCount }} | int |
| mapPawns.ColonistCount | {{ map.mapPawns.ColonistCount }} | int |

# Pawn Scriban fields (level 2, expanded: health / skills / relations / mind)
Notes:
- `pawn.health`, `pawn.skills`, and `pawn.relations` are shadowed by magic keys; subfields may be inaccessible unless mappings are fixed.
- `pawn.mindState.*` is directly accessible and safe to use.

| Field | Example | Notes |
| --- | --- | --- |
| health.State | {{ pawn.health.State }} | enum (shadowed parent) |
| health.Downed | {{ pawn.health.Downed }} | bool (shadowed parent) |
| health.Dead | {{ pawn.health.Dead }} | bool (shadowed parent) |
| health.InPainShock | {{ pawn.health.InPainShock }} | bool (shadowed parent) |
| health.CanBleed | {{ pawn.health.CanBleed }} | bool (shadowed parent) |
| health.CanCrawl | {{ pawn.health.CanCrawl }} | bool (shadowed parent) |
| health.CanCrawlOrMove | {{ pawn.health.CanCrawlOrMove }} | bool (shadowed parent) |
| health.LethalDamageThreshold | {{ pawn.health.LethalDamageThreshold }} | float (shadowed parent) |
| health.summaryHealth.SummaryHealthPercent | {{ pawn.health.summaryHealth.SummaryHealthPercent }} | float (shadowed parent) |
| skills.PassionCount | {{ pawn.skills.PassionCount }} | int (shadowed parent) |
| skills.skills.Count | {{ pawn.skills.skills.Count }} | int (shadowed parent) |
| relations.ChildrenCount | {{ pawn.relations.ChildrenCount }} | int (shadowed parent) |
| relations.RelatedToAnyoneOrAnyoneRelatedToMe | {{ pawn.relations.RelatedToAnyoneOrAnyoneRelatedToMe }} | bool (shadowed parent) |
| relations.IsTryRomanceOnCooldown | {{ pawn.relations.IsTryRomanceOnCooldown }} | bool (shadowed parent) |
| mindState.Active | {{ pawn.mindState.Active }} | bool |
| mindState.AvailableForGoodwillReward | {{ pawn.mindState.AvailableForGoodwillReward }} | bool |
| mindState.IsIdle | {{ pawn.mindState.IsIdle }} | bool |
| mindState.InRoamingCooldown | {{ pawn.mindState.InRoamingCooldown }} | bool |
| mindState.CombatantRecently | {{ pawn.mindState.CombatantRecently }} | bool |
| mindState.MeleeThreatStillThreat | {{ pawn.mindState.MeleeThreatStillThreat }} | bool |
| mindState.WillJoinColonyIfRescued | {{ pawn.mindState.WillJoinColonyIfRescued }} | bool |
| mindState.AnythingPreventsJoiningColonyIfRescued | {{ pawn.mindState.AnythingPreventsJoiningColonyIfRescued }} | bool |
| mindState.anyCloseHostilesRecently | {{ pawn.mindState.anyCloseHostilesRecently }} | bool |
| mindState.lastJobTag | {{ pawn.mindState.lastJobTag }} | JobTag |
| mindState.lastIngestTick | {{ pawn.mindState.lastIngestTick }} | int |
| mindState.lastHarmTick | {{ pawn.mindState.lastHarmTick }} | int |
| mindState.exitMapAfterTick | {{ pawn.mindState.exitMapAfterTick }} | int |
| mindState.traderDismissed | {{ pawn.mindState.traderDismissed }} | bool |
| mindState.hasQuest | {{ pawn.mindState.hasQuest }} | bool |

# Pawn Scriban fields (level 1, additions: combat/state)
Notes:
- Direct Pawn properties that are useful for combat state and prompt logic.

| Field | Example | Notes |
| --- | --- | --- |
| Drafted | {{ pawn.Drafted }} | bool |
| Inspired | {{ pawn.Inspired }} | bool |
| Crawling | {{ pawn.Crawling }} | bool |
| Flying | {{ pawn.Flying }} | bool |
| Swimming | {{ pawn.Swimming }} | bool |
| CanAttackWhileCrawling | {{ pawn.CanAttackWhileCrawling }} | bool |
| CanOpenDoors | {{ pawn.CanOpenDoors }} | bool |
| CanOpenAnyDoor | {{ pawn.CanOpenAnyDoor }} | bool |
| ShouldAvoidFences | {{ pawn.ShouldAvoidFences }} | bool |
| FenceBlocked | {{ pawn.FenceBlocked }} | bool |
| CanPassFences | {{ pawn.CanPassFences }} | bool |
| Roamer | {{ pawn.Roamer }} | bool |
| IsPrisonerOfColony | {{ pawn.IsPrisonerOfColony }} | bool |
| IsSlaveOfColony | {{ pawn.IsSlaveOfColony }} | bool |
| IsFreeNonSlaveColonist | {{ pawn.IsFreeNonSlaveColonist }} | bool |
| IsPlayerControlled | {{ pawn.IsPlayerControlled }} | bool |
| IsColonyMechPlayerControlled | {{ pawn.IsColonyMechPlayerControlled }} | bool |
| IsColonySubhumanPlayerControlled | {{ pawn.IsColonySubhumanPlayerControlled }} | bool |
| IsAwokenCorpse | {{ pawn.IsAwokenCorpse }} | bool |
| IsDuplicate | {{ pawn.IsDuplicate }} | bool |
| IsEntity | {{ pawn.IsEntity }} | bool |
| Deathresting | {{ pawn.Deathresting }} | bool |
| HasDeathRefusalOrResurrecting | {{ pawn.HasDeathRefusalOrResurrecting }} | bool |
| HasPsylink | {{ pawn.HasPsylink }} | bool |
| HarmedByVacuum | {{ pawn.HarmedByVacuum }} | bool |
| ConcernedByVacuum | {{ pawn.ConcernedByVacuum }} | bool |
| LastAttackTargetTick | {{ pawn.LastAttackTargetTick }} | int |
| TicksPerMoveCardinal | {{ pawn.TicksPerMoveCardinal }} | float |
| TicksPerMoveDiagonal | {{ pawn.TicksPerMoveDiagonal }} | float |

# Pawn Scriban fields (level 2, expanded: mindState combat)
Notes:
- These are directly accessible via `pawn.mindState.*`.

| Field | Example | Notes |
| --- | --- | --- |
| mindState.Active | {{ pawn.mindState.Active }} | bool |
| mindState.AvailableForGoodwillReward | {{ pawn.mindState.AvailableForGoodwillReward }} | bool |
| mindState.IsIdle | {{ pawn.mindState.IsIdle }} | bool |
| mindState.InRoamingCooldown | {{ pawn.mindState.InRoamingCooldown }} | bool |
| mindState.CombatantRecently | {{ pawn.mindState.CombatantRecently }} | bool |
| mindState.MeleeThreatStillThreat | {{ pawn.mindState.MeleeThreatStillThreat }} | bool |
| mindState.WillJoinColonyIfRescued | {{ pawn.mindState.WillJoinColonyIfRescued }} | bool |
| mindState.AnythingPreventsJoiningColonyIfRescued | {{ pawn.mindState.AnythingPreventsJoiningColonyIfRescued }} | bool |
| mindState.anyCloseHostilesRecently | {{ pawn.mindState.anyCloseHostilesRecently }} | bool |
| mindState.lastJobTag | {{ pawn.mindState.lastJobTag }} | JobTag |
| mindState.lastIngestTick | {{ pawn.mindState.lastIngestTick }} | int |
| mindState.lastEngageTargetTick | {{ pawn.mindState.lastEngageTargetTick }} | int |
| mindState.lastAttackTargetTick | {{ pawn.mindState.lastAttackTargetTick }} | int |
| mindState.lastMeleeThreatHarmTick | {{ pawn.mindState.lastMeleeThreatHarmTick }} | int |
| mindState.lastHarmTick | {{ pawn.mindState.lastHarmTick }} | int |
| mindState.lastCombatantTick | {{ pawn.mindState.lastCombatantTick }} | int |
| mindState.canFleeIndividual | {{ pawn.mindState.canFleeIndividual }} | bool |
| mindState.nextMoveOrderIsWait | {{ pawn.mindState.nextMoveOrderIsWait }} | bool |
| mindState.nextMoveOrderIsCrawlBreak | {{ pawn.mindState.nextMoveOrderIsCrawlBreak }} | bool |
| mindState.wantsToTradeWithColony | {{ pawn.mindState.wantsToTradeWithColony }} | bool |
| mindState.traderDismissed | {{ pawn.mindState.traderDismissed }} | bool |
| mindState.hasQuest | {{ pawn.mindState.hasQuest }} | bool |
| mindState.lastTakeCombatEnhancingDrugTick | {{ pawn.mindState.lastTakeCombatEnhancingDrugTick }} | int |
| mindState.lastTakeRecreationalDrugTick | {{ pawn.mindState.lastTakeRecreationalDrugTick }} | int |
| mindState.lastHumanMeatIngestedTick | {{ pawn.mindState.lastHumanMeatIngestedTick }} | int |

# Pawn Scriban fields (level 2, expanded: health / skills / relations)
Notes:
- `pawn.health`, `pawn.skills`, and `pawn.relations` are shadowed by magic keys in Scriban; subfields may not resolve until mapping is fixed.

| Field | Example | Notes |
| --- | --- | --- |
| health.State | {{ pawn.health.State }} | enum (shadowed parent) |
| health.Downed | {{ pawn.health.Downed }} | bool (shadowed parent) |
| health.Dead | {{ pawn.health.Dead }} | bool (shadowed parent) |
| health.InPainShock | {{ pawn.health.InPainShock }} | bool (shadowed parent) |
| health.CanBleed | {{ pawn.health.CanBleed }} | bool (shadowed parent) |
| health.CanCrawl | {{ pawn.health.CanCrawl }} | bool (shadowed parent) |
| health.CanCrawlOrMove | {{ pawn.health.CanCrawlOrMove }} | bool (shadowed parent) |
| health.LethalDamageThreshold | {{ pawn.health.LethalDamageThreshold }} | float (shadowed parent) |
| health.summaryHealth.SummaryHealthPercent | {{ pawn.health.summaryHealth.SummaryHealthPercent }} | float (shadowed parent) |
| health.hediffSet.PainTotal | {{ pawn.health.hediffSet.PainTotal }} | float (shadowed parent) |
| health.hediffSet.BleedRateTotal | {{ pawn.health.hediffSet.BleedRateTotal }} | float (shadowed parent) |
| health.hediffSet.HasHead | {{ pawn.health.hediffSet.HasHead }} | bool (shadowed parent) |
| health.hediffSet.PreventVacuumBurns | {{ pawn.health.hediffSet.PreventVacuumBurns }} | bool (shadowed parent) |
| health.hediffSet.HasRegeneration | {{ pawn.health.hediffSet.HasRegeneration }} | bool (shadowed parent) |
| health.hediffSet.RemoveRoamMtb | {{ pawn.health.hediffSet.RemoveRoamMtb }} | bool (shadowed parent) |
| health.hediffSet.HasPreventsDeath | {{ pawn.health.hediffSet.HasPreventsDeath }} | bool (shadowed parent) |
| health.hediffSet.AnyHediffPreventsCrawling | {{ pawn.health.hediffSet.AnyHediffPreventsCrawling }} | bool (shadowed parent) |
| health.hediffSet.HungerRateFactor | {{ pawn.health.hediffSet.HungerRateFactor }} | float (shadowed parent) |
| health.hediffSet.RestFallFactor | {{ pawn.health.hediffSet.RestFallFactor }} | float (shadowed parent) |
| skills.PassionCount | {{ pawn.skills.PassionCount }} | int (shadowed parent) |
| skills.skills.Count | {{ pawn.skills.skills.Count }} | int (shadowed parent) |
| skills.skills[0].def.label | {{ pawn.skills.skills[0].def.label }} | string (shadowed parent) |
| skills.skills[0].Level | {{ pawn.skills.skills[0].Level }} | int (shadowed parent) |
| skills.skills[0].LevelDescriptor | {{ pawn.skills.skills[0].LevelDescriptor }} | string (shadowed parent) |
| skills.skills[0].passion | {{ pawn.skills.skills[0].passion }} | enum (shadowed parent) |
| skills.skills[0].XpProgressPercent | {{ pawn.skills.skills[0].XpProgressPercent }} | float (shadowed parent) |
| skills.skills[0].LearningSaturatedToday | {{ pawn.skills.skills[0].LearningSaturatedToday }} | bool (shadowed parent) |
| skills.skills[0].TotallyDisabled | {{ pawn.skills.skills[0].TotallyDisabled }} | bool (shadowed parent) |
| skills.skills[0].PermanentlyDisabled | {{ pawn.skills.skills[0].PermanentlyDisabled }} | bool (shadowed parent) |
| skills.skills[0].Aptitude | {{ pawn.skills.skills[0].Aptitude }} | int (shadowed parent) |
| relations.DirectRelations.Count | {{ pawn.relations.DirectRelations.Count }} | int (shadowed parent) |
| relations.VirtualRelations.Count | {{ pawn.relations.VirtualRelations.Count }} | int (shadowed parent) |
| relations.ChildrenCount | {{ pawn.relations.ChildrenCount }} | int (shadowed parent) |
| relations.RelatedToAnyoneOrAnyoneRelatedToMe | {{ pawn.relations.RelatedToAnyoneOrAnyoneRelatedToMe }} | bool (shadowed parent) |
| relations.IsTryRomanceOnCooldown | {{ pawn.relations.IsTryRomanceOnCooldown }} | bool (shadowed parent) |
