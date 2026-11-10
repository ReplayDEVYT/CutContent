using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace mizmii.ReClassic.Patches
{
    internal static class HandClipFallback
    {
        private const float FadeIn = 0.12f;
        private const float FadeOut = 0.15f;

        private static readonly float[] Progress =
        {
            0f, 0f, 0f, 0f, 0.035f, 0.181f, 0.396f, 0.631f, 0.841f, 0.975f, 1f, 1f, 1f, 1f, 1f, 1f,
            0.833f, 0.416f, 0.02f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f
        };

        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
        private static readonly Dictionary<Animator, AvatarMask> Masks = new Dictionary<Animator, AvatarMask>();

        private static PlayableGraph _graph;
        private static AnimationPlayableOutput _output;
        private static AnimationClipPlayable _clip;
        private static float _length;
        private static float _time;
        private static float _speed;
        private static bool _stopping;
        private static float _stopWeight;

        public static bool Active => _graph.IsValid();

        public static float CurrentProgress
        {
            get
            {
                if (!Active || _length <= 0f)
                {
                    return 0f;
                }
                float t = Mathf.Clamp01(_time / _length) * (Progress.Length - 1);
                int i = Mathf.Min((int)t, Progress.Length - 2);
                return Mathf.Lerp(Progress[i], Progress[i + 1], t - i);
            }
        }

        public static bool Play(ObjectInHandsAnimator hands, Animator animator, string clipName, float speed)
        {
            Stop(true);
            AnimationClip clip = FindClip(clipName);
            if (animator == null || clip == null)
            {
                return false;
            }
            AvatarMask mask = MaskFor(animator);
            _graph = PlayableGraph.Create("ReClassic Hand Clip");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _clip = AnimationClipPlayable.Create(_graph, clip);
            _clip.SetApplyFootIK(false);
            _clip.SetSpeed(speed);
            AnimationLayerMixerPlayable mixer = AnimationLayerMixerPlayable.Create(_graph, 1);
            _graph.Connect(_clip, 0, mixer, 0);
            mixer.SetInputWeight(0, 1f);
            if (mask != null)
            {
                mixer.SetLayerMaskFromAvatarMask(0, mask);
            }
            _output = AnimationPlayableOutput.Create(_graph, "ReClassic Hand Clip", animator);
            _output.SetSourcePlayable(mixer);
            _output.SetWeight(0f);
            _length = clip.length;
            _speed = speed;
            _time = 0f;
            _stopping = false;
            _graph.Play();
            return true;
        }

        public static void Tick(float dt)
        {
            if (!Active)
            {
                return;
            }
            _time += dt * _speed;
            float weight;
            if (_stopping)
            {
                _stopWeight -= dt / FadeOut;
                weight = _stopWeight;
            }
            else
            {
                float tail = (_length - _time) / Mathf.Max(0.01f, _speed);
                weight = Mathf.Min(Mathf.Clamp01(_time / Mathf.Max(0.01f, _speed) / FadeIn), Mathf.Clamp01(tail / FadeOut));
            }
            if (weight <= 0f && (_stopping || _time >= _length))
            {
                Stop(true);
                return;
            }
            _output.SetWeight(Mathf.Clamp01(weight));
        }

        public static void Stop(bool immediate)
        {
            if (!Active)
            {
                return;
            }
            if (!immediate)
            {
                if (!_stopping)
                {
                    _stopping = true;
                    _stopWeight = _output.GetWeight();
                }
                return;
            }
            _graph.Destroy();
            _stopping = false;
        }

        private static AnimationClip FindClip(string name)
        {
            if (Clips.TryGetValue(name, out AnimationClip cached) && cached != null)
            {
                return cached;
            }
            foreach (AnimationClip clip in Resources.FindObjectsOfTypeAll<AnimationClip>())
            {
                if (clip != null && clip.name == name)
                {
                    Clips[name] = clip;
                    return clip;
                }
            }
            return null;
        }

        private static AvatarMask MaskFor(Animator animator)
        {
            if (Masks.TryGetValue(animator, out AvatarMask cached) && cached != null)
            {
                return cached;
            }
            try
            {
                Transform root = animator.transform;
                var paths = new List<KeyValuePair<string, bool>>();
                Collect(root, root, false, paths);
                var mask = new AvatarMask { name = "ReClassic Left Arm" };
                mask.transformCount = paths.Count;
                for (int i = 0; i < paths.Count; i++)
                {
                    mask.SetTransformPath(i, paths[i].Key);
                    mask.SetTransformActive(i, paths[i].Value);
                }
                Masks[animator] = mask;
                return mask;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not mask the hand clip to the left arm: {e.Message}");
                return null;
            }
        }

        private static void Collect(Transform root, Transform current, bool left, List<KeyValuePair<string, bool>> paths)
        {
            bool active = left || current.name.StartsWith("Base HumanL", StringComparison.Ordinal);
            if (current != root)
            {
                paths.Add(new KeyValuePair<string, bool>(Path(root, current), active));
            }
            for (int i = 0; i < current.childCount; i++)
            {
                Collect(root, current.GetChild(i), active, paths);
            }
        }

        private static string Path(Transform root, Transform current)
        {
            string path = current.name;
            Transform parent = current.parent;
            while (parent != null && parent != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
