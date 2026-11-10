using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using mizmii.ReClassic.Patches;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal enum CoopMessage : byte
    {
        Down = 1,
        Up = 2,
        Pose = 3,
        Drag = 4,
        Revive = 5,
        Reviving = 6,
        Sleep = 7,
        Gone = 8,
        Time = 9,
        Mortar = 10,
        RaidClock = 11,
        Karma = 12,
        KarmaNotice = 13,
        Zones = 14
    }

    internal static class Coop
    {
        private static Action<byte, string, string, float[], string, bool> _send;
        private static Func<bool> _isServer;
        private static Func<bool> _connected;
        private static Func<bool> _headless;
        private static Func<string, bool> _extracted;

        public static readonly HashSet<string> Down = new HashSet<string>();
        public static readonly Dictionary<string, string> Drags = new Dictionary<string, string>();

        public static bool SleepActive { get; private set; }
        public static bool SleepGodMode { get; private set; }
        private static float _sleepEnds;

        public static void Attach(Action<byte, string, string, float[], string, bool> send, Func<bool> isServer, Func<bool> connected, Func<bool> headless, Func<string, bool> extracted)
        {
            _send = send;
            _isServer = isServer;
            _connected = connected;
            _headless = headless;
            _extracted = extracted;
        }

        public static bool Available
        {
            get
            {
                try
                {
                    return _send != null && _connected != null && _connected();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static bool IsServer
        {
            get
            {
                try
                {
                    return _isServer == null || _isServer();
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        public static bool Headless
        {
            get
            {
                try
                {
                    return _headless != null && _headless();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static bool InMatch => Available && Consciousness.MultiplayerMatch();

        public static bool IsDown(Player player)
        {
            if (player == null)
            {
                return false;
            }
            return LiveRagdoll.Frozen.Contains(player) || (Down.Count > 0 && Down.Contains(player.ProfileId));
        }

        public static void Reset()
        {
            Down.Clear();
            Drags.Clear();
            DownedBodies.Clear();
            if (SleepActive)
            {
                TimeSkip.Stop();
            }
            SleepActive = false;
            SleepGodMode = false;
        }

        public static void Send(CoopMessage kind, string target, string source = "", float[] values = null, string extra = "", bool reliable = true)
        {
            if (_send == null)
            {
                return;
            }
            try
            {
                _send((byte)kind, target ?? string.Empty, source ?? string.Empty, values ?? Array.Empty<float>(), extra ?? string.Empty, reliable);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not send a co-op message: {e}");
            }
        }

        public static Player Find(string profileId)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null || string.IsNullOrEmpty(profileId))
            {
                return null;
            }
            return world.AllAlivePlayersList.FirstOrDefault(p => p != null && p.ProfileId == profileId);
        }

        public static Player LocalPlayer => Singleton<GameWorld>.Instance?.MainPlayer;

        public static void LocalDown(Player player)
        {
            Down.Add(player.ProfileId);
            Vector3 position = player.Position;
            Send(CoopMessage.Down, player.ProfileId, values: new[] { position.x, position.y, position.z });
            if (IsServer)
            {
                Knockout.HideFromBots(player);
                CheckAllDown();
            }
        }

        public static void LocalUp(Player player, Vector3 position, float yaw)
        {
            Down.Remove(player.ProfileId);
            Drags.Remove(player.ProfileId);
            Send(CoopMessage.Up, player.ProfileId, values: new[] { position.x, position.y, position.z, yaw });
        }

        public static void LocalGone(Player player)
        {
            Down.Remove(player.ProfileId);
            Drags.Remove(player.ProfileId);
            foreach (string target in Drags.Where(d => d.Value == player.ProfileId).Select(d => d.Key).ToList())
            {
                Drags.Remove(target);
            }
            Send(CoopMessage.Gone, player.ProfileId);
        }

        public static void Receive(byte kind, string target, string source, float[] values, string extra)
        {
            switch ((CoopMessage)kind)
            {
                case CoopMessage.Down:
                    Down.Add(target);
                    Player down = Find(target);
                    if (down != null && !down.IsYourPlayer)
                    {
                        DownedBodies.Start(down);
                        if (IsServer)
                        {
                            Knockout.HideFromBots(down);
                        }
                    }
                    if (IsServer)
                    {
                        CheckAllDown();
                    }
                    break;
                case CoopMessage.Up:
                    Down.Remove(target);
                    Drags.Remove(target);
                    Player up = Find(target);
                    if (up != null && !up.IsYourPlayer && up.HealthController != null && up.HealthController.IsAlive)
                    {
                        DownedBodies.Stop(up);
                        if (IsServer)
                        {
                            Knockout.ShowToBots(up);
                        }
                    }
                    else
                    {
                        DownedBodies.Forget(target);
                    }
                    break;
                case CoopMessage.Time:
                    if (!IsServer && long.TryParse(extra, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ticks))
                    {
                        SleepActive = false;
                        SleepGodMode = false;
                        TimeSkip.SetTo(new DateTime(ticks, DateTimeKind.Utc));
                    }
                    break;
                case CoopMessage.Mortar:
                    if (values != null && values.Length >= 3)
                    {
                        Raid.Mortars.Explode(new Vector3(values[0], values[1], values[2]));
                    }
                    break;
                case CoopMessage.RaidClock:
                    if (!IsServer)
                    {
                        Raid.RaidTimers.OnRemoteClock(extra);
                    }
                    break;
                case CoopMessage.Karma:
                    Karma.KarmaClient.OnRemoteKarma(target, values, extra);
                    break;
                case CoopMessage.Zones:
                    if (!IsServer && int.TryParse(extra, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int seed))
                    {
                        Radiation.RadiationZones.OnRemoteSeed(seed);
                    }
                    break;
                case CoopMessage.KarmaNotice:
                    Karma.KarmaClient.OnRemoteNotice(target, extra);
                    break;
                case CoopMessage.Gone:
                    Down.Remove(target);
                    Drags.Remove(target);
                    foreach (string dragged in Drags.Where(d => d.Value == target).Select(d => d.Key).ToList())
                    {
                        Drags.Remove(dragged);
                    }
                    DownedBodies.Forget(target);
                    break;
                case CoopMessage.Pose:
                    if (values != null && values.Length >= 3)
                    {
                        DownedBodies.SetTarget(target, new Vector3(values[0], values[1], values[2]));
                    }
                    break;
                case CoopMessage.Drag:
                    if (values != null && values.Length > 0 && values[0] > 0.5f)
                    {
                        Drags[target] = source;
                    }
                    else if (Drags.TryGetValue(target, out string dragger) && dragger == source)
                    {
                        Drags.Remove(target);
                    }
                    break;
                case CoopMessage.Revive:
                    if (LocalPlayer != null && LocalPlayer.ProfileId == target)
                    {
                        Knockout.Active?.Revive(values != null && values.Length > 0 && values[0] > 0.5f, extra);
                    }
                    break;
                case CoopMessage.Reviving:
                    if (LocalPlayer != null && LocalPlayer.ProfileId == target && values != null && values.Length > 0 && values[0] > 0.5f && Knockout.Active != null)
                    {
                        NotificationManager.DisplayMessageNotification($"{extra} is waking you up...");
                    }
                    break;
                case CoopMessage.Sleep:
                    if (values != null && values.Length >= 2)
                    {
                        ApplySleep(values[0], values[1], extra);
                    }
                    break;
            }
        }

        private static bool IsExtracted(Player player)
        {
            try
            {
                return _extracted != null && _extracted(player.ProfileId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<Player> ActiveHumans()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return new List<Player>();
            }
            return world.AllAlivePlayersList
                .Where(p => p != null && PlayerKind.IsHuman(p) && p.HealthController != null && p.HealthController.IsAlive && !IsExtracted(p))
                .ToList();
        }

        public static void CheckAllDown()
        {
            if (!IsServer || SleepActive || !Consciousness.Enabled)
            {
                return;
            }
            List<Player> humans = ActiveHumans();
            if (humans.Count == 0 || humans.Any(p => !Down.Contains(p.ProfileId)))
            {
                return;
            }
            float minutes = Consciousness.RandomSkipMinutes();
            float first = Consciousness.RandomDuration();
            List<Player> order = humans.OrderBy(_ => UnityEngine.Random.value).ToList();
            int firstGroup = order.Count > 1 ? UnityEngine.Random.Range(1, 3) : 1;
            var entries = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                Player human = order[i];
                float duration = i < firstGroup ? first : first + Consciousness.RandomStagger();
                bool robbed = Robbery.Robbers(human.Position).Count > 0;
                if (minutes > Consciousness.LongBlackoutMinutes)
                {
                    Robbery.DespawnKillers(null, human.Position);
                }
                entries.Add(string.Join(":", human.ProfileId, duration.ToString(System.Globalization.CultureInfo.InvariantCulture), robbed ? "1" : "0"));
            }
            string list = string.Join(";", entries);
            Send(CoopMessage.Sleep, string.Empty, values: new[] { minutes, first }, extra: list);
            ApplySleep(minutes, first, list);
        }

        private static void ApplySleep(float minutes, float shared, string entries)
        {
            if (SleepActive)
            {
                return;
            }
            SleepActive = true;
            SleepGodMode = IsServer;
            _sleepEnds = Time.time + shared;
            TimeSkip.Start(shared, minutes);
            Player local = LocalPlayer;
            if (local == null || Knockout.Active == null || string.IsNullOrEmpty(entries))
            {
                return;
            }
            foreach (string entry in entries.Split(';'))
            {
                string[] parts = entry.Split(':');
                if (parts.Length < 3 || parts[0] != local.ProfileId)
                {
                    continue;
                }
                float duration = float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : shared;
                Knockout.Active.EnterSleep(duration, shared, parts[2] == "1");
            }
        }

        public static void Tick()
        {
            if (SleepActive && Time.time >= _sleepEnds)
            {
                SleepActive = false;
                SleepGodMode = false;
                TimeSkip.Stop();
                if (IsServer)
                {
                    DateTime? now = TimeSkip.Now();
                    if (now.HasValue)
                    {
                        Send(CoopMessage.Time, string.Empty, extra: now.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
        }
    }

    internal static class TimeSkip
    {
        private static readonly List<GameDateTime> Clocks = new List<GameDateTime>();
        private static readonly List<float> SavedMods = new List<float>();
        private static float _savedSkyInterval = -1f;
        private static bool _active;

        private static List<GameDateTime> FindClocks()
        {
            var clocks = new List<GameDateTime>();
            GameDateTime world = Singleton<GameWorld>.Instance?.GameDateTime;
            GameDateTime game = Singleton<IBotGame>.Instance?.GameDateTime;
            if (world != null)
            {
                clocks.Add(world);
            }
            if (game != null && !ReferenceEquals(game, world))
            {
                clocks.Add(game);
            }
            return clocks;
        }

        public static DateTime? Now()
        {
            List<GameDateTime> clocks = FindClocks();
            return clocks.Count > 0 ? clocks[0].Calculate() : (DateTime?)null;
        }

        public static void Start(float seconds, float minutes)
        {
            if (_active || minutes <= 0f || seconds <= 0f)
            {
                return;
            }
            Clocks.Clear();
            SavedMods.Clear();
            Clocks.AddRange(FindClocks());
            if (Clocks.Count == 0)
            {
                return;
            }
            foreach (GameDateTime clock in Clocks)
            {
                float factor = clock.TimeFactor > 0f ? clock.TimeFactor : 1f;
                SavedMods.Add(clock.TimeFactorMod);
                clock.ResetForce(clock.Calculate());
                clock.TimeFactorMod = minutes * 60f / (seconds * factor);
            }
            try
            {
                if (TODSkyProvider.Instance != null)
                {
                    _savedSkyInterval = TODSkyProvider.Instance.UpdateInterval;
                    TODSkyProvider.Instance.UpdateInterval = 0f;
                }
            }
            catch (Exception)
            {
                _savedSkyInterval = -1f;
            }
            _active = true;
        }

        public static void Stop()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            for (int i = 0; i < Clocks.Count; i++)
            {
                GameDateTime clock = Clocks[i];
                if (clock == null)
                {
                    continue;
                }
                clock.ResetForce(clock.Calculate());
                clock.TimeFactorMod = i < SavedMods.Count ? SavedMods[i] : 1f;
            }
            RestoreSky();
        }

        public static void SetTo(DateTime time)
        {
            if (_active)
            {
                Stop();
            }
            foreach (GameDateTime clock in FindClocks())
            {
                clock.ResetForce(time);
            }
        }

        private static void RestoreSky()
        {
            try
            {
                if (_savedSkyInterval >= 0f && TODSkyProvider.Instance != null)
                {
                    TODSkyProvider.Instance.UpdateInterval = _savedSkyInterval;
                }
            }
            catch (Exception)
            {
            }
            _savedSkyInterval = -1f;
        }
    }

    internal static class DownedBodies
    {
        private static readonly int RagdollLayer = LayerMask.NameToLayer("Deadbody");
        private static readonly int PlayerLayer = LayerMask.NameToLayer("Player");
        private static readonly int HitLayer = LayerMask.NameToLayer("HitCollider");

        private sealed class Entry
        {
            public Player Player;
            public DownedBody Body;
            public Vector3? Target;
            public OnPlayerDeadOrUnspawn OnGone;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        public static void Start(Player player)
        {
            if (player == null || Entries.ContainsKey(player.ProfileId) || Coop.Headless)
            {
                return;
            }
            try
            {
                player.MovementContext.ReleaseDoorIfInteractingWithOne();
                player.MovementContext.OnStateChanged -= player.MovementContextOnStateChanged;
                player.MovementContext.PhysicalConditionChanged -= player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                player.EnabledAnimators = 0;
                player.BodyAnimatorCommon.enabled = false;
                player.ArmsAnimatorCommon.enabled = false;
                player._characterController.isEnabled = false;
                player.POM?.Off();
                player.ProceduralWeaponAnimation.OnPreCollision -= player.IkStoreRaw;
                TransformTools.SetLayersRecursively(player.gameObject, RagdollLayer);
                PlayerPoolObject pool = player.GetComponent<PlayerPoolObject>();
                new CorpseRagdoll(pool.RigidbodySpawners, pool.JointSpawners, pool.PlayerRigidbodySleepHierarchy, player.Velocity,
                    EFTHardSettings.Instance.CorpseMaxDepenetrationVelocity, CollisionDetectionMode.Discrete, player,
                    (sleeping, time) => sleeping || time >= 15f, player.PlayerBody, player.PlayerBody.IsVisible, () => { }, false, false);
                player.enabled = false;
                DownedBody body = player.gameObject.AddComponent<DownedBody>();
                body.Player = player;
                var entry = new Entry { Player = player, Body = body };
                string id = player.ProfileId;
                entry.OnGone = gone => Forget(id);
                player.OnPlayerDeadOrUnspawn += entry.OnGone;
                Entries[id] = entry;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not ragdoll {player.Profile?.Nickname}: {e}");
            }
        }

        public static void Stop(Player player)
        {
            if (player == null || !Entries.TryGetValue(player.ProfileId, out Entry entry))
            {
                return;
            }
            Entries.Remove(player.ProfileId);
            try
            {
                Detach(entry);
                foreach (Rigidbody body in player.gameObject.GetComponentsInChildren<Rigidbody>())
                {
                    PhysicsExtensions.UpdateController.UnsupportRigidbody(body);
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
                foreach (CharacterJoint joint in player.gameObject.GetComponentsInChildren<CharacterJoint>())
                {
                    joint.enableProjection = false;
                    joint.enablePreprocessing = true;
                    joint.massScale = 1f;
                }
                player.MovementContext.OnStateChanged += player.MovementContextOnStateChanged;
                player.MovementContext.PhysicalConditionChanged += player.ProceduralWeaponAnimation.PhysicalConditionUpdated;
                player.EnabledAnimators = Player.EAnimatorMask.Thirdperson | Player.EAnimatorMask.Arms | Player.EAnimatorMask.Procedural | Player.EAnimatorMask.FBBIK | Player.EAnimatorMask.IK;
                player.BodyAnimatorCommon.enabled = true;
                player.ArmsAnimatorCommon.enabled = true;
                player._characterController.isEnabled = true;
                player.POM?.On();
                TransformTools.SetLayersRecursively(player.gameObject, PlayerLayer);
                foreach (var plate in player.PlayerBones.ArmorPlateColliders)
                {
                    plate.gameObject.layer = HitLayer;
                }
                foreach (var part in player.PlayerBones.BodyPartColliders)
                {
                    part.gameObject.layer = HitLayer;
                }
                player.ProceduralWeaponAnimation.OnPreCollision += player.IkStoreRaw;
                player.enabled = true;
                player.RecalculateEquipmentParams();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not stand {player.Profile?.Nickname} back up: {e}");
            }
        }

        private static void Detach(Entry entry)
        {
            if (entry.Player != null && entry.OnGone != null)
            {
                entry.Player.OnPlayerDeadOrUnspawn -= entry.OnGone;
            }
            if (entry.Body != null)
            {
                Player local = Coop.LocalPlayer;
                if (local != null && local.InteractableObject == entry.Body)
                {
                    local.InteractableObject = null;
                    local.ForceInteractionsChanged();
                }
                UnityEngine.Object.Destroy(entry.Body);
            }
        }

        public static void Forget(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || !Entries.TryGetValue(profileId, out Entry entry))
            {
                return;
            }
            Entries.Remove(profileId);
            try
            {
                Detach(entry);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[Unconsciousness] Could not clean up a downed player: " + e.Message);
            }
        }

        public static void Clear()
        {
            foreach (Entry entry in Entries.Values.ToList())
            {
                if (entry.Player != null)
                {
                    Stop(entry.Player);
                }
            }
            Entries.Clear();
        }

        public static void SetTarget(string profileId, Vector3 position)
        {
            if (Entries.TryGetValue(profileId, out Entry entry))
            {
                entry.Target = position;
            }
        }

        public static void FixedTick()
        {
            foreach (Entry entry in Entries.Values)
            {
                if (entry.Player == null || entry.Target == null)
                {
                    continue;
                }
                Transform pelvisBone = entry.Player.PlayerBones?.Pelvis?.Original;
                Rigidbody pelvis = pelvisBone != null ? pelvisBone.GetComponent<Rigidbody>() : null;
                if (pelvis == null || pelvis.isKinematic)
                {
                    continue;
                }
                Vector3 offset = entry.Target.Value - pelvis.position;
                if (offset.sqrMagnitude > 9f)
                {
                    foreach (Rigidbody body in entry.Player.gameObject.GetComponentsInChildren<Rigidbody>())
                    {
                        body.position += offset;
                        body.velocity = Vector3.zero;
                    }
                }
                else if (offset.sqrMagnitude > 0.0625f && !Coop.Drags.ContainsKey(entry.Player.ProfileId))
                {
                    pelvis.velocity = Vector3.Lerp(pelvis.velocity, Vector3.ClampMagnitude(offset * 2f, 2f), 0.2f);
                }
            }
        }

        public static IEnumerable<Rigidbody> Bodies(Player player)
        {
            PlayerPoolObject pool = player != null ? player.GetComponent<PlayerPoolObject>() : null;
            if (pool?.RigidbodySpawners == null)
            {
                yield break;
            }
            foreach (RigidbodySpawner spawner in pool.RigidbodySpawners)
            {
                if (spawner.Rigidbody != null)
                {
                    yield return spawner.Rigidbody;
                }
            }
        }

        public static Rigidbody GrabBody(Player player)
        {
            PlayerPoolObject pool = player != null ? player.GetComponent<PlayerPoolObject>() : null;
            Transform head = player?.PlayerBones?.Head?.Original;
            if (pool?.RigidbodySpawners == null || head == null)
            {
                return null;
            }
            Rigidbody best = null;
            float bestDistance = float.MaxValue;
            foreach (RigidbodySpawner spawner in pool.RigidbodySpawners)
            {
                Rigidbody body = spawner.Rigidbody;
                if (body == null || body.isKinematic)
                {
                    continue;
                }
                float distance = (body.position - head.position).sqrMagnitude;
                if (distance < bestDistance && distance > 0.01f)
                {
                    bestDistance = distance;
                    best = body;
                }
            }
            return best;
        }
    }

    internal sealed class DownedBody : InteractableObject
    {
        public Player Player;

        public AvailableInteractionState GetActions(GamePlayerOwner owner)
        {
            Player local = owner?.Player;
            if (local == null || Player == null)
            {
                return null;
            }
            string name = Player.Profile.Nickname;
            var state = new AvailableInteractionState();
            state.Actions.Add(new InteractionAction { Name = $"Wake up {name}", Action = () => WakeByHand(owner) });
            Item injector = FindInjector(local);
            if (injector != null)
            {
                state.Actions.Add(new InteractionAction { Name = $"Inject {name} with adrenaline", Action = () => WakeWithAdrenaline(local, injector) });
            }
            bool dragging = Coop.Drags.TryGetValue(Player.ProfileId, out string dragger) && dragger == local.ProfileId;
            state.Actions.Add(new InteractionAction
            {
                Name = dragging ? $"Let go of {name}" : $"Drag {name}",
                Action = () => SetDragging(local, !dragging)
            });
            return state;
        }

        private static Item FindInjector(Player local)
        {
            try
            {
                return local.InventoryController.Inventory.Equipment.GetAllItems()
                    .FirstOrDefault(i => i.TemplateId.ToString() == Consciousness.AdrenalineInjector);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void WakeByHand(GamePlayerOwner owner)
        {
            Player local = owner.Player;
            if (!(local.CurrentState is IdlePlayerState))
            {
                NotificationManager.DisplayWarningNotification("Stand still to wake them up.");
                return;
            }
            string target = Player.ProfileId;
            string me = local.Profile.Nickname;
            owner.ShowObjectivesPanel($"Waking up {Player.Profile.Nickname}", Consciousness.HandWakeTime);
            Coop.Send(CoopMessage.Reviving, target, local.ProfileId, new[] { 1f }, me);
            local.CurrentManagedState.Plant(true, false, Consciousness.HandWakeTime, success =>
            {
                owner.CloseObjectivesPanel();
                if (success && local.HealthController.IsAlive && Coop.Down.Contains(target))
                {
                    Coop.Send(CoopMessage.Revive, target, local.ProfileId, new[] { 0f }, me);
                    ClearMenu(local);
                }
                else
                {
                    Coop.Send(CoopMessage.Reviving, target, local.ProfileId, new[] { 0f }, me);
                }
            });
        }

        private void WakeWithAdrenaline(Player local, Item injector)
        {
            try
            {
                var discard = ItemManipulator.Discard(injector, local.InventoryController, true);
                if (discard.Failed)
                {
                    NotificationManager.DisplayWarningNotification("Could not use the adrenaline injector.");
                    return;
                }
                local.InventoryController.TryRunNetworkTransaction(discard);
                Coop.Send(CoopMessage.Revive, Player.ProfileId, local.ProfileId, new[] { 1f }, local.Profile.Nickname);
                ClearMenu(local);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not use adrenaline on {Player.Profile.Nickname}: {e}");
            }
        }

        private void SetDragging(Player local, bool drag)
        {
            if (drag)
            {
                Coop.Drags[Player.ProfileId] = local.ProfileId;
                DragHands.Begin(local);
            }
            else
            {
                Coop.Drags.Remove(Player.ProfileId);
                DragHands.End(local);
            }
            Coop.Send(CoopMessage.Drag, Player.ProfileId, local.ProfileId, new[] { drag ? 1f : 0f });
            ClearMenu(local);
        }

        private static void ClearMenu(Player local)
        {
            if (local == null)
            {
                return;
            }
            local.InteractableObject = null;
            local.ForceInteractionsChanged();
        }
    }

    internal sealed class CoopDriver : MonoBehaviour
    {
        private float _nextCheck;

        public static void Ensure(GameWorld world)
        {
            if (world != null && world.GetComponent<CoopDriver>() == null)
            {
                world.gameObject.AddComponent<CoopDriver>();
            }
        }

        private sealed class DragHandle
        {
            public GameObject Object;
            public Rigidbody Body;
            public SpringJoint Joint;
            public Rigidbody Grab;
        }

        private static readonly Dictionary<string, DragHandle> Handles = new Dictionary<string, DragHandle>();

        private static DragHandle CreateHandle(Rigidbody grab, Vector3 anchor)
        {
            var handleObject = new GameObject("ReClassicDragHandle");
            handleObject.transform.position = anchor;
            Rigidbody body = handleObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            SpringJoint joint = grab.gameObject.AddComponent<SpringJoint>();
            joint.connectedBody = body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.spring = 4000f;
            joint.damper = 300f;
            joint.minDistance = 0f;
            joint.maxDistance = 0.05f;
            joint.enableCollision = false;
            return new DragHandle { Object = handleObject, Body = body, Joint = joint, Grab = grab };
        }

        private static void ReleaseHandle(string key)
        {
            if (!Handles.TryGetValue(key, out DragHandle handle))
            {
                return;
            }
            Handles.Remove(key);
            if (handle.Joint != null)
            {
                Destroy(handle.Joint);
            }
            if (handle.Object != null)
            {
                Destroy(handle.Object);
            }
        }

        private void OnDestroy()
        {
            foreach (string key in Handles.Keys.ToList())
            {
                ReleaseHandle(key);
            }
            Coop.Reset();
        }

        private void Update()
        {
            try
            {
                Coop.Tick();
                if (Time.time >= _nextCheck)
                {
                    _nextCheck = Time.time + 1f;
                    if (Coop.InMatch && Coop.IsServer && Coop.Down.Count > 0)
                    {
                        Coop.CheckAllDown();
                    }
                }
                LimitDragger();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Co-op update failed: {e}");
                _nextCheck = Time.time + 10f;
            }
        }

        private static void LimitDragger()
        {
            Player local = Coop.LocalPlayer;
            if (local == null || Coop.Drags.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<string, string> drag in Coop.Drags.ToList())
            {
                if (drag.Value != local.ProfileId)
                {
                    continue;
                }
                Player target = Coop.Find(drag.Key);
                Rigidbody grab = target != null ? DownedBodies.GrabBody(target) : null;
                if (!local.HealthController.IsAlive || target == null || grab == null || (grab.position - Anchor(local)).sqrMagnitude > 16f || DragHands.WeaponDrawn(local))
                {
                    Coop.Drags.Remove(drag.Key);
                    Coop.Send(CoopMessage.Drag, drag.Key, local.ProfileId, new[] { 0f });
                    DragHands.End(local);
                    continue;
                }
                if (local.Physical.Sprinting)
                {
                    local.CurrentManagedState.EnableSprint(false);
                }
                MovementContext movement = local.MovementContext;
                if (movement.IsInPronePose)
                {
                    movement.IsInPronePose = false;
                }
                if (movement.PoseLevel > 0.4f)
                {
                    movement.SetPoseLevel(0.3f, true);
                }
            }
        }

        public static Vector3 Anchor(Player dragger)
        {
            return dragger.Position + dragger.Transform.forward * 0.75f + Vector3.up * 0.35f;
        }

        private void FixedUpdate()
        {
            try
            {
                DownedBodies.FixedTick();
                foreach (string stale in Handles.Keys.Where(k => !Coop.Drags.ContainsKey(k)).ToList())
                {
                    ReleaseHandle(stale);
                }
                foreach (KeyValuePair<string, string> drag in Coop.Drags)
                {
                    Player target = Coop.Find(drag.Key);
                    Player dragger = Coop.Find(drag.Value);
                    Rigidbody grab = target != null ? DownedBodies.GrabBody(target) : null;
                    if (dragger == null || grab == null)
                    {
                        ReleaseHandle(drag.Key);
                        continue;
                    }
                    Vector3 anchor = Anchor(dragger);
                    if (!Handles.TryGetValue(drag.Key, out DragHandle handle) || handle.Joint == null || handle.Grab != grab)
                    {
                        ReleaseHandle(drag.Key);
                        handle = CreateHandle(grab, anchor);
                        Handles[drag.Key] = handle;
                    }
                    handle.Body.MovePosition(anchor);
                    grab.WakeUp();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Dragging failed: {e}");
            }
        }
    }

    internal static class DragHands
    {
        private static Item _lastItem;
        private static float _since;

        public static void Begin(Player local)
        {
            try
            {
                _since = Time.time;
                Item inHands = local.HandsController?.Item;
                _lastItem = inHands is Weapon || inHands?.GetItemComponent<KnifeComponent>() != null ? inHands : null;
                if (!(local.HandsController is Player.EmptyHandsController))
                {
                    local.HideWeapon();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[Unconsciousness] Could not put the weapon away to drag: " + e.Message);
            }
        }

        public static bool WeaponDrawn(Player local)
        {
            return Time.time - _since > 1.5f && local.HandsController != null && !(local.HandsController is Player.EmptyHandsController);
        }

        public static void End(Player local)
        {
            Item item = _lastItem;
            _lastItem = null;
            try
            {
                if (item != null && local != null && local.HealthController.IsAlive && local.HandsController is Player.EmptyHandsController)
                {
                    local.TryProceed(item, result => { }, false);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[Unconsciousness] Could not take the weapon back out after dragging: " + e.Message);
            }
        }
    }

    internal class DownedInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions), new[] { typeof(GamePlayerOwner), typeof(IInteractive) });
        }

        [PatchPrefix]
        private static bool Prefix(GamePlayerOwner owner, IInteractive interactive, ref AvailableInteractionState __result)
        {
            if (!(interactive is DownedBody body))
            {
                return true;
            }
            __result = body.GetActions(owner);
            return false;
        }
    }
}
