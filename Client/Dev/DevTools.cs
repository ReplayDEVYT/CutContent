using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using mizmii.ReClassic.Effects;
using mizmii.ReClassic.Karma;
using mizmii.ReClassic.Patches;
using mizmii.ReClassic.Radiation;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace mizmii.ReClassic.Dev
{
    internal static class DevTools
    {
        private const string Section = "Developer";
        private const string FlagFile = Plugin.Guid + ".dev";

        public static bool ForceMask;
        public static bool On { get; private set; }

        public static ConfigEntry<float> RadiationValue;
        public static ConfigEntry<KeyboardShortcut> SetRadiation;
        public static ConfigEntry<float> ToxicityValue;
        public static ConfigEntry<KeyboardShortcut> SetToxicity;
        public static ConfigEntry<KeyboardShortcut> ClearMeters;
        public static ConfigEntry<KeyboardShortcut> TeleportToZone;
        public static ConfigEntry<KeyboardShortcut> OpenNearestDoor;
        public static ConfigEntry<KeyboardShortcut> ToggleMask;
        public static ConfigEntry<string> KarmaTier;
        public static ConfigEntry<KeyboardShortcut> ApplyKarma;
        public static ConfigEntry<KeyboardShortcut> RestoreKarma;
        public static ConfigEntry<KeyboardShortcut> RunTests;
        public static ConfigEntry<bool> TestsOnRaidStart;
        public static ConfigEntry<KeyboardShortcut> TuningPanel;
        public static ConfigEntry<bool> ShowTuningPanel;

        public static bool Recording;
        public static float RecordStart;
        public static readonly List<string> DoorEvents = new List<string>();

        public static string ReportPath => Path.Combine(BepInEx.Paths.BepInExRootPath, "ReClassic-tests.txt");

        public static void Enable(ConfigFile config)
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.ConfigPath, FlagFile)))
            {
                return;
            }
            On = true;
            RadiationValue = config.Bind(Section, "Radiation Value", 40f, new ConfigDescription("Radiation level the Set Radiation key puts you at.", new AcceptableValueRange<float>(0f, 100f)));
            SetRadiation = config.Bind(Section, "Set Radiation", new KeyboardShortcut(KeyCode.Keypad1), "Set your radiation level to Radiation Value.");
            ToxicityValue = config.Bind(Section, "Toxicity Value", 40f, new ConfigDescription("Toxicity level the Set Toxicity key puts you at.", new AcceptableValueRange<float>(0f, 100f)));
            SetToxicity = config.Bind(Section, "Set Toxicity", new KeyboardShortcut(KeyCode.Keypad2), "Set your toxicity level to Toxicity Value.");
            ClearMeters = config.Bind(Section, "Clear Meters", new KeyboardShortcut(KeyCode.Keypad3), "Set radiation and toxicity back to 0.");
            TeleportToZone = config.Bind(Section, "Teleport To Zone", new KeyboardShortcut(KeyCode.Keypad4), "Teleport to the middle of the nearest radiation or chemical zone.");
            OpenNearestDoor = config.Bind(Section, "Open Nearest Door", new KeyboardShortcut(KeyCode.Keypad5), "Step up to the nearest shut door, open it with the full interaction and log the timing.");
            ToggleMask = config.Bind(Section, "Toggle Mask Overlay", new KeyboardShortcut(KeyCode.Keypad6), "Show the gas mask overlay without wearing a mask.");
            KarmaTier = config.Bind(Section, "Karma Tier", "Very Low", new ConfigDescription("Tier the Apply Karma key fakes.", new AcceptableValueList<string>("Very Low", "Low", "Neutral", "High", "Very High")));
            ApplyKarma = config.Bind(Section, "Apply Karma", new KeyboardShortcut(KeyCode.Keypad7), "Fake the chosen karma tier on this client to check the price tags. Works in the menu.");
            RestoreKarma = config.Bind(Section, "Restore Karma", new KeyboardShortcut(KeyCode.Keypad8), "Fetch your real karma from the server again.");
            RunTests = config.Bind(Section, "Run Tests", new KeyboardShortcut(KeyCode.Keypad0), "Run the in-raid checks and write BepInEx/ReClassic-tests.txt.");
            TestsOnRaidStart = config.Bind(Section, "Run Tests On Raid Start", true, "Run the in-raid checks a few seconds after every raid starts.");
            TuningPanel = config.Bind(Section, "Tuning Panel", new KeyboardShortcut(KeyCode.Keypad9), "Open the door animation tuning panel.");
            ShowTuningPanel = config.Bind(Section, "Show Tuning Panel", false, "Show the door animation tuning panel. Same as pressing the Tuning Panel key.");
            UiTuner.Bind(config, Section);
            new DoorStatePatch().Enable();
            new GameStartedPatch().Enable();
            GameObject host = Plugin.Instance != null ? Plugin.Instance.gameObject : new GameObject("ReClassic Dev Keys");
            if (Plugin.Instance == null)
            {
                UnityEngine.Object.DontDestroyOnLoad(host);
            }
            host.AddComponent<DevKeys>();
            host.AddComponent<DevPanel>();
            host.AddComponent<UiTuner>();
            Plugin.LogSource.LogInfo("[Dev] Developer tools are on.");
        }

        public static KarmaInfo FakeKarma(string tier)
        {
            double step;
            switch (tier)
            {
                case "Very Low": step = -2; break;
                case "Low": step = -1; break;
                case "High": step = 1; break;
                case "Very High": step = 2; break;
                default: step = 0; break;
            }
            KarmaInfo real = KarmaClient.Info;
            return new KarmaInfo
            {
                Enabled = true,
                ProfileId = real?.ProfileId,
                Karma = step * 40,
                Tier = tier,
                TraderPrice = 1 - step * 0.05,
                FencePrice = 1 - step * 0.1,
                FenceSell = 1 + step * 0.1,
                FleaTax = 1 - step * 0.1,
                QuestExperience = 1 + step * 0.05
            };
        }

        private class DoorStatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(WorldInteractiveObject), nameof(WorldInteractiveObject.SetDoorState));
            }

            [PatchPrefix]
            private static void Prefix(WorldInteractiveObject __instance, EDoorState state, bool force)
            {
                if (!Recording)
                {
                    return;
                }
                var trace = new System.Diagnostics.StackTrace(2, false);
                var frames = new List<string>();
                for (int i = 0; i < trace.FrameCount && frames.Count < 6; i++)
                {
                    MethodBase method = trace.GetFrame(i)?.GetMethod();
                    if (method != null)
                    {
                        frames.Add((method.DeclaringType != null ? method.DeclaringType.Name + "." : "") + method.Name);
                    }
                }
                DoorEvents.Add($"{Time.time - RecordStart:0.00}s SetDoorState({state}{(force ? ", force" : "")}) on '{__instance.name}' from {string.Join(" < ", frames)}");
            }
        }

        private class GameStartedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
            }

            [PatchPostfix]
            private static void Postfix(GameWorld __instance)
            {
                if (__instance.GetComponent<DevRunner>() == null)
                {
                    __instance.gameObject.AddComponent<DevRunner>();
                }
            }
        }
    }

    internal sealed class DevKeys : MonoBehaviour
    {
        private void Update()
        {
            try
            {
                if (DevTools.ApplyKarma.Value.IsDown())
                {
                    KarmaClient.Override(DevTools.FakeKarma(DevTools.KarmaTier.Value));
                    Plugin.LogSource.LogInfo($"[Dev] Faking karma tier {DevTools.KarmaTier.Value}. Reopen the screen to redraw prices.");
                }
                if (DevTools.RestoreKarma.Value.IsDown())
                {
                    KarmaClient.RefreshSoon();
                    Plugin.LogSource.LogInfo("[Dev] Fetching real karma again.");
                }
                if (DevTools.ToggleMask.Value.IsDown())
                {
                    DevTools.ForceMask = !DevTools.ForceMask;
                    RadiationRunner.MaskSettingsChanged();
                    Plugin.LogSource.LogInfo($"[Dev] Forced mask overlay {(DevTools.ForceMask ? "on" : "off")}.");
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Dev] {e.Message}");
            }
        }
    }

    internal sealed class DevRunner : MonoBehaviour
    {
        private const string Stew = "57347d7224597744596b4e72";
        private const string Mre = "590c5f0d86f77413997acfab";
        private const string Antidote = "5fca138c2a7b221b2852a5c6";
        private const string Afak = "60098ad7c2240c0fe85c570a";

        private bool _busy;
        private StringBuilder _report;
        private int _passed;
        private int _failed;
        private int _warned;

        private void Start()
        {
            if (DevTools.TestsOnRaidStart.Value)
            {
                StartCoroutine(RunAll(5f));
            }
        }

        private void Update()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                return;
            }
            try
            {
                if (DevTools.SetRadiation.Value.IsDown())
                {
                    Radiation.Meters.Radiation.Level = DevTools.RadiationValue.Value;
                    Plugin.LogSource.LogInfo($"[Dev] Radiation set to {Radiation.Meters.Radiation.Level:0}.");
                }
                if (DevTools.SetToxicity.Value.IsDown())
                {
                    Radiation.Meters.Toxicity.Level = DevTools.ToxicityValue.Value;
                    Plugin.LogSource.LogInfo($"[Dev] Toxicity set to {Radiation.Meters.Toxicity.Level:0}.");
                }
                if (DevTools.ClearMeters.Value.IsDown())
                {
                    Radiation.Meters.Radiation.Level = 0f;
                    Radiation.Meters.Toxicity.Clear();
                    Plugin.LogSource.LogInfo("[Dev] Radiation and toxicity cleared.");
                }
                if (DevTools.TeleportToZone.Value.IsDown())
                {
                    GoToZone(player);
                }
                if (!_busy && DevTools.OpenNearestDoor.Value.IsDown())
                {
                    StartCoroutine(SingleDoor());
                }
                if (!_busy && DevTools.RunTests.Value.IsDown())
                {
                    StartCoroutine(RunAll(0f));
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Dev] {e.Message}");
            }
        }

        private static void GoToZone(Player player)
        {
            Zone best = null;
            float distance = float.MaxValue;
            foreach (Zone zone in RadiationZones.Active)
            {
                float d = Vector3.Distance(player.Position, zone.Center);
                if (d < distance)
                {
                    distance = d;
                    best = zone;
                }
            }
            if (best == null)
            {
                Plugin.LogSource.LogInfo("[Dev] There are no zones on this map.");
                return;
            }
            Vector3 target = best.Center;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, Mathf.Max(5f, best.Height), NavMesh.AllAreas))
            {
                target = hit.position;
            }
            player.Teleport(target + Vector3.up * 0.1f);
            Plugin.LogSource.LogInfo($"[Dev] Teleported to {best.Hazard} zone '{best.Name}' ({distance:0} m away).");
        }

        public static DevRunner Instance => Singleton<GameWorld>.Instance?.GetComponent<DevRunner>();

        public bool Busy => _busy;

        public void Replay()
        {
            if (_busy)
            {
                return;
            }
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player != null)
            {
                foreach (Door door in FindObjectsOfType<Door>())
                {
                    if (door != null && door.DoorState == EDoorState.Open && Vector3.Distance(player.Position, door.transform.position) < 4f)
                    {
                        door.SetDoorState(EDoorState.Shut, true);
                    }
                }
            }
            StartCoroutine(SingleDoor());
        }

        private IEnumerator SingleDoor()
        {
            _busy = true;
            Begin("Door check");
            yield return DoorTrial();
            Write();
            _busy = false;
        }

        private IEnumerator RunAll(float delay)
        {
            _busy = true;
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            Begin("Re-Classic checks");
            Static();
            yield return MeterChecks();
            yield return DoorTrial();
            Write();
            _busy = false;
        }

        private void Begin(string title)
        {
            _report = new StringBuilder();
            _passed = _failed = _warned = 0;
            string map = Singleton<GameWorld>.Instance?.LocationId ?? "?";
            _report.AppendLine($"{title} - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - map {map} - plugin {typeof(Plugin).Assembly.GetName().Version}");
            _report.AppendLine();
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok)
            {
                _passed++;
            }
            else
            {
                _failed++;
            }
            Line(ok ? "PASS" : "FAIL", name, detail);
        }

        private void Warn(string name, string detail)
        {
            _warned++;
            Line("WARN", name, detail);
        }

        private void Info(string name, string detail)
        {
            Line("INFO", name, detail);
        }

        private void Line(string tag, string name, string detail)
        {
            _report.AppendLine(string.IsNullOrEmpty(detail) ? $"[{tag}] {name}" : $"[{tag}] {name}: {detail}");
        }

        private void Write()
        {
            _report.AppendLine();
            _report.AppendLine($"{_passed} passed, {_failed} failed, {_warned} warnings");
            try
            {
                File.WriteAllText(DevTools.ReportPath, _report.ToString());
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Dev] Could not write the report: {e.Message}");
            }
            Plugin.LogSource.LogInfo($"[Dev] Checks done: {_passed} passed, {_failed} failed, {_warned} warnings. Report at {DevTools.ReportPath}");
        }

        private void Static()
        {
            Try("Effect icons", () =>
            {
                Check("Radiation effect has an icon", CustomEffects.HasIcon(typeof(IRadiationSickness)));
                Check("Toxicity effect has an icon", CustomEffects.HasIcon(typeof(IToxicBuildup)));
            });
            Try("Karma colours", () =>
            {
                Check("Higher price is red", KarmaPriceTags.Color(1.1, false) == "#c40000");
                Check("Lower price is blue", KarmaPriceTags.Color(0.9, false) == "#54c1ff");
                Check("Higher Fence pay is blue", KarmaPriceTags.Color(1.1, true) == "#54c1ff");
                Check("No change is grey", KarmaPriceTags.Color(1.0, false) == "#747b7e");
                KarmaInfo info = KarmaClient.Info;
                if (info == null || !KarmaClient.Available)
                {
                    Warn("Karma info", "not loaded or karma is off, so no tags will show");
                }
                else
                {
                    Info("Karma info", $"tier {info.Tier}, trader {KarmaClient.Percent(info.TraderPrice)}, fence {KarmaClient.Percent(info.FencePrice)}, fence pays {KarmaClient.Percent(info.FenceSell)}, flea tax {KarmaClient.Percent(info.FleaTax)}, quest exp {KarmaClient.Percent(info.QuestExperience)}");
                }
            });
            Try("Item attributes", () =>
            {
                Check("Beef stew shows a toxicity chance", HasAttribute(Stew, EHealthFactorType.Poisoning));
                Check("MRE has no toxicity chance", !HasAttribute(Mre, EHealthFactorType.Poisoning));
                Check("xTG-12 shows that it clears toxicity", HasAttribute(Antidote, EDamageEffectType.Intoxication));
                Check("AFAK shows its radiation relief", HasAttribute(Afak, EDamageEffectType.RadExposure));
            });
            Try("Gas mask overlay", () =>
            {
                Texture mask = MaskOverlay.Texture();
                Check("Mask texture found", mask != null, MaskOverlay.SourceName);
            });
            Try("Geiger sound", () =>
            {
                AudioClip[] clips = GeigerSound.Clips;
                bool real = clips != null && clips.Length == 1 && clips[0] != null && clips[0].name == "ReClassic Geiger Click";
                Check("geiger_click.ogg loaded", real, real ? $"{clips[0].length:0.000}s" : "using the placeholder clicks");
            });
            Try("Zones", () =>
            {
                Info("Zones on this map", RadiationZones.Active.Count == 0 ? "none" : string.Join(", ", RadiationZones.Active.Select(z => $"{z.Hazard} '{z.Name}' r{z.Radius:0}")));
            });
        }

        private IEnumerator MeterChecks()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            ActiveHealthController health = player?.ActiveHealthController;
            if (health == null)
            {
                Warn("Effects", "no player health to test on");
                yield break;
            }
            float savedRadiation = Radiation.Meters.Radiation.Level;
            float savedToxicity = Radiation.Meters.Toxicity.Level;
            bool radiationTestable = RadiationZones.Enabled.Value && RadiationZones.DamageScale.Value > 0f && CustomEffects.Radiation != null;
            if (radiationTestable)
            {
                Radiation.Meters.Radiation.Level = 30f;
                yield return new WaitForSeconds(1f);
                Check("Radiation effect appears at 30", CustomEffects.Radiation.FindExisting(health, EBodyPart.Head) != null);
                Radiation.Meters.Radiation.Level = 3f;
                yield return new WaitForSeconds(1f);
                Check("Radiation effect clears at 3", CustomEffects.Radiation.FindExisting(health, EBodyPart.Head) == null);
            }
            else
            {
                Warn("Radiation effect", "radiation zones or radiation damage are off");
            }
            if (Toxicity.Enabled.Value && CustomEffects.Toxicity != null)
            {
                Radiation.Meters.Toxicity.Level = 30f;
                yield return new WaitForSeconds(1f);
                Check("Toxicity effect appears at 30", CustomEffects.Toxicity.FindExisting(health, EBodyPart.Head) != null);
                Radiation.Meters.Toxicity.Level = 0f;
                yield return new WaitForSeconds(1f);
                Check("Toxicity effect clears at 0", CustomEffects.Toxicity.FindExisting(health, EBodyPart.Head) == null);
            }
            else
            {
                Warn("Toxicity effect", "toxicity is off");
            }
            Radiation.Meters.Radiation.Level = savedRadiation;
            Radiation.Meters.Toxicity.Level = savedToxicity;
        }

        private IEnumerator DoorTrial()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                Warn("Door", "no player");
                yield break;
            }
            if (!FullInteractions.Active)
            {
                Warn("Door", "Full Interaction Animations is off");
                yield break;
            }
            Door door = NearestDoor(player, out string summary);
            Info("Door search", summary);
            if (door == null)
            {
                Warn("Door", "no shut, unlocked door within 80 m on this floor");
                yield break;
            }
            WorldInteractiveObject.InteractionParameters parameters = door.GetInteractionParameters(player.Position);
            Info("Door", $"'{door.name}' id {door.Id}, {Vector3.Distance(player.Position, door.transform.position):0.0} m away, animation {(EInteraction)parameters.AnimationId}, grip {(parameters.Grip != null ? parameters.Grip.name : "none")}, handle {(door._handle != null ? door._handle.name : "none")}");
            if (parameters.Grip != null && door._handle != null)
            {
                Info("Grip to handle", $"{Vector3.Distance(parameters.Grip.transform.position, door._handle.transform.position):0.00} m");
            }
            if (parameters.Grip != null)
            {
                SmartGrip smart = parameters.Grip.GetComponent<SmartGrip>();
                Info("Grip", $"{(smart != null ? "SmartGrip, pivot " + (smart.Pivot != null ? Path(smart.Pivot, door.transform) : "none") : "plain GripPose")}, path {Path(parameters.Grip.transform, door.transform)}");
            }
            if (door._handle != null)
            {
                AnimationCurve curve = door._handle.OpenAnimation;
                string keys = curve == null ? "none" : string.Join(" ", curve.keys.Select(k => $"{k.time:0.00}:{k.value:0.00}"));
                Info("Handle", $"path {Path(door._handle.transform, door.transform)}, curve {keys}, turn {Quaternion.Angle(door._handle.DefaultRotation, door._handle.OpenRotation):0} deg, my turn time {FullInteractions.TurnSeconds(door):0.00}s");
            }
            Transform handleTransform = door._handle != null ? door._handle.transform : null;
            Quaternion handleRest = handleTransform != null ? handleTransform.localRotation : Quaternion.identity;
            bool handleTurning = false;
            player.Teleport(parameters.InteractionPosition + Vector3.up * 0.05f);
            yield return new WaitForSeconds(0.6f);

            float startAngle = door.CurrentAngle;
            float start = Time.time;
            FullInteractions.LastExecuteTime = -1f;
            FullInteractions.LastSubState = "";
            float seenExecute = -1f;
            DevTools.DoorEvents.Clear();
            DevTools.RecordStart = start;
            DevTools.Recording = true;
            string lastSub = "";
            player.ExecuteInteraction(door, new InteractionResult(EInteractionType.Open));

            var timeline = new List<string>();
            string lastPhase = null;
            string lastState = null;
            EDoorState lastDoor = door.DoorState;
            bool sawState = false;
            bool sawReach = false;
            bool attached = false;
            float moved = -1f;
            float letGo = -1f;
            float opened = -1f;
            while (Time.time - start < 6f)
            {
                float t = Time.time - start;
                string phase = HandActions.PhaseName;
                string state = player.CurrentManagedState?.GetType().Name ?? "?";
                if (phase != lastPhase)
                {
                    timeline.Add($"{t:0.00}s hand {phase} (progress {player.MovementContext.GetHandProgress():0.00})");
                    lastPhase = phase;
                }
                if (FullInteractions.LastSubState != lastSub)
                {
                    lastSub = FullInteractions.LastSubState;
                    timeline.Add($"{t:0.00}s sub-state {lastSub}");
                }
                if (FullInteractions.LastExecuteTime >= start && FullInteractions.LastExecuteTime != seenExecute)
                {
                    seenExecute = FullInteractions.LastExecuteTime;
                    timeline.Add($"{seenExecute - start:0.00}s door told to open");
                }
                if (state != lastState)
                {
                    timeline.Add($"{t:0.00}s state {state}");
                    lastState = state;
                }
                if (door.DoorState != lastDoor)
                {
                    timeline.Add($"{t:0.00}s door {door.DoorState}");
                    lastDoor = door.DoorState;
                }
                sawState |= state == "DoorInteractState";
                sawReach |= phase == "Reach" || phase == "Fallback";
                attached |= FullInteractions.GripAttached;
                if (handleTransform != null)
                {
                    bool turning = Quaternion.Angle(handleRest, handleTransform.localRotation) > 2f;
                    if (turning != handleTurning)
                    {
                        timeline.Add($"{t:0.00}s handle {(turning ? "starts turning" : "back up")}");
                        handleTurning = turning;
                    }
                }
                if (moved < 0f && Mathf.Abs(Mathf.DeltaAngle(startAngle, door.CurrentAngle)) > 1f)
                {
                    moved = t;
                    timeline.Add($"{t:0.00}s door starts swinging");
                }
                if (letGo < 0f && phase == "Release")
                {
                    letGo = t;
                }
                if (opened < 0f && door.DoorState == EDoorState.Open)
                {
                    opened = t;
                }
                if (opened >= 0f && phase == "Idle" && state != "DoorInteractState" && state != "ApproachState")
                {
                    break;
                }
                yield return null;
            }
            DevTools.Recording = false;
            Info("Turn time used", $"{FullInteractions.LastTurnSeconds:0.00}s, open lead {HandTiming.Current.OpenLead:0.00}s");
            foreach (string entry in DevTools.DoorEvents)
            {
                Info("Door state call", entry);
            }
            foreach (string entry in timeline)
            {
                Info("Door timeline", entry);
            }
            Check("Door used the full interaction", sawState);
            Check("Hand animation played", sawReach, HandActions.PhaseName);
            if (door._handle != null)
            {
                if (attached)
                {
                    Check("Hand rode the handle", true);
                }
                else
                {
                    Warn("Hand rode the handle", FullInteractions.LastAttachNote);
                }
            }
            Check("Door started swinging within 2 s", moved >= 0f && moved <= 2f, moved < 0f ? "never moved" : $"{moved:0.00}s");
            Check("Hand let go before 2 s", letGo >= 0f && letGo <= 2f, letGo < 0f ? "never" : $"{letGo:0.00}s");
            Check("Door finished opening", opened >= 0f, opened < 0f ? "still " + door.DoorState : $"{opened:0.00}s");
        }

        private static Door NearestDoor(Player player, out string summary)
        {
            Door withHandle = null;
            Door without = null;
            float handleDistance = 80f;
            float plainDistance = 80f;
            int total = 0, inactive = 0, notShut = 0, noAnimation = 0, far = 0;
            foreach (Door door in FindObjectsOfType<Door>())
            {
                if (door == null)
                {
                    continue;
                }
                total++;
                if (!door.isActiveAndEnabled)
                {
                    inactive++;
                    continue;
                }
                if (door.DoorState != EDoorState.Shut)
                {
                    notShut++;
                    continue;
                }
                if (door.interactWithoutAnimation)
                {
                    noAnimation++;
                    continue;
                }
                float d = Vector3.Distance(player.Position, door.transform.position);
                if (d >= 80f || Mathf.Abs(player.Position.y - door.transform.position.y) > 4f)
                {
                    far++;
                    continue;
                }
                if (door._handle != null && d < handleDistance)
                {
                    handleDistance = d;
                    withHandle = door;
                }
                else if (door._handle == null && d < plainDistance)
                {
                    plainDistance = d;
                    without = door;
                }
            }
            summary = $"{total} doors: {inactive} inactive, {notShut} not shut (locked or open), {noAnimation} without animation, {far} too far or another floor";
            return withHandle ?? without;
        }

        private static string Path(Transform current, Transform root)
        {
            string path = current.name;
            Transform parent = current.parent;
            while (parent != null && parent != root.parent)
            {
                path = parent.name + "/" + path;
                if (parent == root)
                {
                    break;
                }
                parent = parent.parent;
            }
            return path;
        }

        private static bool HasAttribute(string templateId, object id)
        {
            Item item = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), templateId, null);
            if (item == null)
            {
                return false;
            }
            foreach (ItemAttribute attribute in item.Attributes)
            {
                if (attribute.Id != null && attribute.Id.Equals(id))
                {
                    return true;
                }
            }
            return false;
        }

        private void Try(string group, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Check(group, false, "threw " + e.Message);
            }
        }
    }
}

