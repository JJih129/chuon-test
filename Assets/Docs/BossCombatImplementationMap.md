# Boss Combat Implementation Map

Purpose: map the boss-combat improvement goal to concrete project artifacts and remaining verification gaps.

## Implemented Artifacts

- Pattern selection: `Assets/Scripts/Boss/BossPatternSelection.cs`
- Boss integration: `Assets/Scripts/Boss/BossController.cs`
- Animation-event hitbox conflict guard: `Assets/Scripts/Boss/BossAnimationEvents.cs`
- Post-attack actions: `Assets/Scripts/Boss/BossPostActionExecutor.cs`
- Attack timing and parry windows: `Assets/Scripts/Boss/BossAttackTimingController.cs`
- Telegraph VFX presenter: `Assets/Scripts/Boss/BossTelegraphVfxPresenter.cs`
- Attack feedback presenter: `Assets/Scripts/Boss/BossAttackFeedbackPresenter.cs`
- Difficulty profiles: `Assets/Scripts/Boss/BossDifficultyProfile.cs`
- Pattern telemetry recorder: `Assets/Scripts/Boss/BossPatternTelemetryRecorder.cs`
- Runtime safety monitor: `Assets/Scripts/Boss/BossCombatRuntimeSafetyMonitor.cs`
- Data validation: `Assets/Editor/BossCombatDataValidator.cs` validates selector, selector observation-bias direction, deterministic player-observation selection samples, timing fairness, phase, player-state, player-response pattern pools, spatial-response, safety-monitor, and coverage data.
- Manual playtest checklist: `Assets/Docs/BossCombatPlaytestChecklist.md`

## Goal Coverage

- Pattern choice by range, angle, phase, cooldown, recent history: implemented in `BossPatternSelector`.
- Selector context includes delayed `BossPlayerCombatObservation`; normal weighting and fallback scoring use small clamped biases so escape, close pressure, defense, guard, and attack states influence choices without perfect-read spikes.
- Post-action rhythm: implemented in `BossPostActionExecutor`.
- Out-of-range engage handling: implemented through `BossPatternEngageMode` and boss engage routines.
- Range/tempo guardrails: `BossController` clamps overly broad direct SwordWave usage, caps long combat-idle strafe downtime, and limits reactive backstep to very close pressure so ranged, mid, and close bands do not collapse into one repeated behavior.
- Telegraph and hitbox timing: implemented through `BossAttackTimingController`.
- Attack feedback: `BossAttackFeedbackPresenter` maps `BossAttackTelegraphType` to weapon/body flash, warning/charge/swing/impact sounds, camera shake, hit stop, trail effect hooks, ground warnings, and projectile warning lines with null-safe optional assets.
- Difficulty scaling: `BossDifficultyProfile` applies runtime-only multipliers for damage, cooldown, telegraph length, hitbox active time, reaction delay, pattern weight, engage speed, postAction duration, phase transition threshold, follow-up count, and parry window without overwriting source pattern data.
- Phase pattern variation: implemented through `BossPhasePatternModifier`.
- Player-state response: implemented through `BossPlayerCombatObservation`, selector-level weak observation bias, far-pressure, close-linger, dodge, guard, attack, approach, side/back angle, stun/down, repeated-parry, repeated-perfect-dodge, recent-hit, long no-hit pressure bias, and delayed reactive-state commits to avoid perfect-read AI.
- Spatial response: `BossController` spatial pattern bias reduces blocked backsteps, flips blocked strafes, and favors recenter/close pressure in corners.
- Player observation diagnostics: `BossController.CurrentPlayerObservation` and `enablePlayerObservationDebugLog`.
- Pattern selection diagnostics: `enablePatternDebugLog` reports distance, angle, phase, recent history, candidate patterns, exclusion reasons, selected pattern, cooldown, postAction, engage, hitbox, and parry-window state. Editor-only gizmo toggles draw attack ranges, engage ranges, hitbox timing bounds, and arena awareness.
- Pattern execution telemetry: `BossPatternTelemetrySample`, `BossCombatRecoveryTelemetrySample`, `BossController.OnPatternTelemetrySample`, `BossController.OnCombatRecoveryTelemetrySample`, debug log toggles, and optional `BossPatternTelemetryRecorder`; samples include delayed player observation flags, state-to-pattern distributions, far-pressure counts, recovery completion/abort/no-target counts, and a context-menu playtest verdict for response-AI plus recovery verification.
- Runtime safety diagnostics: optional `BossCombatRuntimeSafetyMonitor` detects lingering hitboxes, stuck attack/recovery states, no-pattern combat stalls, repeated-pattern streaks, repeated movement postAction streaks, far-range non-pressure streaks, and player-response failures such as moving-away without pressure, guard without punish, attack without counter, or repeated defense without mixup.
- Break, stun, death, ultimate cleanup: guarded in `BossController.AbortAttackExecution` and timing cleanup.
- Data safety gates: implemented in `BossCombatDataValidator`, including checks that escape, close-linger, guard, attack, repeated-defense responses, and difficulty profiles stay in usable ranges.

## Verified Gates

- Unity compile: error 0.
- Boss combat validator: errors 0, warnings 0.
- PlayMode smoke, 30 seconds: error 0, warning 0.
- Static diff check: no whitespace or conflict-marker errors.

## Remaining Non-Automated Verification

- Telegraph readability under real player input.
- Hitbox fairness against actual dodge, guard, and parry timing.
- Phase 2 and Phase 3 pressure feel.
- Wall, corner, side, and back-position behavior feel after spatial bias.
- Pattern telemetry distribution after real player tests.
- Runtime safety monitor warnings under real player tests.
- VFX and sound quality for attack warning cues.

## Completion Rule

Do not mark the boss-combat goal complete until the manual playtest checklist has no unresolved critical issue.
