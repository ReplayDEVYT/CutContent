using AnimationSystem;
using EFT;
using EFT.Interactive;
using FastAnimatorSystem;
using System;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal sealed class HandActions : MonoBehaviour
    {
        private static readonly float[] Curve =
        {
            0f, 0f, 0f, 0f, 0.035f, 0.181f, 0.396f, 0.631f, 0.841f, 0.975f, 1f, 1f, 1f, 1f, 1f, 1f,
            0.833f, 0.416f, 0.02f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f, -0.1f
        };

        private enum Phase
        {
            Reach,
            Turn,
            Push,
            Release,
            Scrub
        }

        private static HandActions _instance;
        private static IAnimator _animator;
        private static int _layer = -1;
        private static string _state;
        private static float _phaseTime;
        private static float _turnSeconds = 0.2f;
        private static float _normalized;
        private static float _weight;
        private static Phase _phase;
        private static bool _running;
        private static bool _fallback;
        private static bool _cued;
        private static Action _openCue;

        public static bool Active => _running || HandClipFallback.Active;

        public static string PhaseName => _fallback ? "Fallback" : _running ? _phase.ToString() : "Idle";

        public static float Normalized => _normalized;

        public static float TurnTime => _turnSeconds;

        public static float Progress
        {
            get
            {
                if (_fallback)
                {
                    return HandClipFallback.CurrentProgress;
                }
                if (!_running)
                {
                    return -0.1f;
                }
                if (_phase == Phase.Turn)
                {
                    return 1f;
                }
                return Sample(_normalized);
            }
        }

        public static float TurnFor(float handleSeconds)
        {
            HandTiming timing = HandTiming.Current;
            if (timing.TurnSeconds > 0f)
            {
                return timing.TurnSeconds;
            }
            return Mathf.Clamp(handleSeconds, timing.MinTurnSeconds, timing.MaxTurnSeconds);
        }

        public static bool Start(Player player, FirearmsAnimator hands, WorldInteractiveObject target, string state, string clip, float speed, float handleSeconds, Action openCue)
        {
            Stop(true);
            EnsureDriver();
            IAnimator animator = hands?.Animator;
            if (animator == null)
            {
                return false;
            }
            int layer = Layer(hands, animator);
            if (layer >= 0 && HasState(animator, state, layer))
            {
                Begin(animator, layer, state, Phase.Reach);
                _turnSeconds = TurnFor(handleSeconds);
                _openCue = openCue;
                _cued = openCue == null;
                return true;
            }
            if (clip != null && HandClipFallback.Play(hands, FallbackAnimator(player, hands), clip, speed))
            {
                _fallback = true;
                return true;
            }
            return false;
        }

        public static bool Scrub(FirearmsAnimator hands, string state, float normalized)
        {
            EnsureDriver();
            IAnimator animator = hands?.Animator;
            if (animator == null)
            {
                return false;
            }
            if (!_running || _phase != Phase.Scrub || _state != state)
            {
                Stop(true);
                int layer = Layer(hands, animator);
                if (layer < 0 || !HasState(animator, state, layer))
                {
                    return false;
                }
                Begin(animator, layer, state, Phase.Scrub);
                _weight = 1f;
            }
            _normalized = Mathf.Clamp01(normalized);
            return true;
        }

        public static void EndScrub()
        {
            if (_running && _phase == Phase.Scrub)
            {
                Finish();
            }
        }

        public static void Stop(bool immediate)
        {
            if (_fallback)
            {
                HandClipFallback.Stop(immediate);
                if (immediate)
                {
                    _fallback = false;
                }
            }
            if (_running && immediate)
            {
                Finish();
            }
        }

        private static void Begin(IAnimator animator, int layer, string state, Phase phase)
        {
            _animator = animator;
            _layer = layer;
            _state = state;
            _phase = phase;
            _phaseTime = 0f;
            _normalized = 0f;
            _weight = 0f;
            _running = true;
            _fallback = false;
            _openCue = null;
            _cued = true;
            animator.Play(state, layer, 0f);
            animator.SetLayerWeight(layer, 0f);
        }

        private static int Layer(FirearmsAnimator hands, IAnimator animator)
        {
            return hands.LACTIONS_LAYER_INDEX >= 0 ? hands.LACTIONS_LAYER_INDEX : animator.GetLayerIndex("LActions");
        }

        private static void EnsureDriver()
        {
            if (_instance != null)
            {
                return;
            }
            var host = new GameObject("ReClassic Hand Actions");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<HandActions>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_fallback)
            {
                HandClipFallback.Tick(dt);
                if (!HandClipFallback.Active)
                {
                    _fallback = false;
                }
            }
            if (!_running)
            {
                return;
            }
            try
            {
                Step(dt);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] The hand animation stopped: {e.Message}");
                Finish();
            }
        }

        private static void Cue()
        {
            if (_cued)
            {
                return;
            }
            _cued = true;
            try
            {
                _openCue?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not open early: {e.Message}");
            }
        }

        private static void Step(float dt)
        {
            HandTiming timing = HandTiming.Current;
            _phaseTime += dt;
            switch (_phase)
            {
                case Phase.Scrub:
                    _animator.Play(_state, _layer, _normalized);
                    _animator.SetLayerWeight(_layer, 1f);
                    return;
                case Phase.Reach:
                    if (_phaseTime >= timing.ReachSeconds - timing.OpenLead)
                    {
                        Cue();
                    }
                    _normalized = Mathf.Min(timing.GripAt, _phaseTime / Mathf.Max(0.01f, timing.ReachSeconds) * timing.GripAt);
                    if (_normalized >= timing.GripAt)
                    {
                        Next(Phase.Turn);
                    }
                    break;
                case Phase.Turn:
                    Cue();
                    _normalized = Mathf.Lerp(timing.GripAt, timing.TurnEnd, Mathf.Clamp01(_phaseTime / Mathf.Max(0.01f, _turnSeconds)));
                    if (_phaseTime >= _turnSeconds)
                    {
                        Next(Phase.Push);
                    }
                    break;
                case Phase.Push:
                    _normalized = Mathf.Lerp(timing.TurnEnd, timing.LetGoAt, Mathf.Clamp01(_phaseTime / Mathf.Max(0.01f, timing.PushSeconds)));
                    if (_normalized >= timing.LetGoAt)
                    {
                        Next(Phase.Release);
                    }
                    break;
                case Phase.Release:
                    _normalized = Mathf.Lerp(timing.LetGoAt, 1f, Mathf.Clamp01(_phaseTime / Mathf.Max(0.01f, timing.ReleaseSeconds)));
                    break;
            }
            bool ending = _phase == Phase.Release;
            float remaining = timing.ReleaseSeconds - _phaseTime;
            _weight = ending ? Mathf.Min(_weight, Mathf.Clamp01(remaining / Mathf.Max(0.01f, timing.FadeOut))) : Mathf.Min(1f, _weight + dt / Mathf.Max(0.01f, timing.FadeIn));
            if (ending && remaining <= 0f)
            {
                Finish();
                return;
            }
            _animator.Play(_state, _layer, _normalized);
            _animator.SetLayerWeight(_layer, _weight);
        }

        private static void Next(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        private static void Finish()
        {
            _running = false;
            _openCue = null;
            try
            {
                if (_animator != null && _layer >= 0)
                {
                    _animator.SetLayerWeight(_layer, 0f);
                    _animator.Play("Idle", _layer, 0f);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not reset the hand layer: {e.Message}");
            }
            _animator = null;
        }

        private static float Sample(float normalized)
        {
            float t = Mathf.Clamp01(normalized) * (Curve.Length - 1);
            int i = Mathf.Min((int)t, Curve.Length - 2);
            return Mathf.Lerp(Curve[i], Curve[i + 1], t - i);
        }

        private static bool HasState(IAnimator animator, string state, int layer)
        {
            if (animator is UnityAnimatorWrapper wrapper)
            {
                return wrapper.Animator != null && wrapper.Animator.HasState(layer, Animator.StringToHash(state));
            }
            if (animator is FastAnimatorProcessor fast)
            {
                return fast._animatorCache != null && fast._animatorCache.GetState(state, layer, out _);
            }
            return false;
        }

        private static Animator FallbackAnimator(Player player, FirearmsAnimator hands)
        {
            if (hands.Animator is UnityAnimatorWrapper wrapper && wrapper.Animator != null)
            {
                return wrapper.Animator;
            }
            Transform root = player?.PlayerBones?.WeaponRoot?.Original;
            if (root == null)
            {
                return null;
            }
            Animator animator = root.GetComponentInParent<Animator>();
            return animator != null ? animator : root.GetComponentInChildren<Animator>(true);
        }
    }
}
