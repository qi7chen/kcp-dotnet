// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;
using System.Diagnostics;

namespace Network
{
    public class KCP
    {
        public const int IKCP_RTO_NDL = 30;         // no delay min rto
        public const int IKCP_RTO_MIN = 100;        // normal min rto
        public const int IKCP_RTO_DEF = 200;
        public const int IKCP_RTO_MAX = 60000;
        public const int IKCP_CMD_PUSH = 81;        // cmd: push data
        public const int IKCP_CMD_ACK = 82;         // cmd: ack
        public const int IKCP_CMD_WASK = 83;        // cmd: window probe (ask)
        public const int IKCP_CMD_WINS = 84;        // cmd: window size (tell)
        public const int IKCP_ASK_SEND = 1;         // need to send IKCP_CMD_WASK
        public const int IKCP_ASK_TELL = 2;         // need to send IKCP_CMD_WINS
        public const int IKCP_WND_SND = 32;
        public const int IKCP_WND_RCV = 32;
        public const int IKCP_MTU_DEF = 1400;
        public const int IKCP_ACK_FAST = 3;
        public const int IKCP_INTERVAL = 100;
        public const int IKCP_OVERHEAD = 24;
        public const int IKCP_DEADLINK = 20;
        public const int IKCP_THRESH_INIT = 2;
        public const int IKCP_THRESH_MIN = 2;
        public const int IKCP_PROBE_INIT = 7000;    // 7 secs to probe window size
        public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window

        public const int IKCP_LOG_OUTPUT = 0x1;
        public const int IKCP_LOG_INPUT = 0x2;
        public const int IKCP_LOG_SEND = 0x4;
        public const int IKCP_LOG_RECV = 0x8;
        public const int IKCP_LOG_IN_DATA = 0x10;
        public const int IKCP_LOG_IN_ACK = 0x20;
        public const int IKCP_LOG_IN_PROBE = 0x40;
        public const int IKCP_LOG_IN_WINS = 0x80;
        public const int IKCP_LOG_OUT_DATA = 0x100;
        public const int IKCP_LOG_OUT_ACK = 0x200;
        public const int IKCP_LOG_OUT_PROBE = 0x400;
        public const int IKCP_LOG_OUT_WINS = 0x800;

        // encode 8 bits unsigned int
        public static void ikcp_encode8u(byte[] p, int offset, byte c)
        {
            p[offset] = c;
        }

        // decode 8 bits unsigned int
        public static byte ikcp_decode8u(byte[] p, int offset)
        {
            return p[offset];
        }

        // encode 16 bits unsigned int (lsb)
        public static void ikcp_encode16u(byte[] p, int offset, UInt16 v)
        {
            p[offset] = (byte)(v & 0xFF);
            p[offset + 1] = (byte)(v >> 8);
        }

        // decode 16 bits unsigned int (lsb)
        public static UInt16 ikcp_decode16u(byte[] p, int offset)
        {
            UInt16 v = 0;
            v |= (UInt16)p[offset];
            v |= (UInt16)(p[offset + 1] << 8);
            return v;
        }

        // encode 32 bits unsigned int (lsb)
        public static void ikcp_encode32u(byte[] p, int offset, UInt32 l)
        {
            p[offset] = (byte)(l & 0xFF);
            p[offset + 1] = (byte)(l >> 8);
            p[offset + 2] = (byte)(l >> 16);
            p[offset + 3] = (byte)(l >> 24);
        }

        // decode 32 bits unsigned int (lsb)
        public static UInt32 ikcp_decode32u(byte[] p, int offset)
        {
            UInt32 v = 0;
            v |= (UInt32)p[offset];
            v |= (UInt32)(p[offset + 1] << 8);
            v |= (UInt32)(p[offset + 2] << 16);
            v |= (UInt32)(p[offset + 3] << 24);
            return v;
        }

        public static UInt32 _imin_(UInt32 a, UInt32 b)
        {
            return a <= b ? a : b;
        }

        public static UInt32 _imax_(UInt32 a, UInt32 b)
        {
            return a >= b ? a : b;
        }

        public static UInt32 _ibound_(UInt32 lower, UInt32 middle, UInt32 upper)
        {
            return _imin_(_imax_(lower, middle), upper);
        }

        public static Int32 _itimediff(UInt32 later, UInt32 earlier)
        {
            return (Int32)(later - earlier);
        }

        internal class Segment
        {
            internal Segment prev = null;
            internal Segment next = null;

            internal UInt32 conv = 0;
            internal UInt32 cmd = 0;
            internal UInt32 frg = 0;
            internal UInt32 wnd = 0;
            internal UInt32 ts = 0;
            internal UInt32 sn = 0;
            internal UInt32 una = 0;
            internal UInt32 resendts = 0;
            internal UInt32 rto = 0;
            internal UInt32 faskack = 0;
            internal UInt32 xmit = 0;
            internal byte[] data;

            internal Segment()
            {
                prev = this;
                next = this;
            }

