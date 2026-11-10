using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFTEffectManager;
using mizmii.ReClassic.Effects;
using mizmii.ReClassic.Skills;
using System;
using System.Collections;
using UnityEngine;

namespace mizmii.ReClassic.Stress
{
    internal sealed class StressRunner : MonoBehaviour
    {
        private const float StressedAt = 35f;
        private const float CalmAt = 20f;
        private const float NearMiss = 2f;
        private const float CombatSeconds = 20f;

        public static StressRunner Instance { get; private set; }
        public static bool HeavyBreathing { get; private set; }

        public float Level { get; private set; }
        public bool Stressed { get; private set; }

        private Player _player;
        private float _lastCombat = -999f;
        private float _nextVoice;
        private float _nextHurtVoice;
        private float _nextPoll;
        private bool _wasJammed;
        private bool _wasReloading;
        private ActiveHealthController.Effect _tremor;

        private static readonly EPhraseTrigger[] Taunts = { EPhraseTrigger.OnFight, EPhraseTrigger.Provocation, EPhraseTrigger.OnEnemyConversation };

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            Release();
        }

        private float Resistance => SkillEffects.Value(SkillEffects.StressBuild);

        private float Severity => Stressed ? Mathf.Clamp01((Level - StressedAt) / (100f - StressedAt)) * (1f - 0.5f * Resistance) : 0f;

        private bool InCombat => Time.time - _lastCombat < CombatSeconds;

        private void Gain(float amount, string why)
        {
            if (amount <= 0f)
            {
                return;
            }
            Level = Mathf.Min(100f, Level + amount * Stress.BuildUp.Value * (1f - Resistance));
            _lastCombat = Time.time;
        }

        public void Shot(Vector3 origin, Vector3 direction, string shooter)
        {
            Player player = _player;
            if (player == null || !player.HealthController.IsAlive)
            {
                return;
            }
            if (shooter == player.ProfileId)
            {
                _lastCombat = Time.time;
                return;
            }
            Vector3 head = player.PlayerBones != null && player.PlayerBones.Head != null ? player.PlayerBones.Head.Original.position : player.Position + Vector3.up * 1.5f;
            Vector3 forward = direction.normalized;
            Vector3 toHead = head - origin;
            float along = Vector3.Dot(toHead, forward);
            if (along < 3f || along > 400f)
            {
                return;
            }
            Vector3 closest = origin + forward * along;
            float distance = Vector3.Distance(closest, head);
            if (distance > NearMiss)
            {
                return;
            }
            if (Physics.Linecast(origin, closest, LayersMaskController.HighPolyWithTerrainMask))
            {
                return;
            }
            Gain(Mathf.Lerp(6f, 1.5f, distance / NearMiss), "near miss");
        }

        public void Relieve(float amount)
        {
            Level = Mathf.Max(0f, Level - amount);
        }

        public void Hurt(float damage)
        {
            Gain(Mathf.Min(15f, damage * 0.4f), "hit");
            if (Stressed && Stress.Voice.Value && Time.time >= _nextHurtVoice && UnityEngine.Random.value < 0.2f + 0.5f * Severity)
            {
                _nextHurtVoice = Time.time + 6f;
                Say(EPhraseTrigger.OnBeingHurt, 0.3f);
            }
        }

        public void Heard(Player speaker, EPhraseTrigger phrase)
        {
            Player player = _player;
            if (player == null || !Stressed || !Stress.Voice.Value || Array.IndexOf(Taunts, phrase) < 0)
            {
                return;
            }
            if ((speaker.Position - player.Position).sqrMagnitude > 40f * 40f || !Hostile(speaker, player))
            {
                return;
            }
            if (UnityEngine.Random.value < 0.2f + 0.6f * Severity)
            {
                Say(UnityEngine.Random.value < 0.5f ? EPhraseTrigger.OnFight : EPhraseTrigger.Provocation, UnityEngine.Random.Range(0.8f, 2.2f));
            }
        }

        private static bool Hostile(Player speaker, Player player)
        {
            BotsGroup group = speaker.AIData?.BotOwner?.BotsGroup;
            if (group != null)
            {
                return group.IsEnemy(player) || group.IsPlayerEnemy(player);
            }
            return !speaker.IsAI;
        }

        private void Say(EPhraseTrigger phrase, float delay)
        {
            if (Time.time < _nextVoice)
            {
                return;
            }
            _nextVoice = Time.time + 5f;
            StartCoroutine(SayLater(phrase, delay));
        }

