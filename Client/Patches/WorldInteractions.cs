using Audio.SpatialSystem;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
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
    internal static class WorldInteractions
    {
        public static event Action<BallisticCollider, DamageInfo> ObjectHit;
        public static event Action<IExplosiveItem, Vector3> Explosion;
        public static event Action<Player, float, float> PlayerLanded;
        public static event Action<Vector3, int> AlarmStarted;
        public static bool AlarmsFromHost;

        public static void PlayRemoteAlarm(Vector3 position, int clip)
        {
            try
            {
                CarAlarms.PlayRemote(position, clip);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[WorldInteractions] Could not play the host's car alarm: {e}");
            }
        }

        public static void Enable(ConfigFile config)
        {
            CarAlarms.Bind(config);
            ObjectHit += CarAlarms.OnObjectHit;
            Explosion += CarAlarms.OnExplosion;
            PlayerLanded += CarAlarms.OnPlayerLanded;
            new ApplyHitPatch().Enable();
            new WorldExplosionPatch().Enable();
            new LandingPatch().Enable();
        }

        private class LandingPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.OnGrounded));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance, float fallHeight, float jumpHeight)
            {
                if (__instance == null || PlayerLanded == null)
                {
                    return;
                }

                try
                {
                    PlayerLanded(__instance, fallHeight, jumpHeight);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[WorldInteractions] Landing handler failed: {e}");
                }
            }
        }

        private class WorldExplosionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ExplosionSharedMethods), nameof(ExplosionSharedMethods.Explosion));
            }

            [PatchPostfix]
            private static void Postfix(IExplosiveItem grenadeItem, Vector3 explosionPosition)
            {
                if (grenadeItem == null || grenadeItem.IsDummy || Explosion == null)
                {
                    return;
                }

                try
                {
                    Explosion(grenadeItem, explosionPosition);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[WorldInteractions] Explosion handler failed: {e}");
                }
            }
        }

        private class ApplyHitPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(BallisticCollider), nameof(BallisticCollider.ApplyHit));
            }

            [PatchPostfix]
            private static void Postfix(BallisticCollider __instance, DamageInfo damageInfo)
            {
                if (__instance == null || ObjectHit == null)
                {
                    return;
                }

                try
                {
                    ObjectHit(__instance, damageInfo);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[WorldInteractions] Hit handler failed: {e}");
                }
            }
        }

        private static class CarAlarms
        {
            private const string LightSourceToken = "car_light_sourse";
            private const int MaxSearchDepth = 8;

            private static ConfigEntry<bool> _enabled;
            private static ConfigEntry<float> _chance;
            private static ConfigEntry<float> _duration;
            private static ConfigEntry<float> _volume;
            private static ConfigEntry<float> _blinkInterval;
            private static ConfigEntry<float> _audibleDistance;
            private static ConfigEntry<float> _explosionChance;
            private static ConfigEntry<bool> _shakeEnabled;
            private static ConfigEntry<float> _maxTilt;
            private static ConfigEntry<float> _maxBounce;
            private static ConfigEntry<float> _shakeDuration;
            private static ConfigEntry<float> _shakeRange;
            private static ConfigEntry<float> _noWheelScale;
            private static ConfigEntry<bool> _landingEnabled;
            private static ConfigEntry<float> _landingScale;
            private static ConfigEntry<bool> _weightEnabled;
            private static ConfigEntry<float> _weightScale;
            private static ConfigEntry<float> _weightMaxTilt;
            private static ConfigEntry<float> _bodyMass;

            private static readonly string[] WheelTokens = { "wheel", "tire", "tyre", "koleso", "kolesa" };
            private static readonly string[] SpareTokens = { "spare", "zapaska" };
            private static readonly Dictionary<int, CarAlarm> ColliderCache = new Dictionary<int, CarAlarm>();
            private static readonly Collider[] OverlapBuffer = new Collider[512];
            private static int _blastMask = -1;
            private static AudioClip[] _clips;

            public static void Bind(ConfigFile config)
            {
                const string section = "World Interactions - Car Alarms";
                _enabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Cars", "Car Alarms", true, "Shooting a car with working lights can set off its alarm.", section, "Enabled");
                _chance = config.Bind(section, "Alarm Chance", 0.35f, new ConfigDescription("Chance that a car has an alarm, rolled once per car the first time it is hit.", new AcceptableValueRange<float>(0f, 1f)));
                _duration = config.Bind(section, "Alarm Duration", 25f, ConfigHelper.Advanced("How long the alarm sounds, in seconds. Each car can only go off once per raid.", new AcceptableValueRange<float>(3f, 120f)));
                _volume = config.Bind(section, "Alarm Volume", 1f, new ConfigDescription("Alarm volume.", new AcceptableValueRange<float>(0f, 1f)));
                _blinkInterval = config.Bind(section, "Blink Interval", 0.5f, ConfigHelper.Advanced("Seconds between light flashes.", new AcceptableValueRange<float>(0.1f, 2f)));
                _audibleDistance = config.Bind(section, "Audible Distance", 150f, ConfigHelper.Advanced("Distance in meters at which the alarm fades out.", new AcceptableValueRange<float>(20f, 500f)));
                _explosionChance = config.Bind(section, "Explosion Alarm Chance", 0.5f, new ConfigDescription("Chance that a nearby explosion sets off a car's alarm. Rolled once per car, separately from the shot roll.", new AcceptableValueRange<float>(0f, 1f)));

                const string shake = "World Interactions - Car Shake";
                _shakeEnabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Cars", "Car Shake", false, "Can cost performance. Explosions rock nearby cars. Cars with separate wheels rock on their suspension, other cars get a smaller jolt.", shake, "Enabled");
                ConfigHelper.PerformanceHeavy(_shakeEnabled);
                _noWheelScale = config.Bind(shake, "No-Wheel Shake Scale", 0.35f, ConfigHelper.Advanced("Shake strength for cars whose wheels are part of the body mesh, relative to cars with separate wheels. 0 turns it off.", new AcceptableValueRange<float>(0f, 1f)));
                _landingEnabled = config.Bind(shake, "Shake On Landing", true, "Cars rock when a player lands on them.");
                _landingScale = config.Bind(shake, "Landing Shake Scale", 0.5f, ConfigHelper.Advanced("Landing shake strength for a big drop, relative to a point blank explosion. Small hops give a fraction of this.", new AcceptableValueRange<float>(0f, 1.5f)));
                _maxTilt = config.Bind(shake, "Max Tilt", 3f, ConfigHelper.Advanced("Largest body tilt from a point blank explosion, in degrees.", new AcceptableValueRange<float>(0f, 15f)));
                _maxBounce = config.Bind(shake, "Max Bounce", 0.06f, ConfigHelper.Advanced("Largest suspension dip from a point blank explosion, in meters.", new AcceptableValueRange<float>(0f, 0.3f)));
                _shakeDuration = config.Bind(shake, "Duration", 1.6f, ConfigHelper.Advanced("How long the car keeps rocking, in seconds.", new AcceptableValueRange<float>(0.3f, 5f)));
                _shakeRange = config.Bind(shake, "Range Multiplier", 1.5f, ConfigHelper.Advanced("Reach of the shake and the explosion alarm roll, relative to the grenade's max explosion distance.", new AcceptableValueRange<float>(0.5f, 5f)));
                _weightEnabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Cars", "Car Weight Tilt", false, "Can cost performance. Cars lean on their suspension toward players standing on them. Heavier players and gear lean them more, and big trucks barely move.", shake, "Tilt Under Weight");
                ConfigHelper.PerformanceHeavy(_weightEnabled);
                _weightScale = config.Bind(shake, "Weight Tilt Scale", 1f, ConfigHelper.Advanced("Strength of the lean from standing players. 1 is roughly real car suspension.", new AcceptableValueRange<float>(0f, 4f)));
                _weightMaxTilt = config.Bind(shake, "Max Weight Tilt", 2.5f, ConfigHelper.Advanced("Largest lean from standing players, in degrees.", new AcceptableValueRange<float>(0f, 10f)));
                _bodyMass = config.Bind(shake, "Player Body Mass", 80f, ConfigHelper.Advanced("Body mass in kg added to each player's gear weight.", new AcceptableValueRange<float>(0f, 150f)));
                ConfigHelper.Requires(_shakeEnabled, _weightEnabled, _noWheelScale, _landingEnabled, _maxTilt, _maxBounce, _shakeDuration);
                ConfigHelper.Requires(_landingEnabled, _landingScale);
                ConfigHelper.Requires(_weightEnabled, _weightScale, _weightMaxTilt, _bodyMass);

                var driver = new GameObject("ReClassic_CarWeight");
                UnityEngine.Object.DontDestroyOnLoad(driver);
                driver.hideFlags = HideFlags.HideAndDontSave;
                driver.AddComponent<CarWeight>();
            }

            private class CarWeight : MonoBehaviour
            {
                private readonly Dictionary<CarAlarm, Vector3> _moments = new Dictionary<CarAlarm, Vector3>();
                private readonly Dictionary<CarAlarm, float> _masses = new Dictionary<CarAlarm, float>();
                private readonly HashSet<CarAlarm> _loaded = new HashSet<CarAlarm>();
                private readonly List<CarAlarm> _released = new List<CarAlarm>();
                private readonly Dictionary<Player, float> _weights = new Dictionary<Player, float>();
                private float _nextCheck;
                private float _nextWeigh;

                private void Update()
                {
                    if (Time.time < _nextCheck)
                    {
                        return;
                    }
                    _nextCheck = Time.time + 0.1f;
                    if (Time.time >= _nextWeigh)
                    {
                        _nextWeigh = Time.time + 2f;
                        _weights.Clear();
                    }
                    _moments.Clear();
                    _masses.Clear();
                    if (_shakeEnabled.Value && _weightEnabled.Value && _weightScale.Value > 0f && Singleton<GameWorld>.Instantiated)
                    {
                        Gather(Singleton<GameWorld>.Instance.AllAlivePlayersList);
                    }

                    _released.Clear();
                    foreach (CarAlarm car in _loaded)
                    {
                        if (car == null)
                        {
                            _released.Add(car);
                        }
                        else if (!_masses.ContainsKey(car))
                        {
                            car.SetLoad(0f, Vector3.zero);
                            _released.Add(car);
                        }
                    }
                    foreach (CarAlarm car in _released)
                    {
                        _loaded.Remove(car);
                    }

                    foreach (KeyValuePair<CarAlarm, float> pair in _masses)
                    {
                        pair.Key.SetLoad(pair.Value, _moments[pair.Key]);
                        _loaded.Add(pair.Key);
                    }
                }

                private void Gather(List<Player> players)
                {
                    EnsureBlastMask();
                    for (int i = 0; i < players.Count; i++)
                    {
                        Player player = players[i];
                        if (player == null || player.IsAI || !player.HealthController.IsAlive || player.MovementContext == null || !player.MovementContext.IsGrounded)
                        {
                            continue;
                        }

                        Vector3 origin = player.Position + Vector3.up * 0.3f;
                        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.7f, _blastMask, QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }

                        CarAlarm car = GetCar(hit.collider);
                        if (car == null)
                        {
                            continue;
                        }

                        if (!_weights.TryGetValue(player, out float gear))
                        {
                            gear = GearWeight(player);
                            _weights[player] = gear;
                        }
                        float mass = _bodyMass.Value + gear;
                        Vector3 point = player.Position;
                        _masses.TryGetValue(car, out float total);
                        _moments.TryGetValue(car, out Vector3 moment);
                        _masses[car] = total + mass;
                        _moments[car] = moment + point * mass;
                    }
                }

                private static float GearWeight(Player player)
                {
                    try
                    {
                        float weight = player.InventoryController.Inventory.TotalWeight;
                        return float.IsNaN(weight) ? 0f : Mathf.Clamp(weight, 0f, 200f);
                    }
                    catch
                    {
                        return 0f;
                    }
                }
            }

            public static void OnObjectHit(BallisticCollider collider, DamageInfo damageInfo)
            {
                if (!_enabled.Value || damageInfo.DamageType == EDamageType.Fall || damageInfo.DamageType == EDamageType.Explosion)
                {
                    return;
                }

                CarAlarm car = GetCar(collider);
                if (car != null)
                {
                    car.OnShot();
                }
            }

            public static void OnPlayerLanded(Player player, float fallHeight, float jumpHeight)
            {
                if (!_shakeEnabled.Value || !_landingEnabled.Value || _landingScale.Value <= 0f)
                {
                    return;
                }

                float drop = Mathf.Max(fallHeight, jumpHeight * 0.5f);
                if (drop < 0.05f)
                {
                    return;
                }

                EnsureBlastMask();
                Vector3 origin = player.Position + Vector3.up * 0.3f;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.8f, _blastMask, QueryTriggerInteraction.Ignore))
                {
                    return;
                }

                CarAlarm car = GetCar(hit.collider);
                if (car != null)
                {
                    float strength = Mathf.Clamp(0.25f + drop / 2f, 0.25f, 1f) * _landingScale.Value;
                    car.OnLanded(hit.point, strength);
                }
            }

            private static void EnsureBlastMask()
            {
                if (_blastMask != -1)
                {
                    return;
                }
                int high = LayerMask.NameToLayer("HighPolyCollider");
                int low = LayerMask.NameToLayer("LowPolyCollider");
                _blastMask = high >= 0 && low >= 0 ? (1 << high) | (1 << low) : Physics.AllLayers;
            }

            public static void OnExplosion(IExplosiveItem grenade, Vector3 position)
            {
                if (!_enabled.Value && !_shakeEnabled.Value)
                {
                    return;
                }

                float radius = Mathf.Max(grenade.MaxExplosionDistance * _shakeRange.Value, 2f);
                EnsureBlastMask();

                int count = Physics.OverlapSphereNonAlloc(position, radius, OverlapBuffer, _blastMask, QueryTriggerInteraction.Ignore);
                var nearest = new Dictionary<CarAlarm, float>();
                for (int i = 0; i < count; i++)
                {
                    Collider collider = OverlapBuffer[i];
                    if (collider == null)
                    {
                        continue;
                    }

                    CarAlarm car = GetCar(collider);
                    if (car == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(position, collider.bounds.ClosestPoint(position));
                    if (!nearest.TryGetValue(car, out float best) || distance < best)
                    {
                        nearest[car] = distance;
                    }
                }

                float strengthScale = Mathf.Clamp(grenade.GetStrength / 100f, 0.5f, 1.5f);
                foreach (KeyValuePair<CarAlarm, float> pair in nearest)
                {
                    float falloff = 1f - Mathf.Clamp01(pair.Value / radius);
                    if (falloff > 0f)
                    {
                        pair.Key.OnBlast(position, Mathf.Min(falloff * strengthScale, 1.5f));
                    }
                }
            }

            private static CarAlarm GetCar(Component collider)
            {
                int id = collider.GetInstanceID();
                if (!ColliderCache.TryGetValue(id, out CarAlarm car))
                {
                    if (ColliderCache.Count > 10000)
                    {
                        ColliderCache.Clear();
                    }
                    car = FindCar(collider.transform);
                    ColliderCache[id] = car;
                }
                return car;
            }

            private static Transform[] FindWheels(Transform root)
            {
                var wheels = new List<Transform>();
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    string name = child.name;
                    if (WheelTokens.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        && !SpareTokens.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        wheels.Add(child);
                    }
                }
                return wheels.ToArray();
            }

            private static CarAlarm FindCar(Transform hit)
            {
                Transform current = hit;
                for (int depth = 0; current != null && depth < MaxSearchDepth; depth++, current = current.parent)
                {
                    CarAlarm existing = current.GetComponent<CarAlarm>();
                    if (existing != null)
                    {
                        return existing;
                    }

                    Transform lightSource = FindLightSource(current);
                    if (lightSource == null)
                    {
                        continue;
                    }

                    LampController[] lamps = lightSource.GetComponentsInChildren<LampController>(true);
                    CarAlarm alarm = current.gameObject.AddComponent<CarAlarm>();
                    alarm.Setup(lightSource, lamps, FindWheels(current));
                    return alarm;
                }
                return null;
            }

            private static Transform FindLightSource(Transform root)
            {
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child.name.IndexOf(LightSourceToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return child;
                    }
                }
                return null;
            }

            private static AudioClip[] Clips()
            {
                if (_clips == null)
                {
                    _clips = LoadClips();
                }
                return _clips;
            }

            private static int PickClip()
            {
                AudioClip[] clips = Clips();
                return clips.Length == 0 ? -1 : UnityEngine.Random.Range(0, clips.Length);
            }

            private static AudioClip ClipAt(int index)
            {
                AudioClip[] clips = Clips();
                return index >= 0 && index < clips.Length ? clips[index] : null;
            }

            public static void PlayRemote(Vector3 position, int clip)
            {
                EnsureBlastMask();
                int count = Physics.OverlapSphereNonAlloc(position, 6f, OverlapBuffer, _blastMask, QueryTriggerInteraction.Ignore);
                CarAlarm best = null;
                float bestDistance = 1f;
                for (int i = 0; i < count; i++)
                {
                    Collider collider = OverlapBuffer[i];
                    if (collider == null)
                    {
                        continue;
                    }
                    CarAlarm car = GetCar(collider);
                    if (car == null)
                    {
                        continue;
                    }
                    float distance = Vector3.Distance(car.transform.position, position);
                    if (distance < bestDistance)
                    {
                        best = car;
                        bestDistance = distance;
                    }
                }
                if (best == null)
                {
                    Plugin.LogSource.LogWarning($"[WorldInteractions] Could not find the host's alarmed car near {position}.");
                    return;
                }
                best.StartAlarm(clip);
            }

            private static AudioClip[] LoadClips()
            {
                Assembly assembly = typeof(WorldInteractions).Assembly;
                var clips = new List<AudioClip>();
                foreach (string name in assembly.GetManifestResourceNames().Where(n => n.IndexOf("car_alarm", StringComparison.OrdinalIgnoreCase) >= 0 && n.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n))
                {
                    try
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(name))
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            AudioClip clip = Wav.ToClip(Path.GetFileNameWithoutExtension(name), memory.ToArray());
                            if (clip != null)
                            {
                                clips.Add(clip);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogError($"[WorldInteractions] Failed to load {name}: {e.Message}");
                    }
                }

                if (clips.Count == 0)
                {
                    Plugin.LogSource.LogWarning("[WorldInteractions] No car alarm sounds were found in the plugin resources.");
                }
                return clips.ToArray();
            }

            private class CarAlarm : MonoBehaviour
            {
                private Transform _lightSource;
                private LampController[] _lamps;
                private Turnable.EState[] _originalStates;
                private bool[] _wasLive;
                private bool _prepared;
                private bool _armed;
                private bool _shotRolled;
                private bool _blastRolled;
                private Transform _emitter;
                private BetterSource _source;
                private float _endTime;
                private float _nextBlink;
                private bool _lightsOn;
                private bool _sounding;

                private Transform[] _wheels;
                private Vector3[] _wheelPositions;
                private Quaternion[] _wheelRotations;
                private Vector3 _basePosition;
                private Quaternion _baseRotation;
                private Vector3 _pivot;
                private Vector3 _tiltAxis;
                private float _tiltAmplitude;
                private float _bounceAmplitude;
                private float _shakeStart;
                private bool _shaking;
                private bool _wholeBody;
                private bool _hopUp;
                private bool _alarmUsed;

                private const float SagDepth = 0.15f;
                private const float BodyDensity = 110f;
                private const float SpringDamping = 0.3f;

                private bool _poseCaptured;
                private bool _poseValid;
                private Vector3 _forward;
                private Vector3 _right;
                private float _track;
                private float _wheelbase;
                private float _mass;
                private bool _weighted;
                private float _roll;
                private float _rollVelocity;
                private float _rollTarget;
                private float _pitch;
                private float _pitchVelocity;
                private float _pitchTarget;
                private float _dip;
                private float _dipVelocity;
                private float _dipTarget;

                public void Setup(Transform lightSource, LampController[] lamps, Transform[] wheels)
                {
                    _lightSource = lightSource;
                    _lamps = lamps;
                    _wheels = wheels;
                    enabled = false;
                }

                public void OnShot()
                {
                    if (!_shotRolled)
                    {
                        _shotRolled = true;
                        _armed |= UnityEngine.Random.value < _chance.Value;
                    }
                    Trigger();
                }

                public void OnBlast(Vector3 position, float strength)
                {
                    if (_shakeEnabled.Value)
                    {
                        StartShake(position, strength);
                    }

                    if (!_enabled.Value)
                    {
                        return;
                    }

                    if (!_blastRolled)
                    {
                        _blastRolled = true;
                        _armed |= UnityEngine.Random.value < _explosionChance.Value;
                    }
                    Trigger();
                }

                public void OnLanded(Vector3 position, float strength)
                {
                    if (_shakeEnabled.Value && _landingEnabled.Value)
                    {
                        StartShake(position, strength, true);
                    }
                }

                private void StartShake(Vector3 position, float strength, bool landing = false)
                {
                    if (!CapturePose())
                    {
                        return;
                    }
                    if (_wholeBody)
                    {
                        strength *= _noWheelScale.Value;
                        if (strength <= 0f)
                        {
                            return;
                        }
                    }

                    _hopUp = _wholeBody && !landing;
                    Vector3 away = landing ? position - _pivot : _pivot - position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.0001f)
                    {
                        away = _forward;
                    }
                    _tiltAxis = Vector3.Cross(Vector3.up, away.normalized);

                    float envelope = CurrentEnvelope();
                    _tiltAmplitude = Mathf.Max(envelope * _tiltAmplitude, _maxTilt.Value * strength);
                    _bounceAmplitude = Mathf.Max(envelope * _bounceAmplitude, _maxBounce.Value * strength);
                    _shakeStart = Time.time;
                    _shaking = true;
                    enabled = true;
                }

                public void SetLoad(float mass, Vector3 moment)
                {
                    if (mass <= 0f)
                    {
                        _rollTarget = 0f;
                        _pitchTarget = 0f;
                        _dipTarget = 0f;
                        return;
                    }
                    if (!CapturePose())
                    {
                        return;
                    }

                    Vector3 offset = moment / mass - _pivot;
                    float side = Vector3.Dot(offset, _right);
                    float along = Vector3.Dot(offset, _forward);
                    float ratio = mass / _mass * _weightScale.Value;
                    float scale = _wholeBody ? _noWheelScale.Value : 1f;
                    float limit = _weightMaxTilt.Value;

                    _rollTarget = Mathf.Clamp(ratio * SagDepth * side / (_track * _track) * Mathf.Rad2Deg * scale, -limit, limit);
                    _pitchTarget = Mathf.Clamp(ratio * SagDepth * along / (_wheelbase * _wheelbase) * Mathf.Rad2Deg * scale, -limit, limit);
                    _dipTarget = _wholeBody ? 0f : Mathf.Min(ratio * SagDepth, SagDepth);
                    enabled = true;
                }

                private bool CapturePose()
                {
                    if (_poseCaptured)
                    {
                        return _poseValid;
                    }
                    _poseCaptured = true;
                    _basePosition = transform.position;
                    _baseRotation = transform.rotation;

                    Vector3 center = Vector3.zero;
                    int count = 0;
                    foreach (Transform wheel in _wheels)
                    {
                        if (wheel == null)
                        {
                            continue;
                        }
                        center += WheelCenter(wheel);
                        count++;
                    }
                    _wholeBody = count == 0;
                    _wheels = _wholeBody ? new Transform[0] : _wheels.Where(w => w != null).ToArray();
                    _wheelPositions = _wheels.Select(w => w.position).ToArray();
                    _wheelRotations = _wheels.Select(w => w.rotation).ToArray();

                    if (!MeasureBody(out Vector3 bottom, out Vector3 size))
                    {
                        _poseValid = false;
                        return false;
                    }
                    _pivot = _wholeBody ? bottom : center / count;
                    _track = Mathf.Max(size.x * 0.5f * 0.85f, 0.4f);
                    _wheelbase = Mathf.Max(size.z * 0.5f * 0.65f, 0.8f);
                    _mass = Mathf.Clamp(size.x * size.y * size.z * BodyDensity, 500f, 20000f);
                    _poseValid = true;
                    return true;
                }

                private bool MeasureBody(out Vector3 bottom, out Vector3 size)
                {
                    Vector3[] axes = { transform.right, transform.up, transform.forward };
                    int upAxis = 0;
                    for (int i = 1; i < 3; i++)
                    {
                        if (Mathf.Abs(axes[i].y) > Mathf.Abs(axes[upAxis].y))
                        {
                            upAxis = i;
                        }
                    }

                    var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
                    var max = new[] { float.MinValue, float.MinValue, float.MinValue };
                    var corners = new Vector3[8];
                    bool found = false;
                    foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                    {
                        if (renderer is ParticleSystemRenderer || (_lightSource != null && renderer.transform.IsChildOf(_lightSource)))
                        {
                            continue;
                        }
                        Bounds local = renderer.localBounds;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 sign = new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f);
                            corners[c] = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, sign));
                        }
                        foreach (Vector3 point in corners)
                        {
                            for (int a = 0; a < 3; a++)
                            {
                                float projection = Vector3.Dot(point, axes[a]);
                                min[a] = Mathf.Min(min[a], projection);
                                max[a] = Mathf.Max(max[a], projection);
                            }
                        }
                        found = true;
                    }

                    bottom = _basePosition;
                    size = Vector3.zero;
                    _forward = Vector3.forward;
                    _right = Vector3.right;
                    if (!found)
                    {
                        return false;
                    }

                    int first = upAxis == 0 ? 1 : 0;
                    int second = upAxis == 2 ? 1 : 2;
                    int longAxis = max[first] - min[first] >= max[second] - min[second] ? first : second;
                    int sideAxis = longAxis == first ? second : first;

                    Vector3 forward = axes[longAxis];
                    forward.y = 0f;
                    if (forward.sqrMagnitude > 0.0001f)
                    {
                        _forward = forward.normalized;
                    }
                    _right = Vector3.Cross(Vector3.up, _forward);

                    size = new Vector3(max[sideAxis] - min[sideAxis], max[upAxis] - min[upAxis], max[longAxis] - min[longAxis]);
                    Vector3 middle = Vector3.zero;
                    for (int a = 0; a < 3; a++)
                    {
                        middle += axes[a] * ((min[a] + max[a]) * 0.5f);
                    }
                    bottom = middle - axes[upAxis] * (Mathf.Sign(axes[upAxis].y) * size.y * 0.5f);
                    return size.x > 0.1f && size.z > 0.1f;
                }

                private static Vector3 WheelCenter(Transform wheel)
                {
                    Renderer[] renderers = wheel.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0)
                    {
                        return wheel.position;
                    }
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                    return bounds.center;
                }

                private float CurrentEnvelope()
                {
                    if (!_shaking)
                    {
                        return 0f;
                    }
                    return Mathf.Exp(-ShakeDecay() * (Time.time - _shakeStart));
                }

                private float ShakeDecay()
                {
                    return 4.5f / Mathf.Max(_shakeDuration.Value, 0.1f);
                }

                private static void Spring(ref float value, ref float velocity, float target, float frequency, float dt)
                {
                    float omega = 2f * Mathf.PI * frequency;
                    velocity += (-omega * omega * (value - target) - 2f * SpringDamping * omega * velocity) * dt;
                    value += velocity * dt;
                }

                private bool StepWeight()
                {
                    float dt = Mathf.Min(Time.deltaTime, 0.05f);
                    float frequency = Mathf.Lerp(1.7f, 1.1f, Mathf.InverseLerp(1000f, 8000f, _mass));
                    Spring(ref _roll, ref _rollVelocity, _rollTarget, frequency, dt);
                    Spring(ref _pitch, ref _pitchVelocity, _pitchTarget, frequency, dt);
                    Spring(ref _dip, ref _dipVelocity, _dipTarget, frequency, dt);

                    bool resting = _rollTarget == 0f && _pitchTarget == 0f && _dipTarget == 0f
                        && Mathf.Abs(_roll) < 0.002f && Mathf.Abs(_pitch) < 0.002f && Mathf.Abs(_dip) < 0.0002f
                        && Mathf.Abs(_rollVelocity) < 0.01f && Mathf.Abs(_pitchVelocity) < 0.01f && Mathf.Abs(_dipVelocity) < 0.001f;
                    if (resting)
                    {
                        _roll = _pitch = _dip = 0f;
                        _rollVelocity = _pitchVelocity = _dipVelocity = 0f;
                    }
                    return !resting;
                }

                private void UpdateMotion()
                {
                    Quaternion rotation = Quaternion.identity;
                    Vector3 offset = Vector3.zero;

                    if (_shaking)
                    {
                        float t = Time.time - _shakeStart;
                        if (t >= _shakeDuration.Value)
                        {
                            _shaking = false;
                        }
                        else
                        {
                            float envelope = Mathf.Exp(-ShakeDecay() * t);
                            float wave = Mathf.Sin(t * 2f * Mathf.PI * 1.8f);
                            float bounceWave = Mathf.Sin(t * 2f * Mathf.PI * 2.4f);
                            rotation = Quaternion.AngleAxis(_tiltAmplitude * envelope * wave, _tiltAxis);
                            offset = _hopUp
                                ? Vector3.up * (_bounceAmplitude * envelope * Mathf.Abs(bounceWave))
                                : _wholeBody
                                    ? Vector3.zero
                                    : Vector3.down * (_bounceAmplitude * envelope * bounceWave);
                        }
                    }

                    _weighted = StepWeight();
                    if (_weighted)
                    {
                        Quaternion lean = Quaternion.AngleAxis(_roll, Vector3.Cross(_right, Vector3.down))
                            * Quaternion.AngleAxis(_pitch, Vector3.Cross(_forward, Vector3.down));
                        rotation *= lean;
                        offset += Vector3.down * _dip;
                    }

                    if (!_shaking && !_weighted)
                    {
                        transform.SetPositionAndRotation(_basePosition, _baseRotation);
                    }
                    else
                    {
                        transform.SetPositionAndRotation(_pivot + rotation * (_basePosition - _pivot) + offset, rotation * _baseRotation);
                    }
                    PinWheels();
                }

                private void PinWheels()
                {
                    for (int i = 0; i < _wheels.Length; i++)
                    {
                        if (_wheels[i] != null)
                        {
                            _wheels[i].SetPositionAndRotation(_wheelPositions[i], _wheelRotations[i]);
                        }
                    }
                }

                public void Trigger()
                {
                    if (AlarmsFromHost || !_armed || _alarmUsed || _lamps.Length == 0)
                    {
                        return;
                    }

                    if (!_prepared)
                    {
                        Prepare();
                    }

                    if (!HasWorkingLamp())
                    {
                        _armed = false;
                        return;
                    }

                    int clip = PickClip();
                    StartAlarm(clip);
                    AlarmStarted?.Invoke(transform.position, clip);
                }

                public void StartAlarm(int clip)
                {
                    if (_alarmUsed || _lamps.Length == 0)
                    {
                        return;
                    }
                    if (!_prepared)
                    {
                        Prepare();
                    }

                    HoldLampsOff();
                    StartSound(ClipAt(clip));

                    _alarmUsed = true;
                    _endTime = Time.time + _duration.Value;
                    _sounding = true;
                    _lightsOn = false;
                    _nextBlink = Time.time;
                    enabled = true;
                }

                private void Prepare()
                {
                    _prepared = true;
                    _originalStates = new Turnable.EState[_lamps.Length];
                    _wasLive = new bool[_lamps.Length];
                    for (int i = 0; i < _lamps.Length; i++)
                    {
                        if (_lamps[i] != null)
                        {
                            _originalStates[i] = _lamps[i].LampState;
                            _wasLive[i] = _lamps[i].gameObject.activeInHierarchy;
                        }
                    }

                    ActivateChain(_lightSource, transform);
                    foreach (LampController lamp in _lamps)
                    {
                        if (lamp != null)
                        {
                            ActivateChain(lamp.transform, _lightSource);
                        }
                    }

                    Vector3 center = Vector3.zero;
                    int count = 0;
                    foreach (LampController lamp in _lamps)
                    {
                        if (lamp != null)
                        {
                            center += lamp.transform.position;
                            count++;
                        }
                    }

                    var emitter = new GameObject("ReClassic_CarAlarmEmitter");
                    _emitter = emitter.transform;
                    _emitter.SetParent(transform, false);
                    _emitter.position = count > 0 ? center / count : transform.position;
                }

                private static void ActivateChain(Transform from, Transform stopAt)
                {
                    for (Transform t = from; t != null && t != stopAt; t = t.parent)
                    {
                        if (!t.gameObject.activeSelf)
                        {
                            t.gameObject.SetActive(true);
                        }
                    }
                }

                private void HoldLampsOff()
                {
                    foreach (LampController lamp in _lamps)
                    {
                        if (lamp == null || lamp.IsDestroyed)
                        {
                            continue;
                        }
                        lamp.LampState = Turnable.EState.Off;
                        lamp.TurnLights(false, true);
                    }
                }

                private void StartSound(AudioClip clip)
                {
                    if (clip == null || !MonoBehaviourSingleton<BetterAudio>.Instantiated)
                    {
                        return;
                    }

                    _source = MonoBehaviourSingleton<BetterAudio>.Instance.GetSource(BetterAudio.AudioSourceGroupType.Environment);
                    if (_source == null)
                    {
                        return;
                    }

                    _source.SetBaseVolume(_volume.Value);
                    _source.SetRolloff(_audibleDistance.Value);
                    _source.StartTrackingPosition(_emitter);
                    _source.Loop = true;
                    if (MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated)
                    {
                        MonoBehaviourSingleton<SpatialAudioSystem>.Instance.ProcessSourceOcclusion(_emitter.gameObject, _source, EOcclusionTest.ContinuousPropagated);
                    }
                    _source.Play(clip, null, 1f, _volume.Value, false, false);
                }

                private void ReleaseSound()
                {
                    if (_source == null)
                    {
                        return;
                    }

                    _source.Stop();
                    _source.StopTrackingPosition();
                    _source.Loop = false;
                    _source.Release();
                    _source = null;
                }

                private void Update()
                {
                    if (_poseValid && (_shaking || _weighted || _rollTarget != 0f || _pitchTarget != 0f || _dipTarget != 0f))
                    {
                        UpdateMotion();
                    }

                    if (_sounding)
                    {
                        if (Time.time >= _endTime || !HasWorkingLamp())
                        {
                            Stop();
                        }
                        else if (Time.time >= _nextBlink)
                        {
                            _lightsOn = !_lightsOn;
                            SetLamps(_lightsOn);
                            _nextBlink = Time.time + _blinkInterval.Value;
                        }
                    }

                    if (!_shaking && !_sounding && !_weighted && _rollTarget == 0f && _pitchTarget == 0f && _dipTarget == 0f)
                    {
                        enabled = false;
                    }
                }

                private bool HasWorkingLamp()
                {
                    foreach (LampController lamp in _lamps)
                    {
                        if (lamp != null && !lamp.IsDestroyed)
                        {
                            return true;
                        }
                    }
                    return false;
                }

                private void SetLamps(bool on)
                {
                    foreach (LampController lamp in _lamps)
                    {
                        if (lamp == null || lamp.IsDestroyed)
                        {
                            continue;
                        }

                        lamp.LampState = Turnable.EState.Off;
                        lamp.TurnLights(on, true);
                        if (on)
                        {
                            lamp.VisualUpdate(1f);
                        }
                    }
                }

                private void RestoreLamps()
                {
                    for (int i = 0; i < _lamps.Length; i++)
                    {
                        LampController lamp = _lamps[i];
                        if (lamp == null || lamp.IsDestroyed)
                        {
                            continue;
                        }

                        Turnable.EState state = _wasLive[i] ? _originalStates[i] : Turnable.EState.Off;
                        bool on = state == Turnable.EState.On
                            || state == Turnable.EState.TurningOn
                            || state == Turnable.EState.ConstantFlickering;
                        lamp.LampState = state == Turnable.EState.TurningOn ? Turnable.EState.On : state;
                        lamp.TurnLights(on, true);
                        if (on)
                        {
                            lamp.VisualUpdate(1f);
                        }
                    }
                }

                private void Stop()
                {
                    _sounding = false;
                    ReleaseSound();
                    RestoreLamps();
                }

                private void OnDestroy()
                {
                    ReleaseSound();
                }
            }
        }

        internal static class Wav
        {
            public static AudioClip ToClip(string name, byte[] data)
            {
                if (data.Length < 12 || ReadTag(data, 0) != "RIFF" || ReadTag(data, 8) != "WAVE")
                {
                    throw new InvalidDataException("Not a RIFF WAVE file");
                }

                int format = 0, channels = 0, rate = 0, bits = 0;
                int offset = 12;
                while (offset + 8 <= data.Length)
                {
                    string tag = ReadTag(data, offset);
                    int size = BitConverter.ToInt32(data, offset + 4);
                    int body = offset + 8;
                    if (tag == "fmt ")
                    {
                        format = BitConverter.ToInt16(data, body);
                        channels = BitConverter.ToInt16(data, body + 2);
                        rate = BitConverter.ToInt32(data, body + 4);
                        bits = BitConverter.ToInt16(data, body + 14);
                        if (format == -2 && size >= 26)
                        {
                            format = BitConverter.ToInt16(data, body + 24);
                        }
                    }
                    else if (tag == "data")
                    {
                        if (channels <= 0 || rate <= 0)
                        {
                            throw new InvalidDataException("Missing fmt chunk");
                        }
                        int length = Math.Min(size, data.Length - body);
                        float[] samples = Decode(data, body, length, format, bits);
                        AudioClip clip = AudioClip.Create(name, samples.Length / channels, channels, rate, false);
                        clip.SetData(samples, 0);
                        return clip;
                    }
                    offset = body + size + (size & 1);
                }
                throw new InvalidDataException("Missing data chunk");
            }

            private static float[] Decode(byte[] data, int start, int length, int format, int bits)
            {
                int bytes = bits / 8;
                int count = length / bytes;
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int p = start + i * bytes;
                    if (format == 3 && bits == 32)
                    {
                        samples[i] = BitConverter.ToSingle(data, p);
                    }
                    else if (bits == 8)
                    {
                        samples[i] = (data[p] - 128) / 128f;
                    }
                    else if (bits == 16)
                    {
                        samples[i] = BitConverter.ToInt16(data, p) / 32768f;
                    }
                    else if (bits == 24)
                    {
                        samples[i] = ((data[p] << 8 | data[p + 1] << 16 | data[p + 2] << 24) >> 8) / 8388608f;
                    }
                    else if (bits == 32)
                    {
                        samples[i] = BitConverter.ToInt32(data, p) / 2147483648f;
                    }
                    else
                    {
                        throw new InvalidDataException($"Unsupported WAV bit depth {bits}");
                    }
                }
                return samples;
            }

            private static string ReadTag(byte[] data, int offset)
            {
                return System.Text.Encoding.ASCII.GetString(data, offset, 4);
            }
        }
    }
}
