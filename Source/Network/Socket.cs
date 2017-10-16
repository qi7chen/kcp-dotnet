// Copyright (C) 2017 ichenq@outlook.com. All rights reserved.
// Distributed under the terms and conditions of the MIT License.
// See accompanying files LICENSE.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;

namespace Network
{
    internal struct UDPSocketStateObject
    {
        internal Socket socket;
        internal EndPoint remote;
        internal KCPSocket obj;
        internal byte[] buf;
    }

    public class KCPSocket
    {
        private Socket socket;
        private EndPoint remoteEnd;
        private KCP kcp;

        // recv buffer
        private byte[] udpRcvBuf;
        private byte[] kcpRcvBuf;
        private Queue<byte[]> rcvQueue;
        private Queue<byte[]> forground;
        private Queue<Exception> errors;

        // time-out control
        private Int64 lastRecvTime = 0;
        private int recvTimeoutSec = 0;

        private bool needUpdate = false;
        private UInt32 nextUpdateTime = 0;

        Action<byte[], int> handler;


        public KCPSocket(int timeoutSec = 60)
        {
            recvTimeoutSec = timeoutSec;
            udpRcvBuf = new byte[(KCP.IKCP_MTU_DEF + KCP.IKCP_OVERHEAD) * 3];
            kcpRcvBuf = new byte[(KCP.IKCP_MTU_DEF + KCP.IKCP_OVERHEAD) * 3];
            rcvQueue = new Queue<byte[]>(64);
            forground = new Queue<byte[]>(64);
            errors = new Queue<Exception>(8);
        }

        public void SetHandler(Action<byte[], int> cb)
        {
            handler = cb;
        }

        public KCP GetKCPObject()
        {
            return kcp;
        }

        public void Connect(UInt32 conv, string host, UInt16 port)
        {
            var addr = IPAddress.Parse(host);
            remoteEnd = new IPEndPoint(addr, port);
            socket = new Socket(addr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(remoteEnd);

            kcp = new KCP(conv, this);
            kcp.SetOutput(OutputKCP);

            // fast mode
            kcp.NoDelay(1, 10, 2, 1);
            kcp.WndSize(128, 128);
        }

        public void Close()
        {
            socket.Close();
            kcp.Release();
        }

        void OutputKCP(byte[] data, int size, object ud)
        {
            UDPSendTo(data, 0, size);
        }

        public void PushToRecvQueue(byte[] data)
        {
            lock(rcvQueue)
            {
                rcvQueue.Enqueue(data);
            }
        }

        // if `rcvqueue` is not empty, swap it with `forground`
        public Queue<byte[]> SwitchRecvQueue()
        {
            lock(rcvQueue)
            {
                if (rcvQueue.Count > 0)
                {
                    var tmp = rcvQueue;
                    rcvQueue = forground;
                    forground = tmp;
                }
            }
            return forground;
        }

        // dirty write
        public void PushError(Exception ex)
        {
            lock(errors)
            {
                errors.Enqueue(ex);
            }
        }

        // dirty read
        public Exception GetError()
        {
            Exception ex = null;
            lock(errors)
            {
                if (errors.Count > 0)
                {
                    ex = errors.Dequeue();
                }
            }
            return ex;
        }

        public void StartRead()
        {
            UDPSocketStateObject state = new UDPSocketStateObject
            {
                obj = this,
                socket = socket,
                buf = udpRcvBuf,
                remote = remoteEnd,
            };
            PostReadRequest(state);
        }

        public void Send(byte[] data, int offset, int count)
        {
            kcp.Send(data, offset, count);
            needUpdate = true;
        }

        void PostReadRequest(UDPSocketStateObject state)
        {
            socket.BeginReceiveFrom(state.buf, 0, state.buf.Length, 0, ref remoteEnd,
                new AsyncCallback(RecvCallback), state);
        }

        public static void RecvCallback(IAsyncResult ar)
        {
            UDPSocketStateObject state = (UDPSocketStateObject)ar.AsyncState;
            try
            {
                int bytesRead = state.socket.EndReceiveFrom(ar, ref state.remote);
                if (bytesRead <= 0)
                {
                    var ex = new EndOfStreamException("socket closed by peer");
                    state.obj.PushError(ex);
                    return;
                }
                var data = new byte[bytesRead];
                Buffer.BlockCopy(state.buf, 0, data, 0, bytesRead);
                state.obj.PushToRecvQueue(data);
                state.obj.PostReadRequest(state);
            }
            catch (SocketException ex)
            {
                state.obj.PushError(ex);
            }
        }

        void UDPSendTo(byte[] data, int offset, int size)
        {
            UDPSocketStateObject state = new UDPSocketStateObject
            {
                obj = this,
                socket = socket,
            };
            socket.BeginSendTo(data, offset, size, 0, remoteEnd, new AsyncCallback(WriteCallback), state);
        }

        public static void WriteCallback(IAsyncResult ar)
        {
            UDPSocketStateObject state = (UDPSocketStateObject)ar.AsyncState;
            try
            {
                state.socket.EndSendTo(ar);
            }
            catch(SocketException ex)
            {
                state.obj.PushError(ex);
            }
        }

        void CheckTimeout(UInt32 current)
        {
            if (lastRecvTime == 0)
            {
                lastRecvTime = current;
            }
            if (current - lastRecvTime > recvTimeoutSec * 1000)
            {
                var ex = new TimeoutException("socket recv timeout");
                PushError(ex);
            }
        }

        public void ProcessRecv(UInt32 current)
        {
            var queue = SwitchRecvQueue();
            while (queue.Count > 0)
            {
                lastRecvTime = current;
                var data = queue.Dequeue();
                int r = kcp.Input(data, 0, data.Length);
                Debug.Assert(r >= 0);
                needUpdate = true;
                while (true)
                {
                    var size = kcp.PeekSize();
                    if (size > 0)
                    {
                        r = kcp.Recv(kcpRcvBuf, 0, kcpRcvBuf.Length);
                        if (r <= 0)
                        {
                            break;
                        }
                        handler(kcpRcvBuf, size);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public void Update(UInt32 current)
        {
            ProcessRecv(current);
            var err = GetError();
            if (err != null)
            {
                throw err;
            }
            if (needUpdate || current > nextUpdateTime)
            {
                kcp.Update(current);
                nextUpdateTime = kcp.Check(current);
                needUpdate = false;
            }
            CheckTimeout(current);
        }
    }
}