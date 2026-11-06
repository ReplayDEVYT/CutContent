using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using System;
using UnityEngine;

namespace mizmii.ReClassic.Fika
{
    public sealed class SettingsPacket : INetSerializable
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

    public sealed class CarAlarmPacket : INetSerializable
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

    public static class FikaBridge
    {
        private static IFikaNetworkManager _manager;
        private static Func<string> _snapshot;
        private static Action<string> _applyHost;
        private static Action _lockCurrent;
        private static Action _unlock;
        private static Action<bool> _setAlarmsFromHost;
        private static Action<Vector3, int> _playRemoteAlarm;
        private static Action<string> _logError;

        public static Action<Vector3, int> Init(Func<string> snapshot, Action<string> applyHost, Action lockCurrent, Action unlock, Action<bool> setAlarmsFromHost, Action<Vector3, int> playRemoteAlarm, Action<string> logError)
        {
            _snapshot = snapshot;
            _applyHost = applyHost;
            _lockCurrent = lockCurrent;
            _unlock = unlock;
            _setAlarmsFromHost = setAlarmsFromHost;
            _playRemoteAlarm = playRemoteAlarm;
            _logError = logError;
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnDestroyed);
            FikaEventDispatcher.SubscribeEvent<FikaGameEndedEvent>(OnGameEnded);
            FikaEventDispatcher.SubscribeEvent<PeerConnectedEvent>(OnPeerConnected);
            return OnAlarmStarted;
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
                _logError($"[Fika] Could not register Re-Classic packets: {ex}");
            }
            if (FikaBackendUtils.IsServer)
            {
                _setAlarmsFromHost(false);
                _lockCurrent();
            }
            else
            {
                _setAlarmsFromHost(true);
            }
        }

        private static void OnDestroyed(FikaNetworkManagerDestroyedEvent e)
        {
            _manager = null;
            _setAlarmsFromHost(false);
            _unlock();
        }

        private static void OnGameEnded(FikaGameEndedEvent e)
        {
            _setAlarmsFromHost(false);
            _unlock();
        }

        private static void OnPeerConnected(PeerConnectedEvent e)
        {
            if (!FikaBackendUtils.IsServer || e.NetworkManager == null || e.Peer == null)
            {
                return;
            }
            var packet = new SettingsPacket { Data = _snapshot() };
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
            _applyHost(packet.Data);
        }

        private static void OnCarAlarm(CarAlarmPacket packet)
        {
            if (FikaBackendUtils.IsServer)
            {
                return;
            }
            _playRemoteAlarm(packet.Position, packet.Clip);
        }
    }
}
