using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using StayInTarkov.Coop.Components.CoopGameComponents;
using StayInTarkov.Coop.Matchmaker;
using StayInTarkov.Coop.NetworkPacket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx.Logging;
using Comfort.Common;
using Mono.Cecil;
using StayInTarkov.Coop.SITGameModes;
using StayInTarkov.Coop.NetworkPacket.Player;
using UnityEngine;
using System.Collections.Concurrent;

namespace StayInTarkov.Networking
{
    public class SITGameServerClientDataProcessing : MonoBehaviour
    {
        public const string PACKET_TAG_METHOD = "m";
        public const string PACKET_TAG_SERVERID = "serverId";
        public const string PACKET_TAG_DATA = "data";

        public static event Action<ushort> OnLatencyUpdated;

        public static ManualLogSource Logger { get; }

        private ConcurrentQueue<(byte[], string)> QueuedBytes = new();

        static SITGameServerClientDataProcessing()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource($"{nameof(SITGameServerClientDataProcessing)}");
        }

        void Update()
        {
            while (QueuedBytes.TryDequeue(out var result))
                ProcessPacketBytes(result.Item1, result.Item2);
        }

        public void AddPacketToQueue(byte[] data, string sData)
        {
            QueuedBytes.Enqueue((data, sData));
        }