        private IEnumerator SayLater(EPhraseTrigger phrase, float delay)
        {
            yield return new WaitForSeconds(delay);
            Player player = _player;
            if (player != null && player.HealthController.IsAlive)
            {
                try
                {
                    player.Say(phrase, true);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Stress] Could not say {phrase}: {e.Message}");
                }
            }
        }

        private void Update()
        {
            try
            {
                Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Stress] {e.Message}");
                Release();
                enabled = false;
            }
        }

        private void Tick(float dt)
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player != _player)
            {
                Release();
                _player = player;
                Level = 0f;
            }
            ActiveHealthController health = player?.ActiveHealthController;
            if (health == null || !health.IsAlive || !Stress.Enabled.Value || dt <= 0f)
            {
                Release();
                return;
            }

            float whole = BloodLeft(health);
            if (InCombat && whole < 0.35f)
            {
                Gain((1f - whole / 0.35f) * 3f * dt, "near death");
            }
            float calm = InCombat ? 0.2f : 1.5f;
            Level = Mathf.Max(0f, Level - calm * (1f + Resistance) * dt);

            bool was = Stressed;
            Stressed = Level >= (Stressed ? CalmAt : StressedAt);
            if (Stressed != was)
            {
                Plugin.LogSource.LogInfo($"[Stress] {(Stressed ? "Stressed" : "Calmed down")} at {Level:0}.");
            }
            Show(health, Stressed);
            Tremors(health, Stressed);
            bool breathing = Stressed && Stress.Breathing.Value;
            if (breathing != HeavyBreathing)
            {
                HeavyBreathing = breathing;
                try
                {
                    player.method_36();
                }
                catch (Exception)
                {
                }
            }

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + 0.2f;
                Poll(player);
            }

            if (Stress.LogStress.Value.IsDown())
            {
                Plugin.LogSource.LogInfo($"[Stress] Level {Level:0}, {(Stressed ? "stressed" : "calm")}, severity {Severity:0.00}, {(InCombat ? "in combat" : "out of combat")}, resistance {Resistance * 100f:0}%.");
            }
        }

        private void Poll(Player player)
        {
            if (!(player.HandsController is Player.FirearmController firearm) || firearm.Item == null)
            {
                _wasJammed = false;
                _wasReloading = false;
                return;
            }
            bool jammed = firearm.Item.MalfState.State != Weapon.EMalfunctionState.None;
            if (jammed && !_wasJammed && Stressed && Stress.Voice.Value && UnityEngine.Random.value < 0.3f + 0.5f * Severity)
            {
                Say(EPhraseTrigger.OnWeaponJammed, 0.4f);
            }
            _wasJammed = jammed;
            bool reloading = firearm.IsInReloadOperation();
            if (reloading && !_wasReloading && Stressed && Stress.Voice.Value && UnityEngine.Random.value < 0.15f + 0.4f * Severity)
            {
                Say(EPhraseTrigger.OnWeaponReload, 0.2f);
            }
            _wasReloading = reloading;
        }

        private static float BloodLeft(ActiveHealthController health)
        {
            float current = 0f;
            float maximum = 0f;
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                ValueStruct value = health.GetBodyPartHealth(part);
                current += value.Current;
                maximum += value.Maximum;
            }
            return maximum > 0f ? Mathf.Clamp01(current / maximum) : 1f;
        }

        private static void Show(ActiveHealthController health, bool on)
        {
            EffectRegistration effect = CustomEffects.Stressed;
            if (effect == null)
            {
                return;
            }
            bool shown = effect.FindExisting(health, EBodyPart.Head) != null;
            if (on && !shown)
            {
                effect.Add(health, EBodyPart.Head);
            }
            else if (!on && shown)
            {
                effect.Remove(health, EBodyPart.Head);
            }
        }

        private void Tremors(ActiveHealthController health, bool on)
        {
            bool alive = _tremor != null && _tremor.State != EEffectState.Residued && _tremor.State != EEffectState.Removed;
            if (on && !alive)
            {
                _tremor = health.AddEffect<ActiveHealthController.Tremor>(EBodyPart.Head, 0f);
            }
            else if (!on && alive)
            {
                _tremor.ForceResidue();
                _tremor = null;
            }
        }

        private void Release()
        {
            ActiveHealthController health = _player?.ActiveHealthController;
            if (health != null && health.IsAlive)
            {
                Show(health, false);
                Tremors(health, false);
            }
            _tremor = null;
            Stressed = false;
            if (HeavyBreathing)
            {
                HeavyBreathing = false;
                try
                {
                    _player?.method_36();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
