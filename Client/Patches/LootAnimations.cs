using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class LootAnimations
    {
        private const string Resource = "ReClassic.Resources.looting_animations.json";
        private const float BlendSeconds = 0.3f;

        private enum Loop
        {
            Body,
            Loot,
            Search
        }

        private sealed class Clip
        {
            public int Frames;
            public Vector3[][] Positions;
            public Quaternion[][] Rotations;
            public float Time;
            public float Weight;
        }

        private static ConfigEntry<bool> _enabled;
        private static string[] _bones;
        private static Vector3[] _restPositions;
        private static Quaternion[] _restRotations;
        private static float _fps = 24f;
        private static readonly Dictionary<Loop, Clip> Clips = new Dictionary<Loop, Clip>();
        private static readonly Dictionary<ObjectInHandsAnimator, bool> RequestedInventory = new Dictionary<ObjectInHandsAnimator, bool>();
        private static ObjectInHandsAnimator _heldOutOfInventory;
        private static bool _lootOpen;
        private static bool _hookSeen;
        private static Loop? _loggedLoop;
        private static bool _applyLogged;
        private static int _lastFrame = -1;
        private static float _total;
        private static readonly Vector3[] PosePositions = new Vector3[32];
        private static readonly Quaternion[] PoseRotations = new Quaternion[32];

        private static readonly AccessTools.FieldRef<Player, TwistRelax[]> TwistBones = AccessTools.FieldRefAccess<Player, TwistRelax[]>("_twistBones");

        private static Transform _rigRoot;
        private static Transform[] _rigBones;
        private static Transform _bodyPalm;
        private static Transform[] _bodyBones;
        private static TwistRelax[] _leftTwist;
        private static Transform _weaponRoot;
        private static Vector3 _baseLocalPosition;
        private static Quaternion _baseLocalRotation;
        private static Vector3 _writtenLocalPosition;
        private static Quaternion _writtenLocalRotation;
        private static bool _wrote;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Animations", "Looting Animations", true,
                "Your free hand goes through a body while you search it, rummages through loot while the loot screen is open, and digs into containers while they're being searched.",
                "Visuals - Looting Animations", "Looting Animations");
            if (!Load())
            {
                return;
            }
            new IkProcessPatch().Enable();
            new IkApplyPatch().Enable();
            new LootScreenPatch().Enable();
            new InventoryPosePatch().Enable();
        }

        private static bool Load()
        {
            try
            {
                using (Stream stream = typeof(LootAnimations).Assembly.GetManifestResourceStream(Resource))
                using (var reader = new StreamReader(stream))
                {
                    JObject data = JObject.Parse(reader.ReadToEnd());
                    _fps = data.Value<float?>("fps") ?? 24f;
                    var rest = (JObject)data["rest"];
                    _bones = rest.Properties().Select(p => p.Name).ToArray();
                    _restPositions = new Vector3[_bones.Length];
                    _restRotations = new Quaternion[_bones.Length];
                    for (int i = 0; i < _bones.Length; i++)
                    {
                        Read((JArray)rest[_bones[i]], out _restPositions[i], out _restRotations[i]);
                    }
                    foreach (Loop loop in Enum.GetValues(typeof(Loop)))
                    {
                        var clipData = (JObject)data["clips"][loop.ToString().ToLowerInvariant()];
                        var clip = new Clip { Frames = clipData.Value<int>("frames") };
                        clip.Positions = new Vector3[_bones.Length][];
                        clip.Rotations = new Quaternion[_bones.Length][];
                        var bones = (JObject)clipData["bones"];
                        for (int b = 0; b < _bones.Length; b++)
                        {
                            var frames = (JArray)bones[_bones[b]];
                            clip.Positions[b] = new Vector3[clip.Frames];
                            clip.Rotations[b] = new Quaternion[clip.Frames];
                            for (int f = 0; f < clip.Frames; f++)
                            {
                                Read((JArray)frames[f], out clip.Positions[b][f], out clip.Rotations[b][f]);
                            }
                        }
                        Clips[loop] = clip;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Looting Animations] Could not load the animations: {e}");
                return false;
            }
        }

        private static void Read(JArray values, out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(values[0].Value<float>(), values[1].Value<float>(), values[2].Value<float>());
            rotation = new Quaternion(values[3].Value<float>(), values[4].Value<float>(), values[5].Value<float>(), values[6].Value<float>());
        }

        private static Loop? Wanted(Player player)
        {
            if (BodySearch.Running)
            {
                return Loop.Body;
            }
            if (_lootOpen && !player.HealthController.IsAlive)
            {
                _lootOpen = false;
            }
            if (!_lootOpen)
            {
                return null;
            }
            try
            {
                if (player.SearchController != null && player.SearchController.SearchOperations.Any())
                {
                    return Loop.Search;
                }
            }
            catch (Exception)
            {
            }
            return Loop.Loot;
        }

        private static void Tick(Player player)
        {
            if (_lastFrame == Time.frameCount)
            {
                return;
            }
            _lastFrame = Time.frameCount;
            Loop? wanted = _enabled.Value && player.HandsController is Player.FirearmController ? Wanted(player) : null;
            HoldInventoryPose(player, wanted.HasValue && wanted.Value != Loop.Body);
            float step = Time.deltaTime / BlendSeconds;
            _total = 0f;
            foreach (KeyValuePair<Loop, Clip> pair in Clips)
            {
                Clip clip = pair.Value;
                bool on = wanted.HasValue && wanted.Value == pair.Key;
                if (on && clip.Weight <= 0f)
                {
                    clip.Time = 0f;
                }
                clip.Weight = Mathf.MoveTowards(clip.Weight, on ? 1f : 0f, step);
                if (clip.Weight > 0f)
                {
                    clip.Time += Time.deltaTime;
                }
                _total += clip.Weight;
            }
            if (_total <= 0f)
            {
                return;
            }
            for (int b = 0; b < _bones.Length; b++)
            {
                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                float accumulated = 0f;
                foreach (Clip clip in Clips.Values)
                {
                    if (clip.Weight <= 0f)
                    {
                        continue;
                    }
                    Sample(clip, b, out Vector3 p, out Quaternion q);
                    accumulated += clip.Weight;
                    position += p * clip.Weight;
                    rotation = accumulated <= clip.Weight ? q : Quaternion.Slerp(rotation, q, clip.Weight / accumulated);
                }
                PosePositions[b] = position / accumulated;
                PoseRotations[b] = rotation;
            }
            _total = Mathf.Clamp01(_total);
        }

        private static bool Stale(ObjectInHandsAnimator animator)
        {
            try
            {
                return animator == null || animator.Animator == null;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void HoldInventoryPose(Player player, bool hold)
        {
            ObjectInHandsAnimator animator = (player.HandsController as Player.FirearmController)?.FirearmsAnimator;
            if (RequestedInventory.Count > 16)
            {
                foreach (ObjectInHandsAnimator stale in RequestedInventory.Keys.Where(Stale).ToList())
                {
                    RequestedInventory.Remove(stale);
                }
            }
            if (_heldOutOfInventory != null && (!hold || _heldOutOfInventory != animator))
            {
                ObjectInHandsAnimator released = _heldOutOfInventory;
                _heldOutOfInventory = null;
                if (RequestedInventory.TryGetValue(released, out bool open) && open)
                {
                    released.SetInventory(true);
                }
            }
            if (hold && animator != null && _heldOutOfInventory == null)
            {
                _heldOutOfInventory = animator;
                AnimationControllerParametersTable.SetInventory(animator.Animator, false);
            }
        }

        private static void Report(Player player)
        {
            Loop? playing = null;
            float best = 0f;
            foreach (KeyValuePair<Loop, Clip> pair in Clips)
            {
                if (pair.Value.Weight > best)
                {
                    best = pair.Value.Weight;
                    playing = pair.Key;
                }
            }
            if (playing == _loggedLoop)
            {
                return;
            }
            _loggedLoop = playing;
            if (!playing.HasValue)
            {
                Plugin.LogSource.LogInfo("[Looting Animations] Stopped.");
                return;
            }
            _applyLogged = false;
            bool bound = Bind(player);
            int rig = _rigBones?.Count(t => t != null) ?? 0;
            int body = _bodyBones?.Count(t => t != null) ?? 0;
            string missingRig = _rigBones == null ? "all" : string.Join(", ", _bones.Where((n, i) => _rigBones[i] == null));
            string missingBody = _bodyBones == null ? "all" : string.Join(", ", _bones.Skip(1).Where((n, i) => _bodyBones[i + 1] == null));
            Plugin.LogSource.LogInfo($"[Looting Animations] Playing {playing}: hands {player.HandsController?.GetType().Name}, bound {bound}, rig root {(_rigRoot != null ? _rigRoot.name : "none")}, rig bones {rig}/{_bones.Length} (missing: {missingRig}), body bones {body}/{_bones.Length - 1} (missing: {missingBody}), twist {_leftTwist?.Length ?? 0}, loot screen {_lootOpen}.");
        }

        private static void Sample(Clip clip, int bone, out Vector3 position, out Quaternion rotation)
        {
            float frame = clip.Time * _fps % clip.Frames;
            int a = (int)frame;
            int b = (a + 1) % clip.Frames;
            float t = frame - a;
            position = Vector3.Lerp(clip.Positions[bone][a], clip.Positions[bone][b], t);
            rotation = Quaternion.Slerp(clip.Rotations[bone][a], clip.Rotations[bone][b], t);
        }

        private static bool Applies(Player player)
        {
            return player != null && player.IsYourPlayer && player.PointOfView == EPointOfView.FirstPerson && player.HealthController.IsAlive;
        }

        private static bool Bind(Player player)
        {
            Transform weaponRoot = player.PlayerBones?.Weapon_Root_Anim;
            Transform rigRoot = weaponRoot != null && weaponRoot.parent != null ? weaponRoot.parent.parent : null;
            if (weaponRoot == null || rigRoot == null || player.PlayerBones.LeftPalm == null)
            {
                return false;
            }
            if (weaponRoot != _weaponRoot)
            {
                _weaponRoot = weaponRoot;
                _wrote = false;
            }
            if (rigRoot != _rigRoot)
            {
                _rigRoot = rigRoot;
                _rigBones = new Transform[_bones.Length];
                Transform collar = rigRoot.Find("Base HumanLCollarbone");
                Transform[] all = collar != null ? collar.GetComponentsInChildren<Transform>(true) : new Transform[0];
                for (int b = 0; b < _bones.Length; b++)
                {
                    _rigBones[b] = b == 0 ? weaponRoot : all.FirstOrDefault(t => t.name == _bones[b]);
                }
            }
            Transform palm = player.PlayerBones.LeftPalm;
            if (palm != _bodyPalm)
            {
                _bodyPalm = palm;
                _bodyBones = new Transform[_bones.Length];
                Transform arm = palm;
                while (arm != null && arm.name != "Base HumanLCollarbone")
                {
                    arm = arm.parent;
                }
                Transform[] all = arm != null ? arm.GetComponentsInChildren<Transform>(true) : palm.GetComponentsInChildren<Transform>(true);
                for (int b = 1; b < _bones.Length; b++)
                {
                    _bodyBones[b] = all.FirstOrDefault(t => t.name == _bones[b]);
                }
                Transform upper = _bodyBones[Array.IndexOf(_bones, "Base HumanLUpperarm")];
                _leftTwist = (TwistRelax(player) ?? new TwistRelax[0]).Where(t => t != null && upper != null && t.transform.IsChildOf(upper)).ToArray();
            }
            return true;
        }

        private static TwistRelax[] TwistRelax(Player player)
        {
            try
            {
                return TwistBones(player);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ApplyToRig()
        {
            Transform weapon = _rigBones[0];
            if (_wrote && weapon.localPosition == _writtenLocalPosition && weapon.localRotation == _writtenLocalRotation)
            {
                weapon.localPosition = _baseLocalPosition;
                weapon.localRotation = _baseLocalRotation;
            }
            _baseLocalPosition = weapon.localPosition;
            _baseLocalRotation = weapon.localRotation;
            Quaternion motion = PoseRotations[0] * Quaternion.Inverse(_restRotations[0]);
            Vector3 shift = PosePositions[0] - motion * _restPositions[0];
            motion = Quaternion.Slerp(Quaternion.identity, motion, _total);
            shift *= _total;
            Vector3 local = _rigRoot.InverseTransformPoint(weapon.position);
            Quaternion localRotation = Quaternion.Inverse(_rigRoot.rotation) * weapon.rotation;
            weapon.SetPositionAndRotation(_rigRoot.TransformPoint(motion * local + shift), _rigRoot.rotation * (motion * localRotation));
            _writtenLocalPosition = weapon.localPosition;
            _writtenLocalRotation = weapon.localRotation;
            _wrote = true;

            for (int b = 1; b < _bones.Length; b++)
            {
                Transform bone = _rigBones[b];
                if (bone == null)
                {
                    continue;
                }
                bone.localPosition = Vector3.Lerp(bone.localPosition, PosePositions[b], _total);
                bone.localRotation = Quaternion.Slerp(bone.localRotation, PoseRotations[b], _total);
            }
        }

        private static void ApplyToBody()
        {
            for (int b = 1; b < _bones.Length; b++)
            {
                Transform body = _bodyBones[b];
                Transform rig = _rigBones[b];
                if (body == null || rig == null || _bones[b] == "Base HumanLCollarbone")
                {
                    continue;
                }
                if (_bones[b] == "Base HumanLUpperarm")
                {
                    body.SetPositionAndRotation(Vector3.Lerp(body.position, rig.position, _total), Quaternion.Slerp(body.rotation, rig.rotation, _total));
                }
                else
                {
                    body.rotation = Quaternion.Slerp(body.rotation, rig.rotation, _total);
                }
            }
            foreach (TwistRelax twist in _leftTwist)
            {
                twist.Relax();
            }
        }

        private class IkProcessPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.IkProcess));
            }

            [PatchPrefix]
            private static void Prefix(Player __instance)
            {
                if (!Applies(__instance))
                {
                    return;
                }
                try
                {
                    if (!_hookSeen)
                    {
                        _hookSeen = true;
                        Plugin.LogSource.LogInfo("[Looting Animations] Hooked into the first-person arm pass.");
                    }
                    Tick(__instance);
                    Report(__instance);
                    if (_total > 0f && Bind(__instance))
                    {
                        ApplyToRig();
                    }
                    else
                    {
                        _wrote = false;
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Looting Animations] {e}");
                    _total = 0f;
                }
            }
        }

        private class IkApplyPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.IkApply));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance)
            {
                if (!Applies(__instance) || _total <= 0f || _bodyBones == null || _rigBones == null)
                {
                    return;
                }
                try
                {
                    Vector3 before = _bodyPalm != null ? _bodyPalm.position : Vector3.zero;
                    ApplyToBody();
                    if (!_applyLogged && _bodyPalm != null && _total >= 0.99f)
                    {
                        _applyLogged = true;
                        Plugin.LogSource.LogInfo($"[Looting Animations] Applied to the body: left hand moved {(_bodyPalm.position - before).magnitude * 100f:0.0} cm by the clip, now {(_bodyPalm.position - _rigBones[Array.IndexOf(_bones, "Base HumanLPalm")].position).magnitude * 100f:0.0} cm from the clip's hand.");
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Looting Animations] {e}");
                }
            }
        }

        private class InventoryPosePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ObjectInHandsAnimator), nameof(ObjectInHandsAnimator.SetInventory));
            }

            [PatchPrefix]
            private static void Prefix(ObjectInHandsAnimator __instance, ref bool open)
            {
                RequestedInventory[__instance] = open;
                if (__instance == _heldOutOfInventory)
                {
                    open = false;
                }
            }
        }

        private class LootScreenPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.ShowInventoryScreenLoot));
            }

            [PatchPrefix]
            private static void Prefix(ref Action callback)
            {
                _lootOpen = true;
                Action original = callback;
                callback = () =>
                {
                    _lootOpen = false;
                    original?.Invoke();
                };
            }
        }
    }
}
