using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static partial class PropPhysics
    {
        private const float SweepInterval = 0.25f;
        private const float SettleTime = 1f;
        private const float SettleSpeed = 0.15f;
        private const float SettleSpin = 0.5f;
        private const float MaxAwakeTime = 12f;
        private const float JitterSpeed = 1f;

        private class AwakeBody
        {
            public float Since;
            public float StillSince = -1f;
        }

        private static readonly Dictionary<Rigidbody, AwakeBody> AwakeBodies = new Dictionary<Rigidbody, AwakeBody>();
        private static readonly Dictionary<Rigidbody, bool> PropBodyCache = new Dictionary<Rigidbody, bool>();
        private static readonly List<Rigidbody> SweepBuffer = new List<Rigidbody>();

        private static bool IsPropBody(Rigidbody body)
        {
            if (!PropBodyCache.TryGetValue(body, out bool isProp))
            {
                if (PropBodyCache.Count > 20000)
                {
                    PropBodyCache.Clear();
                }
                isProp = body.GetComponentInParent<SmallPhysicsObject>() != null;
                PropBodyCache[body] = isProp;
            }
            return isProp;
        }

        private static void Freeze(Rigidbody body)
        {
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
                RemoveFromSimulation(body);
            }
            AwakeBodies.Remove(body);
        }

        private static void RemoveFromSimulation(Rigidbody body)
        {
            List<PhysicsExtensions.UpdateController.RigidbodyData> list = PhysicsExtensions.UpdateController._rigidbodies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].rigidbody == body)
                {
                    list.RemoveAt(i);
                }
            }
            PhysicsExtensions.UpdateController.CheckRigidbodiesDone();
        }

        private static void Sweep()
        {
            if (AwakeBodies.Count == 0)
            {
                return;
            }

            float now = Time.time;
            SweepBuffer.Clear();
            SweepBuffer.AddRange(AwakeBodies.Keys);
            foreach (Rigidbody body in SweepBuffer)
            {
                if (body == null || !body.gameObject.activeInHierarchy)
                {
                    AwakeBodies.Remove(body);
                    continue;
                }

                AwakeBody state = AwakeBodies[body];
                if (body.isKinematic)
                {
                    Freeze(body);
                    continue;
                }

                float speed = body.velocity.magnitude;
                bool still = body.IsSleeping() || (speed < SettleSpeed && body.angularVelocity.magnitude < SettleSpin);
                if (still)
                {
                    if (state.StillSince < 0f)
                    {
                        state.StillSince = now;
                    }
                    else if (now - state.StillSince >= SettleTime)
                    {
                        Freeze(body);
                        continue;
                    }
                }
                else
                {
                    state.StillSince = -1f;
                }

                if (now - state.Since > MaxAwakeTime && speed < JitterSpeed)
                {
                    Freeze(body);
                }
            }
        }

        private class BodySleeper : MonoBehaviour
        {
            private float _nextSweep;

            private void Update()
            {
                if (Time.time < _nextSweep)
                {
                    return;
                }
                _nextSweep = Time.time + SweepInterval;

                try
                {
                    Sweep();
                }
                catch (System.Exception e)
                {
                    Plugin.LogSource.LogError($"[PropPhysics] Sleep sweep failed: {e}");
                    AwakeBodies.Clear();
                }
            }
        }

        private class SupportRigidbodyPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(PhysicsExtensions.UpdateController), nameof(PhysicsExtensions.UpdateController.SupportRigidbody));
            }

            [PatchPrefix]
            private static bool Prefix(Rigidbody rigidbody)
            {
                if (rigidbody == null || !PhysicsExtensions.UpdateController._enabled || !IsPropBody(rigidbody))
                {
                    return true;
                }

                if (AwakeBodies.TryGetValue(rigidbody, out AwakeBody state))
                {
                    state.StillSince = -1f;
                    state.Since = Time.time;
                    return false;
                }

                AwakeBodies[rigidbody] = new AwakeBody { Since = Time.time };
                return true;
            }
        }
    }
}