            internal Segment(int size)
            {
                prev = this;
                next = this;
                data = new byte[size];
            }

            internal void Add(Segment seg)
            {
                prev = seg;
                next = seg.next;
                next.prev = this;
                seg.next = this;
            }

            internal void AddTail(Segment seg)
            {
                prev = seg.prev;
                next = seg;
                seg.prev.next = this;
                seg.prev = this;
            }

            internal void DelEntry()
            {
                next.prev = prev;
                prev.next = next;
                next = null;
                prev = null;
            }

            internal bool IsEmpty()
            {
                return next == this;
            }

            internal int Encode(byte[] ptr, int offset)
            {
                ikcp_encode32u(ptr, offset, conv);
                ikcp_encode8u(ptr, offset, (byte)cmd);
                ikcp_encode8u(ptr, offset, (byte)frg);
                ikcp_encode16u(ptr, offset, (UInt16)wnd);
                ikcp_encode32u(ptr, offset, ts);
                ikcp_encode32u(ptr, offset, sn);
                ikcp_encode32u(ptr, offset, una);
                ikcp_encode32u(ptr, offset, (UInt32)data.Length);
                return IKCP_OVERHEAD;
            }

        }

        UInt32 conv = 0;
        UInt32 mtu = 0;
        UInt32 mss = 0;
        UInt32 state = 0;

        UInt32 snd_una = 0;
        UInt32 snd_nxt = 0;
        UInt32 rcv_nxt = 0;

        UInt32 ts_recent = 0;
        UInt32 ts_lastack = 0;
        UInt32 ssthresh = 0;

        Int32 rx_rttval = 0;
        Int32 rx_srtt = 0;
        Int32 rx_rto = 0;
        Int32 rx_minrto = 0;

        UInt32 snd_wnd = 0;
        UInt32 rcv_wnd = 0;
        UInt32 rmt_wnd = 0;
        UInt32 cwnd = 0;
        UInt32 probe = 0;

        UInt32 current = 0;
        UInt32 interval = 0;
        UInt32 ts_flush = 0;
        UInt32 xmit = 0;

        UInt32 nrcv_buf = 0;
        UInt32 nsnd_buf = 0;
        UInt32 nrcv_que = 0;
        UInt32 nsnd_que = 0;

        UInt32 nodelay = 0;
        UInt32 updated = 0;
        UInt32 ts_probe = 0;
        UInt32 probe_wait = 0;
        UInt32 dead_link = 0;
        UInt32 incr = 0;

        Segment snd_queue = new Segment();
        Segment rcv_queue = new Segment();
        Segment snd_buf = new Segment();
        Segment rcv_buf = new Segment();

        UInt32[] acklist;
        UInt32 ackcount = 0;
        UInt32 ackblock = 0;

        byte[] buffer;

        Int32 fastresend = 0;
        bool nocwnd = false;

        Action<byte[], int> output;

        KCP(UInt32 conv, Action<byte[], int> output)
        {
            this.output = output;
            this.conv = conv;
            snd_wnd = IKCP_WND_SND;
            rcv_wnd = IKCP_WND_RCV;
            rmt_wnd = IKCP_WND_RCV;
            mtu = IKCP_MTU_DEF;
            mss = mtu - IKCP_OVERHEAD;
            rx_rto = IKCP_RTO_DEF;
            rx_minrto = IKCP_RTO_MIN;
            interval = IKCP_INTERVAL;
            ts_flush = IKCP_INTERVAL;
            ssthresh = IKCP_THRESH_INIT;
            dead_link = IKCP_DEADLINK;
            buffer = new byte[(mtu + IKCP_OVERHEAD) * 3];
        }

        void Release()
        {
            nrcv_buf = 0;
            nsnd_buf = 0;
            nrcv_que = 0;
            nsnd_que = 0;
            snd_buf = null;
            rcv_buf = null;
            snd_queue = null;
            rcv_queue = null;
            buffer = null;
            acklist = null;
        }

        // user/upper level recv: returns size, returns below zero for EAGAIN
        int Recv(byte[] buffer, int offset, int len)
        {
            if (rcv_queue.IsEmpty())
                return -1;

            if (len < 0)
                len = -len;

            int peeksize = PeekSize();
            if (peeksize < 0)
                return -2;

            if (peeksize > len)
                return -3;

            bool ispeek = len < 0;
            bool recover = false;
            if (nrcv_que >= rcv_wnd)
                recover = true;

            // merge fragment
            len = 0;
            for (var seg = rcv_queue.next; seg != rcv_queue; seg = seg.next)
            {
                if (buffer != null)
                {
                    Array.Copy(seg.data, 0, buffer, offset, seg.data.Length);
                    offset += seg.data.Length;
                }
                len += seg.data.Length;
                var fragment = seg.frg;

                Log(IKCP_LOG_RECV, "recv sn=%lu", seg.sn);

                if (!ispeek)
                {
                    seg.DelEntry();
                    nrcv_que--;
                }

                if (fragment == 0)
                    break;
            }

            Debug.Assert(len == peeksize);

            // move available data from rcv_buf -> rcv_queue
            while (!rcv_buf.IsEmpty())
            {
                var seg = rcv_buf.next;
                if (seg.sn == rcv_nxt && nrcv_que < rcv_wnd)
                {
                    seg.DelEntry();
                    nrcv_buf--;
                    rcv_queue.AddTail(seg);
                    nrcv_que++;
                    rcv_nxt++;
                }
                else
                {
                    break;
                }
            }

            // fast recover
            if (nrcv_que < rcv_wnd && recover)
            {
                // ready to send back IKCP_CMD_WINS in ikcp_flush
                // tell remote my window size
                probe |= IKCP_ASK_TELL;
            }

            return len;
        }