namespace mizmii.ReClassic.Dev
{
    internal sealed class DevPanel : MonoBehaviour
    {
        private void Start()
        {
            Plugin.LogSource.LogInfo($"[Dev] Tuning panel ready on '{gameObject.name}'. Key: {DevTools.TuningPanel.Value}, or tick Show Tuning Panel.");
        }

        private void OnDestroy()
        {
            Plugin.LogSource.LogWarning("[Dev] The tuning panel was destroyed.");
        }

        private static readonly string[] States =
        {
            "OPEN_PUSH_LEFT_HINGE", "OPEN_PULL_RIGHT_HINGE", "OPEN_PUSH_RIGHT_HINGE", "OPEN_PULL_LEFT_HINGE",
            "Trunk Open", "generic_0", "generic_180", "hand_slap_forward", "hand_key_use"
        };

        private bool _open;
        private bool _scrubbing;
        private int _stateIndex;
        private float _scrub;
        private string _status = "";
        private Rect _window = new Rect(40f, 80f, 430f, 640f);
        private Vector2 _scroll;
        private bool _drawLogged;
        private bool _errorLogged;
        private Door _door;
        private float _nextDoorSearch;

        private void Update()
        {
            KeyboardShortcut key = DevTools.TuningPanel.Value;
            if (key.IsDown() || (key.MainKey != KeyCode.None && Input.GetKeyDown(key.MainKey)))
            {
                DevTools.ShowTuningPanel.Value = !DevTools.ShowTuningPanel.Value;
            }
            if (DevTools.ShowTuningPanel.Value != _open)
            {
                _open = DevTools.ShowTuningPanel.Value;
                Plugin.LogSource.LogInfo($"[Dev] Tuning panel {(_open ? "open" : "closed")}.");
                if (!_open)
                {
                    StopScrub();
                }
            }
            if (_open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            if (_scrubbing)
            {
                FirearmsAnimator hands = Singleton<GameWorld>.Instance?.MainPlayer?.HandsController?.FirearmsAnimator;
                if (hands == null || !HandActions.Scrub(hands, States[_stateIndex], _scrub))
                {
                    _status = "This weapon has no " + States[_stateIndex] + " state.";
                    StopScrub();
                }
            }
        }

        private void StopScrub()
        {
            if (_scrubbing)
            {
                _scrubbing = false;
                HandActions.EndScrub();
            }
        }

        private void OnGUI()
        {
            if (!_open)
            {
                return;
            }
            if (!_drawLogged)
            {
                _drawLogged = true;
                Plugin.LogSource.LogInfo("[Dev] Tuning panel is drawing.");
            }
            GUI.depth = -1000;
            try
            {
                _window = GUILayout.Window(83017, _window, Draw, "Re-Classic door tuning", GUILayout.MinWidth(430f), GUILayout.MinHeight(300f));
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Plugin.LogSource.LogWarning($"[Dev] Tuning panel failed to draw: {e}");
                }
            }
        }

