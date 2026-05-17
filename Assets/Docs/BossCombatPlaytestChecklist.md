# Boss Combat Playtest Checklist

Purpose: verify boss combat feel after pattern selector, post action, engage, timing, phase modifier, and player-state bias changes.

## Required Setup

- Use MainScene boss encounter.
- Clear Unity Console before each pass.
- Enable boss debug logs only when diagnosing a failed case.
- Enable `enablePlayerObservationDebugLog` only when verifying player-response AI; keep it off for normal feel tests.
- Enable `enablePatternDebugLog` only when diagnosing why a pattern was selected or excluded.
- Enable `enablePatternTelemetryDebugLog` only when verifying pattern repetition, phase pressure, engage use, or distance-based decisions.
- Set `difficultyTier` to Easy, Normal, Hard, and Expert in separate passes; do not edit source pattern data for difficulty tests.
- Optionally add `BossPatternTelemetryRecorder` to the boss during focused tests to aggregate pattern, phase, range, engage, follow-up, delayed player-observation counts, recovery counts, and state-to-pattern distributions.
- Use `BossPatternTelemetryRecorder/Log Playtest Verdict` after focused tests to get a quick pass/tuning summary for far pressure, moving-away pressure, guard punish, attack counter, repeated-defense mixup, and recovery completion ratios.
- Optionally add `BossCombatRuntimeSafetyMonitor` during focused tests to catch lingering hitboxes, stuck attack/recovery states, no-pattern combat stalls, repeated-pattern streaks, repeated movement postActions, far-range non-pressure samples, and player-response failures.
- Do not judge balance from one run; repeat each case at least 3 times.

## Core Pass Criteria

- The boss does not use close-range attacks while clearly out of range.
- The boss pressures far players with chase, dash, or sword wave behavior.
- Attack telegraphs are readable before hitbox activation.
- Parryable attacks have a fair parry window.
- Dodge-only or heavy attacks are visually distinct from normal attacks.
- Parryable, DodgeOnly, Unblockable, Heavy, and Ranged attacks are readable from boss weapon/body/sound/camera feedback without relying only on UI text.
- Phase 2 feels more active than Phase 1.
- Phase 3 feels faster or more threatening than Phase 2 without becoming unfair.
- Difficulty changes are noticeable: Easy is slower/readable, Normal preserves baseline, Hard/Expert increase pressure without unreadable hit timing.
- Hitboxes and parry windows do not remain active after stun, break, death, or ultimate interruption.
- Recovery telemetry shows completed recoveries after parry stun, break, stagger, phase transition, and ultimate victim cases; unexpected aborted recovery counts need investigation.

## Distance Tests

- Close range, 0m to 3m: quick slash, heavy slash, or backstep punish should appear.
- Mid range, 3m to 8m: dash or chase pressure should appear.
- Far range, 8m or more: sword wave or chase pressure should appear.
- If the player keeps running away, the boss should not idle indefinitely.

## Player Response Tests

- Repeated parry: boss should reduce obvious parryable repetition and mix other responses.
- Repeated perfect dodge: boss should reduce dodge-only repetition and mix other responses.
- Repeated normal dodge: boss should bias delayed, fakeout, or heavier pressure without becoming unreadable.
- Guard holding: boss should bias heavy, backstep, or guard-break-style pressure.
- Player attack spam: boss should mix short counter pressure instead of only passively waiting.
- Reactive-state response should feel slightly delayed, not like the boss instantly reads the exact input frame.
- Player repeatedly hits boss at close range: boss should bias backstep or heavy counter pressure.
- Boss fails to hit player for several seconds: boss should increase dash or ranged pressure without becoming unfair.
- Long close-range stay: boss should use heavier punish or backstep pressure more often.
- Long far-range stay: boss should increase ranged or chase pressure.

## Phase Tests

- Phase 1: longer telegraph, clearer learning window, fewer follow-ups.
- Phase 2: more sword wave, dash, chase, strafe, and occasional follow-up.
- Phase 3: shorter telegraph, faster engage, more follow-up pressure.
- During phase transition: no lingering hitbox, no new attack until transition lock ends.
- With `BossCombatRuntimeSafetyMonitor`: no lingering-hitbox, stuck-attack, stuck-recovery, no-pattern-stall, repeated-pattern, or far-no-pressure warning during a normal pass.

## Arena Tests

- Wall behind boss: backstep or strafe should not push through the wall.
- Wall between boss and player: engage should abort or recover, not slide forever.
- Player at boss side: boss should recenter or turn before committing.
- Player behind boss: boss should not repeatedly attack empty space.
- Player circles behind boss repeatedly: boss should bias recenter/back-angle counter behavior without instant unreadable hits.
- Boss in corner: repeated backstep/side strafe should reduce; recenter or close pressure should appear instead.

## Attack Feedback Tests

- Parryable: short weapon flash and parry-readable sound, with no strong camera shake.
- DodgeOnly: red danger cue and dodge-readable sound before hitbox open.
- Unblockable: weapon/body warning, stronger warning sound, clear dodge timing, and restrained camera shake.
- Heavy: charge sound, longer telegraph, mild camera pressure, and clear recovery.
- Ranged: weapon or hand cue plus projectile warning line toward the target before firing.
- Impact feedback: hit stop and impact sound must not obscure the hitbox timing.

## Difficulty Tests

- Easy: longer telegraphs, longer cooldowns, reduced follow-up pressure, and wider parry windows should make learning possible.
- Normal: behavior should match the baseline tuning unless the Normal profile is intentionally edited.
- Hard: shorter telegraphs, faster engage, quicker postAction recovery, and more follow-up pressure should be visible.
- Expert: pressure should be stronger than Hard, but attacks must still have readable weapon/body/sound tells before hitbox open.
- Across all tiers: no negative or inverted hitbox/parry-window timing, no phase transition at impossible HP thresholds, and no Console errors.

## Failure Notes To Capture

- Boss phase.
- Player distance.
- Player relative direction: front, side, back.
- Boss pattern or animation name if visible.
- Pattern telemetry line if `enablePatternTelemetryDebugLog` is enabled.
- Pattern debug line if `enablePatternDebugLog` is enabled: selected pattern, candidates, exclusion reasons, cooldown, postAction, engage, hitbox, and parry-window state.
- Whether hitbox felt early, late, too long, or invisible.
- Console errors or warnings.
