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

        private const string _path = "Assets/_Core/Code/Scripts/LayeredAnimation/temp_animatorcontroller.controller";
        
        private SerializedProperty _animationInfosProperty;
        private Animator _animator;
        private AnimatorController _tempController;

        private int _previousInfoCount;

        private void OnEnable()
        {
            _animationInfosProperty = serializedObject.FindProperty("animationInfos");
            _animator = component.GetComponent<Animator>();
        }

        private void OnDisable()
        {
            if (!EditorUtility.IsPersistent(target))
            {
                CleanupTempController();
            }
        }

        public override void OnInspectorGUI()
        {
            if (EditorApplication.isPlaying)
            {
                base.OnInspectorGUI();
                return;
            }
            
            if (_animator.runtimeAnimatorController == null)
            {
                CreateTempController();
            }
            
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                if (_animationInfosProperty.arraySize != _previousInfoCount)
                {
                    _previousInfoCount = _animationInfosProperty.arraySize;

                    UpdateTempController();
                }
            }
        }

        private void CreateTempController()
        {
            var infos = SerializationHelper.SerializedPropertyToObject<AnimationInfo[]>(_animationInfosProperty);
            if(infos == null || infos.Length == 0)
                return;
            
            _tempController = AnimatorController.CreateAnimatorControllerAtPath(_path);
            _animator.runtimeAnimatorController = _tempController;
            _tempController.hideFlags = HideFlags.HideAndDontSave;
            
            var clips = new List<AnimationClip>();
            foreach (var info in infos)
            {
                clips.Add(info.Clip);
            }
            _tempController.AddLayer("Base Layer");
            AnimatorControllerLayer layer = _tempController.layers[0];
            AnimatorStateMachine stateMachine = layer.stateMachine;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null) continue;
                AnimatorState newState = stateMachine.AddState(clip.name);
                newState.motion = clip;
            }
        }

        private void UpdateTempController()
        {
            // clear first
            AnimatorControllerLayer layer = _tempController.layers[0];
            AnimatorStateMachine stateMachine = layer.stateMachine;
            var states = stateMachine.states;
            foreach (var state in states)
            {
                stateMachine.RemoveState(state.state);
            }
            if (_animationInfosProperty.arraySize > 0)
            {
                var infos = SerializationHelper.SerializedPropertyToObject<AnimationInfo[]>(_animationInfosProperty);
                var clips = new List<AnimationClip>();
                foreach (var info in infos)
                {
                    clips.Add(info.Clip);
                }
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null) continue;
                    AnimatorState newState = stateMachine.AddState(clip.name);
                    newState.motion = clip;
                }
            }
        }

        private void CleanupTempController()
        {
            if (_animator != null && _animator.runtimeAnimatorController == _tempController)
            {
                _animator.runtimeAnimatorController = null;
            }

            if (_tempController != null)
            {
                AssetDatabase.DeleteAsset(_path);
                _tempController = null;
            }
        }
    }
}