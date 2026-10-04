# Layered Animation Controller

A lightweight Unity animation system built on the **Playable API** — no Animator Controller required. Manage animation states, layers, crossfades, looping, and time-based events entirely through code.

---

## Features

- **No Animator Controller** — runs purely via Unity's Playable API
- **State-based playback** — define and switch between named animation states
- **Layers** — per-layer weight and additive blending, each layer plays its own state
- **Crossfades** — smooth blends between states, including interrupting a running crossfade
- **Reverse playback** — play any state backwards
- **Loop support** — per-clip loop detection, seamless looping, loop count tracking
- **Time-based events** — fire callbacks at specific normalized times within a clip
- **End events** — trigger actions when a non-looping animation finishes
- **Unscaled time support** — `IgnoreTimeScale` flag for UI or slow-motion scenarios
- **Play on Awake** — optionally auto-play the first state on startup
- **Animation window support** — the component's clips are listed in the Animation window (via `IAnimationClipSource`), no Animator Controller needed

---

## Requirements

- Unity 6000.4 or later

---

## Installation

1. Open **Window → Package Manager** in Unity.
2. Click the **+** button and select **Add package from git URL**.
3. Enter the following URL and click **Add**:
   ```
   https://github.com/AtilganSak/LayeredAnimation.git
   ```

---

## Setup

1. Add the `LayeredAnimationController` component to your GameObject.
2. In the Inspector, add entries to the **Layers** list. Each layer has:
   - **Weight** — the layer's blend weight (0–1)
   - **Additive** — blend the layer additively on top of the layers below
   - **Animations** — the states on this layer:
     - **State** — a string key used to reference this animation in code (must be unique per layer)
     - **Clip** — the `AnimationClip` to play for this state
3. Optionally enable **Play On Awake** to auto-play the first state of layer 0.
4. Optionally enable **Ignore Time Scale** for time-scale-independent playback.

---

## Usage

### Play a State

```csharp
LayeredAnimationController controller = GetComponent<LayeredAnimationController>();

// Play from the beginning on layer 0
controller.SetState("Run");

// Play on layer 1
controller.SetState("Wave", 1);

// Play from a specific time (in seconds)
controller.SetState("Run", 0, 0.5f);
```

Playing a state stops the other states on the same layer. Other layers are unaffected.

### Crossfade

```csharp
// Blend from whatever is visible on layer 0 to "Idle" over 0.25 seconds
controller.SetState("Idle", 0, 0f, 0.25f);
```

The crossfade starts from the current pose of the layer: a playing state, a state frozen on its last frame, or a crossfade that is still running. If nothing is visible on the layer, the state starts instantly. To fade a whole layer in or out, use `SetLayerWeight`.

### Reverse Playback

```csharp
LayeredAnimationState door = controller.GetState("DoorOpen");

door.PlayReverse();      // from the current position (or from the end if the state is stopped)
door.PlayReverse(0.3f);  // from 0.3 seconds
```

### Stop

```csharp
controller.Stop();        // all layers
controller.StopLayer(1);  // one layer
```

### Layer Weight

```csharp
controller.SetLayerWeight(1, 0.5f);
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
LayeredAnimationState state = controller.GetState("Attack");
```

### Try Get a State Reference

```csharp
if (controller.TryGetState("Death", 0, out LayeredAnimationState state))
{
    // use state
}
```

### End-of-Clip Behaviour

By default a non-looping state stays frozen on its last frame when it ends (`FreezeOnEnd = true`). Set it to `false` to hide the state when it ends:

```csharp
controller.GetState("Hit").FreezeOnEnd = false;
```

---

## Animation Events

### End Event

Fires once when a **non-looping** animation finishes (forward or reverse):

```csharp
LayeredAnimationState state = controller.SetState("Death");
state.Events.EndEvent += () =>
{
    Debug.Log("Death animation finished!");
};
```

It is safe to play this or another state from inside the callback.

### Timed Events

Fire a callback at a specific **normalized time** (0.0 – 1.0) within the clip:

```csharp
LayeredAnimationState state = controller.GetState("Attack");

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

- Events reset each time the animation loops or is played again.
- When a state is played from a start time, events before that time are skipped.
- Timed events do not fire during reverse playback.

---

## API Reference

### `LayeredAnimationController`

| Method / Property | Description |
|---|---|
| `SetState(string state, int layer = 0, float time = 0, float crossfadeDuration = 0)` | Plays the given state, returns the `LayeredAnimationState` |
| `GetState(string state, int layer = 0)` | Returns the `LayeredAnimationState` without playing it |
| `TryGetState(string state, int layer, out LayeredAnimationState)` | Safe version of `GetState` |
| `HasState(string state, int layer = 0)` | Returns true if the state key exists |
| `Stop()` | Stops and resets all states on all layers |
| `StopLayer(int layer)` | Stops and resets all states on one layer |
| `SetLayerWeight(int layer, float weight)` | Sets a layer's blend weight (0–1) |
| `PlayOnAwake` | Auto-plays the first state of layer 0 on `Awake` |
| `IgnoreTimeScale` | Uses `Time.unscaledDeltaTime` when true |

### `LayeredAnimationState`

| Method / Property | Description |
|---|---|
| `Play(float time = 0)` | Plays forward from `time` (seconds) |
| `PlayReverse(float startTime = -1)` | Plays backwards; `-1` = from the current position |
| `Stop()` | Stops and resets the state |
| `IsPlaying` | True while the state is playing |
| `IsReversed` | True while the state is playing in reverse |
| `FreezeOnEnd` | Keep the last frame visible when a non-looping clip ends (default `true`) |
| `LoopCount` | Number of loops completed since the last `Play` / `PlayReverse` |
| `Events.EndEvent` | `Action` fired when a non-looping clip ends |
| `AddEvent(float normalizedTime, Action callback)` | Registers a timed event callback |

> Calling `Play` / `Stop` on a state directly bypasses the controller's layer handling (other states on the layer keep playing). Prefer `controller.SetState` unless you need that.

---

## How It Works

```
Animator
  └── AnimationPlayableOutput
        └── AnimationLayerMixerPlayable
              └── AnimationMixerPlayable (one per layer)
                    ├── ScriptPlayable<LayeredAnimationState> [0]  →  AnimationClipPlayable
                    ├── ScriptPlayable<LayeredAnimationState> [1]  →  AnimationClipPlayable
                    └── ...
```

Each animation state is wrapped in a `ScriptPlayable<LayeredAnimationState>` that handles time tracking, event dispatch, and loop detection via `PrepareFrame`. Within a layer, the active state has mixer weight `1` and all others `0`; during a crossfade the weights are blended so the layer's total weight stays constant.

---

## Notes

- Nothing runs in edit mode: selecting the object never samples a clip, so values you set in the scene (e.g. blend shapes) stay as they are.
- Any Animator Controller assigned to the Animator is cleared at runtime.
- The graph runs in **Manual** update mode and is evaluated in `Update` using `Time.unscaledDeltaTime` or `Time.deltaTime` depending on the `IgnoreTimeScale` setting.

---

## Upgrading from 1.x

- `AnimationState` was renamed to `LayeredAnimationState`, and `Event` to `TimedEvent`, to avoid name clashes with `UnityEngine.AnimationState` and `UnityEngine.Event`.
- `Play` now resets timed events and `LoopCount`.
- `PlayReverse()` on a stopped state now plays from the end of the clip instead of ending immediately.
- Looping reverse playback now loops instead of stopping.
- A crossfade when nothing is visible on the layer now starts the state instantly instead of fading in from the default pose.
- The custom Inspector and its temporary Animator Controller were removed. Clips appear in the Animation window through `IAnimationClipSource` instead, and selecting the object no longer overwrites animated values in the scene.

---

## License

[FSL-1.1-MIT](LICENSE.md) — free for internal use, education, research, and non-competing projects. Converts to MIT automatically 2 years after each release.
