using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace mizmii.ReClassic.Patches
{
    internal static class DoorPhysics
    {
        private const float MaxScanAngle = 170f;
        private const float ScanStep = 2f;
        private const float RestSpeed = 4f;
        private const float LatchSpeed = 40f;

        private static ConfigEntry<bool> _swing;
        private static ConfigEntry<bool> _blast;
        private static ConfigEntry<bool> _breach;
        private static ConfigEntry<bool> _breachLocked;
        private static ConfigEntry<float> _offHingesChance;
        private static ConfigEntry<float> _blastStrength;
        private static ConfigEntry<float> _swingStrength;
        private static ConfigEntry<float> _breachDistance;

        private static readonly AccessTools.FieldRef<Door, BrokenDoor> Broken = AccessTools.FieldRefAccess<Door, BrokenDoor>("_broken");
        private static readonly FieldInfo InteractionField = AccessTools.Field(typeof(WorldInteractiveObject), "_interaction");
        private static readonly ConditionalWeakTable<Door, Hinge> Hinges = new ConditionalWeakTable<Door, Hinge>();
        private static readonly Dictionary<int, bool> DoorColliderCache = new Dictionary<int, bool>();
        private static readonly Collider[] OverlapBuffer = new Collider[32];
        private static readonly Collider[] BlastBuffer = new Collider[1024];
        private static int _wallMask;
        private static int _blastMask;
        private static bool _internal;
        private static Runner _runner;

        private class Hinge
        {
            public Door Door;
            public bool Valid;
            public bool Scanned;
            public float Direction;
            public float OpenS;
            public float MaxS;
            public Bounds Box;
            public Bounds Slab;
            public float S;
            public float Omega;
            public bool Active;
            public float LastHit;
            public int Thin;
            public int Wide;
            public int Tall;
        }

        public static void Enable(ConfigFile config)
        {
            const string section = "Environment - Doors";
            _swing = ConfigHelper.Feature(config, FeatureCategory.Environment, "Doors", "Physical Door Swing", true, "Like pre-alpha Tarkov, doors keep swinging on their hinges after they open until they hit a wall, and knock against it.", section, "Physical Door Swing");
            _blast = ConfigHelper.Feature(config, FeatureCategory.Environment, "Doors", "Door Blast Physics", true, "Like the alpha gameplay trailer, explosions physically push nearby open doors on their hinges, away from the blast.", section, "Door Blast Physics");
            _breach = config.Bind(section, "Blast Breaching", true, "A grenade going off right at a closed door either blows it wide open or tears it off its hinges.");
            _breachLocked = config.Bind(section, "Blast Breaches Locked Doors", false, "Blast Breaching also works on locked doors. Doors that trigger quests or extracts are never breached.");
            _offHingesChance = config.Bind(section, "Off Hinges Chance", 0.5f, new ConfigDescription("Chance that a breached door is torn off its hinges instead of blown open. A torn off door can no longer be used.", new AcceptableValueRange<float>(0f, 1f)));
            _blastStrength = config.Bind(section, "Blast Strength", 1f, ConfigHelper.Advanced("Scales how hard explosions push doors.", new AcceptableValueRange<float>(0.1f, 3f)));
            _swingStrength = config.Bind(section, "Swing Strength", 1f, ConfigHelper.Advanced("Scales how hard doors keep swinging after being opened or kicked.", new AcceptableValueRange<float>(0.1f, 3f)));
            _breachDistance = config.Bind(section, "Breach Distance", 1f, ConfigHelper.Advanced("How close (metres) a grenade has to explode to a closed door to breach it.", new AcceptableValueRange<float>(0.3f, 3f)));

            WorldInteractions.Explosion += OnExplosion;
            new DoorStatePatch().Enable();
            new OpenCoroutinePatch().Enable();
        }

        private static void EnsureMasks()
        {
            if (_wallMask != 0)
            {
                return;
            }
            int low = LayerMask.NameToLayer("LowPolyCollider");
            int high = LayerMask.NameToLayer("HighPolyCollider");
            _wallMask = low >= 0 ? 1 << low : Physics.DefaultRaycastLayers;
            _blastMask = (low >= 0 ? 1 << low : 0) | (high >= 0 ? 1 << high : 0);
            if (_blastMask == 0)
            {
                _blastMask = Physics.DefaultRaycastLayers;
            }
        }

        private static Runner GetRunner()
        {
            if (_runner == null)
            {
                var go = new GameObject("ReClassic Door Physics");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<Runner>();
            }
            return _runner;
        }

        private static bool Usable(Door door)
        {
            return door != null && door.GetType() == typeof(Door) && door.Operatable && !door.NoInteractionsAllowed && door.transform.parent != null && door.isActiveAndEnabled && !door._keepRotation;
        }

        private static bool InProgress(Door door)
        {
            object state = InteractionField?.GetValue(door);
            return state is WorldInteractiveObject.InteractionState interaction && interaction.IsInProgress;
        }

        private static Vector3 Axis(Door door)
        {
            return WorldInteractiveObject.GetRotationAxis(door.DoorAxis, door.transform.parent);
        }

        private static Hinge GetHinge(Door door)
        {
            Hinge hinge = Hinges.GetValue(door, d => new Hinge { Door = d });
            if (!hinge.Scanned)
            {
                hinge.Scanned = true;
                Setup(hinge);
            }
            return hinge;
        }

        private static void Setup(Hinge hinge)
        {
            Door door = hinge.Door;
            float span = Mathf.DeltaAngle(door.CloseAngle, door.OpenAngle);
            if (Mathf.Abs(span) < 5f || !LocalBox(door, out Bounds box))
            {
                return;
            }
            hinge.Direction = Mathf.Sign(span);
            hinge.OpenS = Mathf.Abs(span);
            hinge.Box = box;
            hinge.Slab = Slab(box);
            Vector3 size = box.size;
            hinge.Thin = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);
            hinge.Tall = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            hinge.Wide = 3 - hinge.Thin - hinge.Tall;
            if (hinge.Wide < 0 || hinge.Wide > 2 || hinge.Wide == hinge.Thin || hinge.Wide == hinge.Tall)
            {
                hinge.Wide = (hinge.Thin + 1) % 3 == hinge.Tall ? (hinge.Thin + 2) % 3 : (hinge.Thin + 1) % 3;
            }
            hinge.MaxS = ScanStop(hinge);
            hinge.Valid = true;
        }

        private static bool LocalBox(Door door, out Bounds result)
        {
            result = default;
            bool any = false;
            Matrix4x4 toDoor = door.transform.worldToLocalMatrix;
            List<Collider> colliders = door.CollisionColliders;
            IEnumerable<Collider> source = colliders != null && colliders.Count > 0 ? colliders : (IEnumerable<Collider>)door.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in source)
            {
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }
                Bounds local;
                if (collider is BoxCollider boxCollider)
                {
                    local = new Bounds(boxCollider.center, boxCollider.size);
                }
                else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
                {
                    local = meshCollider.sharedMesh.bounds;
                }
                else
                {
                    continue;
                }
                Matrix4x4 matrix = toDoor * collider.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = matrix.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        result = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }
            return any && result.size.sqrMagnitude > 0.1f;
        }

        private static Bounds Slab(Bounds box)
        {
            Vector3 size = box.size;
            int thin = 0;
            int tall = 0;
            for (int i = 1; i < 3; i++)
            {
                if (size[i] < size[thin])
                {
                    thin = i;
                }
                if (size[i] > size[tall])
                {
                    tall = i;
                }
            }
            int wide = 3 - thin - tall;
            if (wide == thin || wide == tall || wide < 0 || wide > 2)
            {
                wide = thin == 0 ? (tall == 1 ? 2 : 1) : (tall == 0 ? (thin == 1 ? 2 : 1) : 0);
            }
            Vector3 min = box.min;
            Vector3 max = box.max;
            float trim = Mathf.Min(0.12f, size[wide] * 0.2f);
            if (Mathf.Abs(min[wide]) < Mathf.Abs(max[wide]))
            {
                min[wide] += trim;
            }
            else
            {
                max[wide] -= trim;
            }
            float tallTrim = size[tall] * 0.08f;
            min[tall] += tallTrim;
            max[tall] -= tallTrim;
            float center = (min[thin] + max[thin]) * 0.5f;
            float half = Mathf.Max(size[thin] * 0.25f, 0.005f);
            min[thin] = center - half;
            max[thin] = center + half;
            var slab = new Bounds();
            slab.SetMinMax(min, max);
            return slab;
        }

        private static Quaternion RotationAt(Door door, float angle)
        {
            return Quaternion.AngleAxis(angle, Axis(door)) * door.transform.parent.rotation;
        }

        private static float AngleOf(Hinge hinge, float s)
        {
            return hinge.Door.CloseAngle + hinge.Direction * s;
        }

        private static float SOf(Hinge hinge, float angle)
        {
            return Mathf.DeltaAngle(hinge.Door.CloseAngle, angle) * hinge.Direction;
        }

        private static bool Blocked(Hinge hinge, float s)
        {
            Door door = hinge.Door;
            Quaternion rotation = RotationAt(door, AngleOf(hinge, s));
            Vector3 scale = door.transform.lossyScale;
            Vector3 center = door.transform.position + rotation * Vector3.Scale(hinge.Slab.center, scale);
            Vector3 half = Vector3.Scale(hinge.Slab.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = Physics.OverlapBoxNonAlloc(center, half, OverlapBuffer, rotation, _wallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider != null && !IsDoorCollider(collider))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsDoorCollider(Collider collider)
        {
            int id = collider.GetInstanceID();
            if (!DoorColliderCache.TryGetValue(id, out bool isDoor))
            {
                if (DoorColliderCache.Count > 20000)
                {
                    DoorColliderCache.Clear();
                }
                isDoor = collider.GetComponentInParent<WorldInteractiveObject>() != null;
                DoorColliderCache[id] = isDoor;
            }
            return isDoor;
        }

        private static float ScanStop(Hinge hinge)
        {
            EnsureMasks();
            if (Blocked(hinge, hinge.OpenS))
            {
                return hinge.OpenS;
            }
            for (float s = hinge.OpenS + ScanStep; s <= MaxScanAngle; s += ScanStep)
            {
                if (Blocked(hinge, s))
                {
                    float low = s - ScanStep;
                    float high = s;
                    for (int i = 0; i < 4; i++)
                    {
                        float mid = (low + high) * 0.5f;
                        if (Blocked(hinge, mid))
                        {
                            high = mid;
                        }
                        else
                        {
                            low = mid;
                        }
                    }
                    return low;
                }
            }
            return MaxScanAngle;
        }

        private static void Push(Door door, float omega)
        {
            Hinge hinge = GetHinge(door);
            if (!hinge.Valid)
            {
                return;
            }
            float s = Mathf.Clamp(SOf(hinge, door.CurrentAngle), 0f, hinge.MaxS);
            if (!hinge.Active)
            {
                hinge.S = s;
                hinge.Omega = 0f;
            }
            hinge.Omega += omega * hinge.Direction;
            if (Mathf.Abs(hinge.Omega) < RestSpeed)
            {
                return;
            }
            if (!hinge.Active)
            {
                hinge.Active = true;
                GetRunner().Add(hinge);
            }
        }

        private static bool Step(Hinge hinge, float dt)
        {
            Door door = hinge.Door;
            if (door == null || !door.isActiveAndEnabled || door.DoorState != EDoorState.Open || InProgress(door))
            {
                return false;
            }
            const float friction = 30f;
            const float damping = 0.6f;
            float omega = hinge.Omega;
            float decel = friction * dt;
            omega = Mathf.Abs(omega) <= decel ? 0f : omega - Mathf.Sign(omega) * decel;
            omega *= Mathf.Exp(-damping * dt);
            float s = hinge.S + omega * dt;

            if (s >= hinge.MaxS)
            {
                s = hinge.MaxS;
                if (omega > 0f)
                {
                    Knock(hinge, omega);
                    omega = -omega * 0.3f;
                }
            }
            else if (s <= 0f)
            {
                s = 0f;
                if (omega < 0f)
                {
                    if (-omega < LatchSpeed)
                    {
                        hinge.S = 0f;
                        hinge.Omega = 0f;
                        Latch(door);
                        return false;
                    }
                    door.PlayShut();
                    omega = -omega * 0.2f;
                }
            }

            hinge.S = s;
            hinge.Omega = omega;
            door.CurrentAngle = AngleOf(hinge, s);
            return Mathf.Abs(omega) >= RestSpeed;
        }

        private static void Latch(Door door)
        {
            door.CurrentAngle = door.CloseAngle;
            door.PlayShut();
            _internal = true;
            try
            {
                door.DoorState = EDoorState.Shut;
            }
            finally
            {
                _internal = false;
            }
        }

        private static void Knock(Hinge hinge, float omega)
        {
            if (omega < 20f || Time.time - hinge.LastHit < 0.15f)
            {
                return;
            }
            hinge.LastHit = Time.time;
            Door door = hinge.Door;
            AudioClip clip = door.HitClip;
            if (clip == null && door.ShutSound != null && door.ShutSound.Length > 0)
            {
                clip = door.ShutSound[UnityEngine.Random.Range(0, door.ShutSound.Length)];
            }
            if (clip == null)
            {
                return;
            }
            float volume = Mathf.Clamp(omega / 250f, 0.15f, 1f);
            Vector3 edge = door.transform.TransformPoint(hinge.Box.center);
            try
            {
                door.PlaySoundAtPoint(edge, clip, BetterAudio.AudioSourceGroupType.Collisions, 40, volume);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Doors] Could not play the door knock: {e.Message}");
            }
        }

        private static void OnExplosion(IExplosiveItem grenade, Vector3 position)
        {
            if (!_blast.Value && !_breach.Value)
            {
                return;
            }
            if (grenade.GetStrength <= 0f || grenade.MaxExplosionDistance <= 0f)
            {
                return;
            }
            EnsureMasks();
            float strength = Mathf.Clamp(grenade.GetStrength / 100f, 0.5f, 1.5f);
            float radius = Mathf.Clamp(grenade.MaxExplosionDistance, 4f, 12f);
            int count = Physics.OverlapSphereNonAlloc(position, radius, BlastBuffer, Physics.AllLayers, QueryTriggerInteraction.Collide);
            var doors = new HashSet<Door>();
            for (int i = 0; i < count; i++)
            {
                Collider collider = BlastBuffer[i];
                Door door = collider != null ? collider.GetComponentInParent<Door>() : null;
                if (Usable(door))
                {
                    doors.Add(door);
                }
            }
            foreach (Door door in doors)
            {
                try
                {
                    Blast(door, position, radius, strength);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Doors] Blast failed on door {door.Id}: {e}");
                }
            }
        }

        private static void Blast(Door door, Vector3 position, float radius, float strength)
        {
            Hinge hinge = GetHinge(door);
            if (!hinge.Valid)
            {
                return;
            }
            Transform t = door.transform;
            Vector3 local = t.InverseTransformPoint(position);
            Vector3 closest = t.TransformPoint(new Vector3(
                Mathf.Clamp(local.x, hinge.Box.min.x, hinge.Box.max.x),
                Mathf.Clamp(local.y, hinge.Box.min.y, hinge.Box.max.y),
                Mathf.Clamp(local.z, hinge.Box.min.z, hinge.Box.max.z)));
            float distance = Vector3.Distance(position, closest);
            Vector3 center = t.TransformPoint(hinge.Box.center);
            EDoorState state = door.DoorState;

            bool breachable = state == EDoorState.Shut || state == EDoorState.Open || (state == EDoorState.Locked && _breachLocked.Value);
            if (_breach.Value && distance <= _breachDistance.Value && breachable && !InProgress(door) && (door.TriggersMap == null || door.TriggersMap.Length == 0))
            {
                if (Breach(door, hinge, position, center, strength))
                {
                    return;
                }
                state = door.DoorState;
            }
            if (!_blast.Value || state != EDoorState.Open || InProgress(door) || distance > radius)
            {
                return;
            }

            float exposure = 1f;
            Vector3 toCenter = center - position;
            if (Physics.Raycast(position, toCenter.normalized, out RaycastHit hit, toCenter.magnitude, _blastMask, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(t))
            {
                exposure = 0.15f;
            }
            float falloff = Mathf.Pow(1f - Mathf.Clamp01(distance / radius), 1.5f);
            Vector3 axis = Axis(door);
            Vector3 lever = Vector3.ProjectOnPlane(center - t.position, axis);
            if (lever.sqrMagnitude < 0.01f)
            {
                return;
            }
            Vector3 force = Vector3.ProjectOnPlane(center - position, axis).normalized;
            float torque = Vector3.Dot(Vector3.Cross(lever.normalized, force), axis);
            float omega = Mathf.Clamp(900f * strength * falloff * exposure * torque * _blastStrength.Value, -1400f, 1400f);
            Push(door, omega);
        }

        private static int Seed(Door door, Vector3 position)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in door.Id ?? "")
                {
                    hash = hash * 31 + c;
                }
                hash = hash * 31 + Mathf.RoundToInt(position.x * 4f);
                hash = hash * 31 + Mathf.RoundToInt(position.y * 4f);
                hash = hash * 31 + Mathf.RoundToInt(position.z * 4f);
                return hash;
            }
        }

        private static bool Breach(Door door, Hinge hinge, Vector3 position, Vector3 center, float strength)
        {
            var random = new System.Random(Seed(door, position));
            bool offHinges = random.NextDouble() < _offHingesChance.Value;
            if (door.DoorState == EDoorState.Open && !offHinges)
            {
                return false;
            }
            if (door.BreachSound != null)
            {
                door.PlaySoundAtPoint(center, door.BreachSound, BetterAudio.AudioSourceGroupType.InteractiveObjects, door._breachSoundRolloff, 1f);
            }
            BrokenDoor broken = Broken(door);
            if (broken != null)
            {
                try
                {
                    broken.Breach(position);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Doors] Broken door effect failed: {e.Message}");
                }
            }
            door.IsBroken = true;
            _internal = true;
            try
            {
                door.DoorState = EDoorState.Open;
            }
            finally
            {
                _internal = false;
            }

            if (offHinges)
            {
                TearOff(door, hinge, position, center, strength, random);
                return true;
            }
            Push(door, hinge.Direction * Mathf.Clamp(1100f * strength * _blastStrength.Value, 300f, 1600f));
            return true;
        }

        private static void TearOff(Door door, Hinge hinge, Vector3 position, Vector3 center, float strength, System.Random random)
        {
            Transform t = door.transform;
            var debris = new GameObject("ReClassic Door Debris " + door.Id);
            debris.transform.SetPositionAndRotation(t.position, t.rotation);
            debris.transform.localScale = t.lossyScale;
            Vector3 inverseScale = new Vector3(1f / NonZero(t.lossyScale.x), 1f / NonZero(t.lossyScale.y), 1f / NonZero(t.lossyScale.z));

            foreach (Renderer renderer in VisibleRenderers(door))
            {
                if (renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        var part = new GameObject(renderer.name);
                        part.transform.SetParent(debris.transform, false);
                        part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                        part.transform.localScale = Vector3.Scale(renderer.transform.lossyScale, inverseScale);
                        part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        MeshRenderer copy = part.AddComponent<MeshRenderer>();
                        copy.sharedMaterials = meshRenderer.sharedMaterials;
                        copy.shadowCastingMode = meshRenderer.shadowCastingMode;
                        copy.receiveShadows = meshRenderer.receiveShadows;
                        copy.lightProbeUsage = LightProbeUsage.BlendProbes;
                    }
                }
                renderer.enabled = false;
            }
            foreach (Collider collider in t.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            int layer = LayerMask.NameToLayer("Loot");
            debris.layer = layer >= 0 ? layer : 0;
            BoxCollider box = debris.AddComponent<BoxCollider>();
            box.center = hinge.Box.center;
            box.size = Vector3.Max(hinge.Box.size * 0.98f, new Vector3(0.04f, 0.04f, 0.04f));
            Rigidbody body = debris.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.drag = 0.05f;
            body.angularDrag = 0.3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Vector3 away = center - position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -t.forward;
            float speed = (5f + (float)random.NextDouble() * 3f) * strength * _blastStrength.Value;
            body.velocity = away * speed + Vector3.up * (1f + (float)random.NextDouble() * 1.5f);
            body.angularVelocity = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 6f;

            door.Operatable = false;
            door.NoInteractionsAllowed = true;
        }

        private static float NonZero(float value)
        {
            return Mathf.Abs(value) < 0.0001f ? 1f : value;
        }

        private static IEnumerable<Renderer> VisibleRenderers(Door door)
        {
            var hidden = new HashSet<Renderer>();
            foreach (LODGroup group in door.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                {
                    foreach (Renderer renderer in lods[i].renderers)
                    {
                        if (renderer != null)
                        {
                            hidden.Add(renderer);
                        }
                    }
                }
            }
            var result = new List<Renderer>();
            foreach (Renderer renderer in door.GetComponentsInChildren<Renderer>(true))
            {
                if (hidden.Contains(renderer))
                {
                    renderer.enabled = false;
                    continue;
                }
                result.Add(renderer);
            }
            return result;
        }

        private static WorldInteractiveObject.InteractionState Interaction(Door door)
        {
            return InteractionField?.GetValue(door) as WorldInteractiveObject.InteractionState;
        }

        private class Watch
        {
            public Door Door;
            public float LastS = -1f;
            public float Omega;
        }

        private static bool WatchStep(Watch watch, float dt)
        {
            Door door = watch.Door;
            if (!_swing.Value || door == null || !door.isActiveAndEnabled)
            {
                return false;
            }
            WorldInteractiveObject.InteractionState interaction = Interaction(door);
            if (interaction == null || interaction.Break || (interaction.ResultState != EDoorState.Open && interaction.ResultState != EDoorState.Breaching))
            {
                return false;
            }
            if (interaction.ProgressTime >= interaction.Duration)
            {
                return false;
            }
            Hinge hinge = GetHinge(door);
            if (!hinge.Valid || hinge.Active)
            {
                return false;
            }
            float s = SOf(hinge, door.CurrentAngle);
            if (watch.LastS < 0f || s < watch.LastS - 0.01f)
            {
                watch.LastS = Mathf.Max(s, 0f);
                return true;
            }
            float omega = (s - watch.LastS) / dt;
            watch.Omega = watch.Omega <= 0f ? omega : Mathf.Lerp(watch.Omega, omega, 0.5f);
            watch.LastS = s;
            bool kicked = interaction.ResultState == EDoorState.Breaching;
            float release = hinge.OpenS * (kicked ? 0.3f : 0.45f);
            if (s < release || watch.Omega <= 1f)
            {
                return true;
            }
            interaction.Break = true;
            _internal = true;
            try
            {
                door.DoorState = EDoorState.Open;
            }
            finally
            {
                _internal = false;
            }
            float boost = kicked ? 1.4f : 1.15f;
            float floor = kicked ? 220f : 90f;
            hinge.S = Mathf.Clamp(s, 0f, hinge.MaxS);
            hinge.Omega = 0f;
            Push(door, hinge.Direction * Mathf.Max(watch.Omega * boost, floor) * _swingStrength.Value);
            return false;
        }

        private const float PushRadius = 0.42f;
        private const float PushRange = 2.6f;
        private static readonly List<Door> AllDoors = new List<Door>();
        private static GameWorld _doorsWorld;
        private static float _nextDoorScan;
        private static readonly Dictionary<int, Vector3> LastPositions = new Dictionary<int, Vector3>();

        private static void RefreshDoors()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world != _doorsWorld || (world != null && Time.time > _nextDoorScan))
            {
                _doorsWorld = world;
                _nextDoorScan = Time.time + 30f;
                AllDoors.Clear();
                LastPositions.Clear();
                if (world != null)
                {
                    foreach (Door door in Object.FindObjectsOfType<Door>())
                    {
                        if (door.GetType() == typeof(Door))
                        {
                            AllDoors.Add(door);
                        }
                    }
                }
            }
        }

        private static void PlayerPush(float dt)
        {
            RefreshDoors();
            if (_doorsWorld == null || AllDoors.Count == 0)
            {
                return;
            }
            foreach (Player player in PlayerKind.Humans())
            {
                if (player == null || !player.HealthController.IsAlive)
                {
                    continue;
                }
                int id = player.GetInstanceID();
                Vector3 position = player.Position;
                Vector3 velocity = LastPositions.TryGetValue(id, out Vector3 last) ? (position - last) / dt : Vector3.zero;
                LastPositions[id] = position;
                velocity.y = 0f;
                if (velocity.sqrMagnitude < 0.04f || velocity.sqrMagnitude > 100f)
                {
                    continue;
                }
                foreach (Door door in AllDoors)
                {
                    if (door == null || door.DoorState != EDoorState.Open || !door.isActiveAndEnabled || !door.Operatable)
                    {
                        continue;
                    }
                    if ((door.transform.position - position).sqrMagnitude > PushRange * PushRange)
                    {
                        continue;
                    }
                    PushByPlayer(door, position, velocity, dt);
                }
            }
        }

        private static void PushByPlayer(Door door, Vector3 feet, Vector3 velocity, float dt)
        {
            if (InProgress(door))
            {
                return;
            }
            Hinge hinge = GetHinge(door);
            if (!hinge.Valid)
            {
                return;
            }
            Transform t = door.transform;
            Vector3 scale = t.lossyScale;
            float thinScale = Mathf.Max(Mathf.Abs(scale[hinge.Thin]), 0.0001f);
            float wideScale = Mathf.Max(Mathf.Abs(scale[hinge.Wide]), 0.0001f);
            Bounds box = hinge.Box;
            Vector3 point = feet + Vector3.up * 1f;
            Vector3 local = t.InverseTransformPoint(point);
            if (local[hinge.Tall] < box.min[hinge.Tall] - 0.5f || local[hinge.Tall] > box.max[hinge.Tall] + 0.5f)
            {
                return;
            }
            float radius = PushRadius / thinScale;
            float wideRadius = PushRadius * 0.5f / wideScale;
            if (local[hinge.Wide] < box.min[hinge.Wide] - wideRadius || local[hinge.Wide] > box.max[hinge.Wide] + wideRadius)
            {
                return;
            }
            float offset = local[hinge.Thin] - box.center[hinge.Thin];
            float depth = box.extents[hinge.Thin] + radius - Mathf.Abs(offset);
            if (depth <= 0f)
            {
                return;
            }
            Vector3 thinAxis = Vector3.zero;
            thinAxis[hinge.Thin] = offset < 0f ? -1f : 1f;
            Vector3 normal = t.TransformDirection(thinAxis).normalized;
            float approach = -Vector3.Dot(velocity, normal);
            if (approach < 0.25f)
            {
                return;
            }
            Vector3 axis = Axis(door);
            Vector3 contact = point - normal * (Mathf.Abs(offset) * thinScale);
            Vector3 lever = Vector3.ProjectOnPlane(contact - t.position, axis);
            float leverLength = Mathf.Max(lever.magnitude, 0.25f);
            float tangent = Vector3.Dot(Vector3.Cross(axis, lever.normalized), -normal);
            if (Mathf.Abs(tangent) < 0.2f)
            {
                return;
            }
            float worldDepth = depth * thinScale;
            float degrees = Mathf.Min(worldDepth, approach * dt * 1.3f) / leverLength * Mathf.Rad2Deg;
            float delta = Mathf.Sign(tangent) * degrees * hinge.Direction;
            float s = Mathf.Clamp(SOf(hinge, door.CurrentAngle), 0f, hinge.MaxS);
            float next = Mathf.Clamp(s + delta, 0f, hinge.MaxS);
            if (Mathf.Abs(next - s) < 0.001f)
            {
                return;
            }
            float omega = (next - s) / dt;
            hinge.S = next;
            door.CurrentAngle = AngleOf(hinge, next);
            if (next <= 0.5f && delta < 0f)
            {
                hinge.Omega = 0f;
                hinge.Active = false;
                Latch(door);
                return;
            }
            float carried = omega * 0.6f;
            if (hinge.Active)
            {
                if (Mathf.Abs(carried) > Mathf.Abs(hinge.Omega) || Mathf.Sign(carried) != Mathf.Sign(hinge.Omega))
                {
                    hinge.Omega = carried;
                }
            }
            else
            {
                Push(door, carried * hinge.Direction);
            }
        }

        private class Runner : MonoBehaviour
        {
            private readonly List<Hinge> _active = new List<Hinge>();
            private readonly List<Watch> _watches = new List<Watch>();

            public void Watch(Door door)
            {
                foreach (Watch watch in _watches)
                {
                    if (watch.Door == door)
                    {
                        watch.LastS = -1f;
                        watch.Omega = 0f;
                        return;
                    }
                }
                _watches.Add(new Watch { Door = door });
            }

            public void Add(Hinge hinge)
            {
                if (!_active.Contains(hinge))
                {
                    _active.Add(hinge);
                }
            }

            private void Update()
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                if (dt <= 0f)
                {
                    return;
                }
                for (int i = _watches.Count - 1; i >= 0; i--)
                {
                    bool keep;
                    try
                    {
                        keep = WatchStep(_watches[i], dt);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogError($"[Doors] Door opening hand-off failed: {e}");
                        keep = false;
                    }
                    if (!keep)
                    {
                        _watches.RemoveAt(i);
                    }
                }
                if (_swing.Value || _blast.Value)
                {
                    try
                    {
                        PlayerPush(dt);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogError($"[Doors] Player door push failed: {e}");
                    }
                }
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    Hinge hinge = _active[i];
                    bool keep;
                    try
                    {
                        keep = Step(hinge, dt);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogError($"[Doors] Door simulation failed: {e}");
                        keep = false;
                    }
                    if (!keep)
                    {
                        hinge.Active = false;
                        hinge.Omega = 0f;
                        _active.RemoveAt(i);
                    }
                }
            }
        }

        private class OpenCoroutinePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(Door), nameof(Door.SmoothDoorOpenCoroutine));
            }

            [PatchPrefix]
            private static void Prefix(Door __instance, EDoorState state)
            {
                if (!_swing.Value || state != EDoorState.Open || !Usable(__instance))
                {
                    return;
                }
                if (Hinges.TryGetValue(__instance, out Hinge existing) && existing.Active)
                {
                    existing.Active = false;
                }
                GetRunner().Watch(__instance);
            }
        }

        private class DoorStatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.PropertySetter(typeof(Door), nameof(Door.DoorState));
            }

            [PatchPrefix]
            private static void Prefix(Door __instance, out EDoorState __state)
            {
                __state = __instance.DoorState;
            }

            [PatchPostfix]
            private static void Postfix(Door __instance, EDoorState __state, EDoorState value)
            {
                if (_internal || !_swing.Value || value != EDoorState.Open || __state != EDoorState.Interacting || !Usable(__instance))
                {
                    return;
                }
                try
                {
                    object state = InteractionField?.GetValue(__instance);
                    bool kicked = state is WorldInteractiveObject.InteractionState interaction && interaction.ResultState == EDoorState.Breaching;
                    GetRunner().StartCoroutine(HandOff(__instance, kicked ? 260f : 110f));
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Doors] Swing hand-off failed: {e}");
                }
            }

            private static System.Collections.IEnumerator HandOff(Door door, float speed)
            {
                yield return null;
                if (door != null && door.DoorState == EDoorState.Open && !InProgress(door))
                {
                    Hinge hinge = GetHinge(door);
                    if (hinge.Valid)
                    {
                        Push(door, hinge.Direction * speed * _swingStrength.Value);
                    }
                }
            }
        }
    }
}
