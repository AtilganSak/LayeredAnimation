# Layered Animation Controller

A lightweight Unity animation system built on the **Playable API** — no Animator Controller required. Manage animation states, blending, looping, and time-based events entirely through code.

---

## Features

- **No Animator Controller** — runs purely via Unity's Playable API
- **State-based playback** — define and switch between named animation states
- **Loop support** — per-clip loop detection, loop count tracking
- **Time-based events** — fire callbacks at specific normalized times within a clip
- **End events** — trigger actions when a non-looping animation finishes
- **Unscaled time support** — `IgnoreTimeScale` flag for UI or slow-motion scenarios
- **Play on Awake** — optionally auto-play the first state on startup
- **Editor preview** — Inspector previews clips via a temporary Animator Controller (edit-time only)

---

## Requirements

- Unity 2021.3 or later
- `com.unity.playables` package (included with Unity)

---

## Installation

1. Copy the `LayeredAnimation` folder into your project's `Scripts` directory.
2. Attach `LayeredAnimationController` to any GameObject with an `Animator` component.
3. Add your animation clips and state names in the Inspector.

---

## Setup

1. Add the `LayeredAnimationController` component to your GameObject.
2. In the Inspector, populate the **Animation Infos** list:
    - **State** — a string key used to reference this animation in code
    - **Clip** — the `AnimationClip` to play for this state
3. Optionally enable **Play On Awake** to auto-play the first state.
4. Optionally enable **Ignore Time Scale** for time-scale-independent playback.

---

## Usage

### Play a State

```csharp
LayeredAnimationController controller = GetComponent<LayeredAnimationController>();

// Play from the beginning
controller.SetState("Run");

// Play from a specific time (in seconds)
controller.SetState("Run", 0.5f);
```

### Stop All States

```csharp
controller.Stop();
```

### Check if a State Exists

```csharp
if (controller.HasState("Jump"))
{
    controller.SetState("Jump");
}
```

### Get a State Reference

```csharp
AnimationState state = controller.GetState("Attack");
```

### Try Get a State Reference

```csharp
if (controller.TryGetState("Death", 0, out AnimationState state))
{
    // use state
}
```

---

## Animation Events

### End Event

Fires once when a **non-looping** animation finishes:

```csharp
AnimationState state = controller.SetState("Death");
state.Events.EndEvent += () =>
{
    Debug.Log("Death animation finished!");
};
```

### Timed Events

Fire a callback at a specific **normalized time** (0.0 – 1.0) within the clip:

```csharp
AnimationState state = controller.GetState("Attack");

// Fires at 50% through the clip
state.AddEvent(0.5f, () =>
{
    Debug.Log("Hit frame!");
});

// Fires at 80% through the clip
state.AddEvent(0.8f, () =>
{
    SpawnEffect();
});
```

Events automatically reset each time the animation loops or replays.

---

## API Reference

### `LayeredAnimationController`

| Method / Property | Description |
|---|---|
| `SetState(string state, float time = 0)` | Plays the given state, returns the `AnimationState` |
| `GetState(string state)` | Returns the `AnimationState` without playing it |
| `TryGetState(string state, int layer, out AnimationState)` | Safe version of `GetState` |
| `HasState(string state)` | Returns true if the state key exists |
| `Stop()` | Stops and resets all active states |
| `PlayOnAwake` | Auto-plays the first state on `Awake` |
| `IgnoreTimeScale` | Uses `Time.unscaledDeltaTime` when true |

### `AnimationState`

| Method / Property | Description |
|---|---|
| `IsPlaying` | True while the state is playing |
| `LoopCount` | Number of times the clip has looped |
| `Events.EndEvent` | `Action` fired when a non-looping clip ends |
| `AddEvent(float normalizedTime, Action callback)` | Registers a timed event callback |

---

## How It Works

```
Animator
  └── AnimationPlayableOutput
        └── AnimationLayerMixerPlayable
              └── AnimationMixerPlayable
                    ├── ScriptPlayable<AnimationState> [0]  →  AnimationClipPlayable
                    ├── ScriptPlayable<AnimationState> [1]  →  AnimationClipPlayable
                    └── ...
```

Each animation state is wrapped in a `ScriptPlayable<AnimationState>` that handles time tracking, event dispatch, and loop detection via `PrepareFrame`. The mixer input weights are set to `1` for the active state and `0` for all others.

---

## Notes

- Only one state plays at a time (no blending between states).
- The editor-time Animator Controller is a temporary preview tool and is removed at runtime automatically.
- The graph runs in **Manual** update mode and is evaluated in `Update` using `Time.unscaledDeltaTime` or `Time.deltaTime` depending on the `IgnoreTimeScale` setting.

---

## License

MIT License. Feel free to use and modify in personal or commercial projects.