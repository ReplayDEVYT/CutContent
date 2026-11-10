using BepInEx.Bootstrap;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Sync
{
    internal static class FikaSync
    {
        public const string FikaGuid = "com.fika.core";
        private const string BridgeResource = "ReClassic.Fika.dll";
        private const string BridgeType = "mizmii.ReClassic.Fika.FikaBridge";

        public static bool Present => Chainloader.PluginInfos.ContainsKey(FikaGuid);

        public static void Init()
        {
            if (!Present)
            {
                return;
            }
            try
            {
                Assembly bridge = LoadBridge();
                MethodInfo init = bridge.GetType(BridgeType, true).GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                Func<string> snapshot = SettingsSync.Snapshot;
                Action<string> applyHost = SettingsSync.ApplyHost;
                Action lockCurrent = SettingsSync.LockCurrent;
                Action unlock = SettingsSync.Unlock;
                Action<bool> setAlarmsFromHost = value => Patches.WorldInteractions.AlarmsFromHost = value;
                Action<Vector3, int> playRemoteAlarm = Patches.WorldInteractions.PlayRemoteAlarm;
                Action<string> logError = message => Plugin.LogSource.LogError(message);
                var sendAlarm = (Action<Vector3, int>)init.Invoke(null, new object[] { snapshot, applyHost, lockCurrent, unlock, setAlarmsFromHost, playRemoteAlarm, logError });
                Patches.WorldInteractions.AlarmStarted += sendAlarm;
                Type bridgeType = bridge.GetType(BridgeType, true);
                var send = (Action<byte, string, string, float[], string, bool>)Delegate.CreateDelegate(typeof(Action<byte, string, string, float[], string, bool>), bridgeType.GetMethod("Send"));
                SettingsSync.Broadcast = (Action)Delegate.CreateDelegate(typeof(Action), bridgeType.GetMethod("BroadcastSettings"));
                var isServer = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), bridgeType.GetMethod("IsServer"));
                var connected = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), bridgeType.GetMethod("Connected"));
                var headless = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), bridgeType.GetMethod("IsHeadless"));
                Action<byte, string, string, float[], string> receive = Consciousness.Coop.Receive;
                bridgeType.GetMethod("SetMessageHandler").Invoke(null, new object[] { receive });
                var extracted = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), bridgeType.GetMethod("IsExtracted"));
                Consciousness.Coop.Attach(send, isServer, connected, headless, extracted);
                Plugin.LogSource.LogInfo("[Fika] Fika found, car alarms and settings will follow the match host.");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Fika] Could not hook into Fika: {e}");
            }
        }

        private static Assembly LoadBridge()
        {
            using (Stream stream = typeof(FikaSync).Assembly.GetManifestResourceStream(BridgeResource))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("The Fika bridge is missing from the plugin.");
                }
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return Assembly.Load(memory.ToArray());
                }
            }
        }
    }
}
