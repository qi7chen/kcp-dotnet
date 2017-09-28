// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;
using System.Collections.Generic;

namespace Network
{
	public class Utils
    {
        private static readonly DateTime epoch = new DateTime(2000, 1, 1);

        public static UInt32 iclock()
        {
            var now = Convert.ToInt64(DateTime.UtcNow.Subtract(epoch).TotalMilliseconds);
            return (UInt32)(now & 0xFFFFFFFF);
        }
    }
	
    // 带延迟的数据包
    public class DelayPacket
    {
        private byte[] data;
        private int size;
        private UInt32 ts;

        public DelayPacket(byte[] data_, int size_)
        {
            data = data_;
            size = size_;
        }

        public byte[] Ptr()
        {
            return data;
        }

        public int Size()
        {
            return size;
        }

        public UInt32 Ts()
        {
            return ts;
        }

        public void SetTs(UInt32 ts_)
        {
            ts = ts_;
        }
    }

    // 网络延迟模拟器
    public class LatencySimulator
    {
        public int tx1 = 0;
        public int tx2 = 0;
        private UInt32 current;
        private int lostrate;
        int rttmin;
        int rttmax;
        int nmax;
        List<DelayPacket> p12 = new List<DelayPacket>();
        List<DelayPacket> p21 = new List<DelayPacket>();
        Random r12 = new Random();
        Random r21 = new Random();
        Random rand = new Random();

        // lostrate: 往返一周丢包率的百分比，默认 10%
        // rttmin：rtt最小值，默认 60
        // rttmax：rtt最大值，默认 125
        public LatencySimulator(int lostrate_ = 10, int rttmin_ = 60, int rttmax_ = 125, int nmax_ = 1000)
        {
            current = Utils.iclock();
            lostrate = lostrate_ / 2;
            rttmin = rttmin_ / 2;
            rttmax = rttmax_ / 2;
            nmax = nmax_;
        }

        public void Send(int peer, byte[] data, int size)
        {
            if (peer == 0)
            {
                tx1++;
                if (r12.Next(100) < lostrate)
                    return;
                if (p12.Count >= nmax)
                    return;
            }
            else
            {
                tx2++;
                if (r21.Next(100) < lostrate)
                    return;
                if (p21.Count >= nmax)
                    return;
            }
            var pkt = new DelayPacket(data, size);
            current = Utils.iclock();
            Int32 delay = rttmin;
            if (rttmax > rttmin)
                delay += rand.Next() % (rttmax - rttmin);
            pkt.SetTs(current + (UInt32)delay);
            if (peer == 0)
            {
                p12.Add(pkt);
            }
            else
            {
                p21.Add(pkt);
            }
        }

        // 接收数据
        public int Recv(int peer, byte[] data, int maxsize)
        {
            DelayPacket pkt;
            if (peer == 0)
            {
                if (p21.Count == 0)
                    return -1;
                pkt = p21[0];
            }
            else
            {
                if (p12.Count == 0)
                    return -1;
                pkt = p12[0];
            }
            current = Utils.iclock();
            if (current < pkt.Ts())
                return -2;
            if (maxsize < pkt.Size())
                return -3;
            if (peer == 0)
                p21.RemoveAt(0);
            else
                p12.RemoveAt(0);

            maxsize = pkt.Size();
            Array.Copy(pkt.Ptr(), 0, data, 0, maxsize);
            return maxsize;
        }
    }
}
