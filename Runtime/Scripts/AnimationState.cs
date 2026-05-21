using System;
using System.Collections.Generic;
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
        /// <summary>Normalized time (0–1) at which this event fires.</summary>
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
        public bool IsReversed { get; private set; }
        public bool FreezeOnEnd { get; set; } = true;

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
        private int _mixerInputIndex;
        private bool _isLooping;
        private bool _isInitialized;
        private float _duration;
        private int _loopCount;

        public void Init(ScriptPlayable<AnimationState> scriptPlayable, AnimationClipPlayable playable, AnimationMixerPlayable parentMixer, int mixerInputIndex, bool isLooping, float duration)
        {
            if (_isInitialized) return;

            _scriptPlayable = scriptPlayable;
            _playableClip = playable;
            _parentMixer = parentMixer;
            _mixerInputIndex = mixerInputIndex;
            _isLooping = isLooping;
            _duration = duration;
            _loopCount = 0;

            _isInitialized = true;
        }

        public void Play(float time = 0)
        {
            IsPlaying = true;
            IsReversed = false;

            _scriptPlayable.Play();
            _parentMixer.SetInputWeight(_mixerInputIndex, 1);
            _playableClip.SetSpeed(1);
            _playableClip.SetTime(time);
            _playableClip.Play();
        }

        /// <summary>
        /// Plays the animation in reverse.
        /// startTime &lt; 0 = reverse from current position; >= 0 = reverse from that time.
        /// </summary>
        public void PlayReverse(float startTime = -1)
        {
            IsPlaying = true;
            IsReversed = true;

            _scriptPlayable.Play();
            _parentMixer.SetInputWeight(_mixerInputIndex, 1);
            _playableClip.SetSpeed(-1);

            if (startTime >= 0)
                _playableClip.SetTime(startTime);

            _playableClip.Play();
        }

        public void Stop()
        {
            IsPlaying = false;
            IsReversed = false;

            _playableClip.SetSpeed(1);
            _scriptPlayable.SetTime(0);
            _playableClip.SetTime(0);
            _scriptPlayable.Pause();
            _playableClip.Pause();
            _parentMixer.SetInputWeight(_mixerInputIndex, 0);

            ResetEvents();
        }

        internal void SetWeight(float weight)
        {
            _parentMixer.SetInputWeight(_mixerInputIndex, weight);
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
            float normalizedTime = _duration > 0 ? time / _duration : 0;

            if (IsReversed)
            {
                if (time <= 0f)
                {
                    IsPlaying = false;
                    IsReversed = false;
                    ResetEvents();
                    _playableClip.SetSpeed(1);
                    Events.EndEvent?.Invoke();

                    if (FreezeOnEnd)
                    {
                        _playableClip.SetTime(0);
                        playable.Pause();
                        _playableClip.Pause();
                        _scriptPlayable.Pause();
                    }
                    else
                    {
                        playable.SetTime(0);
                        _playableClip.SetTime(0);
                        playable.Pause();
                        _playableClip.Pause();
                        _scriptPlayable.Pause();
                        _parentMixer.SetInputWeight(_mixerInputIndex, 0);
                    }
                }
                return;
            }

            if (Events.OtherEvents.Count > 0)
            {
                int count = Events.OtherEvents.Count;
                for (int i = 0; i < count; i++)
                {
                    var e = Events.OtherEvents[i];
                    if (!e.HasTriggered && normalizedTime >= e.EventTime)
                    {
                        e.EventCallback?.Invoke();
                        e.HasTriggered = true;
                    }
                }
            }

            if (normalizedTime >= 1)
            {
                ResetEvents();

                if (_isLooping)
                {
                    playable.SetTime(0);
                    _playableClip.SetTime(0);
                    _loopCount++;
                }
                else
                {
                    IsPlaying = false;
                    Events.EndEvent?.Invoke();

                    if (FreezeOnEnd)
                    {
                        _playableClip.SetTime(_duration);
                        playable.Pause();
                        _playableClip.Pause();
                        _scriptPlayable.Pause();
                    }
                    else
                    {
                        playable.SetTime(0);
                        _playableClip.SetTime(0);
                        playable.Pause();
                        _playableClip.Pause();
                        _scriptPlayable.Pause();
                        _parentMixer.SetInputWeight(_mixerInputIndex, 0);
                    }
                }
            }
        }

        private void ResetEvents()
        {
            int count = Events.OtherEvents.Count;
            for (int i = 0; i < count; i++)
                Events.OtherEvents[i].HasTriggered = false;
        }
    }
}
