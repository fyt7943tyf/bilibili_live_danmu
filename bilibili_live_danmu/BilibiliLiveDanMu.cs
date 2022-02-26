using System.Text;
using System.Net.WebSockets;
using System.IO.Compression;
using System;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace live_danmu
{
    public class BilibiliLiveDanMuMsg
    {
        public BilibiliLiveDanMuMsg(string content, string userName, DateTime time, byte[] rawPacket)
        {
            this.content = content;
            this.userName = userName;
            this.time = time;
            this.rawPacket = rawPacket;
        }

        public string content;
        public string userName;
        public DateTime time;
        public byte[] rawPacket;
    }

    public class BilibiliLiveSendGiftMsg
    {
        public BilibiliLiveSendGiftMsg(UInt32 giftId, string giftName, UInt32 giftNum, UInt32 giftPrice, string userName, DateTime time, byte[] rawPacket)
        {
            this.giftId = giftId;
            this.giftName = giftName;
            this.giftNum = giftNum;
            this.giftPrice = giftPrice;
            this.userName = userName;
            this.time = time;
            this.rawPacket = rawPacket;
        }

        public UInt32 giftId;
        public string giftName;
        public UInt32 giftNum;
        public UInt32 giftPrice;
        public string userName;
        public DateTime time;
        public byte[] rawPacket;
    }
    public class BilibiliLiveDanMu
    {

        private UInt32 live_id;
        private ClientWebSocket clientWebScoket;
        private ArraySegment<byte> wsLoginData;
        private byte[] heartBeatMsg = new byte[] { 0x00, 0x00, 0x00, 0x1f, 0x00, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x5b, 0x6f, 0x62, 0x6a, 0x65, 0x63, 0x74, 0x20, 0x4f, 0x62, 0x6a, 0x65, 0x63, 0x74, 0x5d, 0x20 };
        private Timer heartBeatTimer;
        private Mutex mutex = new Mutex();
        public delegate void onDanMu(BilibiliLiveDanMuMsg msg);
        public delegate void onSendGift(BilibiliLiveSendGiftMsg msg);
        public onDanMu onDanmuCallback;
        public onSendGift onSendGiftCallback;

        public BilibiliLiveDanMu(UInt32 live_id)
        {
            this.live_id = live_id;
        }

        public async Task init()
        {
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync("https://api.live.bilibili.com/room/v1/Room/room_init?id=" + live_id.ToString());
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            JObject data = new JObject();
            data["roomid"] = (UInt32)JObject.Parse(responseBody)["data"]["room_id"];
            data["uin"] = (Int64)(1e14 + 2e14 * new System.Random().NextDouble());
            data["protover"] = 1;
            var dataBytes = Encoding.ASCII.GetBytes(data.ToString());
            var wsLoginData = new byte[16 + dataBytes.Length];
            GetBytes(wsLoginData, 0, (UInt32)(dataBytes.Length + 16));
            wsLoginData[4] = 0x00;
            wsLoginData[5] = 0x10;
            wsLoginData[6] = 0x00;
            wsLoginData[7] = 0X01;
            wsLoginData[8] = 0x00;
            wsLoginData[9] = 0x00;
            wsLoginData[10] = 0x00;
            wsLoginData[11] = 0X07;
            wsLoginData[12] = 0x00;
            wsLoginData[13] = 0x00;
            wsLoginData[14] = 0x00;
            wsLoginData[15] = 0X01;
            dataBytes.CopyTo(wsLoginData, 16);
            this.wsLoginData = new ArraySegment<byte>(wsLoginData);
        }

        public async Task start()
        {
            if (clientWebScoket != null)
            {
                return;
            }
            clientWebScoket = new ClientWebSocket();
            await clientWebScoket.ConnectAsync(new Uri("wss://broadcastlv.chat.bilibili.com/sub"), CancellationToken.None);
            await clientWebScoket.SendAsync(wsLoginData, WebSocketMessageType.Binary, true, CancellationToken.None);
            heartBeatTimer = new Timer(async delegate
            {
                await heartBeat();
            }, null, 0, 60000);
            try
            {
                decodeTask();
            } catch (Exception) { 
            }
        }

        public void stop()
        {
            clientWebScoket?.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client requested", CancellationToken.None);
            clientWebScoket = null;
            heartBeatTimer?.Dispose();
            heartBeatTimer = null;
        }

        private async Task heartBeat()
        {
            await sendData(heartBeatMsg);
        }

        private async void decodeTask()
        {
            ArraySegment<byte> bytesReceived = new ArraySegment<byte>(new byte[1024]);
            while (true)
            {
                List<byte> resp = new List<byte>();
                while (true)
                {
                    WebSocketReceiveResult res = await clientWebScoket.ReceiveAsync(bytesReceived, CancellationToken.None);
                    byte[] data = new byte[res.Count];
                    Array.Copy(bytesReceived.Array, data, res.Count);
                    resp.AddRange(data);
                    if (res.EndOfMessage)
                    {
                        break;
                     }
                }
                decodeMsg(resp.ToArray());
            }
        }

        private void decodeMsg(byte[] data)
        {
            UInt32 beginIndex = 0;
            List<UInt32> ops = new List<UInt32>();
            List<byte[]> msgList = new List<byte[]>();
            List<byte[]> encodeMsgList = new List<byte[]>();
            while (true)
            {
                UInt32 packetLen, op, seq;
                UInt16 headerLen, ver;
                try
                {
                    packetLen = ReadU32(data, beginIndex);
                    headerLen = ReadU16(data, beginIndex + 4);
                    ver = ReadU16(data, beginIndex + 6);
                    op = ReadU32(data, beginIndex + 8);
                    seq = ReadU32(data, beginIndex + 12);
                } 
                catch (Exception)
                {
                    break;
                }
                if (data.Length - beginIndex < packetLen)
                {
                    break;
                }
                if (ver == 0 || ver == 1)
                {
                    ops.Add(op);
                    byte[] msgData = new byte[packetLen - 16];
                    Array.Copy(data, beginIndex + 16, msgData, 0, packetLen - 16);
                    msgList.Add(msgData);
                } 
                else if (ver == 2)
                {
                    byte[] msgData = new byte[packetLen - 16];
                    Array.Copy(data, beginIndex + 16, msgData, 0, packetLen - 16);
                    encodeMsgList.Add(msgData);
                }
                beginIndex += packetLen;
            }
            for (int i = 0; i < encodeMsgList.Count; i++)
            {
                byte[] msg;
                try
                {
                    msg = MicrosoftDecompress(encodeMsgList[i]);
                } catch (Exception)
                {
                    continue;
                }
                beginIndex = 0;
                while (true)
                {
                    UInt32 packetLen, op, seq;
                    UInt16 headerLen, ver;
                    try
                    {
                        packetLen = ReadU32(msg, beginIndex);
                        headerLen = ReadU16(msg, beginIndex + 4);
                        ver = ReadU16(msg, beginIndex + 6);
                        op = ReadU32(msg, beginIndex + 8);
                        seq = ReadU32(msg, beginIndex + 12);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                    if (msg.Length - beginIndex < packetLen)
                    {
                        break;
                    }
                    ops.Add(op);
                    byte[] msgData = new byte[packetLen - 16];
                    Array.Copy(msg, beginIndex + 16, msgData, 0, packetLen - 16);
                    msgList.Add(msgData);
                    beginIndex += packetLen;
                }
            }
            for(int i = 0; i < msgList.Count; i++)
            {
                if (ops[i] != 5)
                {
                    continue;
                }
                try
                {
                    JObject jsonNode = JObject.Parse(Encoding.UTF8.GetString(msgList[i]));
                    JToken jCmd = jsonNode["cmd"];
                    string msgType = jCmd == null ? "UNKNOW" : jCmd.ToString();
                    switch (msgType)
                    {
                        case "DANMU_MSG":
                            process_danmu(jsonNode, msgList[i]);
                            break;
                        case "SEND_GIFT":
                            process_send_gift(jsonNode, msgList[i]);
                            break;
                    }
                } catch (Exception)
                {
                    continue;
                }
                
            }
        }

        private void process_danmu(JObject jsonNode, byte[] rawMsg)
        {
            JToken jInfo = jsonNode["info"];
            BilibiliLiveDanMuMsg msg = new BilibiliLiveDanMuMsg((string)jInfo[1], (string)jInfo[2][1], UnixTimeStampToDateTime((UInt64)jInfo[0][4]), rawMsg);
            onDanmuCallback?.Invoke(msg);
        }

        private void process_send_gift(JObject jsonNode, byte[] rawMsg)
        {
            JToken jData = jsonNode["data"];
            BilibiliLiveSendGiftMsg msg = new BilibiliLiveSendGiftMsg((UInt32)jData["giftId"], (string)jData["giftName"], (UInt32)jData["num"], (UInt32)jData["price"], (string)jData["uname"], UnixTimeStampToDateTime(UInt64.Parse((string)jData["tid"]) / 1000000), rawMsg);
            onSendGiftCallback?.Invoke(msg);
        }

        private async Task sendData(byte[] data)
        {
            if (clientWebScoket.State != WebSocketState.Open)
            {
                mutex.WaitOne();
                if (clientWebScoket.State != WebSocketState.Open)
                {
                    await clientWebScoket.ConnectAsync(new Uri("wss://broadcastlv11.chat.bilibili.com/sub"), CancellationToken.None);
                    await clientWebScoket.SendAsync(wsLoginData, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                mutex.ReleaseMutex();
            }
            await clientWebScoket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        private void GetBytes(byte[] bytes, UInt32 index, UInt32 v)
        {
            bytes[index] = (byte)(v >> 24);
            bytes[index + 1] = (byte)(v >> 16);
            bytes[index + 2] = (byte)(v >> 8);
            bytes[index + 3] = (byte)v;
        }

        private UInt32 ReadU32(byte[] bytes, UInt32 index)
        {
            return (UInt32)bytes[index] << 24 | (UInt32)bytes[index + 1] << 16 | (UInt32)bytes[index + 2] << 8 | bytes[index + 3];
        } 

        private UInt16 ReadU16(byte[] bytes, UInt32 index)
        {
            return (ushort)((UInt16)bytes[index] << 8 | bytes[index + 1]);
        }

        public static byte[] MicrosoftDecompress(byte[] data)
        {
            byte[] compress_data = new byte[data.Length - 6];
            Array.Copy(data, 2, compress_data, 0, data.Length - 6);
            MemoryStream compressed = new MemoryStream(compress_data);
            MemoryStream decompressed = new MemoryStream();
            DeflateStream deflateStream = new DeflateStream(compressed, CompressionMode.Decompress); // 注意： 這裡第一個引數同樣是填寫壓縮的資料，但是這次是作為輸入的資料
            deflateStream.CopyTo(decompressed);
            byte[] result = decompressed.ToArray();
            return result;
        }

        public static DateTime UnixTimeStampToDateTime(UInt64 mill)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddMilliseconds(mill).ToLocalTime();
            return dateTime;
        }
    }
}