        private void ProcessPacketBytes(byte[] data, string sData)
        {
            try
            {
                if (data == null)
                {
                    Logger.LogError($"{nameof(ProcessPacketBytes)}. Data is Null");
                    return;
                }

                if (data.Length == 0)
                {
                    Logger.LogError($"{nameof(ProcessPacketBytes)}. Data is Empty");
                    return;
                }

                Dictionary<string, object> packet = null;
                ISITPacket sitPacket = null;

                // Is a dictionary from Spt-Aki
                if (!string.IsNullOrEmpty(sData) && sData.StartsWith("{"))
                {
                    // Use StreamReader & JsonTextReader to improve memory / cpu usage
                    using (var streamReader = new StreamReader(new MemoryStream(data)))
                    {
                        using (var reader = new JsonTextReader(streamReader))
                        {
                            var serializer = new JsonSerializer();
                            packet = serializer.Deserialize<Dictionary<string, object>>(reader);
                        }
                    }
                }
                // Is a RAW SIT Serialized packet
                else
                {
                    ProcessSITPacket(data, ref packet, out sitPacket);
                }

                //var coopGameComponent = SITGameComponent.GetCoopGameComponent();

                //if (coopGameComponent == null)
                //{
                //    Logger.LogError($"{nameof(ProcessPacketBytes)}. coopGameComponent is Null");
                //    return;
                //}

                if (packet == null)
                {
                    //Logger.LogError($"{nameof(ProcessPacketBytes)}. Packet is Null");
                    return;
                }

                // If this is a pong packet, resolve and create a smooth ping
                if (packet.ContainsKey("pong"))
                {
                    var pongRaw = long.Parse(packet["pong"].ToString());
                    var dtPong = new DateTime(pongRaw);
                    var latencyMs = (DateTime.UtcNow - dtPong).TotalMilliseconds / 2;
                    OnLatencyUpdated((ushort)latencyMs);
                    return;
                }

                // Receiving a Player Extracted packet. Process into ExtractedPlayers List
                if (packet.ContainsKey("Extracted"))
                {
                    if (Singleton<ISITGame>.Instantiated && !Singleton<ISITGame>.Instance.ExtractedPlayers.Contains(packet["profileId"].ToString()))
                    {
                        Singleton<ISITGame>.Instance.ExtractedPlayers.Add(packet["profileId"].ToString());
                    }
                    return;
                }

                // If this is an endSession packet, end the session for the clients
                //if (packet.ContainsKey("endSession") && SITMatchmaking.IsClient)
                //{
                //    Logger.LogDebug("Received EndSession from Server. Ending Game.");
                //    if (coopGameComponent.LocalGameInstance == null)
                //        return;

                //    coopGameComponent.ServerHasStopped = true;
                //    return;
                //}

                //// -------------------------------------------------------
                //// Add to the Coop Game Component Action Packets
                //if (coopGameComponent == null || coopGameComponent.ActionPackets == null || coopGameComponent.ActionPacketHandler == null)
                //    return;

                //if (packet.ContainsKey(PACKET_TAG_METHOD)
                //    && packet[PACKET_TAG_METHOD].ToString() == "Move")
                //    coopGameComponent.ActionPacketHandler.ActionPacketsMovement.TryAdd(packet);
                //else if (packet.ContainsKey(PACKET_TAG_METHOD)
                //    && packet[PACKET_TAG_METHOD].ToString() == "ApplyDamageInfo")
                //{
                //    coopGameComponent.ActionPacketHandler.ActionPacketsDamage.TryAdd(packet);
                //}
                //else
                //{

                if (!Singleton<ISITGame>.Instantiated)
                    return;

                if (sitPacket != null)
                    Singleton<ISITGame>.Instance.GameClient.PacketHandler.ActionSITPackets.Add(sitPacket);
                else
                {
#if DEBUG
                    Logger.LogDebug($">> DEV TODO <<");
                    Logger.LogDebug($">> Convert the following packet to binary <<");
                    Logger.LogDebug($"{packet.ToJson()}");
#endif 
                    Singleton<ISITGame>.Instance.GameClient.PacketHandler.ActionPackets.TryAdd(packet);
                }
                //}

            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        public void ProcessSITPacket(byte[] data, ref Dictionary<string, object> dictObject, out ISITPacket packet)
        {
            packet = null;

            // If the data is empty. Return;
            if (data == null || data.Length == 0)
            {
                Logger.LogError($"{nameof(ProcessSITPacket)}. {nameof(data)} is null");
            }

            var stringData = Encoding.UTF8.GetString(data);
            // If the string Data isn't a SIT serialized string. Return;
            if (!stringData.StartsWith("SIT"))
            {
                //Logger.LogError($"{nameof(ProcessSITPacket)}. {stringData} does not start with SIT");
                return;
            }

            var serverId = stringData.Substring(3, 24);
            // If the serverId is not the same as the one we are connected to. Return;
            if (serverId != SITMatchmaking.GetGroupId())
            {
                Logger.LogError($"{nameof(ProcessSITPacket)}. {serverId} does not equal {SITMatchmaking.GetGroupId()}");
                return;
            }

            var bp = new BasePacket("");
            using (var br = new BinaryReader(new MemoryStream(data)))
                bp.ReadHeader(br);

            dictObject = new Dictionary<string, object>();
            dictObject[PACKET_TAG_DATA] = data;
            dictObject[PACKET_TAG_METHOD] = bp.Method;

            if (!dictObject.ContainsKey("profileId"))
            {
                try
                {
                    var bpp = new BasePlayerPacket("", dictObject[PACKET_TAG_METHOD].ToString());
                    bpp.Deserialize(data);
                    dictObject.Add("profileId", new string(bpp.ProfileId.ToCharArray()));
                    bpp.Dispose();
                    bpp = null;
                }
                catch { }
            }

            packet = DeserializeIntoPacket(data, packet, bp);
        }

        private ISITPacket DeserializeIntoPacket(byte[] data, ISITPacket packet, BasePacket bp)
        {
            var sitPacketType =
                            StayInTarkovHelperConstants
                            .SITTypes
                            .Union(ReflectionHelpers.EftTypes)
                            .FirstOrDefault(x => x.Name == bp.Method);
            if (sitPacketType != null)
            {
                //Logger.LogInfo($"{sitPacketType} found");
                packet = (ISITPacket)Activator.CreateInstance(sitPacketType);
                packet = packet.Deserialize(data);
            }
            else
            {
#if DEBUG
                Logger.LogDebug($"{nameof(DeserializeIntoPacket)}:{bp.Method} could not find a matching ISITPacket type");
#endif
            }

            return packet;
        }

        
    }
}
