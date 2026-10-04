using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeatInteractive.LayeredAnimation
{
    [RequireComponent(typeof(Animator))]
    public class LayeredAnimationController : MonoBehaviour, IAnimationClipSource
    {
        public bool IgnoreTimeScale;
        public bool PlayOnAwake;
        [SerializeField] private LayerData[] layers;

        private Animator _animator;
        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _layerMixerPlayable;
        private AnimationPlayableOutput _playableOutput;

        private readonly Dictionary<(int layer, string state), LayeredAnimationState> _states = new();
        private List<LayeredAnimationState>[] _statesByLayer = Array.Empty<List<LayeredAnimationState>>();
        // At most one crossfade per layer; null = none.
        private Crossfade[] _crossfades = Array.Empty<Crossfade>();

        private bool _isInitialized;

        private void Awake()
        {
            Init();
            if (PlayOnAwake && _statesByLayer.Length > 0 && _statesByLayer[0].Count > 0)
                _statesByLayer[0][0].Play();
        }

        private void Init()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _animator = GetComponent<Animator>();
            _animator.runtimeAnimatorController = null;

            int layerCount = layers != null ? layers.Length : 0;
            if (layerCount == 0) return;

            _statesByLayer = new List<LayeredAnimationState>[layerCount];
            _crossfades = new Crossfade[layerCount];

            string graphName = $"PlayableGraph_{gameObject.name}";
            _playableGraph = PlayableGraph.Create(graphName);
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _layerMixerPlayable = AnimationLayerMixerPlayable.Create(_playableGraph, layerCount);
            _playableOutput = AnimationPlayableOutput.Create(_playableGraph, $"{graphName}_Output", _animator);
            _playableOutput.SetSourcePlayable(_layerMixerPlayable);

            for (int layerIdx = 0; layerIdx < layerCount; layerIdx++)
            {
                var layerData = layers[layerIdx];
                int animCount = layerData.Animations != null ? layerData.Animations.Length : 0;

                var mixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(animCount, 1));
                _statesByLayer[layerIdx] = new List<LayeredAnimationState>();

                _playableGraph.Connect(mixer, 0, _layerMixerPlayable, layerIdx);
                _layerMixerPlayable.SetInputWeight(layerIdx, layerData.Weight);
                _layerMixerPlayable.SetLayerAdditive((uint)layerIdx, layerData.Additive);

                for (int mixerIndex = 0; mixerIndex < animCount; mixerIndex++)
                {
                    var info = layerData.Animations[mixerIndex];
                    if (info.Clip == null) continue;

                    var key = (layerIdx, info.State);
                    if (_states.ContainsKey(key))
                    {
                        Debug.LogWarning($"Duplicate state '{info.State}' on layer {layerIdx} of '{name}'. Only the first one is used.", this);
                        continue;
                    }

                    var clip = AnimationClipPlayable.Create(_playableGraph, info.Clip);
                    clip.SetTime(0);
                    clip.SetDuration(info.Clip.length);
                    clip.Pause();

                    var scriptPlayable = ScriptPlayable<LayeredAnimationState>.Create(_playableGraph);
                    scriptPlayable.Pause();
                    scriptPlayable.AddInput(clip, 0, 0);

                    var state = scriptPlayable.GetBehaviour();
                    state.Init(scriptPlayable, clip, mixer, mixerIndex, info.Clip.isLooping, info.Clip.length);

                    _playableGraph.Connect(scriptPlayable, 0, mixer, mixerIndex);
                    mixer.SetInputWeight(mixerIndex, 0);

                    _states[key] = state;
                    _statesByLayer[layerIdx].Add(state);
                }
            }

            _playableGraph.Play();
        }

        private void Update()
        {
            if (!_isInitialized || !_playableGraph.IsValid()) return;

            float deltaTime = IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            ProcessCrossfades(deltaTime);
            _playableGraph.Evaluate(deltaTime);
        }

        private void ProcessCrossfades(float deltaTime)
        {
            for (int layer = 0; layer < _crossfades.Length; layer++)
            {
                var cf = _crossfades[layer];
                if (cf == null) continue;

                cf.Elapsed += deltaTime;
                float t = Mathf.Clamp01(cf.Elapsed / cf.Duration);

                // Target rises from its start weight to 1, the rest scale down from theirs to 0,
                // so the layer's total weight stays constant even when a crossfade interrupts another.
                foreach (var s in _statesByLayer[layer])
                {
                    if (!s.IsActive) continue; // stopped or ended on its own, weight already 0
                    s.SetWeight(s == cf.To ? Mathf.Lerp(s.FadeStartWeight, 1f, t) : s.FadeStartWeight * (1f - t));
                }

                if (t < 1f) continue;

                foreach (var s in _statesByLayer[layer])
                    if (s != cf.To && s.IsActive) s.Stop();
                _crossfades[layer] = null;
            }
        }

        /// <summary>
        /// Stops all states across all layers.
        /// </summary>
        public void Stop()
        {
            Array.Clear(_crossfades, 0, _crossfades.Length);
            foreach (var state in _states.Values)
                state.Stop();
        }

        /// <summary>
        /// Stops all states on the specified layer.
        /// </summary>
        public void StopLayer(int layer)
        {
            Init();
            if (!IsValidLayer(layer)) return;

            _crossfades[layer] = null;
            foreach (var state in _statesByLayer[layer])
                state.Stop();
        }

        /// <summary>
        /// Plays the state. Other states on the same layer are stopped; other layers are unaffected.
        /// Use crossfadeDuration > 0 for a smooth blend from whatever is currently visible on the layer
        /// (playing, frozen on its end frame, or mid-crossfade). If nothing is visible on the layer, the state starts instantly.
        /// </summary>
        public LayeredAnimationState SetState(string stateName, int layer = 0, float time = 0, float crossfadeDuration = 0)
        {
            Init();

            if (!_states.TryGetValue((layer, stateName), out var animationState))
            {
#if UNITY_EDITOR
                Debug.LogError($"State '{stateName}' on layer {layer} does not exist!");
#endif
                return null;
            }

            var layerStates = _statesByLayer[layer];

            // Already transitioning to this state on this layer — skip
            if (crossfadeDuration > 0 && _crossfades[layer]?.To == animationState)
                return animationState;

            bool hasVisibleOther = false;
            foreach (var s in layerStates)
            {
                if (s == animationState || !s.IsActive) continue;
                s.FadeStartWeight = s.Weight;
                hasVisibleOther = true;
            }

            if (crossfadeDuration <= 0 || !hasVisibleOther)
            {
                _crossfades[layer] = null;
                foreach (var s in layerStates)
                    if (s != animationState) s.Stop();

                animationState.Play(time);
                return animationState;
            }

            float startWeight = animationState.IsActive ? animationState.Weight : 0f;
            animationState.Play(time);
            animationState.FadeStartWeight = startWeight;
            animationState.SetWeight(startWeight);

            _crossfades[layer] = new Crossfade { To = animationState, Duration = crossfadeDuration };
            return animationState;
        }

        public LayeredAnimationState GetState(string stateName, int layer = 0)
        {
            Init();

            if (_states.TryGetValue((layer, stateName), out var animationState))
                return animationState;

#if UNITY_EDITOR
            Debug.LogError($"State '{stateName}' on layer {layer} does not exist!");
#endif
            return null;
        }

        public bool TryGetState(string stateName, int layer, out LayeredAnimationState animationState)
        {
            Init();
            return _states.TryGetValue((layer, stateName), out animationState);
        }

        public bool HasState(string stateName, int layer = 0)
        {
            Init();
            return _states.ContainsKey((layer, stateName));
        }

        /// <summary>
        /// Sets the layer blend weight at runtime (0 = invisible, 1 = full).
        /// </summary>
        public void SetLayerWeight(int layer, float weight)
        {
            Init();
            if (IsValidLayer(layer))
                _layerMixerPlayable.SetInputWeight(layer, Mathf.Clamp01(weight));
        }

        private bool IsValidLayer(int layer) => layer >= 0 && layer < _statesByLayer.Length;

        /// <summary>
        /// Lists the clips in the Animation window without assigning an Animator Controller,
        /// so selecting the object in edit mode never writes clip values into the scene.
        /// </summary>
        public void GetAnimationClips(List<AnimationClip> results)
        {
            if (layers == null) return;

            foreach (var layer in layers)
            {
                if (layer.Animations == null) continue;
                foreach (var info in layer.Animations)
                    if (info.Clip != null && !results.Contains(info.Clip))
                        results.Add(info.Clip);
            }
        }

        private void OnDestroy()
        {
            if (_isInitialized && _playableGraph.IsValid())
                _playableGraph.Destroy();
        }

        private sealed class Crossfade
        {
            public LayeredAnimationState To;
            public float Duration;
            public float Elapsed;
        }
    }

    [Serializable]
    public class LayerData
    {
        [SerializeField, Range(0f, 1f)] public float Weight = 1f;
        [SerializeField] public bool Additive = false;
        [SerializeField] public AnimationInfo[] Animations;
    }

    [Serializable]
    public class AnimationInfo
    {
        [SerializeField] public string State;
        [SerializeField] public AnimationClip Clip;
    }
}
