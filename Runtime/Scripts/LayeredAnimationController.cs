using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeatInteractive.LayeredAnimation
{
    [RequireComponent(typeof(Animator))]
    public class LayeredAnimationController : MonoBehaviour
    {
        public bool IgnoreTimeScale;
        public bool PlayOnAwake;
        [SerializeField] private LayerData[] layers;

        private Animator _animator;
        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _layerMixerPlayable;
        private AnimationPlayableOutput _playableOutput;

        private Dictionary<(int layer, string state), AnimationState> _states = new();
        private Dictionary<int, AnimationMixerPlayable> _layerMixers = new();
        private Dictionary<int, List<AnimationState>> _statesByLayer = new();
        private List<CrossfadeInfo> _activeCrossfades = new();

        private bool _isInitialized;

        private void Awake()
        {
            Init();
            if (PlayOnAwake &&
                layers != null && layers.Length > 0 &&
                layers[0].Animations != null && layers[0].Animations.Length > 0)
            {
                SetState(layers[0].Animations[0].State, 0);
            }
        }

        private void Init()
        {
            if (_isInitialized) return;

            _animator = GetComponent<Animator>();
            _animator.runtimeAnimatorController = null;

            int layerCount = layers != null ? layers.Length : 0;
            if (layerCount == 0)
            {
                _isInitialized = true;
                return;
            }

            string graphName = $"PlayableGraph_{gameObject.name}";
            _playableGraph = PlayableGraph.Create(graphName);
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _layerMixerPlayable = AnimationLayerMixerPlayable.Create(_playableGraph, layerCount);
            _playableOutput = AnimationPlayableOutput.Create(_playableGraph, $"{graphName}_Output", _animator);
            _playableOutput.SetSourcePlayable(_layerMixerPlayable);

            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var layerData = layers[layerIdx];
                int animCount = layerData.Animations != null ? layerData.Animations.Length : 0;

                var mixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(animCount, 1));
                _layerMixers[layerIdx] = mixer;
                _statesByLayer[layerIdx] = new List<AnimationState>();

                _playableGraph.Connect(mixer, 0, _layerMixerPlayable, layerIdx);
                _layerMixerPlayable.SetInputWeight(layerIdx, layerData.Weight);
                _layerMixerPlayable.SetLayerAdditive((uint)layerIdx, layerData.Additive);

                if (animCount == 0) continue;

                for (int mixerIndex = 0; mixerIndex < animCount; mixerIndex++)
                {
                    var info = layerData.Animations[mixerIndex];
                    if (info.Clip == null) continue;

                    var clip = AnimationClipPlayable.Create(_playableGraph, info.Clip);
                    clip.SetTime(0);
                    clip.SetDuration(info.Clip.length);
                    clip.Pause();

                    var scriptPlayable = ScriptPlayable<AnimationState>.Create(_playableGraph);
                    scriptPlayable.Pause();
                    scriptPlayable.AddInput(clip, 0, 0);

                    var state = scriptPlayable.GetBehaviour();
                    state.Init(scriptPlayable, clip, mixer, mixerIndex, info.Clip.isLooping, info.Clip.length);

                    _playableGraph.Connect(scriptPlayable, 0, mixer, mixerIndex);
                    mixer.SetInputWeight(mixerIndex, 0);

                    _states[(layerIdx, info.State)] = state;
                    _statesByLayer[layerIdx].Add(state);
                }
            }

            _playableGraph.Play();
            _isInitialized = true;
        }

        private void TryInitialize()
        {
            if (!_isInitialized) Init();
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
            for (int i = _activeCrossfades.Count - 1; i >= 0; i--)
            {
                var cf = _activeCrossfades[i];
                cf.Elapsed += deltaTime;
                float t = Mathf.Clamp01(cf.Elapsed / cf.Duration);

                cf.From?.SetWeight(1f - t);
                cf.To.SetWeight(t);

                _activeCrossfades[i] = cf;

                if (t >= 1f)
                {
                    cf.From?.Stop();
                    _activeCrossfades.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Stops all states across all layers.
        /// </summary>
        public void Stop()
        {
            foreach (var state in _states.Values)
                state.Stop();
        }

        /// <summary>
        /// Stops all states on the specified layer.
        /// </summary>
        public void StopLayer(int layer)
        {
            if (_statesByLayer.TryGetValue(layer, out var states))
                foreach (var state in states)
                    state.Stop();
        }

        /// <summary>
        /// Plays the state. Other states on the same layer are stopped; other layers are unaffected.
        /// Use crossfadeDuration > 0 for a smooth blend from the currently playing state.
        /// </summary>
        public AnimationState SetState(string stateName, int layer = 0, float time = 0, float crossfadeDuration = 0)
        {
            TryInitialize();

            if (!_states.TryGetValue((layer, stateName), out var animationState))
            {
#if UNITY_EDITOR
                Debug.LogError($"State '{stateName}' on layer {layer} does not exist!");
#endif
                return null;
            }

            if (crossfadeDuration <= 0)
            {
                if (_statesByLayer.TryGetValue(layer, out var layerStates))
                    foreach (var s in layerStates)
                        if (s != animationState) s.Stop();

                animationState.Play(time);
            }
            else
            {
                // Already transitioning to this state on this layer — skip
                for (int i = 0; i < _activeCrossfades.Count; i++)
                    if (_activeCrossfades[i].Layer == layer && _activeCrossfades[i].To == animationState)
                        return animationState;

                // Cancel any active crossfade on the same layer
                for (int i = _activeCrossfades.Count - 1; i >= 0; i--)
                    if (_activeCrossfades[i].Layer == layer)
                        _activeCrossfades.RemoveAt(i);

                AnimationState fromState = null;
                if (_statesByLayer.TryGetValue(layer, out var layerStates))
                {
                    foreach (var s in layerStates)
                        if (s != animationState && s.IsPlaying) { fromState = s; break; }

                    foreach (var s in layerStates)
                        if (s != animationState && s != fromState) s.Stop();
                }

                animationState.Play(time);
                animationState.SetWeight(0f);

                _activeCrossfades.Add(new CrossfadeInfo
                {
                    Layer = layer,
                    From = fromState,
                    To = animationState,
                    Duration = crossfadeDuration,
                    Elapsed = 0f
                });
            }

            return animationState;
        }

        public AnimationState GetState(string stateName, int layer)
        {
            TryInitialize();

            if (_states.TryGetValue((layer, stateName), out var animationState))
                return animationState;

#if UNITY_EDITOR
            Debug.LogError($"State '{stateName}' on layer {layer} does not exist!");
#endif
            return null;
        }

        public bool TryGetState(string stateName, int layer, out AnimationState animationState)
        {
            TryInitialize();
            return _states.TryGetValue((layer, stateName), out animationState);
        }

        public bool HasState(string stateName, int layer) => _states.ContainsKey((layer, stateName));

        /// <summary>
        /// Sets the layer blend weight at runtime (0 = invisible, 1 = full).
        /// </summary>
        public void SetLayerWeight(int layer, float weight)
        {
            if (_layerMixerPlayable.IsValid())
                _layerMixerPlayable.SetInputWeight(layer, Mathf.Clamp01(weight));
        }

        private void OnDestroy()
        {
            if (_isInitialized && _playableGraph.IsValid())
                _playableGraph.Destroy();
        }
    }

    internal struct CrossfadeInfo
    {
        public int Layer;
        public AnimationState From;
        public AnimationState To;
        public float Duration;
        public float Elapsed;
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
