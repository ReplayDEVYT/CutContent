using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal enum FootstepStyle
    {
        Modern,
        ModernAlphaSprint,
        PreAlpha
    }

    internal static class Footsteps
    {
        private const string BundleResource = "ReClassic.Resources.footsteps.bundle";
        private const string AlphaSprint = "alpha_sprint_concrete";

        private static ConfigEntry<FootstepStyle> _style;
        private static readonly AccessTools.FieldRef<Player, Sounds> PlayerSounds = AccessTools.FieldRefAccess<Player, Sounds>("_playerSounds");
        private static readonly Dictionary<SoundBank, Replacement> Active = new Dictionary<SoundBank, Replacement>();
        private static readonly Dictionary<string, AudioClip[]> Groups = new Dictionary<string, AudioClip[]>();
        private static readonly HashSet<Sounds> Known = new HashSet<Sounds>();
        private static AssetBundle _bundle;
        private static AudioClip[] _all;
        private static bool _bundleFailed;

        private sealed class Replacement
        {
            public AudioClip[] Clips;
            public int Last = -1;
        }

        private struct Set
        {
            public readonly string Run;
            public readonly string Sprint;
            public readonly string Duck;

            public Set(string run, string sprint, string duck = null)
            {
                Run = run;
                Sprint = sprint;
                Duck = duck;
            }
        }

        private static readonly BaseBallistic.ESurfaceSound[] ConcreteLike =
        {
            BaseBallistic.ESurfaceSound.Concrete,
            BaseBallistic.ESurfaceSound.Asphalt,
            BaseBallistic.ESurfaceSound.Slate
        };

        private static readonly Dictionary<BaseBallistic.ESurfaceSound, Set> HiredOps = new Dictionary<BaseBallistic.ESurfaceSound, Set>
        {
            { BaseBallistic.ESurfaceSound.Concrete, new Set("hops_walk_concrete", AlphaSprint) },
            { BaseBallistic.ESurfaceSound.Asphalt, new Set("hops_walk_concrete", AlphaSprint) },
            { BaseBallistic.ESurfaceSound.Slate, new Set("hops_walk_concrete", AlphaSprint) },
            { BaseBallistic.ESurfaceSound.Tile, new Set("hops_fs_plite_crush_run", "hops_fs_plite_crush_sprint", "hops_fs_plite_crush_crouch") },
            { BaseBallistic.ESurfaceSound.Metal, new Set("hops_walk_metal", null) },
            { BaseBallistic.ESurfaceSound.MetalThin, new Set("hops_walk_metal", null) },
            { BaseBallistic.ESurfaceSound.GarbageMetal, new Set("hops_walk_metal", null) },
            { BaseBallistic.ESurfaceSound.Wood, new Set("hops_footstep_wood", "hops_footstep_wood") },
            { BaseBallistic.ESurfaceSound.WoodThick, new Set("hops_footstep_wood", "hops_footstep_wood") },
            { BaseBallistic.ESurfaceSound.WoodThin, new Set("hops_footstep_wood", "hops_footstep_wood") },
            { BaseBallistic.ESurfaceSound.Soil, new Set("hops_walk_soil", "hops_fs_sand_sprint") },
            { BaseBallistic.ESurfaceSound.Grass, new Set("hops_movement_grass", "hops_movement_grass") },
            { BaseBallistic.ESurfaceSound.Gravel, new Set("hops_fs_gravel_run", "hops_fs_gravel_sprint") },
            { BaseBallistic.ESurfaceSound.Glass, new Set("hops_fs_run_glass", "hops_fs_sprint_glass", "hops_fs_crouch_glass") },
            { BaseBallistic.ESurfaceSound.WholeGlass, new Set("hops_fs_run_glass", "hops_fs_sprint_glass", "hops_fs_crouch_glass") },
            { BaseBallistic.ESurfaceSound.Garbage, new Set("hops_fs_run_rubbish", "hops_fs_sprint_rubbish", "hops_fs_crouch_rubbish") },
            { BaseBallistic.ESurfaceSound.Puddle, new Set("hops_footstep_water", "hops_footstep_water") },
            { BaseBallistic.ESurfaceSound.Swamp, new Set("hops_footstep_water", "hops_footstep_water") },
            { BaseBallistic.ESurfaceSound.Snow, new Set("hops_walk_snow", "hops_walk_snow") },
        };

        public static void Enable(ConfigFile config)
        {
            const string section = "Audio - Footsteps";
            _style = config.Bind(section, "Footstep Sounds", FootstepStyle.Modern, new ConfigDescription(
                "Modern: the current EFT footsteps. Modern Alpha Sprint: current footsteps, but sprinting on concrete and asphalt uses the old alpha sprint sound. Pre Alpha: Hired Ops footsteps on every surface with the old concrete sprint, like the pre-alpha. Only changes what you hear."));
            ConfigHelper.Option(FeatureCategory.VisualsAndAudio, "Audio", _style);
            _style.SettingChanged += (sender, args) => ApplyAll();
            new InitAudioPatch().Enable();
            new BankPlayPatch().Enable();
        }

        private static void ApplyAll()
        {
            Active.Clear();
            Known.RemoveWhere(s => s == null);
            foreach (Sounds sounds in Known)
            {
                Apply(sounds);
            }
        }

        private static void Apply(Sounds sounds)
        {
            if (sounds == null || sounds.Sets == null)
            {
                return;
            }
            foreach (SurfaceSet set in sounds.Sets)
            {
                if (set == null)
                {
                    continue;
                }
                Active.Remove(set.RunSoundBank);
                Active.Remove(set.SprintSoundBank);
                Active.Remove(set.DuckSoundBank);
            }
            FootstepStyle style = _style.Value;
            if (style == FootstepStyle.Modern)
            {
                return;
            }
            foreach (SurfaceSet set in sounds.Sets)
            {
                if (set == null)
                {
                    continue;
                }
                if (style == FootstepStyle.PreAlpha && HiredOps.TryGetValue(set.Surface, out Set replacement))
                {
                    Use(set.RunSoundBank, replacement.Run);
                    Use(set.SprintSoundBank, replacement.Sprint);
                    Use(set.DuckSoundBank, replacement.Duck);
                }
                else if (ConcreteLike.Contains(set.Surface))
                {
                    Use(set.SprintSoundBank, AlphaSprint);
                }
            }
        }

        private static void Use(SoundBank bank, string group)
        {
            if (bank == null || string.IsNullOrEmpty(group))
            {
                return;
            }
            AudioClip[] clips = Load(group);
            if (clips.Length > 0)
            {
                Active[bank] = new Replacement { Clips = clips };
            }
        }

        private static AudioClip[] AllClips()
        {
            if (_all != null && _all.All(c => c != null))
            {
                return _all;
            }
            if (_bundleFailed)
            {
                return Array.Empty<AudioClip>();
            }
            try
            {
                if (_bundle == null)
                {
                    Assembly assembly = typeof(Footsteps).Assembly;
                    using (Stream stream = assembly.GetManifestResourceStream(BundleResource))
                    {
                        if (stream == null)
                        {
                            throw new FileNotFoundException("The footsteps bundle is missing from the plugin.");
                        }
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            _bundle = AssetBundle.LoadFromMemory(memory.ToArray());
                        }
                    }
                    if (_bundle == null)
                    {
                        throw new InvalidDataException("Unity could not open the footsteps bundle.");
                    }
                }
                _all = _bundle.LoadAllAssets<AudioClip>();
                Groups.Clear();
            }
            catch (Exception e)
            {
                _bundleFailed = true;
                _all = Array.Empty<AudioClip>();
                Plugin.LogSource.LogError($"[Footsteps] Could not load the footsteps bundle: {e.Message}");
            }
            return _all;
        }

        private static AudioClip[] Load(string group)
        {
            AudioClip[] all = AllClips();
            if (Groups.TryGetValue(group, out AudioClip[] cached) && cached.All(c => c != null))
            {
                return cached;
            }
            AudioClip[] clips = all
                .Where(c => c != null && c.name.StartsWith(group, StringComparison.OrdinalIgnoreCase))
                .Where(c =>
                {
                    string rest = c.name.Substring(group.Length);
                    return rest.Length == 0 || char.IsDigit(rest[0]) || rest[0] == '_';
                })
                .OrderBy(c => c.name)
                .ToArray();
            Groups[group] = clips;
            if (clips.Length == 0)
            {
                Plugin.LogSource.LogWarning($"[Footsteps] No sounds found for {group}.");
            }
            return clips;
        }

        private static AudioClip Pick(Replacement replacement)
        {
            AudioClip[] clips = replacement.Clips;
            if (clips.Length == 1)
            {
                return clips[0];
            }
            int index;
            if (replacement.Last < 0)
            {
                index = UnityEngine.Random.Range(0, clips.Length);
            }
            else
            {
                index = UnityEngine.Random.Range(0, clips.Length - 1);
                if (index >= replacement.Last)
                {
                    index++;
                }
            }
            index = Mathf.Clamp(index, 0, clips.Length - 1);
            replacement.Last = index;
            return clips[index];
        }

        private class BankPlayPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SoundBank), nameof(SoundBank.Play));
            }

            [PatchPrefix]
            private static bool Prefix(SoundBank __instance, BetterSource source, float distance, float volume, bool forceStereo, bool oneShot)
            {
                if (!Active.TryGetValue(__instance, out Replacement replacement))
                {
                    return true;
                }
                try
                {
                    if (source == null || distance > source.MaxDistance)
                    {
                        return false;
                    }
                    AudioClip clip = Pick(replacement);
                    if (clip == null)
                    {
                        Active.Remove(__instance);
                        return true;
                    }
                    source.SetPitch(__instance.RandomPitch);
                    source.Play(clip, null, 1f, Mathf.Clamp01(volume), forceStereo, oneShot);
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Footsteps] Could not play a replaced footstep: {e}");
                    Active.Remove(__instance);
                    return true;
                }
            }
        }

        private class InitAudioPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.InitAudioController));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance)
            {
                try
                {
                    Sounds sounds = PlayerSounds(__instance);
                    if (sounds != null && Known.Add(sounds))
                    {
                        Apply(sounds);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Footsteps] Could not set up footsteps: {e}");
                }
            }
        }
    }
}
