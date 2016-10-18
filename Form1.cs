using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UDPBroadcast
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            //StartListen();
        }
        public class UDPPack
        {
            public byte[] buffer;
            public Socket listener;
        }
        private void StartListen()
        {
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var listenAddress = new IPEndPoint(IPAddress.Any, 8552);
            listener.Bind(listenAddress);
            var udpPack =  new UDPPack(){ buffer = new byte[1024], listener = listener };
            EndPoint ep = (EndPoint)listenAddress;
            listener.BeginReceiveFrom(udpPack.buffer, 0, 1024, SocketFlags.None, ref ep, EndRecv, udpPack);
        }

        private void EndRecv(IAsyncResult result)
        {
            var context = result.AsyncState as UDPPack;
            EndPoint recv = new IPEndPoint(IPAddress.Broadcast, 8552);
            int recvCount = context.listener.EndReceiveFrom(result, ref recv);
            if (recvCount > 0)
            {
                var str = Encoding.ASCII.GetString(context.buffer, 0, recvCount);
                this.Invoke((Action)(
                    () =>
                    {
                        this.textBox1.Text += (DateTime.Now.ToString() + ":" + str + "\r\n");
                    }));
            }
            var listener = context.listener;
            var udpPack = new UDPPack() { buffer = new byte[1024], listener = listener };
            var ep = (EndPoint)new IPEndPoint(IPAddress.Any, 8552);
            listener.BeginReceiveFrom(udpPack.buffer, 0, 1024, SocketFlags.None, ref ep, EndRecv, udpPack);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Socket SendSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            SendSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
            var buf = Encoding.ASCII.GetBytes("this is test");
            SendSock.SendTo(buf, new IPEndPoint(IPAddress.Broadcast, 8552));
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var listener = new ChromeListener(new IPEndPoint(IPAddress.Any, 8558));
            listener.Start();
        }
    }
}
