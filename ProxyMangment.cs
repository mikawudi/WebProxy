using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UDPBroadcast
{
    public class ProxyMangment
    {
        #region 单例部分
        private static ProxyMangment _instance;
        public static ProxyMangment GetInstance()
        {
            if (_instance != null)
                return _instance;
            lock(typeof(ProxyMangment))
            {
                if (_instance == null)
                    _instance = new ProxyMangment();
            }
            return _instance;
        }
        #endregion
        public Dictionary<IPAddress, RemoteClient> ConDic = new Dictionary<IPAddress, RemoteClient>();
        private ProxyMangment()
        {

        }
        public void SendData(string host, byte[] requestPack, Socket resultContext)
        {
            RemoteClient client = null;
            try
            {
                var result = Dns.GetHostEntry(host);
                var address = result.AddressList[0];
                lock (ConDic)
                {
                    if (!ConDic.TryGetValue(address, out client))
                    {
                        client = new RemoteClient(address);
                        client.ConnectionClose += client_ConnectionClose;
                        client.ConnectFail += client_ConnectFail;
                        ConDic.Add(address, client);
                    }
                }
                client.AddDataToQueue(requestPack, resultContext);
            }
            catch
            {
                
            }
        }

        void client_ConnectFail(RemoteClient obj, Exception arg2)
        {
            lock (ConDic)
            {
                ConDic.Remove(obj.address);
            }
        }

        void client_ConnectionClose(RemoteClient obj)
        {
            lock(ConDic)
            {
                ConDic.Remove(obj.address);
            }
        }
    }

    public class RemoteClient
    {
        public string hostInfo;
        public Socket SockObj;
        Queue<SendPack> queue = new Queue<SendPack>();
        private AutoResetEvent evn = new AutoResetEvent(false);
        public event Action<RemoteClient, Exception> ConnectFail;
        protected virtual void OnConnectFail(Exception ex)
        {
            if (ConnectFail != null)
                ConnectFail(this, ex);
        }
        public event Action<RemoteClient> ConnectionClose;
        protected virtual void OnConnectionClose()
        {
            if (ConnectionClose != null)
                ConnectionClose(this);
        }
        private byte[] buffer = new byte[1024];
        private List<byte> dataList = new List<byte>();
        public IPAddress address = null;
        bool isConnect = false;
        bool isFailed = false;
        public RemoteClient(IPAddress address)
        {
            this.address = address;
            this.ConnectionClose += RemoteClient_ConnectionClose;
            this.ConnectFail += RemoteClient_ConnectFail;
            this.SockObj = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.SockObj.BeginConnect(new IPEndPoint(address, 80), EndConnect, SockObj);
        }

        void RemoteClient_ConnectFail(RemoteClient arg1, Exception arg2)
        {
            this.isFailed = true;
        }

        void RemoteClient_ConnectionClose(RemoteClient obj)
        {
            this.isConnect = false;
            this.evn.Set();
        }
        private void EndConnect(IAsyncResult result)
        {
            try
            {
                this.SockObj.EndConnect(result);
                isConnect = true;
                this.SockObj.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, EndRecv, null);
                new Thread(WorkThread).Start();
            }
            catch(Exception ex)
            {
                OnConnectFail(ex);
            }
        }
        private void EndRecv(IAsyncResult result)
        {
            int recvCount = 0;
            try
            {
                recvCount = this.SockObj.EndReceive(result);
            }
            catch(Exception ex)
            {
                OnConnectionClose();
                return;
            }
            if (recvCount == 0)
            {
                OnConnectionClose();
                return;
            }
            this.dataList.AddRange(this.buffer.Take(recvCount));
            var respSet = ResponseParse.GetInfo(this.dataList.ToArray());
            if (respSet.Count > 0)
            {
                foreach (var resp in respSet)
                {
                    SendPack sp = null;
                    lock(this.queue)
                    {
                        if (queue.Count > 0)
                            sp = queue.Dequeue();
                    }
                    if (sp != null)
                        sp.Result.Send(this.dataList.Take(resp.BufferCount).ToArray());
                    this.dataList.RemoveRange(0, resp.BufferCount);
                }
            }
            this.SockObj.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, EndRecv, null);
        }
        private void WorkThread()
        {
            while(true)
            {
                this.evn.WaitOne();
                if (this.isConnect == false)
                {
                    while(queue.Count > 0)
                    {
                        try
                        {
                            queue.Dequeue().Result.Close();
                        }
                        catch{

                        }
                    }
                    return;
                }
                SendPack pack = null;
                foreach(var q in queue)
                {
                    if (q.IsSend == false)
                    {
                        pack = q;
                        break;
                    }
                }
                if (pack == null)
                    continue;
                pack.IsSend = true;
                try
                {
                    this.SockObj.BeginSend(pack.Data, 0, pack.Data.Length, SocketFlags.None, EndSend, pack);
                }
                catch
                {
                    pack.IsSend = false;
                    Thread.Sleep(300);
                    continue;
                }
                bool hasNoSendData = false;
                lock (queue)
                {
                    foreach(var q in queue)
                    {
                        if (q.IsSend == false)
                            hasNoSendData = true;
                    }
                }
                if (hasNoSendData)
                    this.evn.Set();
            }
        }
        private void EndSend(IAsyncResult result)
        {
            try
            {
                var pack = result.AsyncState as SendPack;
                var sendCount = this.SockObj.EndSend(result);
            }
            catch
            {

            }
        }
        public void AddDataToQueue(byte[] data, Socket context)
        {
            lock (queue)
            {
                queue.Enqueue(new SendPack(data, context));
                evn.Set();
            }
        }
        //public void AddDataToQueue(byte[] data, Socket context)
        //{
        //    lock (queue)
        //    {
        //        var c = new SendPack(data, context);
        //        c.IsSend = true;
        //        queue.Enqueue(c);
        //        while(true)
        //        {
        //            if(this.isConnect == false && this.isFailed == false)
        //            {
        //                Thread.Sleep(100);
        //                continue;
        //            }
        //            break;
        //        }
        //        if (this.isConnect == false || this.isFailed == true)
        //            return;
        //        //evn.Set();
        //        try
        //        {
        //            this.SockObj.BeginSend(data, 0, data.Length, SocketFlags.None, endSend, data);
        //        }
        //        catch
        //        {
        //            this.isConnect = false;
        //            this.isFailed = true;
        //            context.Close();
        //        }
        //    }
        //}
        private void endSend(IAsyncResult result)
        {
            try
            {
                var data = result.AsyncState as byte[];
                int sendData = this.SockObj.EndSend(result);
                if (sendData != data.Length)
                    return;
            }
            catch
            {

            }
        }
        public class SendPack
        {
            public byte[] Data;
            public Socket Result;
            public bool IsSend;
            public SendPack(byte[] data, Socket result)
            {
                this.Data = data;
                this.Result = result;
                this.IsSend = false;
            }
        }
    }
}
