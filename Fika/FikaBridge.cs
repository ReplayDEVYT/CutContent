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

    public sealed class MessagePacket : INetSerializable
    {
        public byte Kind;
        public string Target = string.Empty;
        public string Source = string.Empty;
        public float[] Values = Array.Empty<float>();
        public string Extra = string.Empty;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Kind);
            writer.Put(Target ?? string.Empty);
            writer.Put(Source ?? string.Empty);
            float[] values = Values ?? Array.Empty<float>();
            writer.Put(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Put(values[i]);
            }
            writer.Put(Extra ?? string.Empty);
        }

        public void Deserialize(NetDataReader reader)
        {
            Kind = reader.GetByte();
            Target = reader.GetString();
            Source = reader.GetString();
            int count = reader.GetInt();
            Values = new float[Math.Max(0, Math.Min(count, 256))];
            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = reader.GetFloat();
            }
            Extra = reader.GetString();
        }
    }

    public static class FikaBridge
    {
        private static Action<byte, string, string, float[], string> _onMessage;

        public static void SetMessageHandler(Action<byte, string, string, float[], string> handler)
        {
            _onMessage = handler;
        }

        public static bool Connected()
        {
            return _manager != null;
        }

        public static bool IsServer()
        {
            return FikaBackendUtils.IsServer;
        }

        public static bool IsHeadless()
        {
            return FikaBackendUtils.IsHeadless;
        }

        public static bool IsExtracted(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || !global::Fika.Core.Main.Components.CoopHandler.TryGetCoopHandler(out var handler) || handler.ExtractedPlayers == null)
            {
                return false;
            }
            foreach (var pair in handler.Players)
            {
                if (pair.Value != null && pair.Value.ProfileId == profileId)
                {
                    return handler.ExtractedPlayers.Contains(pair.Value.NetId);
                }
            }
            return false;
        }

        public static void Send(byte kind, string target, string source, float[] values, string extra, bool reliable)
        {
            if (_manager == null)
            {
                return;
            }
            var packet = new MessagePacket { Kind = kind, Target = target, Source = source, Values = values ?? Array.Empty<float>(), Extra = extra };
            _manager.SendData(ref packet, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced, true);
        }

        private static void OnMessage(MessagePacket packet)
        {
            try
            {
                _onMessage?.Invoke(packet.Kind, packet.Target, packet.Source, packet.Values, packet.Extra);
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"[Fika] Could not handle a Re-Classic message: {ex}");
            }
        }

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
                _manager.RegisterPacket<MessagePacket>(OnMessage);
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

        public static void BroadcastSettings()
        {
            if (_manager == null || !FikaBackendUtils.IsServer || _snapshot == null)
            {
                return;
            }
            var packet = new SettingsPacket { Data = _snapshot() };
            _manager.SendData(ref packet, DeliveryMethod.ReliableOrdered);
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
