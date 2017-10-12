// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Network;

namespace UnitTest
{
    class Program
    {
        static LatencySimulator vnet;

        static void udp_output(byte[] data, int size, object ud)
        {
            int peer = (int)ud;
            vnet.Send(peer, data, size);
        }

        // 测试用例
        static void KCPTest(int mode)
        {
            // 创建模拟网络：丢包率10%，Rtt 60ms~125ms
            vnet = new LatencySimulator(10, 60, 125);

            // 创建两个端点的 kcp对象，第一个参数 conv是会话编号，同一个会话需要相同
            // 最后一个是 user参数，用来传递标识
            var kcp1 = new KCP(0x11223344, 1);
            var kcp2 = new KCP(0x11223344, 2);

            // 设置kcp的下层输出，这里为 udp_output，模拟udp网络输出函数
            kcp1.SetOutput(udp_output);
            kcp2.SetOutput(udp_output);

            UInt32 current = Utils.iclock();
            UInt32 slap = current + 20;
            UInt32 index = 0;
            UInt32 next = 0;
            Int64 sumrtt = 0;
            int count = 0;
            int maxrtt = 0;

            // 配置窗口大小：平均延迟200ms，每20ms发送一个包，
            // 而考虑到丢包重发，设置最大收发窗口为128
            kcp1.WndSize(128, 128);
            kcp2.WndSize(128, 128);

            if (mode == 0) // 默认模式
            {
                kcp1.NoDelay(0, 10, 0, 0);
                kcp2.NoDelay(0, 10, 0, 0);
            }
            else if (mode == 1) // 普通模式，关闭流控等
            {
                kcp1.NoDelay(0, 10, 0, 1);
                kcp2.NoDelay(0, 10, 0, 1);
            }
            else // 启动快速模式
            {
                // 第1个参数 nodelay-启用以后若干常规加速将启动
                // 第2个参数 interval为内部处理时钟，默认设置为 10ms
                // 第3个参数 resend为快速重传指标，设置为2
                // 第4个参数 为是否禁用常规流控，这里禁止
                kcp1.NoDelay(1, 10, 2, 1);
                kcp2.NoDelay(1, 10, 2, 1);
                kcp1.SetMinRTO(10);
                kcp1.SetFastResend(1);
            }

            var buffer = new byte[2000];
            int hr = 0;
            UInt32 ts1 = Utils.iclock();

            while (true)
            {
                Thread.Sleep(1);
                current = Utils.iclock();
                kcp1.Update(current);
                kcp2.Update(current);

                // 每隔 20ms，kcp1发送数据
                for (; current >= slap; slap += 20)
                {
                    KCP.ikcp_encode32u(buffer, 0, index++);
                    KCP.ikcp_encode32u(buffer, 4, current);

                    // 发送上层协议包
                    kcp1.Send(buffer, 0, 8);
                }

                // 处理虚拟网络：检测是否有udp包从p1->p2
                while (true)
                {
                    hr = vnet.Recv(1, buffer, 2000);
                    if (hr < 0)
                        break;

                    // 如果 p2收到udp，则作为下层协议输入到kcp2
                    hr = kcp2.Input(buffer, 0, hr);
                    Debug.Assert(hr >= 0);
                }

                // 处理虚拟网络：检测是否有udp包从p2->p1
                while (true)
                {
                    hr = vnet.Recv(0, buffer, 2000);
                    if (hr < 0)
                        break;

                    // 如果 p1收到udp，则作为下层协议输入到kcp1
                    hr = kcp1.Input(buffer, 0, hr);
                    Debug.Assert(hr >= 0);
                }

                // kcp2接收到任何包都返回回去
                while (true)
                {
                    hr = kcp2.Recv(buffer, 0, 10);
                    if (hr < 0)
                        break;

                    // 如果收到包就回射
                    hr = kcp2.Send(buffer, 0, hr);
                    Debug.Assert(hr >= 0);
                }

                // kcp1收到kcp2的回射数据
                while (true)
                {
                    hr = kcp1.Recv(buffer, 0, 10);
                    if (hr < 0) // 没有收到包就退出
                        break;

                    int offset = 0;
                    UInt32 sn = KCP.ikcp_decode32u(buffer, ref offset);
                    UInt32 ts = KCP.ikcp_decode32u(buffer, ref offset);
                    UInt32 rtt = current - ts;

                    if (sn != next)
                    {
                        // 如果收到的包不连续
                        Console.WriteLine(String.Format("ERROR sn {0}<->{1}", count, next));
                        return;
                    }
                    next++;
                    sumrtt += rtt;
                    count++;
                    if (rtt > maxrtt)
                        maxrtt = (int)rtt;

                    Console.WriteLine(String.Format("[RECV] mode={0} sn={1} rtt={2}", mode, sn, rtt));
                }
                if (next > 1000)
                    break;
            }
            ts1 = Utils.iclock() - ts1;
            var names = new string[3] { "default", "normal", "fast" };
            Console.WriteLine("{0} mode result ({1}ms):", names[mode], ts1);
            Console.WriteLine("avgrtt={0} maxrtt={1} tx={2}", sumrtt / count, maxrtt, vnet.tx1);
            Console.WriteLine("Press any key to next...");
            Console.Read();
        }


        static void Main(string[] args)
        {
            string testcase = "kcp";
            if (args.Length > 0)
            {
                testcase = args[0];
            }

            if (testcase == "kcp")
            {
                KCPTest(0); // 默认模式，类似 TCP：正常模式，无快速重传，常规流控
                KCPTest(1); // 普通模式，关闭流控等
                KCPTest(2); // 快速模式，所有开关都打开，且关闭流控
            }
            else if (testcase == "socket")
            {
                TestSocket();
            }
        }


        static void TestSocket()
        {
            UInt32 conv = 0x12345678;
            var counter = 1;
            var originText = "a quick brown fox jumps over the lazy dog";
            var rawbytes = Encoding.UTF8.GetBytes(String.Format("{0} {1}", originText, counter));

            KCPSocket sock = new KCPSocket();
            sock.SetHandler((byte[] data, int size) =>
            {
                Console.WriteLine(Encoding.UTF8.GetString(data, 0, size));

                Thread.Sleep(500);
                rawbytes = Encoding.UTF8.GetBytes(String.Format("{0} {1}", originText, ++counter));
                sock.Send(rawbytes, 0, rawbytes.Length);
            });

            sock.Connect(conv, "127.0.0.1", 9527);
            sock.StartRead();
            sock.Send(rawbytes, 0, rawbytes.Length);

            while (true)
            {
                Thread.Sleep(100);
                try
                {
                    sock.Update(Utils.iclock());
                }
                catch(Exception ex)
                {
                    sock.Close();
                    Console.WriteLine("Exception: {0}", ex);
                    break;
                }
            }
        }
    }
}
