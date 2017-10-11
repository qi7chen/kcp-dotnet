// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;
using System.Collections.Generic;

namespace Network
{	
    // 带延迟的数据包
    public class DelayPacket
    {
        private byte[]  data_;
        private int     size_;
        private UInt32  ts_;

        public DelayPacket(byte[] data, int size)
        {
            size_ = size;
            data_ = new byte[size];
            Buffer.BlockCopy(data, 0, data_, 0, size);
        }

        public byte[] Data()
        {
            return data_;
        }

        public int Size()
        {
            return size_;
        }

        public UInt32 Ts()
        {
            return ts_;
        }

        public void SetTs(UInt32 ts)
        {
            ts_ = ts;
        }
    }

    // 网络延迟模拟器
    public class LatencySimulator
    {
        public int tx1 = 0;
        public int tx2 = 0;

        private UInt32 current_;
        private int lostrate_;
        private int rttmin_;
        private int rttmax_;
        private int nmax_;
        private LinkedList<DelayPacket> p12_;
        private LinkedList<DelayPacket> p21_;
        private Random r12_;
        private Random r21_;
        private Random rand_;

        public static int GetRandomSeed()
        {
            var guid = new Guid();
            return guid.GetHashCode();
        }

        // lostrate: 往返一周丢包率的百分比，默认 10%
        // rttmin：rtt最小值，默认 60
        // rttmax：rtt最大值，默认 125
        public LatencySimulator(int lostrate = 10, int rttmin = 60, int rttmax = 125, int nmax = 1000)
        {
            current_ = Utils.iclock();
            lostrate_ = lostrate / 2;
            rttmin_ = rttmin / 2;
            rttmax_ = rttmax / 2;
            nmax_ = nmax;

            r12_ = new Random(GetRandomSeed());
            r21_ = new Random(GetRandomSeed());
            rand_ = new Random(GetRandomSeed());
            p12_ = new LinkedList<DelayPacket>();
            p21_ = new LinkedList<DelayPacket>();
        }

        public void Clear()
        {
            p12_.Clear();
            p21_.Clear();
        }

        // 发送数据
        // peer - 端点0/1，从0发送，从1接收；从1发送从0接收
        public void Send(int peer, byte[] data, int size)
        {
            if (peer == 0)
            {
                tx1++;
                if (r12_.Next(100) < lostrate_)
                    return;
                if (p12_.Count >= nmax_)
                    return;
            }
            else
            {
                tx2++;
                if (r21_.Next(100) < lostrate_)
                    return;
                if (p21_.Count >= nmax_)
                    return;
            }
            var pkt = new DelayPacket(data, size);
            current_ = Utils.iclock();
            Int32 delay = rttmin_;
            if (rttmax_ > rttmin_)
                delay += rand_.Next() % (rttmax_ - rttmin_);
            pkt.SetTs(current_ + (UInt32)delay);
            if (peer == 0)
            {
                p12_.AddLast(pkt);
            }
            else
            {
                p21_.AddLast(pkt);
            }
        }

        // 接收数据
        public int Recv(int peer, byte[] data, int maxsize)
        {
            DelayPacket pkt;
            if (peer == 0)
            {
                if (p21_.Count == 0)
                    return -1;
                pkt = p21_.First.Value;
            }
            else
            {
                if (p12_.Count == 0)
                    return -1;
                pkt = p12_.First.Value;
            }
            current_ = Utils.iclock();
            if (current_ < pkt.Ts())
                return -2;
            if (maxsize < pkt.Size())
                return -3;
            if (peer == 0)
                p21_.RemoveFirst();
            else
                p12_.RemoveFirst();

            maxsize = pkt.Size();
            Buffer.BlockCopy(pkt.Data(), 0, data, 0, maxsize);
            return maxsize;
        }
    }
}