        // peek data size
        int PeekSize()
        {
            if (rcv_queue.IsEmpty())
                return -1;

            var seg = rcv_queue.next;
            if (seg.frg == 0)
                return seg.data.Length;

            if (nrcv_que < seg.frg + 1)
                return -1;

            int length = 0;
            for (seg = rcv_queue.next; seg != rcv_queue; seg = seg.next)
            {
                length += seg.data.Length;
                if (seg.frg == 0)
                    break;
            }
            return length;
        }

        // user/upper level send, returns below zero for error
        int Send(byte[] buffer, int offset, int len)
        {
            Debug.Assert(mss > 0);
            if (len < 0)
                return -1;

            // we not implement streaming mode
            int count = 0;
            if (len <= (int)mss)
                count = 1;
            else
                count = (len + (int)mss - 1) / (int)mss;

            if (count > 0xFF) // maximum value `frg` can present
                return -2;

            if (count == 0)
                count = 1;

            // fragment
            for (int i = 0; i < count; i++)
            {
                int size = len > (int)mss ? (int)mss : len;
                var seg = new Segment(size);
                if (buffer != null && len > 0)
                {
                    Array.Copy(buffer, offset, seg.data, 0, size);
                    offset += size;
                }
                seg.frg = (UInt32)(count - i - 1);
                snd_queue.AddTail(seg);
                nsnd_que++;
                len -= size;
            }
            return 0;
        }

        void UpdateACK(Int32 rtt)
        {
            if (rx_srtt == 0)
            {
                rx_srtt = rtt;
                rx_rttval = rtt / 2;
            }
            else
            {
                Int32 delta = rtt - rx_srtt;
                if (delta < 0)
                    delta = -delta;

                rx_rttval = (3 * rx_rttval + delta) / 4;
                rx_srtt = (7 * rx_srtt + rtt) / 8;
                if (rx_srtt < 1)
                    rx_srtt = 1;
            }

            var rto = rx_srtt + _imax_(1, (UInt32)(4 * rx_rttval));
            rx_rto = (Int32)_ibound_((UInt32)rx_minrto, (UInt32)rto, IKCP_RTO_MAX);
        }

        void ShrinkBuffer()
        {
            var seg = snd_buf.next;
            if (seg != snd_buf)
            {
                snd_una = seg.sn;
            }
            else
            {
                snd_una = snd_nxt;
            }
        }

        void ParseACK(UInt32 sn)
        {
            if (_itimediff(sn, snd_una) < 0 || _itimediff(sn, snd_nxt) >= 0)
                return;
            for (var seg = snd_buf.next; seg != snd_buf; seg = seg.next)
            {
                if (sn == seg.sn)
                {
                    seg.DelEntry();
                    nsnd_buf--;
                    break;
                }
                if (_itimediff(sn, seg.sn) < 0)
                    break;
            }
        }

        void ParseUNA(UInt32 una)
        {
            for (var seg = snd_buf.next; seg != snd_buf; seg = seg.next)
            {
                if (_itimediff(una, seg.sn) > 0)
                {
                    seg.DelEntry();
                    nsnd_buf--;
                }
                else
                {
                    break;
                }
            }
        }

        void ParseFastACK(UInt32 sn)
        {
            if (_itimediff(sn, snd_una) < 0 || _itimediff(sn, snd_nxt) >= 0)
                return;

            for (var seg = snd_buf.next; seg != snd_buf; seg = seg.next)
            {
                if (_itimediff(sn, seg.sn) < 0)
                {
                    break;
                }
                else if (sn != seg.sn)
                {
                    seg.faskack++;
                }
            }
        }

        void ACKPush(UInt32 sn, UInt32 ts)
        {
            var newsize = ackcount + 1;
            if (newsize > ackblock)
            {
                UInt32 newblock = 8;
                for (; newblock < newsize; newblock <<= 1)
                    ; // do nothing
                var newlist = new UInt32[newblock * 2];
                if (acklist != null)
                {
                    for (var i = 0; i < ackcount; i++)
                    {
                        newlist[i * 2] = acklist[i * 2];
                        newlist[i * 2 + 1] = acklist[i * 2 + 1];
                    }
                }
            }
        }
        
        void Log(int mask, string format, params object[] args)
        {
            
        }
    }
}
