using live_danmu;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace bilibili_live_danmu
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            string[] arguments = Environment.GetCommandLineArgs().AsMemory(1).ToArray();
            UInt32 liveId = 0;
            bool hasArgumentErr = true;
            if (arguments.Length != 0)
            {
                try
                {
                    liveId = UInt32.Parse(arguments[0]);
                    hasArgumentErr = false;
                }
                catch (Exception) { }
            }
            if (hasArgumentErr)
            {
                Console.WriteLine("请输入BILIBILI直播号");
                while (true)
                {
                    if (UInt32.TryParse(Console.ReadLine(), out liveId))
                    {
                        break;
                    }
                    Console.WriteLine("输入格式错误");
                }
            }
            BilibiliLiveDanMu danmu = new BilibiliLiveDanMu(liveId);
            await danmu.init();
            await danmu.start();
            danmu.onDanmuCallback = (msg) =>
            {
                Console.Write(msg.time);
                Console.Write(" ");
                Console.Write(msg.userName);
                Console.Write(": ");
                Console.WriteLine(msg.content);
            };
            danmu.onSendGiftCallback = (msg) =>
            {
                Console.Write(msg.time);
                Console.Write(" ");
                Console.Write(msg.userName);
                Console.Write(" 打赏 ");
                Console.Write(msg.giftName);
                Console.Write(" x ");
                Console.Write(msg.giftNum);
                Console.Write(" price=");
                Console.WriteLine(msg.giftPrice);
            };
            Console.ReadLine();
        }
    }
}
