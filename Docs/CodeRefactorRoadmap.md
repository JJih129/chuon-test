# Code Refactor Roadmap

## Goals

- Keep runtime scene searches out of per-frame and repeated cinematic paths.
- Move cinematic timing, dialogue, and camera shot data out of monolithic controllers.
- Split large gameplay controllers by responsibility before changing behavior.
- Prefer cached references and explicit scene bindings over dynamic discovery.

## Current Hotspots

- `Assets/Scripts/Boss/BossController.cs`: split pattern selection, locomotion, attack execution, and intro pause handling.
- `Assets/Scripts/UI/MainSceneArrivalController.cs`: split arrival sequence, dialogue presentation, elevator control, boss reveal, and camera control.
- `Assets/Scripts/Ultimate/PlayerUltimateController.cs`: split gauge, activation validation, cinematic session, and damage application.
- `Assets/Scripts/Combat/PlayerCombatController.cs`: split input buffering, combo state, root motion, and hitbox windows.

## Refactor Rules

- One behavior-preserving slice per change.
- Compile after every slice.
- Do not move scene references silently; keep serialized fields backward-compatible.
- Replace repeated `FindObjectOfType` calls with cached resolver methods first, then move to explicit binders.
- Convert magic timing values to named constants or ScriptableObject data before tuning.

## MainScene Intro Next Steps

- Move Timeline timings into a `ScriptableObject` profile.
- Move dialogue lines into a data asset usable by designers.
- Extract elevator door/grille logic into `ElevatorDoorController`.
- Extract boss hologram/walkout into `BossIntroRevealController`.
- Keep `MainSceneArrivalController` as an orchestrator only.
