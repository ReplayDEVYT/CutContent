using BepInEx.Configuration;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class FullInteractions
    {
        private const string Section = "Full Interactions";
        private const float ReachTime = 0.55f;
        private const float HoldTime = 0.25f;
        private const float ReleaseTime = 0.25f;
        private const float StepSpeed = 0.8f;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _stepIn;
        private static ConfigEntry<float> _wiggleRoom;
        private static ConfigEntry<float> _maxStep;

        private static Player _player;
        private static WorldInteractiveObject _target;
        private static WorldInteractiveObject _snapCandidate;
        private static bool _realProgress;
        private static float _enterTime;
        private static Vector3 _anchor;
        private static Vector2 _input;
        private static float _startTime;
        private static FirearmsAnimator _hands;
        private static bool _executed;
        private static bool _waitingForOpen;
        private static bool _releasing;
        private static Transform _movedGrip;
        private static Transform _gripParent;
        private static Vector3 _gripLocalPosition;
        private static Quaternion _gripLocalRotation;
        private static float _peak;
        private static float _lastRaw;
        private static bool _rising;
        private static bool _plateau;
        private static int _progressFrame = -1;
        private static bool _gripping;

        public static bool Active => _enabled != null && _enabled.Value;

        public static bool GripAttached => _movedGrip != null;

        public static string LastAttachNote = "";

        public static float LastTurnSeconds;

        public static float LastExecuteTime = -1f;

        public static string LastSubState = "";

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Interaction", "Full Interaction Animations", true,
                "Opening doors, car doors, trunks and containers plays the full interaction like the alpha: you step up to it, your hand reaches for the handle with the alpha's own hand animations, turns it and pushes or pulls, and it opens while your hand is on it. You are held in place until your hand lets go, but you can still lean and shuffle a little. Closing, unlocking, breaching and searching bodies are unchanged. Interaction Focus does nothing while this is on.",
                Section, "Enabled");
            _stepIn = config.Bind(Section, "Step Into Position", true, "Step to the spot the door or container was made to be opened from before reaching for it, like the alpha. Off only steps in for doors that always asked for it.");
            _maxStep = config.Bind(Section, "Max Step Distance", 1.2f, new ConfigDescription("How far you will step to reach the opening spot, in meters. Further than this and you open it from where you stand.", new AcceptableValueRange<float>(0.3f, 2f)));
            _wiggleRoom = config.Bind(Section, "Wiggle Room", 0.25f, new ConfigDescription("How far you can shuffle while your hand is on the door, in meters. 0 holds you completely still.", new AcceptableValueRange<float>(0f, 0.6f)));
            ConfigHelper.Requires(_enabled, _stepIn, _maxStep, _wiggleRoom);
            ConfigHelper.Requires(_stepIn, _maxStep);
            ConfigHelper.Conflicts(_enabled, InteractionFocus.Toggle);
            HandTiming.Load();

            var harmony = new Harmony(Plugin.Guid + ".fullinteractions");
            HarmonyMethod executePrefix = new HarmonyMethod(typeof(FullInteractions), nameof(ExecutePrefix)) { priority = Priority.First };
            foreach (MethodInfo method in ExecuteOverrides())
            {
                harmony.Patch(method, prefix: executePrefix);
            }
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.TryInteractionCallback)),
                prefix: new HarmonyMethod(typeof(FullInteractions), nameof(CallbackPrefix)));
            harmony.Patch(AccessTools.Method(typeof(MovementContext), nameof(MovementContext.GetHandProgress)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(ProgressPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.Enter)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(EnterPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.Exit)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(ExitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.ManualAnimatorMoveUpdate)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(UpdatePostfix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.ExecuteInteraction)),
                prefix: new HarmonyMethod(typeof(FullInteractions), nameof(ExecuteMarkPrefix)),
                finalizer: new HarmonyMethod(typeof(FullInteractions), nameof(ExecuteMarkFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractSubState), nameof(DoorInteractSubState.OnEnter)),
                prefix: new HarmonyMethod(typeof(FullInteractions), nameof(SubStatePrefix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.StartInteractionAnimation)),
                prefix: new HarmonyMethod(typeof(FullInteractions), nameof(StartAnimationPrefix)));
            harmony.Patch(AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.StopInteractionAnimation)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(StopAnimationPostfix)));
            harmony.Patch(AccessTools.Method(typeof(MovementState), nameof(MovementState.Move)),
                postfix: new HarmonyMethod(typeof(FullInteractions), nameof(MovePostfix)));
            HarmonyMethod snapPostfix = new HarmonyMethod(typeof(FullInteractions), nameof(SnapPostfix));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Door), nameof(Door.GetInteractionParameters)), postfix: snapPostfix);
            harmony.Patch(AccessTools.DeclaredMethod(typeof(LootableContainer), nameof(LootableContainer.GetInteractionParameters)), postfix: snapPostfix);
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Trunk), nameof(Trunk.GetInteractionParameters)), postfix: snapPostfix);
        }

        private static IEnumerable<MethodInfo> ExecuteOverrides()
        {
            var found = new List<MethodInfo>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch
                {
                    continue;
                }
                foreach (Type type in types)
                {
                    if (type == null || !typeof(Player).IsAssignableFrom(type))
                    {
                        continue;
                    }
                    MethodInfo method = AccessTools.DeclaredMethod(type, nameof(Player.ExecuteInteraction), new[] { typeof(WorldInteractiveObject), typeof(InteractionResult) });
                    if (method != null && !method.IsAbstract && !found.Contains(method))
                    {
                        found.Add(method);
                    }
                }
            }
            return found;
        }

        private static bool Busy(Player player)
        {
            MovementState state = player.CurrentManagedState;
            return state is DoorInteractState || state is ApproachState;
        }

        private static bool ExecutePrefix(Player __instance, WorldInteractiveObject __0, InteractionResult __1)
        {
            try
            {
                if (!Active || __instance == null || !__instance.IsYourPlayer || __0 == null || __1 == null)
                {
                    return true;
                }
                if (!Animated(__0))
                {
                    return true;
                }
                if (__1.InteractionType != EInteractionType.Open)
                {
                    return true;
                }
                if (__0.interactWithoutAnimation || Busy(__instance) || __0.DoorState == EDoorState.Interacting)
                {
                    return true;
                }
                MovementContext movement = __instance.MovementContext;
                if (movement == null || movement.IsInMountedState || movement.IsStationaryWeaponInHands || !__instance.HealthController.IsAlive)
                {
                    return true;
                }
                Consciousness.Knockout.ClearSprint(__instance);
                _player = __instance;
                _target = __0;
                _snapCandidate = __0;
                _startTime = Time.time;
                try
                {
                    __instance.StartInteraction(__0, __1, null);
                }
                finally
                {
                    _snapCandidate = null;
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Falling back to the quick interaction: {e.Message}");
                _snapCandidate = null;
                Clear();
                return true;
            }
        }

        private sealed class HandMove
        {
            public int Index;
            public string State;
            public string Clip;
            public float Speed;

            public HandMove(int index, string state, string clip, float speed)
            {
                Index = index;
                State = state;
                Clip = clip;
                Speed = speed;
            }
        }

        private static readonly HandMove PushLeft = new HandMove(51, "OPEN_PUSH_LEFT_HINGE", "rot_push_hinge_left", 1.2f);
        private static readonly HandMove PullRight = new HandMove(52, "OPEN_PULL_RIGHT_HINGE", "rot_pull_hinge_right", 1.2f);
        private static readonly HandMove PushRight = new HandMove(53, "OPEN_PUSH_RIGHT_HINGE", "rot_pull_hinge_right", 1.2f);
        private static readonly HandMove PullLeft = new HandMove(54, "OPEN_PULL_LEFT_HINGE", "rot_pull_hinge_left", 1.2f);
        private static readonly HandMove Slap = new HandMove(99, "hand_slap_forward", "hand_slap_forward", 1.4f);
        private static readonly HandMove Key = new HandMove(50, "hand_key_use", "hand_key_use", 1.25f);
        private static readonly HandMove LidOpen = new HandMove(101, "Trunk Open", null, 1.25f);
        private static readonly HandMove LidClose = new HandMove(102, "Trunk Close", null, 1.25f);
        private static readonly HandMove ReachForward = new HandMove(11, "generic_0", "generic_0", 1.3f);
        private static readonly HandMove ReachBack = new HandMove(13, "generic_180", "generic_180", 1.3f);

        private static HandMove HandAction(DoorInteractState state)
        {
            EInteraction interaction = (EInteraction)state._animationId;
            switch (interaction)
            {
                case EInteraction.PushHingeLeft:
                case EInteraction.DoorPushForward:
                    return PushLeft;
                case EInteraction.PullHingeRight:
                    return PullRight;
                case EInteraction.PushHingeRight:
                    return PushRight;
                case EInteraction.PullHingeLeft:
                case EInteraction.DoorPullBackward:
                    return PullLeft;
                case EInteraction.DoorKeyOpen:
                    return Key;
                case EInteraction.ContainerOpenUp:
                case EInteraction.ContainerHingeUpOpen:
                    return LidOpen;
                case EInteraction.ContainerCloseDown:
                case EInteraction.ContainerHingeUpClose:
                    return LidClose;
                case EInteraction.ContainerOpenPull:
                    return ReachBack;
                case EInteraction.ContainerClosePush:
                case EInteraction.ContainerOpenDefault:
                case EInteraction.ContainerCloseDefault:
                    return ReachForward;
                default:
                    return null;
            }
        }

        private static bool Animated(WorldInteractiveObject target)
        {
            return target is Door || target is LootableContainer || target is Trunk;
        }

        private static bool StartAnimationPrefix(DoorInteractState __instance)
        {
            try
            {
                MovementContext movement = __instance.MovementContext;
                if (!Ours(movement) || !Animated(__instance.Door))
                {
                    return true;
                }
                FirearmsAnimator hands = _player.HandsController?.FirearmsAnimator;
                HandMove move = HandAction(__instance);
                if (hands == null || move == null || !hands.IsIdling() || movement.IsSprintEnabled)
                {
                    return true;
                }
                movement.CancelInventoryAnimation();
                _player.RemoveLeftHandItem(2.5f);
                if (!(__instance.Door is Door))
                {
                    if (__instance._grip == null)
                    {
                        return true;
                    }
                }
                else if (!HandActions.Start(_player, hands, __instance.Door, move.State, move.Clip, move.Speed, TurnSeconds(__instance.Door), () => OpenEarly(__instance)))
                {
                    return true;
                }
                if (__instance.Door is Door)
                {
                    AttachGrip(__instance);
                }
                LastTurnSeconds = HandActions.TurnTime;
                movement.SetIKInteraction(__instance._grip);
                _hands = hands;
                _gripping = true;
                return false;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Using the modern hand animation: {e.Message}");
                _hands = null;
                return true;
            }
        }

        private static void StopAnimationPostfix(DoorInteractState __instance)
        {
            DetachGrip();
            if (_hands == null || _player == null || _player.MovementContext != __instance.MovementContext)
            {
                return;
            }
            try
            {
                HandActions.Stop(false);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not release the hand: {e.Message}");
            }
            _hands = null;
            _gripping = false;
        }

        private static void TurnHandle(DoorInteractState state)
        {
            SmartGrip grip = state._smartGrip;
            if (!_gripping || grip == null || _movedGrip != null)
            {
                return;
            }
            float progress = state.MovementContext.GetHandProgress();
            if (progress < 0f || (progress < 0.05f && Time.realtimeSinceStartup - state._enterTime > 1f))
            {
                grip.Reset();
                _gripping = false;
                return;
            }
            Transform root = _player.PlayerBones?.WeaponRoot?.Original;
            Vector3 hand = root != null && root.parent != null ? root.parent.position : _player.Position + Vector3.up * 1.4f;
            grip.LookAt(hand - Vector3.up / 4f);
        }

        private static void OpenEarly(DoorInteractState state)
        {
            if (_executed || state == null || state.Door == null || !Ours(state.MovementContext) || !(state.MovementContext.CurrentState is DoorInteractState))
            {
                return;
            }
            state.ExecuteInteraction();
        }

        private static bool ExecuteMarkPrefix(DoorInteractState __instance, out float __state)
        {
            __state = -1f;
            if (Ours(__instance.MovementContext))
            {
                if (_executed)
                {
                    return false;
                }
                _executed = true;
            }
            LastExecuteTime = Time.time;
            EFTHardSettings settings = EFTHardSettings.Instance;
            if (settings != null && Ours(__instance.MovementContext))
            {
                __state = settings.DelayToOpenContainer;
                settings.DelayToOpenContainer = 0f;
            }
            return true;
        }

        private static Exception ExecuteMarkFinalizer(Exception __exception, float __state)
        {
            if (__state >= 0f && EFTHardSettings.Instance != null)
            {
                EFTHardSettings.Instance.DelayToOpenContainer = __state;
            }
            return __exception;
        }

        private static void SubStatePrefix(DoorInteractSubState __instance)
        {
            LastSubState = __instance.GetType().Name;
        }

        internal static float TurnSeconds(WorldInteractiveObject target)
        {
            AnimationCurve curve = target?._handle?.OpenAnimation;
            if (curve == null || curve.length == 0)
            {
                return 0.2f;
            }
            float time = 0f;
            foreach (Keyframe key in curve.keys)
            {
                if (key.value >= 0.99f)
                {
                    time = key.time;
                }
            }
            if (time <= 0f)
            {
                time = curve.keys[curve.length - 1].time;
            }
            return time;
        }

        private static void AttachGrip(DoorInteractState state)
        {
            DetachGrip();
            LastAttachNote = "";
            try
            {
                DoorHandle handle = state.Door?._handle;
                GripPose grip = state._grip;
                if (handle == null || grip == null)
                {
                    LastAttachNote = handle == null ? "no handle" : "no grip";
                    return;
                }
                Transform moving = state._smartGrip != null && state._smartGrip.Pivot != null ? state._smartGrip.Pivot : grip.transform;
                Transform handleTransform = handle.transform;
                if (moving == handleTransform || moving.IsChildOf(handleTransform))
                {
                    LastAttachNote = "grip is already under the handle";
                    return;
                }
                if (handleTransform.IsChildOf(moving))
                {
                    LastAttachNote = $"handle is under the grip's '{moving.name}'";
                    return;
                }
                if (Vector3.Distance(grip.transform.position, handleTransform.position) > 0.35f)
                {
                    LastAttachNote = "grip too far from the handle";
                    return;
                }
                LastAttachNote = $"attached '{moving.name}'";
                _movedGrip = moving;
                _gripParent = moving.parent;
                _gripLocalPosition = moving.localPosition;
                _gripLocalRotation = moving.localRotation;
                moving.SetParent(handleTransform, true);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not attach the hand to the handle: {e.Message}");
                _movedGrip = null;
            }
        }

        private static void DetachGrip()
        {
            Transform moving = _movedGrip;
            _movedGrip = null;
            if (moving == null)
            {
                return;
            }
            try
            {
                moving.SetParent(_gripParent, false);
                moving.localPosition = _gripLocalPosition;
                moving.localRotation = _gripLocalRotation;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not put the door grip back: {e.Message}");
            }
            _gripParent = null;
        }

        private static void Clear()
        {
            DetachGrip();
            HandActions.Stop(false);
            _hands = null;
            _gripping = false;
            _player = null;
            _target = null;
            _input = Vector2.zero;
        }

        private static bool Ours(MovementContext movement)
        {
            if (_player == null || _target == null)
            {
                return false;
            }
            if (Time.time - _startTime > 8f && !Busy(_player))
            {
                Clear();
                return false;
            }
            return movement != null && _player.MovementContext == movement;
        }

        private static bool CallbackPrefix(Player __instance, LootableContainer container)
        {
            if (_releasing || container == null || __instance != _player || container != _target || !Ours(__instance.MovementContext))
            {
                return true;
            }
            if (container.DoorState == EDoorState.Open)
            {
                return true;
            }
            if (!_waitingForOpen)
            {
                _waitingForOpen = true;
                __instance.StartCoroutine(OpenWhenReady(__instance, container));
            }
            return false;
        }

        private static IEnumerator OpenWhenReady(Player player, LootableContainer container)
        {
            float giveUp = Time.time + 6f;
            while (player != null && container != null && container.DoorState != EDoorState.Open && Time.time < giveUp)
            {
                if (container.DoorState == EDoorState.Shut && !(player.CurrentManagedState is DoorInteractState) && Time.time - _startTime > 0.5f)
                {
                    break;
                }
                yield return null;
            }
            _waitingForOpen = false;
            if (player == null || container == null || !player.HealthController.IsAlive || container.DoorState == EDoorState.Shut || container.DoorState == EDoorState.Locked)
            {
                yield break;
            }
            _releasing = true;
            try
            {
                player.TryInteractionCallback(container);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not open the container: {e.Message}");
            }
            finally
            {
                _releasing = false;
            }
        }

        private static void SnapPostfix(WorldInteractiveObject __instance, Vector3 yourPosition, ref WorldInteractiveObject.InteractionParameters __result)
        {
            if (!Active || __instance != _snapCandidate || __result.Snap || !_stepIn.Value)
            {
                return;
            }
            Vector3 spot = __result.InteractionPosition;
            Vector3 flat = spot - yourPosition;
            float height = Mathf.Abs(flat.y);
            flat.y = 0f;
            if (__result.Grip == null || height > 0.6f || flat.magnitude > _maxStep.Value || flat.magnitude < 0.05f)
            {
                return;
            }
            if (Vector3.Distance(spot, __instance.transform.position) < 0.2f)
            {
                return;
            }
            __result.Snap = true;
        }

        private static void EnterPostfix(DoorInteractState __instance)
        {
            if (!Ours(__instance.MovementContext))
            {
                return;
            }
            _realProgress = false;
            _executed = false;
            _peak = 0f;
            _lastRaw = 0f;
            _rising = false;
            _plateau = false;
            _progressFrame = -1;
            _enterTime = __instance._enterTime;
            _anchor = __instance.MovementContext.TransformPosition;
            _input = Vector2.zero;
        }

        private static void ExitPostfix(DoorInteractState __instance)
        {
            if (_player != null && _player.MovementContext == __instance.MovementContext)
            {
                Clear();
            }
        }

        private static void ProgressPostfix(MovementContext __instance, ref float __result)
        {
            if (!Ours(__instance) || !(__instance.CurrentState is DoorInteractState))
            {
                return;
            }
            if (HandActions.Active && _hands != null)
            {
                _realProgress = true;
                __result = HandActions.Progress;
                return;
            }
            if (__result > 0.05f)
            {
                _realProgress = true;
            }
            if (_realProgress)
            {
                __result = Latch(__result);
                return;
            }
            float t = Time.realtimeSinceStartup - _enterTime;
            if (t < ReachTime)
            {
                float k = t / ReachTime;
                __result = k * k * (3f - 2f * k);
            }
            else if (t < ReachTime + HoldTime)
            {
                __result = 1f;
            }
            else if (t < ReachTime + HoldTime + ReleaseTime)
            {
                __result = 1f - (t - ReachTime - HoldTime) / ReleaseTime;
            }
            else
            {
                __result = -0.01f;
            }
        }

        private static float Latch(float raw)
        {
            if (Time.frameCount != _progressFrame)
            {
                _progressFrame = Time.frameCount;
                _rising = raw > _lastRaw + 0.002f;
                _lastRaw = raw;
                if (raw > _peak)
                {
                    _peak = raw;
                }
                if (!_plateau && raw >= 0.3f && !_rising)
                {
                    _plateau = true;
                }
            }
            if (!_plateau)
            {
                return Mathf.Min(raw, 0.98f);
            }
            if (raw >= _peak - 0.01f)
            {
                return 1f;
            }
            return raw <= 0f || _peak <= 0f ? raw : raw / _peak;
        }

        private static void MovePostfix(MovementState __instance, Vector2 direction)
        {
            if (__instance is DoorInteractState && Ours(__instance.MovementContext))
            {
                _input = direction;
            }
        }

        private static void UpdatePostfix(DoorInteractState __instance, float deltaTime)
        {
            try
            {
                MovementContext movement = __instance.MovementContext;
                if (Ours(movement))
                {
                    TurnHandle(__instance);
                }
                if (!Ours(movement) || _wiggleRoom.Value <= 0f || deltaTime <= 0f)
                {
                    return;
                }
                Vector2 input = Vector2.ClampMagnitude(_input, 1f);
                _input = Vector2.zero;
                if (input.sqrMagnitude < 0.0001f)
                {
                    return;
                }
                Vector3 current = movement.TransformPosition - _anchor;
                current.y = 0f;
                Vector3 step = movement.TransformRotation * new Vector3(input.x, 0f, input.y) * (StepSpeed * deltaTime);
                Vector3 desired = Vector3.ClampMagnitude(current + step, _wiggleRoom.Value);
                Vector3 motion = desired - current;
                if (motion.sqrMagnitude > 0f)
                {
                    movement.ApplyMotion(motion, deltaTime);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not shuffle: {e.Message}");
            }
        }
    }
}
