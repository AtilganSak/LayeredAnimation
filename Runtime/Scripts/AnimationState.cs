using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace HeatInteractive.LayeredAnimation
{
    public class Events
    {
        public Action EndEvent;
        public List<Event> OtherEvents;

        public Events()
        {
            OtherEvents = new List<Event>();
        }
    }

    public class Event
    {
        public float EventTime;
        public Action EventCallback;
        public bool HasTriggered;

        public Event(float eventTime, Action eventCallback)
        {
            EventTime = eventTime;
            EventCallback = eventCallback;
        }
    }

    public class AnimationState : PlayableBehaviour
    {
        public int LoopCount => _loopCount;
        public bool IsPlaying { get; private set; }

        public Events Events
        {
            get
            {
                if (_events == null)
                    _events = new Events();
                return _events;
            }
        }

        private Events _events;

        private ScriptPlayable<AnimationState> _scriptPlayable;
        private AnimationClipPlayable _playableClip;
        private AnimationMixerPlayable _parentMixer;
        private Playable _playable;
        private int _mixerInputIndex;
        private bool _isLooping;
        private bool _isInitialized;
        private float _duration;
        private int _loopCount;

        public void Init(ScriptPlayable<AnimationState> scriptPlayable, AnimationClipPlayable playable, AnimationMixerPlayable parentMixer, int mixerInputIndex, bool isLooping, float duration)
        {
            if (_isInitialized)
                return;

            _scriptPlayable = scriptPlayable;
            _playableClip = playable;
            _parentMixer = parentMixer;
            _mixerInputIndex = mixerInputIndex;
            _isLooping = isLooping;
            _duration = duration;
            _loopCount = 0;

            _isInitialized = true;
        }

        public override void OnPlayableCreate(Playable playable)
        {
            _playable = playable;
        }

        public void Play(float time = 0)
        {
            IsPlaying = true;
            
            _scriptPlayable.Play();
            _parentMixer.SetInputWeight(_mixerInputIndex, 1);
            _playableClip.SetTime(time);
            _playableClip.Play();
        }

        public void Stop()
        {
            IsPlaying = false;
            
            _playable.SetTime(0);
            _playableClip.SetTime(0);
            _playable.Pause();
            _playableClip.Pause();
            _scriptPlayable.Pause();
            _parentMixer.SetInputWeight(_mixerInputIndex, 0);
            
            if (Events.OtherEvents.Count > 0)
            {
                int eventCount = Events.OtherEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    Events.OtherEvents[i].HasTriggered = false;
                }
            }
        }

        public void AddEvent(float eventTime, Action eventCallback)
        {
            AddEvent(new Event(eventTime, eventCallback));
        }

        public void AddEvent(Event e)
        {
            if (e != null)
                Events.OtherEvents.Add(e);
        }

        public override void PrepareFrame(Playable playable, FrameData info)
        {
            if (!_isInitialized || !IsPlaying) return;
            
            var time = (float)playable.GetInput(0).GetTime();
            float currentNormalizedTime = _duration > 0 ? time / _duration : 0;

            if (Events.OtherEvents.Count > 0)
            {
                int eventCount = Events.OtherEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    if (!Events.OtherEvents[i].HasTriggered && currentNormalizedTime >= Events.OtherEvents[i].EventTime)
                    {
                        Events.OtherEvents[i].EventCallback?.Invoke();
                        Events.OtherEvents[i].HasTriggered = true;
                    }
                }
            }

            if (currentNormalizedTime >= 1)
            {
                playable.SetTime(0);
                _playableClip.SetTime(0);
                if (!_isLooping)
                {
                    playable.Pause();
                    _playableClip.Pause();
                    _scriptPlayable.Pause();
                    _parentMixer.SetInputWeight(_mixerInputIndex, 0);

                    Events.EndEvent?.Invoke();
                }
                else
                    _loopCount++;

                if (Events.OtherEvents.Count > 0)
                {
                    int eventCount = Events.OtherEvents.Count;
                    for (int i = 0; i < eventCount; i++)
                    {
                        Events.OtherEvents[i].HasTriggered = false;
                    }
                }
            }
        }
    }
}