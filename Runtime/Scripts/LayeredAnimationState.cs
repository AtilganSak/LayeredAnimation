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
        public List<TimedEvent> OtherEvents;

        public Events()
        {
            OtherEvents = new List<TimedEvent>();
        }
    }

    public class TimedEvent
    {
        /// <summary>Normalized time (0–1) at which this event fires.</summary>
        public float EventTime;
        public Action EventCallback;
        public bool HasTriggered;

        public TimedEvent(float eventTime, Action eventCallback)
        {
            EventTime = eventTime;
            EventCallback = eventCallback;
        }
    }

    public class LayeredAnimationState : PlayableBehaviour
    {
        /// <summary>Number of loops completed since the last Play / PlayReverse.</summary>
        public int LoopCount { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsReversed { get; private set; }
        public bool FreezeOnEnd { get; set; } = true;

        public Events Events => _events ??= new Events();

        /// <summary>True while the state contributes to the pose: playing, or frozen on its end frame.</summary>
        internal bool IsActive { get; private set; }
        internal float Weight => _parentMixer.GetInputWeight(_mixerInputIndex);
        internal float FadeStartWeight;

        private Events _events;

        private ScriptPlayable<LayeredAnimationState> _scriptPlayable;
        private AnimationClipPlayable _playableClip;
        private AnimationMixerPlayable _parentMixer;
        private int _mixerInputIndex;
        private bool _isLooping;
        private bool _isInitialized;
        private float _duration;
        // Bumped on every Play / PlayReverse / Stop so PrepareFrame can tell when a callback restarted or stopped the state.
        private int _playId;

        public void Init(ScriptPlayable<LayeredAnimationState> scriptPlayable, AnimationClipPlayable playable, AnimationMixerPlayable parentMixer, int mixerInputIndex, bool isLooping, float duration)
        {
            if (_isInitialized) return;

            _scriptPlayable = scriptPlayable;
            _playableClip = playable;
            _parentMixer = parentMixer;
            _mixerInputIndex = mixerInputIndex;
            _isLooping = isLooping;
            _duration = duration;

            _isInitialized = true;
        }

        /// <summary>
        /// Plays the animation forward from the given time (seconds).
        /// Events before the start time are skipped.
        /// </summary>
        public void Play(float time = 0)
        {
            Begin(false);

            float startNormalized = _duration > 0 ? time / _duration : 0;
            foreach (var e in Events.OtherEvents)
                e.HasTriggered = e.EventTime < startNormalized;

            _playableClip.SetSpeed(1);
            _playableClip.SetTime(time);
            _playableClip.Play();
        }

        /// <summary>
        /// Plays the animation in reverse.
        /// startTime &lt; 0 = reverse from current position; >= 0 = reverse from that time.
        /// If the start position is 0 (e.g. the state is stopped), it reverses from the end of the clip.
        /// Timed events do not fire in reverse.
        /// </summary>
        public void PlayReverse(float startTime = -1)
        {
            if (startTime < 0)
                startTime = (float)_playableClip.GetTime();
            if (startTime <= 0)
                startTime = _duration;

            Begin(true);

            _playableClip.SetSpeed(-1);
            _playableClip.SetTime(startTime);
            _playableClip.Play();
        }

        public void Stop()
        {
            _playId++;
            IsPlaying = false;
            IsReversed = false;
            IsActive = false;

            _playableClip.SetSpeed(1);
            _playableClip.SetTime(0);
            _scriptPlayable.Pause();
            _playableClip.Pause();
            SetWeight(0);

            ResetEvents();
        }

        internal void SetWeight(float weight)
        {
            _parentMixer.SetInputWeight(_mixerInputIndex, weight);
        }

        public void AddEvent(float eventTime, Action eventCallback)
        {
            AddEvent(new TimedEvent(eventTime, eventCallback));
        }

        public void AddEvent(TimedEvent e)
        {
            if (e != null)
                Events.OtherEvents.Add(e);
        }

        public override void PrepareFrame(Playable playable, FrameData info)
        {
            if (!_isInitialized || !IsPlaying) return;

            var time = (float)_playableClip.GetTime();

            if (IsReversed)
            {
                if (time > 0f) return;

                if (_isLooping && _duration > 0)
                {
                    _playableClip.SetTime(Mathf.Repeat(time, _duration));
                    LoopCount++;
                }
                else
                {
                    Finish(0f);
                }
                return;
            }

            float normalizedTime = _duration > 0 ? time / _duration : 1;

            int playId = _playId;
            var events = Events.OtherEvents;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.HasTriggered || normalizedTime < e.EventTime) continue;

                e.HasTriggered = true;
                e.EventCallback?.Invoke();
                if (playId != _playId) return;
            }

            if (normalizedTime < 1) return;

            if (_isLooping && _duration > 0)
            {
                // Keep the overshoot so the loop stays seamless.
                _playableClip.SetTime(time % _duration);
                LoopCount += (int)(time / _duration);
                ResetEvents();
            }
            else
            {
                Finish(_duration);
            }
        }

        private void Begin(bool reversed)
        {
            _playId++;
            IsPlaying = true;
            IsActive = true;
            IsReversed = reversed;
            LoopCount = 0;

            _scriptPlayable.Play();
            SetWeight(1);
        }

        private void Finish(float endTime)
        {
            IsPlaying = false;
            IsReversed = false;

            _playableClip.SetSpeed(1);
            _playableClip.Pause();
            _scriptPlayable.Pause();

            if (FreezeOnEnd)
            {
                _playableClip.SetTime(endTime);
            }
            else
            {
                IsActive = false;
                _playableClip.SetTime(0);
                SetWeight(0);
            }

            // Last, so the callback may safely play this or another state.
            Events.EndEvent?.Invoke();
        }

        private void ResetEvents()
        {
            var events = Events.OtherEvents;
            for (int i = 0; i < events.Count; i++)
                events[i].HasTriggered = false;
        }
    }
}
