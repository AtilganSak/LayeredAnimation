using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HeatInteractive.LayeredAnimation.Editor
{
    [CustomEditor(typeof(LayeredAnimationController))]
    public class LayeredAnimatorEditor : UnityEditor.Editor
    {
        private LayeredAnimationController component => target as LayeredAnimationController;

        private SerializedProperty _layersProperty;
        private Animator _animator;
        private AnimatorController _tempController;

        private int _previousAnimCount;

        private void OnEnable()
        {
            _layersProperty = serializedObject.FindProperty("layers");
            _animator = component.GetComponent<Animator>();
        }

        private void OnDisable()
        {
            if (!EditorUtility.IsPersistent(target))
                CleanupTempController();
        }

        public override void OnInspectorGUI()
        {
            if (EditorApplication.isPlaying)
            {
                base.OnInspectorGUI();
                return;
            }

            if (_animator.runtimeAnimatorController == null)
                CreateTempController();

            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                int currentCount = CountTotalAnimations();
                if (currentCount != _previousAnimCount)
                {
                    _previousAnimCount = currentCount;
                    UpdateTempController();
                }
            }
        }

        private int CountTotalAnimations()
        {
            int total = 0;
            for (int i = 0; i < _layersProperty.arraySize; i++)
            {
                var animsProp = _layersProperty.GetArrayElementAtIndex(i).FindPropertyRelative("Animations");
                if (animsProp != null) total += animsProp.arraySize;
            }
            return total;
        }

        private void CollectClips(List<AnimationClip> clips)
        {
            for (int i = 0; i < _layersProperty.arraySize; i++)
            {
                var animsProp = _layersProperty.GetArrayElementAtIndex(i).FindPropertyRelative("Animations");
                if (animsProp == null) continue;
                for (int j = 0; j < animsProp.arraySize; j++)
                {
                    var clipProp = animsProp.GetArrayElementAtIndex(j).FindPropertyRelative("Clip");
                    if (clipProp?.objectReferenceValue is AnimationClip clip)
                        clips.Add(clip);
                }
            }
        }

        private void CreateTempController()
        {
            var clips = new List<AnimationClip>();
            CollectClips(clips);
            if (clips.Count == 0) return;

            _tempController = new AnimatorController();
            _tempController.hideFlags = HideFlags.HideAndDontSave;
            _tempController.AddLayer("Base Layer");

            AnimatorStateMachine stateMachine = _tempController.layers[0].stateMachine;
            foreach (var clip in clips)
            {
                AnimatorState newState = stateMachine.AddState(clip.name);
                newState.motion = clip;
            }

            _animator.runtimeAnimatorController = _tempController;
            _previousAnimCount = clips.Count;
        }

        private void UpdateTempController()
        {
            if (_tempController == null || _tempController.layers.Length == 0)
                return;

            AnimatorStateMachine stateMachine = _tempController.layers[0].stateMachine;
            foreach (var state in stateMachine.states)
                stateMachine.RemoveState(state.state);

            var clips = new List<AnimationClip>();
            CollectClips(clips);
            foreach (var clip in clips)
            {
                AnimatorState newState = stateMachine.AddState(clip.name);
                newState.motion = clip;
            }
        }

        private void CleanupTempController()
        {
            if (_animator != null && _animator.runtimeAnimatorController == _tempController)
                _animator.runtimeAnimatorController = null;

            if (_tempController != null)
            {
                DestroyImmediate(_tempController);
                _tempController = null;
            }
        }
    }
}
