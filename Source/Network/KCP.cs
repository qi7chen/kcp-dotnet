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


        //
        // we only allow little endian machine to run
        //

        // encode 8 bits unsigned int
        public static void ikcp_encode8u(byte[] p, int offset, byte c)
        {
            p[offset] = c;
        }

        // decode 8 bits unsigned int
        public static byte ikcp_decode8u(byte[] p, ref int offset)
        {
            int pos = offset;
            offset += 1;
            return p[pos];
        }

        // encode 16 bits unsigned int (lsb)
        public static void ikcp_encode16u(byte[] p, int offset, UInt16 v)
        {
            p[offset] = (byte)(v & 0xFF);
            p[offset + 1] = (byte)(v >> 8);
        }

        // decode 16 bits unsigned int (lsb)
        public static UInt16 ikcp_decode16u(byte[] p, ref int offset)
        {
            int pos = offset;
            offset += 2;
            UInt16 v = 0;
            v |= (UInt16)p[pos];
            v |= (UInt16)(p[pos + 1] << 8);
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
        public static UInt32 ikcp_decode32u(byte[] p, ref int offset)
        {
            int pos = offset;
            offset += 4;
            UInt32 v = 0;
            v |= (UInt32)p[pos];
            v |= (UInt32)(p[pos + 1] << 8);
            v |= (UInt32)(p[pos + 2] << 16);
            v |= (UInt32)(p[pos + 3] << 24);
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

            internal void Add(Segment node)
            {
                node.prev = this;
                node.next = next;
                next.prev = node;
                next = node;
            }

            internal void AddTail(Segment node)
            {
                node.prev = prev;
                node.next = this;
                prev.next = node;
                prev = node;
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
                UInt32 len = 0;
                if (data != null)
                    len = (UInt32)data.Length;

                ikcp_encode32u(ptr, offset, conv);
                ikcp_encode8u(ptr, offset + 4, (byte)cmd);
                ikcp_encode8u(ptr, offset + 5, (byte)frg);
                ikcp_encode16u(ptr, offset + 6, (UInt16)wnd);
                ikcp_encode32u(ptr, offset + 8, ts);
                ikcp_encode32u(ptr, offset + 12, sn);
                ikcp_encode32u(ptr, offset + 16, una);
                ikcp_encode32u(ptr, offset + 20, len);
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
        object user;

        Int32 fastresend = 0;
        Int32 nocwnd = 0;

        public delegate void OutputDelegate(byte[] data, int size, object user);
        OutputDelegate output;

        public KCP(UInt32 conv_, object ud)
        {
            user = ud;
            conv = conv_;
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

            // magic number to identifiy queue head sentinel
            snd_queue.conv = 20170901;
            rcv_queue.conv = 20170902;
            snd_buf.conv = 20170903;
            rcv_buf.conv = 20170904;
        }

        public void Release()
        {
            while (!snd_buf.IsEmpty())
            {
                snd_buf.next.DelEntry();
            }
            while (!rcv_buf.IsEmpty())
            {
                rcv_buf.next.DelEntry();
            }
            while (!snd_queue.IsEmpty())
            {
                snd_queue.next.DelEntry();
            }
            while (!rcv_queue.IsEmpty())
            {
                rcv_queue.DelEntry();
            }
            nrcv_buf = 0;
            nsnd_buf = 0;
            nrcv_que = 0;
            nsnd_que = 0;
            ackblock = 0;
            ackcount = 0;
            snd_buf = null;
            rcv_buf = null;
            snd_queue = null;
            rcv_queue = null;
            buffer = null;
            acklist = null;
        }

        // set output callback, which will be invoked by kcp
        public void SetOutput(OutputDelegate output_)
        {
            output = output_;
        }

        // user/upper level recv: returns size, returns below zero for EAGAIN
        public int Recv(byte[] buffer, int offset, int len)
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

            int ispeek = (len < 0 ? 1 : 0);
            int recover = 0;

            if (nrcv_que >= rcv_wnd)
                recover = 1;

            // merge fragment
            len = 0;
            for (var seg = rcv_queue.next; seg != rcv_queue; seg = seg.next)
            {
                int fragment = 0;
                if (buffer != null)
                {
                    Array.Copy(seg.data, 0, buffer, offset, seg.data.Length);
                    offset += seg.data.Length;
                }
                len += seg.data.Length;
                fragment = (int)seg.frg;

                Log(IKCP_LOG_RECV, "recv sn=%lu", seg.sn);

                if (ispeek == 0)
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
            if (nrcv_que < rcv_wnd && recover != 0)
            {
                // ready to send back IKCP_CMD_WINS in ikcp_flush
                // tell remote my window size
                probe |= IKCP_ASK_TELL;
            }

            return len;
        }

        // peek data size
        public int PeekSize()
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
        public int Send(byte[] buffer, int offset, int len)
        {
            Debug.Assert(mss > 0);
            if (len < 0)
                return -1;

            //
            // we do not implement streaming mode here as in ikcp.c
            //

            int count = 0;
            if (len <= (int)mss)
                count = 1;
            else
                count = (len + (int)mss - 1) / (int)mss;

            if (count > 255) // maximum value `frg` can present
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

        // parse ack
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

            var rto = rx_srtt + _imax_(interval, (UInt32)(4 * rx_rttval));
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
            var seg = snd_buf;
            var count = nsnd_buf;
            for (int i = 0; i < count; i++)
            {
                seg = seg.next;
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

        // ack append
        void ACKPush(UInt32 sn, UInt32 ts)
        {
            var newsize = ackcount + 1;
            if (newsize > ackblock)
            {
                UInt32 newblock = 8;
                for (; newblock < newsize; newblock <<= 1)
                    ;

                var newlist = new UInt32[newblock * 2];
                if (acklist != null)
                {
                    for (var i = 0; i < ackcount; i++)
                    {
                        newlist[i * 2] = acklist[i * 2];
                        newlist[i * 2 + 1] = acklist[i * 2 + 1];
                    }
                }
                acklist = newlist;
                ackblock = newblock;
            }
            acklist[ackcount * 2] = sn;
            acklist[ackcount * 2 + 1] = ts;
            ackcount++;
        }

        void ACKGet(int pos, ref UInt32 sn, ref UInt32 ts)
        {
            sn = acklist[pos * 2];
            ts = acklist[pos * 2 + 1];
        }

        // parse data
        void ParseData(Segment newseg)
        {
            UInt32 sn = newseg.sn;
            if (_itimediff(sn, rcv_nxt + rcv_wnd) >= 0 ||
                _itimediff(sn, rcv_nxt) < 0)
            {
                return;
            }

            int repeat = 0;
            var seg = rcv_buf.prev;
            for (; seg != rcv_buf; seg = seg.prev)
            {
                if (seg.sn == sn)
                {
                    repeat = 1;
                    break;
                }
                if (_itimediff(sn, seg.sn) > 0) 
                {
                    break;
                }
            }
            if (repeat == 0)
            {
                seg.Add(newseg);
                nrcv_buf++;
            }

            // move available data from rcv_buf -> rcv_queue
            while (!rcv_buf.IsEmpty())
            {
                seg = rcv_buf.next;
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
        }

        // input data
        public int Input(byte[] data, int offset, int size)
        {
            Log(IKCP_LOG_INPUT, "[RI] {0} bytes", size);

            if (data == null || size < IKCP_OVERHEAD)
                return -1;

            UInt32 maxack = 0;
            int flag = 0;
            while (true)
            {
                if (size < IKCP_OVERHEAD)
                    break;

                UInt32 conv_ = ikcp_decode32u(data, ref offset);
                if (conv_ != conv)
                    return -1;
                UInt32 cmd = ikcp_decode8u(data, ref offset);
                UInt32 frg = ikcp_decode8u(data, ref offset);
                UInt32 wnd = ikcp_decode16u(data, ref offset);
                UInt32 ts = ikcp_decode32u(data, ref offset);
                UInt32 sn = ikcp_decode32u(data, ref offset);
                UInt32 una_ = ikcp_decode32u(data, ref offset);
                UInt32 len = ikcp_decode32u(data, ref offset);

                size -= IKCP_OVERHEAD;
                if (size < len)
                    return -2;

                if (cmd != IKCP_CMD_PUSH && cmd != IKCP_CMD_ACK &&
                    cmd != IKCP_CMD_WASK && cmd != IKCP_CMD_WINS)
                    return -3;

                rmt_wnd = wnd;
                ParseUNA(una_);
                ShrinkBuffer();

                if (cmd == IKCP_CMD_ACK)
                {
                    if (_itimediff(current, ts) >= 0)
                    {
                        UpdateACK(_itimediff(current, ts));
                    }
                    ParseACK(sn);
                    ShrinkBuffer();
                    if (flag == 0)
                    {
                        flag = 1;
                        maxack = sn;
                    }
                    else
                    {
                        if (_itimediff(sn, maxack) > 0)
                        {
                            maxack = sn;
                        }
                    }
                    Log(IKCP_LOG_IN_DATA, "input ack: sn={0} rtt={1} rto={2}",
                        sn, _itimediff(current, ts), rx_rto);
                }
                else if (cmd == IKCP_CMD_PUSH)
                {
                    Log(IKCP_LOG_IN_DATA, "input psh: sn={0} ts={1}", sn, ts);
                    if (_itimediff(sn, rcv_nxt + rcv_wnd) < 0)
                    {
                        ACKPush(sn, ts);
                        if (_itimediff(sn, rcv_nxt) >= 0)
                        {
                            var seg = new Segment((int)len);
                            seg.conv = conv;
                            seg.cmd = cmd;
                            seg.frg = frg;
                            seg.wnd = wnd;
                            seg.ts = ts;
                            seg.sn = sn;
                            seg.una = una_;
                            if (len > 0)
                            {
                                Array.Copy(data, offset, seg.data, 0, (int)len);
                            }
                            ParseData(seg);
                        }
                    }
                }
                else if (cmd == IKCP_CMD_WASK)
                {
                    // ready to send back IKCP_CMD_WINS in ikcp_flush
                    // tell remote my window size
                    probe |= IKCP_ASK_TELL;
                    Log(IKCP_LOG_IN_PROBE, "input probe");
                }
                else if (cmd == IKCP_CMD_WINS)
                {
                    // do nothing
                    Log(IKCP_LOG_IN_WINS, "input wins: {0}", wnd);
                }
                else
                {
                    return -3;
                }

                offset += (int)len;
                size -= (int)len;
            }

            if (flag != 0)
            {
                ParseFastACK(maxack);
            }

            UInt32 una = snd_una;
            if (_itimediff(snd_una, una) > 0)
            {
                if (cwnd < rmt_wnd)
                {
                    if (cwnd < ssthresh)
                    {
                        cwnd++;
                        incr += mss;
                    }
                    else
                    {
                        if (incr < mss)
                            incr = mss;
                        incr += (mss * mss) / incr + (mss / 16);
                        if ((cwnd + 1) * mss <= incr)
                            cwnd++;
                    }
                    if (cwnd > rmt_wnd)
                    {
                        cwnd = rmt_wnd;
                        incr = rmt_wnd * mss;
                    }
                }
            }

            return 0;
        }

        int WndUnused()
        {
            if (nrcv_que < rcv_wnd)
                return (int)(rcv_wnd - nrcv_que);
            return 0;
        }

        // ikcp_flush
        void Flush()
        {
            // 'ikcp_update' haven't been called. 
            if (updated == 0)
                return;

            int offset = 0;

            var seg = new Segment();
            seg.conv = conv;
            seg.cmd = IKCP_CMD_ACK;
            seg.wnd = (UInt32)WndUnused();
            seg.una = rcv_nxt;

            // flush acknowledges
            int count = (int)ackcount;
            for (int i = 0; i < count; i++)
            {
                if ((offset + IKCP_OVERHEAD) > mtu)
                {
                    output(buffer, offset, user);
                    offset = 0;
                }
                ACKGet(i, ref seg.sn, ref seg.ts);
                offset += seg.Encode(buffer, offset);
            }
            ackcount = 0;

            // probe window size (if remote window size equals zero)
            if (rmt_wnd == 0)
            {
                if (probe_wait == 0)
                {
                    probe_wait = IKCP_PROBE_INIT;
                    ts_probe = current + probe_wait;
                }
                else
                {
                    if (_itimediff(current, ts_probe) >= 0)
                    {
                        if (probe_wait < IKCP_PROBE_INIT)
                            probe_wait = IKCP_PROBE_INIT;
                        probe_wait += probe_wait / 2;
                        if (probe_wait > IKCP_PROBE_LIMIT)
                            probe_wait = IKCP_PROBE_LIMIT;
                        ts_probe = current + probe_wait;
                        probe |= IKCP_ASK_SEND;
                    }
                }
            }
            else
            {
                ts_probe = 0;
                probe_wait = 0;
            }

            // flush window probing commands
            if ((probe & IKCP_ASK_SEND) > 0)
            {
                seg.cmd = IKCP_CMD_WASK;
                if ((offset + IKCP_OVERHEAD) > mtu)
                {
                    output(buffer, offset, user);
                    offset = 0;
                }
                offset += seg.Encode(buffer, offset);
            }

            // flush window probing commands
            if ((probe & IKCP_ASK_TELL) > 0)
            {
                seg.cmd = IKCP_CMD_WINS;
                if ((offset + IKCP_OVERHEAD) > mtu)
                {
                    output(buffer, offset, user);
                    offset = 0;
                }
                offset += seg.Encode(buffer, offset);
            }

            probe = 0;

            // calculate window size
            UInt32 cwnd_ = _imin_(snd_wnd, rmt_wnd);
            if (nocwnd == 0)
                cwnd_ = _imin_(cwnd, cwnd_);

            // move data from snd_queue to snd_buf
            while (_itimediff(snd_nxt, snd_una + cwnd_) < 0)
            {
                if (snd_queue.IsEmpty())
                    break;

                var newseg = snd_queue.next;
                newseg.DelEntry();
                snd_buf.AddTail(newseg);
                nsnd_que--;
                nsnd_buf++;

                newseg.conv = conv;
                newseg.cmd = IKCP_CMD_PUSH;
                newseg.wnd = seg.wnd;
                newseg.ts = current;
                newseg.sn = snd_nxt++;
                newseg.una = rcv_nxt;
                newseg.resendts = current;
                newseg.rto = (UInt32)rx_rto;
                newseg.faskack = 0;
                newseg.xmit = 0;
            }

            // calculate resent
            UInt32 resent = (fastresend > 0 ? (UInt32)fastresend : 0xffffffff);
            UInt32 rtomin = (nodelay == 0 ? (UInt32)(rx_rto >> 3) : 0);

            int change = 0;
            int lost = 0;

            // flush data segments
            for (var segment = snd_buf.next; segment != snd_buf; segment = segment.next)
            {
                int needsend = 0;
                if (segment.xmit == 0)
                {
                    needsend = 1;
                    segment.xmit++;
                    segment.rto = (UInt32)rx_rto;
                    segment.resendts = current + segment.rto + rtomin;
                }
                else if (_itimediff(current, segment.resendts) >= 0)
                {
                    needsend = 1;
                    segment.xmit++;
                    xmit++;
                    if (nodelay == 0)
                        segment.rto += (UInt32)rx_rto;
                    else
                        segment.rto += (UInt32)rx_rto / 2;
                    segment.resendts = current + segment.rto;
                    lost = 1;
                }
                else if (segment.faskack >= resent)
                {
                    needsend = 1;
                    segment.xmit++;
                    segment.faskack = 0;
                    segment.resendts = current + segment.rto;
                    change++;
                }

                if (needsend > 0)
                {
                    segment.ts = current;
                    segment.wnd = seg.wnd;
                    segment.una = rcv_nxt;

                    int need = IKCP_OVERHEAD;
                    if (segment.data != null)
                        need += segment.data.Length;

                    if (offset + need > mtu)
                    {
                        output(buffer, offset, user);
                        offset = 0;
                    }
                    offset += segment.Encode(buffer, offset);
                    if (segment.data != null && segment.data.Length > 0)
                    {
                        Array.Copy(segment.data, 0, buffer, offset, segment.data.Length);
                        offset += segment.data.Length;
                    }
                    if (segment.xmit >= dead_link)
                        state = 0xffffffff;
                }
            }

            // flush remain segments
            if (offset > 0)
            {
                output(buffer, offset, user);
                offset = 0;
            }

            // update ssthresh
            if (change > 0)
            {
                UInt32 inflight = snd_nxt - snd_una;
                ssthresh = inflight / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                    ssthresh = IKCP_THRESH_MIN;
                cwnd = ssthresh + resent;
                incr = cwnd * mss;
            }

            if (lost > 0)
            {
                ssthresh = cwnd_ / 2;
                if (ssthresh < IKCP_THRESH_MIN)
                    ssthresh = IKCP_THRESH_MIN;
                cwnd = 1;
                incr = mss;
            }

            if (cwnd < 1)
            {
                cwnd = 1;
                incr = mss;
            }
        }

        // update state (call it repeatedly, every 10ms-100ms), or you can ask 
        // ikcp_check when to call it again (without ikcp_input/_send calling).
        // 'current' - current timestamp in millisec. 
        public void Update(UInt32 current_)
        {
            current = current_;
            if (updated == 0)
            {
                updated = 1;
                ts_flush = current;
            }

            Int32 slap = _itimediff(current, ts_flush);
            if (slap >= 10000 || slap < -10000)
            {
                ts_flush = current;
                slap = 0;
            }

            if (slap >= 0)
            {
                ts_flush += interval;
                if (_itimediff(current, ts_flush) >= 0)
                    ts_flush = current + interval;

                Flush();
            }
        }

        // Determine when should you invoke ikcp_update:
        // returns when you should invoke ikcp_update in millisec, if there 
        // is no ikcp_input/_send calling. you can call ikcp_update in that
        // time, instead of call update repeatly.
        // Important to reduce unnacessary ikcp_update invoking. use it to 
        // schedule ikcp_update (eg. implementing an epoll-like mechanism, 
        // or optimize ikcp_update when handling massive kcp connections)
        public UInt32 Check(UInt32 current_)
        {
            UInt32 ts_flush_ = ts_flush;
            Int32 tm_flush = 0x7fffffff;
            Int32 tm_packet = 0x7fffffff;

            if (updated == 0)
                return current_;

            if (_itimediff(current_, ts_flush_) >= 10000 || 
                _itimediff(current_, ts_flush_) < -10000)
            {
                ts_flush_ = current;
            }

            if (_itimediff(current_, ts_flush_) >= 0)
                return current;

            tm_flush = _itimediff(ts_flush_, current_);

            for (var seg = snd_buf.next; seg != snd_buf; seg = seg.next)
            {
                Int32 diff = _itimediff(seg.resendts, current_);
                if (diff <= 0)
                    return current_;

                if (diff < tm_packet)
                    tm_packet = diff;
            }

            UInt32 minimal = (UInt32)(tm_packet < (int)ts_flush_ ? tm_packet : (int)ts_flush_);
            if (minimal >= interval)
                minimal = interval;

            return current_ + minimal;
        }

        public int SetMTU(int mtu_)
        {
            if (mtu_ < 50 || mtu_ < IKCP_OVERHEAD)
                return -1;

            mtu = (UInt32)mtu_;
            mss = mtu - IKCP_OVERHEAD;
            buffer = new byte[(mtu + IKCP_OVERHEAD) * 3];
            return 0;
        }

        public int Interval(int interval_)
        {
            if (interval_ > 5000)
                interval_ = 5000;
            else if (interval_ < 10)
                interval_ = 10;

            interval = (UInt32)interval_;
            return 0;
        }

        public int NoDelay(int nodelay_, int interval_, int resend_, int nc)
        {
            if (nodelay >= 0)
            {
                nodelay = (UInt32)nodelay_;
                if (nodelay_ != 0)
                {
                    rx_minrto = IKCP_RTO_NDL;
                }
                else
                {
                    rx_minrto = IKCP_RTO_MIN;
                }
            }
            if (interval_ >= 0)
            {
                if (interval_ > 5000)
                    interval_ = 5000;
                else if (interval_ < 10)
                    interval_ = 10;

                interval = (UInt32)interval_;
            }

            if (resend_ >= 0)
                fastresend = resend_;

            if (nc >= 0)
                nocwnd = nc;

            return 0;
        }

        public int WndSize(int sndwnd, int rcvwnd)
        {
            if (sndwnd > 0)
                snd_wnd = (UInt32)sndwnd;
            if (rcvwnd > 0)
                rcv_wnd = (UInt32)rcvwnd;
            return 0;
        }

        public int WaitSnd()
        {
            return (int)(nsnd_buf + nsnd_que);
        }

        public UInt32 GetConv()
        {
            return conv;
        }

        public void SetMinRTO(int minrto)
        {
            rx_minrto = minrto;
        }

        public void SetFastresend(int resend)
        {
            fastresend = resend;
        }

        void Log(int mask, string format, params object[] args)
        {
            // log things
        }
    }
}
