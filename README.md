# ◈ SEMI-SPACES — Integration Guide

> *"Between dimensions, the boundaries thin. What emerges from the semi-spaces does not come in peace."*

**Semi-Spaces** is a first-person survival shooter set in fractured interdimensional zones where reality itself is unstable. The player navigates collapsing pocket dimensions, fighting off Swarm entities that bleed through the cracks and engaging Duelist-class anomalies that mirror the player's own combat patterns. Every encounter reshapes the space around you.

> [!IMPORTANT]
> **This document is the single source of truth for cross-team integration.**
> All team members must read this before writing or merging any code.

---

## ◈ Division of Labor

| Callsign | Name | Domain | Ownership |
|----------|------|--------|-----------|
| **Architect** | Aisaiah | Core Architecture & Player Controller | `Assets/Scripts/Core/`, `Assets/Scripts/Player/` |
| **Swarm Lead** | Jen | Swarm AI | `Assets/Scripts/AI/Swarm/` |
| **Duelist Lead** | Ash | Duelist AI | `Assets/Scripts/AI/Duelist/` |
| **World Builder** | Jyesh | Level Design & Game Manager | `Assets/Scenes/`, `Assets/Scripts/GameManager/` |

**Golden Rule:** You own your directory. You do *not* modify files outside your domain without a PR review from that domain's owner.

---

## ◈ Core Architecture

### The IDamageable Contract

All damage in Semi-Spaces flows through a single interface:

```
Assets/Scripts/Core/IDamageable.cs
Assets/Scripts/Core/Health.cs          (canonical implementation)
```

```csharp
public interface IDamageable
{
    void TakeDamage(int damageAmount);
}
```