        private void Draw(int id)
        {
            HandTiming t = HandTiming.Current;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(560f));
            GUILayout.Label($"Hand {HandActions.PhaseName}  clip {HandActions.Normalized:0.00}  progress {HandActions.Progress:0.00}  turn {FullInteractions.LastTurnSeconds:0.00}s");
            if (Time.unscaledTime >= _nextDoorSearch)
            {
                _nextDoorSearch = Time.unscaledTime + 0.5f;
                _door = NearestDoor();
            }
            Door door = _door;
            if (door != null)
            {
                string handle = door._handle != null ? $"{Quaternion.Angle(door._handle.DefaultRotation, door._handle.transform.localRotation):0} deg" : "none";
                GUILayout.Label($"Door '{door.name}' {door.DoorState}  angle {door.CurrentAngle:0}  handle {handle}");
            }
            GUILayout.Space(6f);
            GUILayout.Label("Timing (seconds)");
            t.ReachSeconds = Slider("Reach", t.ReachSeconds, 0.1f, 1.5f);
            t.OpenLead = Slider("Open fires this early before the grab", t.OpenLead, 0f, 1.5f);
            t.TurnSeconds = Slider("Turn (0 = follow the handle)", t.TurnSeconds, 0f, 1f);
            t.MinTurnSeconds = Slider("Turn minimum", t.MinTurnSeconds, 0f, 1f);
            t.MaxTurnSeconds = Slider("Turn maximum", t.MaxTurnSeconds, 0f, 1.5f);
            t.PushSeconds = Slider("Push", t.PushSeconds, 0.05f, 1.5f);
            t.ReleaseSeconds = Slider("Let go", t.ReleaseSeconds, 0.05f, 1.5f);
            t.FadeIn = Slider("Fade in", t.FadeIn, 0f, 0.5f);
            t.FadeOut = Slider("Fade out", t.FadeOut, 0f, 0.5f);
            GUILayout.Space(6f);
            GUILayout.Label("Clip points (0 to 1 through the clip)");
            t.GripAt = Slider("Hand on the handle", t.GripAt, 0f, 1f);
            t.TurnEnd = Slider("Turn ends", t.TurnEnd, t.GripAt, 1f);
            t.LetGoAt = Slider("Hand leaves", t.LetGoAt, t.TurnEnd, 1f);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Replay nearest door"))
            {
                DevRunner runner = DevRunner.Instance;
                if (runner == null)
                {
                    _status = "Only works in raid.";
                }
                else
                {
                    StopScrub();
                    runner.Replay();
                    _status = "Replaying. The report updates when it finishes.";
                }
            }
            if (GUILayout.Button("Save"))
            {
                try
                {
                    HandTiming.Save();
                    _status = "Saved to " + HandTiming.FilePath;
                }
                catch (Exception e)
                {
                    _status = "Could not save: " + e.Message;
                }
            }
            if (GUILayout.Button("Reload"))
            {
                HandTiming.Load();
                _status = "Reloaded.";
            }
            if (GUILayout.Button("Defaults"))
            {
                HandTiming.Reset();
                _status = "Back to the defaults (not saved).";
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label("Freeze the hand on a frame");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30f)))
            {
                _stateIndex = (_stateIndex + States.Length - 1) % States.Length;
            }
            GUILayout.Label(States[_stateIndex]);
            if (GUILayout.Button(">", GUILayout.Width(30f)))
            {
                _stateIndex = (_stateIndex + 1) % States.Length;
            }
            GUILayout.EndHorizontal();
            _scrub = Slider("Frame", _scrub, 0f, 1f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_scrubbing ? "Stop freezing" : "Freeze"))
            {
                if (_scrubbing)
                {
                    StopScrub();
                }
                else
                {
                    _scrubbing = true;
                }
            }
            if (GUILayout.Button("Use as grab point"))
            {
                t.GripAt = _scrub;
            }
            if (GUILayout.Button("Use as let go"))
            {
                t.LetGoAt = Mathf.Max(_scrub, t.TurnEnd);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label(_status);
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(230f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120f));
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            return value;
        }

        private static Door NearestDoor()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                return null;
            }
            Door best = null;
            float distance = 4f;
            foreach (Door door in FindObjectsOfType<Door>())
            {
                if (door == null)
                {
                    continue;
                }
                float d = Vector3.Distance(player.Position, door.transform.position);
                if (d < distance)
                {
                    distance = d;
                    best = door;
                }
            }
            return best;
        }
    }
}
