using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class PlateHitboxes
    {
        private static ConfigEntry<bool> _enabled;

        private static readonly List<PlayerBones> Bones = new List<PlayerBones>();

        private sealed class Original
        {
            public BoxCollider Box;
            public Vector3 Center;
            public Vector3 Size;
        }

        private static readonly Dictionary<PlayerBones, List<Original>> Resized = new Dictionary<PlayerBones, List<Original>>();
        private static readonly Dictionary<PlayerBones, List<Collider>> Disabled = new Dictionary<PlayerBones, List<Collider>>();

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Combat, "Damage", "Realistic Plate Hitboxes", true,
                "Armor plates cover what a real plate covers, like the plate hitboxes BSG first showed off. The front and back plates are flat 22 by 30 cm plates on the upper chest and upper back, instead of wrapping around the sides of the torso. Side plates are small 12 by 18 cm plates under the arms. Shots that miss a plate hit the soft armor or the body behind it.",
                "Combat - Plate Hitboxes", "Enabled");
            _enabled.SettingChanged += (sender, args) => RefreshAll();
            new BonesAwakePatch().Enable();
        }

        private static void RefreshAll()
        {
            for (int i = Bones.Count - 1; i >= 0; i--)
            {
                PlayerBones bones = Bones[i];
                if (bones == null)
                {
                    Bones.RemoveAt(i);
                    Resized.Remove(bones);
                    Disabled.Remove(bones);
                    continue;
                }
                Apply(bones);
            }
        }

        private static bool Shape(string name, out Vector3 center, out Vector3 size)
        {
            if (name.Contains("chest"))
            {
                center = new Vector3(0.0491f, 0.0082f, 0.0003f);
                size = new Vector3(0.305f, 0.0928f, 0.22f);
                return true;
            }
            if (name.Contains("back"))
            {
                center = new Vector3(-0.4067f, -0.021f, 0f);
                size = new Vector3(0.305f, 0.1f, 0.22f);
                return true;
            }
            size = new Vector3(0.12f, 0.18f, 0.065f);
            if (name.Contains("side_left"))
            {
                center = new Vector3(-0.0377f, 0.0001f, 0.0004f);
                return true;
            }
            if (name.Contains("side_right_high"))
            {
                center = new Vector3(-0.0414f, -0.0074f, 0.0006f);
                return true;
            }
            if (name.Contains("side_right_low"))
            {
                center = new Vector3(-0.0414f, 0.0276f, -0.0002f);
                return true;
            }
            center = Vector3.zero;
            return false;
        }

        private static void Capture(PlayerBones bones)
        {
            var resized = new List<Original>();
            var disabled = new List<Collider>();
            var seen = new HashSet<Collider>();
            foreach (ArmorPlateCollider plate in bones.ArmorPlateColliders)
            {
                if (plate == null)
                {
                    continue;
                }
                Transform root = plate.transform;
                while (root.parent != null && root.parent.name.StartsWith("Plate_"))
                {
                    root = root.parent;
                }
                if (!root.name.StartsWith("Plate_"))
                {
                    continue;
                }
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!seen.Add(collider))
                    {
                        continue;
                    }
                    if (collider.transform == root && collider is BoxCollider box && Shape(root.name, out _, out _))
                    {
                        resized.Add(new Original { Box = box, Center = box.center, Size = box.size });
                    }
                    else if (collider.transform != root)
                    {
                        disabled.Add(collider);
                    }
                }
            }
            Resized[bones] = resized;
            Disabled[bones] = disabled;
        }

        private static void Apply(PlayerBones bones)
        {
            if (!Resized.TryGetValue(bones, out List<Original> resized) || !Disabled.TryGetValue(bones, out List<Collider> disabled))
            {
                return;
            }
            bool on = _enabled.Value;
            foreach (Original original in resized)
            {
                if (original.Box == null)
                {
                    continue;
                }
                if (on && Shape(original.Box.name, out Vector3 center, out Vector3 size))
                {
                    original.Box.center = center;
                    original.Box.size = size;
                }
                else
                {
                    original.Box.center = original.Center;
                    original.Box.size = original.Size;
                }
            }
            foreach (Collider collider in disabled)
            {
                if (collider != null)
                {
                    collider.enabled = !on;
                }
            }
        }

        private class BonesAwakePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(PlayerBones), nameof(PlayerBones.Awake));
            }

            [PatchPostfix]
            private static void Postfix(PlayerBones __instance)
            {
                try
                {
                    if (__instance.ArmorPlateColliders == null || __instance.ArmorPlateColliders.Length == 0)
                    {
                        return;
                    }
                    Bones.RemoveAll(b => b == null);
                    foreach (PlayerBones gone in new List<PlayerBones>(Resized.Keys))
                    {
                        if (gone == null)
                        {
                            Resized.Remove(gone);
                            Disabled.Remove(gone);
                        }
                    }
                    if (!Bones.Contains(__instance))
                    {
                        Bones.Add(__instance);
                    }
                    Capture(__instance);
                    Apply(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Plate Hitboxes] Could not reshape the plates: {e.Message}");
                }
            }
        }
    }
}
