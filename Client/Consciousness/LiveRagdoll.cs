using EFT;
using EFT.AssetsManager;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal sealed class LiveRagdoll
    {
        private readonly Player _player;
        private readonly PlayerPoolObject _pool;
        private readonly List<Rigidbody> _created = new List<Rigidbody>();
        private readonly List<Joint> _createdJoints = new List<Joint>();
        private readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();
        private readonly List<Animator> _animators = new List<Animator>();
        private bool _active;

        public static readonly HashSet<Player> Frozen = new HashSet<Player>();

        private readonly List<Rigidbody> _supported = new List<Rigidbody>();

        public LiveRagdoll(Player player)
        {
            _player = player;
            _pool = player.GetComponent<PlayerPoolObject>();
        }

        public bool Active => _active;

        public Transform Pelvis => _player.PlayerBones != null ? _player.PlayerBones.Pelvis.Original : null;

        public bool Start(Vector3 velocity)
        {
            if (_active || _pool == null || _pool.RigidbodySpawners == null || Pelvis == null)
            {
                return false;
            }
            try
            {
                _active = true;
                Frozen.Add(_player);
                _player.CharacterController.isEnabled = false;
                _player.enabled = false;
                foreach (Animator animator in _player.GetComponentsInChildren<Animator>())
                {
                    if (animator.enabled)
                    {
                        animator.enabled = false;
                        _animators.Add(animator);
                    }
                }

                int deadbody = LayerMask.NameToLayer("Deadbody");
                int shells = LayerMask.NameToLayer("Shells");
                foreach (Transform child in Pelvis.GetComponentsInChildren<Transform>(true))
                {
                    if (child.gameObject.layer != shells)
                    {
                        _layers[child.gameObject] = child.gameObject.layer;
                        child.gameObject.layer = deadbody;
                    }
                }

                foreach (CharacterJointSpawner spawner in _pool.JointSpawners ?? Array.Empty<CharacterJointSpawner>())
                {
                    if (spawner.GetComponent<Joint>() == null)
                    {
                        Joint joint = spawner.Create();
                        if (joint != null)
                        {
                            _createdJoints.Add(joint);
                        }
                    }
                }
                foreach (RigidbodySpawner spawner in _pool.RigidbodySpawners)
                {
                    bool existed = spawner.Rigidbody != null;
                    Rigidbody body = spawner.Create();
                    if (!existed)
                    {
                        _created.Add(body);
                    }
                    body.isKinematic = false;
                    body.detectCollisions = true;
                    body.WakeUp();
                    body.velocity = Vector3.ClampMagnitude(velocity, 2f) + Vector3.down * 0.5f;
                    body.angularVelocity = Vector3.zero;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    PhysicsExtensions.UpdateController.SupportRigidbody(body, 1f);
                    _supported.Add(body);
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not ragdoll the player: {e}");
                Stop();
                return false;
            }
        }

        public void ReleaseForDeath()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            Frozen.Remove(_player);
            _supported.Clear();
            _created.Clear();
            _createdJoints.Clear();
            _layers.Clear();
            _animators.Clear();
            _player.enabled = true;
        }

        public void Push(Vector3 impulse)
        {
            if (!_active || _pool?.RigidbodySpawners == null)
            {
                return;
            }
            foreach (RigidbodySpawner spawner in _pool.RigidbodySpawners)
            {
                if (spawner.Rigidbody != null)
                {
                    spawner.Rigidbody.AddForce(impulse, ForceMode.VelocityChange);
                }
            }
        }

        public void Stop()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            foreach (Rigidbody body in _supported)
            {
                if (body != null)
                {
                    PhysicsExtensions.UpdateController.UnsupportRigidbody(body);
                }
            }
            _supported.Clear();
            if (_pool?.RigidbodySpawners != null)
            {
                foreach (RigidbodySpawner spawner in _pool.RigidbodySpawners)
                {
                    if (spawner.Rigidbody == null)
                    {
                        continue;
                    }
                    spawner.Rigidbody.velocity = Vector3.zero;
                    spawner.Rigidbody.angularVelocity = Vector3.zero;
                    spawner.Rigidbody.isKinematic = true;
                    if (_created.Contains(spawner.Rigidbody))
                    {
                        spawner.Remove();
                    }
                }
            }
            foreach (Joint joint in _createdJoints)
            {
                if (joint != null)
                {
                    UnityEngine.Object.Destroy(joint);
                }
            }
            _createdJoints.Clear();
            _created.Clear();
            foreach (KeyValuePair<GameObject, int> pair in _layers)
            {
                if (pair.Key != null)
                {
                    pair.Key.layer = pair.Value;
                }
            }
            _layers.Clear();
            foreach (Animator animator in _animators)
            {
                if (animator != null)
                {
                    animator.enabled = true;
                }
            }
            _animators.Clear();
            Frozen.Remove(_player);
            _player.enabled = true;
            _player.CharacterController.isEnabled = true;
        }

        public PoseSnapshot Capture()
        {
            return Pelvis == null ? null : new PoseSnapshot(Pelvis);
        }

        public void GroundPose(out Vector3 position, out float yaw)
        {
            Transform pelvis = Pelvis;
            position = _player.Position;
            yaw = _player.Rotation.x;
            if (pelvis == null)
            {
                return;
            }
            position = pelvis.position;
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, LayersMaskController.HighPolyWithTerrainMask))
            {
                position = hit.point;
            }
            Transform head = _player.PlayerBones.Head?.Original;
            Vector3 along = head != null ? head.position - pelvis.position : pelvis.up;
            along.y = 0f;
            if (along.sqrMagnitude > 0.001f)
            {
                yaw = Quaternion.LookRotation(along.normalized).eulerAngles.y;
            }
        }

        public void Place(Vector3 position, float yaw)
        {
            _player.Teleport(position);
            _player.Rotation = new Vector2(yaw, 0f);
        }
    }

    internal sealed class PoseSnapshot
    {
        public readonly Transform[] Bones;
        public readonly Quaternion[] Rotations;
        public readonly Vector3 RootPosition;

        public PoseSnapshot(Transform root)
        {
            Bones = root.GetComponentsInChildren<Transform>(true);
            Rotations = Bones.Select(b => b.rotation).ToArray();
            RootPosition = root.position;
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class PoseBlend : MonoBehaviour
    {
        private PoseSnapshot _from;
        private float _duration;
        private float _time;
        private Quaternion[] _target;
        private Action _done;

        public static PoseBlend Run(GameObject host, PoseSnapshot from, float duration, Action done)
        {
            PoseBlend blend = host.AddComponent<PoseBlend>();
            blend._from = from;
            blend._duration = Mathf.Max(0.05f, duration);
            blend._target = new Quaternion[from.Bones.Length];
            blend._done = done;
            return blend;
        }

        private void LateUpdate()
        {
            _time += Time.deltaTime;
            float t = Mathf.Clamp01(_time / _duration);
            float weight = t * t * (3f - 2f * t);
            Transform[] bones = _from.Bones;
            for (int i = 0; i < bones.Length; i++)
            {
                _target[i] = bones[i] != null ? bones[i].rotation : Quaternion.identity;
            }
            if (bones.Length > 0 && bones[0] != null)
            {
                Vector3 animated = bones[0].position;
                bones[0].position = Vector3.Lerp(_from.RootPosition, animated, weight);
            }
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    bones[i].rotation = Quaternion.Slerp(_from.Rotations[i], _target[i], weight);
                }
            }
            if (t >= 1f)
            {
                Action done = _done;
                _done = null;
                Destroy(this);
                done?.Invoke();
            }
        }
    }
}
