using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.CameraControl;
using HarmonyLib;
using mizmii.ReClassic.Effects;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal sealed class Knockout : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<EffectsController, DeathFade> DeathFadeField = AccessTools.FieldRefAccess<EffectsController, DeathFade>("_deathFade");
        private static readonly AccessTools.FieldRef<DeathFade, float> FadeTime = AccessTools.FieldRefAccess<DeathFade, float>("_time");


        public static Knockout Active { get; private set; }

        public Player Player { get; private set; }

        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
        private LiveRagdoll _ragdoll;
        private BlackoutScreen _screen;
        private DeathFade _deathFade;
        private float _closeEyesDelay = -1f;
        private float _closeEyesTime = -1f;
        private Action _onWake;
        private RobberyReport _report;
        private bool _simulating;
        private float _savedTimeScale;
        private float _savedFixedDelta;
        private float _savedMaxDelta;
        private float _simulationEnds;

        public bool Simulating => _simulating;
        private bool _finished;
        private bool _revived;
        private bool _adrenalineRevive;
        private string _revivedBy;
        private bool _sleeping;
        private float _sleepEnds;
        private bool _robbedInSleep;
        private bool _ownTimeSkip;

        public bool Cooperative { get; private set; }

        public static void Begin(Player player, Action onWake)
        {
            if (Active != null || player == null)
            {
                return;
            }
            Knockout knockout = player.gameObject.AddComponent<Knockout>();
            knockout.Player = player;
            knockout._onWake = onWake;
            knockout.Cooperative = Coop.InMatch;
            Active = knockout;
            player.OnPlayerDeadOrUnspawn += knockout.OnDeadOrGone;
            knockout.StartCoroutine(knockout.Run());
        }

        private void OnDeadOrGone(Player player)
        {
            Abort();
        }

        public void Revive(bool adrenaline, string by)
        {
            if (_finished)
            {
                return;
            }
            _revived = true;
            _adrenalineRevive |= adrenaline;
            _revivedBy = by;
        }

        public void EnterSleep(float duration, float shared, bool robbed)
        {
            _sleeping = true;
            _sleepEnds = Time.time + duration;
            _robbedInSleep = robbed;
        }

        private IEnumerator Run()
        {
            Step("mark the player unconscious", () => CustomEffects.Unconscious?.Add(Player.HealthController, CustomEffects.PartFor(Player.HealthController)));
            Step("play the knockout voice line", () => Player.Speaker?.Play(EPhraseTrigger.OnDeath, Player.HealthStatus, true));
            Step("hide the HUD", HideInterface);
            Step("release the trigger", ReleaseTrigger);
            Step("stop sprinting", () => ClearSprint(Player));
            Step("block input", () => GamePlayerOwner.SetIgnoreInput(true));
            Step("go prone", () => ForceProne(Player));
            Step("close the eyes", CloseEyes);
            Step("ragdoll the player", () =>
            {
                _ragdoll = new LiveRagdoll(Player);
                if (!_ragdoll.Start(Player.Velocity))
                {
                    Plugin.LogSource.LogWarning("[Unconsciousness] The ragdoll did not start.");
                }
            });
            Step("let the weapon fall with the hand", AttachWeapon);
            if (Cooperative)
            {
                Step("tell the other players", () => Coop.LocalDown(Player));
                StartCoroutine(BroadcastPose());
            }
            else
            {
                Step("hide the player from bots", () => HideFromBots(Player));
            }
            Step("show the blackout screen", () => _screen = BlackoutScreen.Create());

            yield return new WaitForSeconds(3.4f);
            _screen?.FadeBlack(1f, 1.4f);
            yield return new WaitForSeconds(1.6f);

            if (!Cooperative)
            {
                _screen?.ShowStatus(true);
                float minutes = Consciousness.RandomSkipMinutes();
                List<Player> robbers = new List<Player>();
                Player killer = null;
                Step("find who is searching the player", () =>
                {
                    robbers = Robbery.Robbers(Player.Position);
                    killer = Robbery.RecentAttacker(60f);
                });
                if (robbers.Count > 0)
                {
                    Step("let the bots search the player", () => Search(robbers, !MultiplayerTransfersBlocked()));
                }
                float duration;
                if (minutes > Consciousness.LongBlackoutMinutes)
                {
                    Step("send the attackers away", () => Robbery.DespawnKillers(killer, Player.Position));
                    duration = Consciousness.RandomDuration();
                }
                else
                {
                    float simulated = minutes * 60f / ClockFactor();
                    float speed = Mathf.Clamp(simulated / Mathf.Max(1f, Consciousness.RandomDuration()), 1f, Consciousness.SimulationSpeed);
                    duration = simulated / speed;
                    Step("speed up the raid", () => StartSimulation(speed, simulated / speed));
                }
                float skipSeconds = duration;
                _screen?.SetCountdown(duration + 0.6f);
                Step("start the time skip", () =>
                {
                    TimeSkip.Start(skipSeconds, minutes);
                    _ownTimeSkip = true;
                });
                yield return new WaitForSecondsRealtime(duration);
                Step("return the raid to normal speed", StopSimulation);
                Step("stop the time skip", StopOwnTimeSkip);
            }
            else
            {
                _screen?.ShowStatus(true);
                float limit = Consciousness.CoopWaitSeconds();
                float started = Time.time;
                while (!_revived && !_sleeping && Time.time - started < limit)
                {
                    yield return null;
                }
                if (_sleeping && !_revived)
                {
                    _screen?.ShowStatus(true);
                    if (_robbedInSleep)
                    {
                        Step("let the bots search the player", () => Search(new List<Player>(), false));
                    }
                    _screen?.SetCountdown(_sleepEnds - Time.time + 0.6f);
                    while (Time.time < _sleepEnds && !_revived)
                    {
                        yield return null;
                    }
                }
            }

            _screen?.ShowStatus(false);
            yield return new WaitForSeconds(_revived ? 0.2f : 0.6f);
            Wake();
        }

        private IEnumerator BroadcastPose()
        {
            var wait = new WaitForSeconds(0.2f);
            while (!_finished && _ragdoll != null)
            {
                Transform pelvis = _ragdoll.Pelvis;
                if (_ragdoll.Active && pelvis != null)
                {
                    Vector3 position = pelvis.position;
                    Coop.Send(CoopMessage.Pose, Player.ProfileId, values: new[] { position.x, position.y, position.z }, reliable: false);
                }
                yield return wait;
            }
        }

        private void Search(List<Player> robbers, bool giveToBots)
        {
            if (!Consciousness.Robbing)
            {
                return;
            }
            _report = Robbery.Rob(Player, robbers, giveToBots);
            _report.Dropped += Robbery.DropGear(Player, _report);
            Robbery.Forget();
        }

        private static bool MultiplayerTransfersBlocked()
        {
            return Consciousness.MultiplayerMatch();
        }

        private static float ClockFactor()
        {
            float factor = Singleton<GameWorld>.Instance?.GameDateTime?.TimeFactor ?? 7f;
            return factor > 0f ? factor : 7f;
        }

        private void StartSimulation(float speed, float realSeconds)
        {
            if (_simulating || speed <= 1.01f)
            {
                return;
            }
            _savedTimeScale = Time.timeScale;
            _savedFixedDelta = Time.fixedDeltaTime;
            _savedMaxDelta = Time.maximumDeltaTime;
            Time.timeScale = speed;
            Time.fixedDeltaTime = Mathf.Min(_savedFixedDelta * speed, Mathf.Max(_savedFixedDelta, 0.08f));
            Time.maximumDeltaTime = Mathf.Max(_savedMaxDelta, Time.fixedDeltaTime * 4f);
            _simulating = true;
            _simulationEnds = Time.realtimeSinceStartup + realSeconds + 5f;
            Plugin.LogSource.LogInfo($"[Unconsciousness] Running the raid at {speed:0.0}x while the player is out.");
        }

        private void StopSimulation()
        {
            if (!_simulating)
            {
                return;
            }
            _simulating = false;
            Time.timeScale = _savedTimeScale;
            Time.fixedDeltaTime = _savedFixedDelta;
            Time.maximumDeltaTime = _savedMaxDelta;
        }

        private void Update()
        {
            if (_simulating && Time.realtimeSinceStartup > _simulationEnds)
            {
                Plugin.LogSource.LogWarning("[Unconsciousness] The blackout ran past its end, putting the raid back to normal speed.");
                StopSimulation();
                StopOwnTimeSkip();
            }
        }

        private void OnDisable()
        {
            StopSimulation();
            StopOwnTimeSkip();
        }

        private void StopOwnTimeSkip()
        {
            if (_ownTimeSkip)
            {
                _ownTimeSkip = false;
                TimeSkip.Stop();
            }
        }

        private static void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not {what}: {e}");
            }
        }

        private void Wake()
        {
            PoseSnapshot pose = null;
            Vector3 position = Player.Position;
            float yaw = Player.Rotation.x;
            Step("read the ragdoll pose", () =>
            {
                if (_ragdoll != null && _ragdoll.Active)
                {
                    pose = _ragdoll.Capture();
                    _ragdoll.GroundPose(out position, out yaw);
                }
            });
            Step("stop the ragdoll", () => _ragdoll?.Stop());
            Step("put the weapon back in the hands", RestoreWeapon);
            Step("move the player to the body", () => _ragdoll?.Place(position, yaw));
            Step("go prone", () => ForceProne(Player));
            StartCoroutine(HoldProne());
            if (Cooperative)
            {
                Step("tell the other players you woke up", () => Coop.LocalUp(Player, position, yaw));
            }
            if (_adrenalineRevive)
            {
                Step("give the adrenaline effect", () => ConsciousnessController.Current?.GiveAdrenaline(Player.ActiveHealthController, 60f));
            }

            _screen?.FadeBlack(0f, 2.5f);
            Step("open the eyes", () => OpenEyes(2.5f));
            Step("show the HUD", ShowInterface);

            if (pose != null)
            {
                PoseBlend.Run(Player.gameObject, pose, 1.6f, Finish);
            }
            else
            {
                Finish();
            }
        }

        public static void ForceProne(Player player)
        {
            MovementContext movement = player?.MovementContext;
            if (movement == null)
            {
                return;
            }
            ClearSprint(player);
            if (!movement.IsInPronePose)
            {
                player.CurrentManagedState?.Prone();
            }
            movement.SetPoseLevel(0f, true);
            movement.IsInPronePose = true;
        }

        public static void ClearSprint(Player player)
        {
            MovementContext movement = player?.MovementContext;
            if (movement == null)
            {
                return;
            }
            if (player.CurrentManagedState is MovePlayerState move)
            {
                move._sprintPressedAndPostponed = false;
            }
            movement.EnableSprint(false);
            if (player.Physical is Physical physical && physical.Consumptions.TryGetValue(Physical.EConsumptionType.Sprint, out Physical.Consumption sprint) && sprint != null)
            {
                sprint.SetActive(physical, false);
                physical.StaminaConsumptionChanged();
            }
            movement.PlayerAnimatorEnableSprint(false);
        }

        private IEnumerator HoldProne()
        {
            var wait = new WaitForSeconds(0.25f);
            while (!_finished && Player != null && Player.MovementContext != null)
            {
                if (!Player.MovementContext.IsInPronePose)
                {
                    Step("go prone", () => ForceProne(Player));
                }
                yield return wait;
            }
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Step("release the trigger", ReleaseTrigger);
            Step("stop sprinting", () =>
            {
                ClearSprint(Player);
                Player.MovementContext?.ResetSpeedAfterSprint();
            });
            Step("give input back", () => GamePlayerOwner.SetIgnoreInput(false));
            Step("reset the hands", ResetHands);
            Step("hand over the weapon in your hands", () => Robbery.HandOver(Player, _report));
            Step("clear the unconscious effect", () => CustomEffects.Unconscious?.Remove(Player.HealthController, CustomEffects.Any));
            if (_revived && !string.IsNullOrEmpty(_revivedBy))
            {
                Step("say who woke the player", () => NotificationManager.DisplayMessageNotification(
                    _adrenalineRevive ? $"{_revivedBy} woke you up with adrenaline." : $"{_revivedBy} woke you up."));
            }
            if (_report != null && _report.Any)
            {
                Step("tell the player about the theft", () => NotificationManager.DisplayWarningNotification(_report.Describe()));
            }
            Step("finish waking up", () => _onWake?.Invoke());
            Cleanup(3f);
            Step("let bots see the player again", () => ShowToBots(Player));
        }

        public void Abort()
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            StopAllCoroutines();
            bool alive = Player != null && Player.HealthController != null && Player.HealthController.IsAlive;
            Step("return the raid to normal speed", StopSimulation);
            Step("stop the time skip", StopOwnTimeSkip);
            Step("show the HUD", ShowInterface);
            if (alive)
            {
                Step("stop sprinting", () => ClearSprint(Player));
            }
            Step("give input back", () => GamePlayerOwner.SetIgnoreInput(false));
            if (Cooperative && Player != null)
            {
                if (alive)
                {
                    Step("tell the other players", () => Coop.LocalUp(Player, Player.Position, Player.Rotation.x));
                }
                else
                {
                    Step("tell the other players", () => Coop.LocalGone(Player));
                }
            }
            if (!alive)
            {
                Step("hand the body to the death ragdoll", () => _ragdoll?.ReleaseForDeath());
            }
            if (alive)
            {
                Step("stop the ragdoll", () => _ragdoll?.Stop());
                Step("put the weapon back in the hands", RestoreWeapon);
                Step("open the eyes", () => OpenEyes(0.5f));
                Step("clear the unconscious effect", () => CustomEffects.Unconscious?.Remove(Player.HealthController, CustomEffects.Any));
            }
            _screen?.FadeBlack(0f, 0.5f);
            Cleanup(1f);
            if (alive)
            {
                Step("let bots see the player again", () => ShowToBots(Player));
            }
        }

        private void Cleanup(float delay)
        {
            if (Active == this)
            {
                Active = null;
            }
            if (Player != null)
            {
                Player.OnPlayerDeadOrUnspawn -= OnDeadOrGone;
            }
            if (_screen != null)
            {
                Destroy(_screen.gameObject, delay);
            }
            Destroy(this, delay + 0.1f);
        }

        private void OnDestroy()
        {
            if (!_finished)
            {
                Abort();
            }
            if (_deathFade != null)
            {
                if (_closeEyesDelay >= 0f)
                {
                    _deathFade._closeEyesDelay = _closeEyesDelay;
                }
                if (_closeEyesTime >= 0f)
                {
                    _deathFade._closeEyesTime = _closeEyesTime;
                }
                if (Player != null && Player.HealthController != null && Player.HealthController.IsAlive)
                {
                    _deathFade.enabled = false;
                }
            }
        }

        private void HideInterface()
        {
            var roots = new List<GameObject>();
            if (MonoBehaviourSingleton<GameUI>.Instantiated)
            {
                roots.Add(MonoBehaviourSingleton<GameUI>.Instance.gameObject);
            }
            if (MonoBehaviourSingleton<CommonUI>.Instantiated && MonoBehaviourSingleton<CommonUI>.Instance.EftBattleUIScreen != null)
            {
                roots.Add(MonoBehaviourSingleton<CommonUI>.Instance.EftBattleUIScreen.gameObject);
            }
            foreach (GameObject root in roots)
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas.enabled)
                    {
                        canvas.enabled = false;
                        _hiddenCanvases.Add(canvas);
                    }
                }
            }
        }

        private void ShowInterface()
        {
            foreach (Canvas canvas in _hiddenCanvases)
            {
                if (canvas != null)
                {
                    canvas.enabled = true;
                }
            }
            _hiddenCanvases.Clear();
        }

        private void CloseEyes()
        {
            EffectsController effects = CameraManager.Instance.EffectsController;
            _deathFade = effects != null ? DeathFadeField(effects) : null;
            if (_deathFade == null)
            {
                return;
            }
            _closeEyesDelay = _deathFade._closeEyesDelay;
            _closeEyesTime = _deathFade._closeEyesTime;
            _deathFade._closeEyesDelay = 2.2f;
            _deathFade._closeEyesTime = 2.4f;
            _deathFade.enabled = true;
            _deathFade.EnableEffect();
        }

        private void OpenEyes(float seconds)
        {
            if (_deathFade == null)
            {
                return;
            }
            _deathFade.DisableEffect();
            FadeTime(_deathFade) = Mathf.Max(0.01f, seconds);
        }

        private static IEnumerable<Player> AliveBots()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return Enumerable.Empty<Player>();
            }
            return world.AllAlivePlayersList.Where(p => p != null && p.IsAI && p.AIData?.BotOwner != null).ToList();
        }

        public static void HideFromBots(Player player)
        {
            foreach (Player bot in AliveBots())
            {
                try
                {
                    bot.AIData.BotOwner.BotsGroup?.RemoveEnemy(player, EBotEnemyCause.initial);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not clear {bot.Profile?.Nickname} as an enemy: {e.Message}");
                }
            }
        }

        private Transform _weapon;
        private Transform _weaponHand;
        private Vector3 _weaponOffset;
        private Quaternion _weaponRotation;
        private Vector3 _weaponLocalPosition;
        private Quaternion _weaponLocalRotation;

        private void ReleaseTrigger()
        {
            if (Player.HandsController is Player.FirearmController firearm)
            {
                firearm.SetTriggerPressed(false);
            }
        }

        private void AttachWeapon()
        {
            _weapon = Player.PlayerBones?.WeaponRoot?.Original;
            _weaponHand = Player.PlayerBones?.RightPalm;
            if (_weapon == null || _weaponHand == null)
            {
                _weapon = null;
                return;
            }
            _weaponLocalPosition = _weapon.localPosition;
            _weaponLocalRotation = _weapon.localRotation;
            _weaponOffset = _weaponHand.InverseTransformPoint(_weapon.position);
            _weaponRotation = Quaternion.Inverse(_weaponHand.rotation) * _weapon.rotation;
        }

        private void ResetHands()
        {
            if (_report != null && _report.TakeFromHands.Count + _report.DropFromHands.Count > 0 && Robbery.Melee(Player) != null)
            {
                return;
            }
            Player.AbstractHandsController hands = Player.HandsController;
            Item item = hands != null ? hands.Item : null;
            if (hands == null || item == null || hands is Player.EmptyHandsController || item.CurrentAddress == null)
            {
                return;
            }
            Player.DestroyController();
            Player.SetInHands(item, result =>
            {
                if (result.Failed)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not take the {item.ShortName.Localized()} back in hand: {result.Error}");
                }
            });
        }

        private void RestoreWeapon()
        {
            if (_weapon == null)
            {
                return;
            }
            _weapon.localPosition = _weaponLocalPosition;
            _weapon.localRotation = _weaponLocalRotation;
            _weapon = null;
        }

        private void LateUpdate()
        {
            if (_weapon == null || _weaponHand == null || _ragdoll == null || !_ragdoll.Active)
            {
                return;
            }
            _weapon.SetPositionAndRotation(_weaponHand.TransformPoint(_weaponOffset), _weaponHand.rotation * _weaponRotation);
        }

        public static void ShowToBots(Player player)
        {
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            {
                return;
            }
            var groups = new HashSet<BotsGroup>();
            foreach (Player bot in AliveBots())
            {
                BotsGroup group = bot.AIData.BotOwner.BotsGroup;
                if (group == null || !groups.Add(group) || group.Enemies.ContainsKey(player))
                {
                    continue;
                }
                try
                {
                    if (group.IsPlayerEnemy(player))
                    {
                        group.AddEnemy(player, EBotEnemyCause.initial);
                    }
                    else
                    {
                        group.AddNeutral(player);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Unconsciousness] Could not make {bot.Profile?.Nickname} notice the player again: {e.Message}");
                }
            }
        }

    }
}
