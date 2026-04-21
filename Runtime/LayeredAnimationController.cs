using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LayeredAnimation
{
    [RequireComponent(typeof(Animator))]
    public class LayeredAnimationController : MonoBehaviour
    {
        public bool IgnoreTimeScale;
        public bool PlayOnAwake;
        [SerializeField] private AnimationInfo[] animationInfos;

        private Animator _animator;
        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _layerMixerPlayable;
        private AnimationMixerPlayable _mixerPlayable;
        private AnimationPlayableOutput _playableOutput;

        private Dictionary<string, AnimationState> _states = new();

        private bool _isInitialized;

        private void Awake()
        {
            Init();
            if (PlayOnAwake)
            {
                if (_states.Count > 0)
                {
                    SetState(animationInfos[0].State);
                }
            }
        }

        private void Init()
        {
            if (_isInitialized)
                return;

            _animator = GetComponent<Animator>();
            _animator.runtimeAnimatorController = null;

            string graphName = $"PlayableGraph_{gameObject.name}";
            _playableGraph = PlayableGraph.Create(graphName);
            _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _layerMixerPlayable = AnimationLayerMixerPlayable.Create(_playableGraph, 1);
            _playableOutput = AnimationPlayableOutput.Create(_playableGraph, $"{graphName}_Output", _animator);
            _playableOutput.SetSourcePlayable(_layerMixerPlayable);
            _mixerPlayable = AnimationMixerPlayable.Create(_playableGraph, animationInfos.Length);
            _playableGraph.Connect(_mixerPlayable, 0, _layerMixerPlayable, 0);
            _layerMixerPlayable.SetInputWeight(0, 1);

            if (animationInfos.Length > 0)
            {
                _states.Clear();
                int mixerIndex = 0;
                foreach (var animationInfo in animationInfos)
                {
                    var playable = AnimationClipPlayable.Create(_playableGraph, animationInfo.Clip);
                    playable.SetTime(0);
                    playable.Pause();

                    var scriptPlayable = ScriptPlayable<AnimationState>.Create(_playableGraph);
                    scriptPlayable.Pause();
                    
                    var state = scriptPlayable.GetBehaviour();
                    state.Init(scriptPlayable, playable, _mixerPlayable, mixerIndex, animationInfo.Clip.isLooping, animationInfo.Clip.length);
                    scriptPlayable.AddInput(playable, 0, 0);
                    
                    _playableGraph.Connect(scriptPlayable, 0, _mixerPlayable, mixerIndex);
                    _mixerPlayable.SetInputWeight(mixerIndex, 0);
                    
                    playable.SetDuration(animationInfo.Clip.length);
                    
                    _states[animationInfo.State] = state;
                    mixerIndex++;
                }
            }

            _playableGraph.Play();

            _isInitialized = true;
        }

        private void TryInitialize()
        {
            if (!_isInitialized)
                Init();
        }

        private void Update()
        {
            if (!_playableGraph.IsValid())
                return;

            _playableGraph.Evaluate(IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        public void Stop()
        {
            foreach (var state in _states.Values)
            {
                state.Stop();
            }
        }

        public AnimationState SetState(string state, float time = 0)
        {
            TryInitialize();

            if (_states.TryGetValue(state, out var animationState))
            {
                animationState.Play(time);
                return animationState;
            }

#if UNITY_EDITOR
            Debug.LogError($"State {state} does not exist!");
#endif
            return null;
        }

        public AnimationState GetState(string state, int layer = 0)
        {
            TryInitialize();

            if (_states.TryGetValue(state, out var animationState))
            {
                return animationState;
            }

#if UNITY_EDITOR
            Debug.LogError($"State {state} does not exist!");
#endif
            return null;
        }

        public bool TryGetState(string state, int layer, out AnimationState animationState)
        {
            TryInitialize();

            if (_states.TryGetValue(state, out animationState))
            {
                return true;
            }

            animationState = default;
            return false;
        }

        public bool HasState(string state)
        {
            return _states.ContainsKey(state);
        }

        private void OnDestroy()
        {
            _playableGraph.Destroy();
        }
    }

    [Serializable]
    public class AnimationInfo
    {
        [SerializeField] public string State;
        [SerializeField] public AnimationClip Clip;
    }
}