`Health.cs` is the canonical implementation of `IDamageable`. It is a **generic component** — not tied to any specific entity. Player, Swarm drones, Duelist anomalies, and destructible props all use the same `Health` component.

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    DAMAGE FLOW                          │
│                                                         │
│   [Any Damage Source]                                   │
│         │                                               │
│         ▼                                               │
│   TryGetComponent<IDamageable>()                        │
│         │                                               │
│         ▼                                               │
│   IDamageable.TakeDamage(amount)                        │
│         │                                               │
│         ▼                                               │
│   ┌─────────────┐    ┌──────────────┐                   │
│   │  Health.cs   │    │  Future:     │                   │
│   │  (current)   │    │  Shield.cs   │                   │
│   │              │    │  Armor.cs    │                   │
│   └──────┬──────┘    └──────────────┘                   │
│          │                                              │
│          ▼                                              │
│   OnDeath event / onDeathEvent (UnityEvent)             │
│          │                                              │
│          ▼                                              │
│   [Game Manager / Level Triggers / VFX]                 │
└─────────────────────────────────────────────────────────┘
```

---

## ◈ Enemy Architecture — `EnemyBase.cs`

`EnemyBase` is the **shared foundation** for all enemy types. Think of it as a fully functional enemy *body* — it can see, move, and die — but it has no *brain*. The AI team plugs in the brain by extending the class and overriding `OnThink()`.

```
  EnemyBase (abstract "body" — Aisaiah)
  ├── Perception Layer: LOS, FOV cone, Last Known Position (auto-updates)
  ├── Execution Layer:  NavMeshAgent wrappers (PRIVATE agent, public wrappers)
  └── Health Lifecycle:  Auto-subscribes to Health.OnDeath, shuts down on kill
        │
        ├── SwarmAgent : EnemyBase      (Jen's "brain" — Boids algorithm)
        │     └── override OnThink()  →  separation + alignment + cohesion + pursuit
        │
        └── DuelistBrain : EnemyBase    (Ash's "brain" — Utility scoring)
              └── override OnThink()  →  score actions + priority overrides
```

> [!IMPORTANT]
> **Jen and Ash: your scripts MUST extend `EnemyBase`.** Do not create standalone MonoBehaviours with your own NavMeshAgent or Health references. EnemyBase already handles all of that.

> [!CAUTION]
> **`Awake()`, `Update()`, and `OnDestroy()` are PRIVATE in EnemyBase.** You **cannot** override them. If you try, your code will silently run alongside the base — not replace it — and you'll get unpredictable bugs. Use the virtual hooks described below instead.

### Your Hooks (override these)

| Hook | Required? | When It Fires | Purpose |
|---|---|---|---|
| `OnInit()` | Optional | End of `Awake()`, after all base setup | One-time init: cache neighbours, register with managers |
| `OnThink()` | **Yes** (abstract) | Every frame, after perception updates | Your AI algorithm (Boids / Utility scoring) |
| `OnEnemyDeath()` | Optional | After death shutdown (NavMesh disabled) | Cleanup: stop coroutines, play death VFX |
| `OnCleanup()` | Optional | `OnDestroy()`, after health unsubscribe | Final teardown: unsubscribe events, clear statics |

### Perception Data (read-only, auto-updated)

| Property / Method | Type | Description |
|---|---|---|
| `IsPlayerVisible` | `bool` | Player is in range + FOV + LOS |
| `LastKnownPosition` | `Vector3` | Last seen position (persists after LOS break) |
| `HasDetectedPlayer` | `bool` | True once the player has ever been spotted |
| `GetDistanceToPlayer()` | `float` | Current distance to the player |
| `GetDirectionToPlayer()` | `Vector3` | Normalized direction toward the player |
| `Player` | `Transform` | The player's Transform reference |

### Navigation Wrappers (the ONLY way to move)

> [!WARNING]
> **The `NavMeshAgent` is PRIVATE.** You cannot call `agent.SetDestination()`, `agent.speed`, or any NavMeshAgent method directly. Use the wrappers below. This is intentional — it keeps your decision logic decoupled from the execution layer.

| Wrapper | Description |
|---|---|
| `MoveToTarget(Vector3)` | Pathfind to a world position |
| `StopNavigation()` | Halt immediately |
| `SetSpeed(float)` | Override movement speed |
| `ResetSpeed()` | Restore Inspector-configured default speed |
| `HasReachedDestination()` | True when the agent arrives at its destination |
| `Velocity` (read-only) | Current NavMeshAgent velocity vector |
| `RemainingDistance` (read-only) | Path distance remaining |
| `IsOnNavMesh` (read-only) | Whether the agent is on a valid NavMesh |

### Health Data (read-only)

| Property | Description |
|---|---|
| `CurrentHealth` | Current HP (int) — useful for Duelist retreat thresholds |
| `MaxHealth` | Max HP (int) |
| `IsDead` | True after the death sequence runs |

### Minimal Example

```csharp
public class SwarmAgent : EnemyBase
{
    // ✅ Use OnInit() instead of Awake()
    protected override void OnInit()
    {
        // One-time setup after base is fully initialised.
    }

    // ✅ Use OnThink() instead of Update()
    protected override void OnThink()
    {
        if (!IsPlayerVisible) return;

        Vector3 steering = CalculateBoids();
        MoveToTarget(transform.position + steering);
    }

    // ✅ Use OnEnemyDeath() instead of OnDestroy()
    protected override void OnEnemyDeath()
    {
        // Cleanup: notify swarm group, play VFX.
    }
}
```

```csharp
// ❌ WRONG — these will NOT work as expected
public class BrokenEnemy : EnemyBase
{
    private void Awake() { }      // ← HIDDEN, does not replace base Awake
    private void Update() { }     // ← HIDDEN, does not replace base Update
    private NavMeshAgent agent;   // ← REDUNDANT, base agent is private
}
```

---

## ◈ Rules of Engagement — AI (Jen & Ash)

### You MUST extend `EnemyBase`

All enemy scripts **must** inherit from `EnemyBase`. Do not create standalone MonoBehaviours that duplicate navigation, perception, or health wiring. Starter stubs are provided:
- `Assets/Scripts/AI/Swarm/SwarmAgent.cs` — Jen's starting point
- `Assets/Scripts/AI/Duelist/DuelistBrain.cs` — Ash's starting point

### You MUST NOT override Unity lifecycle methods

`Awake()`, `Update()`, and `OnDestroy()` are **private** in EnemyBase. Use the hooks:

| Instead of... | Use... |
|---|---|
| `Awake()` | `OnInit()` |
| `Update()` | `OnThink()` |
| `OnDestroy()` | `OnCleanup()` |

### You MUST NOT access the NavMeshAgent directly

The NavMeshAgent is **private** in EnemyBase. Use the wrapper methods (`MoveToTarget`, `StopNavigation`, `SetSpeed`, etc.). If you need velocity or distance data, use the read-only accessors (`Velocity`, `RemainingDistance`, `IsOnNavMesh`).

### You MUST use `Health.cs` for enemy health

Every Swarm drone and Duelist anomaly prefab **must** have the `Health` component attached. Do not create custom health scripts. Configure `maxHealth` in the Inspector per-prefab. EnemyBase auto-requires it.

### You MUST deal damage through `IDamageable`

When your AI attacks the player or another entity, resolve it through the interface — **never** reference `Health` directly from your attack scripts:

```csharp
// ✅ CORRECT — resolve through the interface
if (targetCollider.TryGetComponent(out IDamageable target))
{
    target.TakeDamage(attackDamage);
}

// ❌ WRONG — do NOT reference Health directly from AI attack code
if (targetCollider.TryGetComponent(out Health health))
{
    health.TakeDamage(attackDamage);
}
```

### You MAY subscribe to death events in code

If your AI needs to react when a target dies (e.g., Swarm drones scattering when their leader is killed):

```csharp
// Get the Health component to subscribe to its events.
Health targetHealth = target.GetComponent<Health>();
if (targetHealth != null)
{
    targetHealth.OnDeath += HandleTargetDeath;
}
```

### Directory Structure

```
Assets/Scripts/AI/
├── EnemyBase.cs        ← Aisaiah's domain (DO NOT MODIFY)
├── Swarm/              ← Jen's domain
│   ├── SwarmAgent.cs   ← extends EnemyBase (starter stub provided)
│   ├── SwarmFormation.cs
│   └── ...
└── Duelist/            ← Ash's domain
    ├── DuelistBrain.cs ← extends EnemyBase (starter stub provided)
    ├── DuelistCombat.cs
    └── ...
```

### Combat & Damage — `ContactDamage.cs`

`ContactDamage.cs` is a **reusable, zero-code damage primitive** that lives in `Core/`. Attach it to any GameObject with a trigger collider to deal damage on contact via `IDamageable`.

> [!TIP]
> **Jen:** To give your Swarm drones melee attacks, you do NOT need to write collision code. Just attach `ContactDamage` to the drone prefab and configure it in the Inspector.

**Setup (Swarm drone example):**

1. Select the Swarm drone prefab.
2. Add a **Collider** (Sphere/Capsule) → check **Is Trigger**.
3. Attach `ContactDamage.cs`.
4. Configure in Inspector:

| Field | What It Does | Suggested Value |
|---|---|---|
| `damageAmount` | Damage per hit (int, resolves via IDamageable) | `5` for drones, `15` for Duelist melee |
| `damageInterval` | Seconds between hits on the **same** target | `0.5` = 2 hits/sec max |
| `targetLayers` | Which layers receive damage | Set to `Player` only |
| `disableAfterHit` | One-shot mode (for projectile impacts) | `false` for melee |

**What it handles for you:**
- Per-target cooldown (won't deal 60 damage ticks per second)
- IDamageable resolution (works with Health, future Shield, Armor, etc.)
- Layer filtering (drones won't damage each other)
- Zero heap allocations inside physics callbacks

**What you still own:**
- Your AI brain decides **when** to chase and get close — `ContactDamage` handles the damage on contact automatically.

```csharp
// You do NOT need to write this in your brain script:
// ❌ void OnTriggerEnter(Collider c) { c.GetComponent<IDamageable>()?.TakeDamage(5); }
//
// ContactDamage.cs already does this with proper cooldowns.
// Just attach it to the prefab and focus on your OnThink() algorithm.
```

### Ranged Attacks — `EnemyProjectile.cs`

`EnemyProjectile.cs` is a **kinematic projectile** in `Core/`. It moves via `transform.Translate` (no Rigidbody), uses a swept raycast each frame to prevent tunnelling through thin walls, and resolves damage through `IDamageable` on impact.

> [!TIP]
> **Ash:** To give your Duelist ranged attacks, create a bullet prefab, attach `EnemyProjectile`, and call `Instantiate()` from your brain script. The projectile handles all raycast math, damage, and self-cleanup.

**Setup (Duelist projectile example):**

1. Create a small **Projectile prefab** (quad, capsule, or VFX particle).
2. Attach `EnemyProjectile.cs`.
3. Configure in Inspector:

| Field | What It Does | Suggested Value |
|---|---|---|
| `speed` | Travel speed (units/sec) | `25` |
| `damage` | Damage on hit (resolves via IDamageable) | `15` |
| `lifetime` | Self-destruct failsafe (seconds) | `4` |
| `hitMask` | Which layers the projectile can hit | `Player` + `Environment` (exclude `Enemy`) |

**Spawning from your brain script:**

```csharp
// In DuelistBrain — add these fields:
[SerializeField] private GameObject projectilePrefab;
[SerializeField] private Transform firePoint;

// Then call this when you want to shoot:
private void FireProjectile()
{
    // The projectile spawns facing firePoint.forward and handles everything else.
    Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
}
```

**What it handles for you:**
- Swept raycasting (anti-tunnelling through thin walls at high speed)
- IDamageable damage resolution on impact
- Self-destructs on ANY hit (wall, player, prop) or after lifetime expires
- Layer filtering via `hitMask` (won't hit the shooter)

**What you still own:**
- Create the `firePoint` Transform on your Duelist prefab (an empty child object at the "muzzle")
- Decide **when** to fire (in your `OnThink()` / `ExecuteAttack()` logic)
- Set the fire rate / cooldown in your brain script

---

## ◈ Rules of Engagement — Level Design (Jyesh)

### You MUST NOT modify internal prefab logic

Do not open or edit scripts in `Assets/Scripts/Player/`, `Assets/Scripts/AI/`, or `Assets/Scripts/Core/`. Your job is to **compose**, not to code internals.

### You SHOULD use the Inspector-wired UnityEvents

The `Health` component exposes two UnityEvents in the Inspector:

| UnityEvent | Fires When | Use Case |
|---|---|---|
| `onDeathEvent` | Entity health reaches zero | Trigger doors, spawn waves, update score, play VFX |
| `onHealthChangedEvent(int, int)` | Entity takes damage or heals | Update world-space health bars, trigger alarms |

**Example workflow for level progression:**

1. Place the Swarm Boss prefab in your scene.
2. Select it, find the `Health` component in the Inspector.
3. Under `onDeathEvent`, click **+** and drag in your `LevelManager` GameObject.
4. Set the function to `LevelManager.OnBossDefeated()` (or whatever your progression method is).

> [!CAUTION]
> **Never call `Destroy()` on player or AI prefabs from your level scripts.**
> Death and cleanup are handled internally by `Health.cs`. Use the `onDeathEvent` to *react* to death, not to *cause* it.

### You MAY place and configure prefabs

- Set `maxHealth` values per-instance in the Inspector.
- Position spawn points, patrol waypoints, and trigger volumes.
- Wire UnityEvents for level flow.

### Directory Structure

```
Assets/Scenes/             ← Your scenes
Assets/Scripts/GameManager/ ← Your game logic
```

---

## ◈ For the UI/UX Team (Jyesh) — `GameManager.cs`

The **GameManager** is now live as a global Singleton (`GameManager.Instance`). It owns all game state transitions — you do **not** need to write state-tracking logic. Your job is to build the visual UI responses (Canvas screens, animations) and wire them to the events below.

> [!IMPORTANT]
> **You do NOT need to track win/loss/pause state yourself.** GameManager handles all of it. You only need to show/hide UI panels in response to these events.

### Available Events (wire in Inspector)

Select the **GameManager** GameObject → find these UnityEvent fields → click **+** → drag your UI Canvas/Panel → select the appropriate method (e.g., `SetActive(true)`).

| UnityEvent | Fires When | What to Show |
|---|---|---|
| `onGameStarted` | Game begins (after Awake/Start) | Hide menu, show HUD |
| `onGamePaused` | Player presses Escape | Show pause menu overlay |
| `onGameResumed` | Player unpauses | Hide pause menu |
| `onGameOver` | Player dies (Health.OnDeath) | Show "GAME OVER" screen + restart button |
| `onGameWon` | All waves cleared (WaveManager) | Show "VICTORY" screen |

### Wiring the Win Condition

The WaveManager fires `onGameWon` when the final wave is cleared. To connect it to GameManager:

1. Select the **WaveManager** GameObject.
2. Find `onGameWon` in the Inspector → click **+**.
3. Drag the **GameManager** GameObject into the slot.
4. Set the function to `GameManager.CompleteGame()`.

### Wiring UI Buttons

GameManager exposes public methods for common UI buttons:

| Button Action | Method to Wire |
|---|---|
| "Restart" button | `GameManager.RestartGame()` |
| "Quit" button | `GameManager.QuitGame()` |
| "Resume" button | `GameManager.ResumeGame()` |
| "Pause" button | `GameManager.PauseGame()` |

### Reading State from Code (if needed)

```csharp
// Check game state from any script:
if (GameManager.Instance.IsPlaying) { /* gameplay logic */ }
if (GameManager.Instance.IsPaused)  { /* skip input */ }
if (GameManager.Instance.IsGameEnded) { /* disable AI */ }

// Access the enum directly:
GameManager.GameState state = GameManager.Instance.CurrentState;
```

### WaveManager Events (for HUD kill counter / wave display)

These events live on the **WaveManager** GameObject, not GameManager:

| UnityEvent | Parameters | Use Case |
|---|---|---|
| `onWaveStarted(int)` | Wave number (1-indexed) | "WAVE 3" splash text |
| `onEnemyKilled(int, int)` | (remaining, total) | Kill counter: "12/20" |
| `onWaveCleared` | — | Wave complete fanfare |

> [!CAUTION]
> **Do not set `Time.timeScale` from your UI scripts.** GameManager owns timeScale. If you need to pause, call `GameManager.Instance.PauseGame()` — don't set it directly or you'll desync the state machine.

---

## ◈ Testing Protocol

> [!WARNING]
> **All features must be tested in `Aisaiah_TestScene` before opening a Pull Request.**

### Pre-PR Checklist

- [ ] Feature works in `Aisaiah_TestScene` with no console errors.
- [ ] No modifications to files outside your owned directories.
- [ ] Damage flows through `IDamageable` — no direct `Health` references from attack scripts.
- [ ] `OnDeath` events fire correctly (check console logs: `[Health] 'EntityName' has died.`).
- [ ] No `NullReferenceException` when the target is destroyed mid-frame.

### How to Test

1. Open `Assets/Scenes/Aisaiah_TestScene.unity`.
2. Enter Play Mode.
3. Verify your feature against the checklist above.
4. Screenshot or screen-record the console output.
5. Include evidence in your PR description.

---

## ◈ Script Reference

| Script | Location | Owner | Purpose |
|--------|----------|-------|---------|
| `IDamageable.cs` | `Core/` | Aisaiah | Interface contract for all damage |
| `Health.cs` | `Core/` | Aisaiah | Canonical health + death logic |
| `ReturnToPool.cs` | `Core/` | Aisaiah | Returns pooled particles to ObjectPool |
| `FirstPersonController.cs` | `Player/` | Aisaiah | FPS movement, sprint, crouch, slide, viewbob |
| `RaycastShooter.cs` | `Player/` | Aisaiah | Hitscan weapon with shotgun spread, ammo, pooled hit sparks |
| `WeaponManager.cs` | `Player/` | Aisaiah | Weapon switching (number keys + scroll wheel) |
| `EnemyBase.cs` | `AI/` | Aisaiah | Abstract enemy foundation — perception, navigation, death lifecycle |
| `ContactDamage.cs` | `Core/` | Aisaiah | Reusable melee/hazard damage via IDamageable with per-target cooldown |
| `EnemyProjectile.cs` | `Core/` | Aisaiah | Kinematic ranged projectile — swept raycast, IDamageable damage, self-cleanup |
| `GameManager.cs` | `GameManager/` | Aisaiah | Singleton game state (Playing/Paused/GameOver/GameWon) + win/loss flow |
| `WaveManager.cs` | `GameManager/` | Aisaiah | Wave spawning, enemy death tracking, wave progression events |

---

## ◈ Tech Stack

| Dependency | Version | Notes |
|---|---|---|
| **Unity** | 6.x | LTS recommended |
| **Input System** | New (`UnityEngine.InputSystem`) | Legacy Input is **not** used |
| **Render Pipeline** | — | As configured per project |

---

## ◈ Branching Convention

```
main                    ← stable, tested, deployable
├── dev                 ← integration branch
│   ├── aisaiah/core-*  ← Aisaiah's feature branches
│   ├── jen/swarm-*     ← Jen's feature branches
│   ├── ash/duelist-*   ← Ash's feature branches
│   └── jyesh/level-*   ← Jyesh's feature branches
```

**All PRs target `dev`.** Merges to `main` require Aisaiah's approval.

---

> *"The semi-spaces don't forgive sloppy code. Neither do we."*
> — Aisaiah, Core Architect