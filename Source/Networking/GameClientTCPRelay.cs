using StayInTarkov.Coop.Components;
using StayInTarkov.Coop.Matchmaker;
using StayInTarkov.Coop.NetworkPacket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StayInTarkov.Networking
{
    public class GameClientTCPRelay : MonoBehaviour, IGameClient
    {
        public BlockingCollection<byte[]> PooledBytesToSend { get; } = [];
        public float DownloadSpeedKbps { get; private set; } = 0;
        public float UploadSpeedKbps { get; private set; } = 0;
        public uint PacketLoss { get; private set; } = 0;
        public ushort Ping
        {
            get
            {
                return AkiBackendCommunication.Instance.Ping;
            }
        }

        public ActionPacketHandlerComponent PacketHandler { get; private set; }
        public SITGameServerClientDataProcessing DataProcessor { get; private set; }


        void Awake()
        {
            AkiBackendCommunication.Instance.WebSocketClose();
            AkiBackendCommunication.Instance.WebSocketCreate(SITMatchmaking.Profile);
        }

        void Start()
        {
            PacketHandler = this.GetOrAddComponent<ActionPacketHandlerComponent>();
            DataProcessor = this.GetOrAddComponent<SITGameServerClientDataProcessing>();
        }

        void Update()
        {
            if (PooledBytesToSend != null)
            {
                while (PooledBytesToSend.Any())
                {
                    while (PooledBytesToSend.TryTake(out var bytes))
                    {
                        AkiBackendCommunication.Instance.PostDownWebSocketImmediately(bytes);
                    }
                }
            }
        }

        public void SendData(byte[] data)
        {
            if (data == null)
                return;

            PooledBytesToSend.Add(data);
        }

        public void SendData<T>(ref T packet) where T : BasePacket
        {
            SendData(packet.Serialize());
        }

        void IGameClient.ResetStats()
        {
            DownloadSpeedKbps = Interlocked.Exchange(ref AkiBackendCommunication.Instance.BytesReceived, 0) / 1024f;
            UploadSpeedKbps = Interlocked.Exchange(ref AkiBackendCommunication.Instance.BytesSent, 0) / 1024f;
        }
    }
}
