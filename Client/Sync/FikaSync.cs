using BepInEx.Bootstrap;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mizmii.ReClassic.Sync
{
    internal static class FikaSync
    {
        public const string FikaGuid = "com.fika.core";

        public static bool Present => Chainloader.PluginInfos.ContainsKey(FikaGuid);

        public static void Init()
        {
            if (!Present)
            {
                return;
            }
            try
            {
                Hook();
                Plugin.LogSource.LogInfo("[Fika] Fika found, car alarms and settings will follow the match host.");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Fika] Could not hook into Fika: {e}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Hook()
        {
            FikaBridge.Init();
        }
    }

    internal sealed class SettingsPacket : INetSerializable
    {
        public string Data = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Data);
        }

        public void Deserialize(NetDataReader reader)
        {
            Data = reader.GetString();
        }
    }

    internal sealed class CarAlarmPacket : INetSerializable
    {
        public Vector3 Position;
        public int Clip;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Position.x);
            writer.Put(Position.y);
            writer.Put(Position.z);
            writer.Put(Clip);
        }

        public void Deserialize(NetDataReader reader)
        {
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Clip = reader.GetInt();
        }
    }

    internal static class FikaBridge
    {
        private static IFikaNetworkManager _manager;

        public static void Init()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnDestroyed);
            FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
            FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
            Patches.WorldInteractions.AlarmStarted += OnAlarmStarted;
        }

        private static void OnCreated(FikaNetworkManagerCreatedEvent e)
        {
            _manager = e.Manager;
            try
            {
                _manager.RegisterPacket<SettingsPacket>(OnSettings);
                _manager.RegisterPacket<CarAlarmPacket>(OnCarAlarm);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[Fika] Could not register Re-Classic packets: {ex}");
            }
            if (FikaBackendUtils.IsServer)
            {
                Patches.WorldInteractions.AlarmsFromHost = false;
                SettingsSync.LockCurrent();
            }
            else
            {
                Patches.WorldInteractions.AlarmsFromHost = true;
            }
        }

        private static void OnDestroyed(FikaNetworkManagerDestroyedEvent e)
        {
            _manager = null;
            Patches.WorldInteractions.AlarmsFromHost = false;
            SettingsSync.Unlock();
        }

        private static void OnGameEnded(FikaGameEndedEvent e)
        {
            Patches.WorldInteractions.AlarmsFromHost = false;
            SettingsSync.Unlock();
        }

        private static void OnPeerConnected(PeerConnectedEvent e)
        {
            if (!FikaBackendUtils.IsServer || e.NetworkManager == null || e.Peer == null)
            {
                return;
            }
            var packet = new SettingsPacket { Data = SettingsSync.Snapshot() };
            e.NetworkManager.SendDataToPeer(ref packet, DeliveryMethod.ReliableOrdered, e.Peer);
        }

        private static void OnAlarmStarted(Vector3 position, int clip)
        {
            if (_manager == null || !FikaBackendUtils.IsServer)
            {
                return;
            }
            var packet = new CarAlarmPacket { Position = position, Clip = clip };
            _manager.SendData(ref packet, DeliveryMethod.ReliableOrdered);
        }

        private static void OnSettings(SettingsPacket packet)
        {
            if (FikaBackendUtils.IsServer)
            {
                return;
            }
            SettingsSync.ApplyHost(packet.Data);
        }

        private static void OnCarAlarm(CarAlarmPacket packet)
        {
            if (FikaBackendUtils.IsServer)
            {
                return;
            }
            Patches.WorldInteractions.PlayRemoteAlarm(packet.Position, packet.Clip);
        }
    }
}
