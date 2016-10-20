using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UDPBroadcast
{
    public class ChromeListener
    {
        public Socket Listener { get; private set; }
        public bool IsClose { get; private set; }
        public ChromeListener(IPEndPoint ipendpoint)
        {
            this.Listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.Listener.Bind(ipendpoint);
            this.Listener.Listen(10);
        }
        public void Start()
        {
            try
            {
                this.Listener.BeginAccept(EndAccept, null);
            }
            catch
            {

            }
        }
        private void EndAccept(IAsyncResult result)
        {
            Socket client = null;
            try
            {
                client = this.Listener.EndAccept(result);
            }
            catch
            {

            }
            if (client != null)
            {
                var httpParse = new HttpParse(client);
                httpParse.RecvreqHeadSuccess += httpParse_RecvreqHeadSuccess;
                httpParse.RecvReqLineSuccess += httpParse_RecvReqLineSuccess;
                httpParse.RecvRequestSuccess += httpParse_RecvRequestSuccess;
                httpParse.Close += httpParse_Close;
                httpParse.StartRecvAndParse();
            }
            if (!IsClose)
            {
                this.Listener.BeginAccept(EndAccept, null);
            }
        }

        void httpParse_Close(HttpParse obj)
        {

        }

        void httpParse_RecvRequestSuccess(HttpParse obj)
        {
            ProxyMangment.GetInstance().SendData(obj.ReqLine.ReqURL.Host, obj.sourceData.ToArray(), obj.sock);
        }

        void httpParse_RecvReqLineSuccess(HttpParse arg1, RequestLineInfo arg2)
        {

        }

        void httpParse_RecvreqHeadSuccess(HttpParse arg1, RequestHeadInfo arg2)
        {

        }
    }
    public enum ParseState
    {
        inited,
        parseRequestLineSuccess,
        parseHeadSuccess,
        parseBodySuccess
    }
    public class HttpParse
    {
        public event Action<HttpParse, RequestLineInfo> RecvReqLineSuccess;
        public virtual void OnRecvReqLineSuccess(RequestLineInfo head)
        {
            if (RecvReqLineSuccess != null)
                RecvReqLineSuccess(this, head);
        }
        public event Action<HttpParse, RequestHeadInfo> RecvreqHeadSuccess;
        public virtual void OnRecvReqHeadSuccess(RequestHeadInfo heads)
        {
            if (RecvreqHeadSuccess != null)
                RecvreqHeadSuccess(this, heads);
        }
        public event Action<HttpParse> RecvRequestSuccess;
        protected virtual void OnRecvRequestSuccess()
        {
            if (RecvRequestSuccess != null)
                RecvRequestSuccess(this);
        }
        public event Action<HttpParse> Close;
        protected virtual void OnClose()
        {
            if (Close != null)
                Close(this);
        }

        public Socket sock;
        public RequestLineInfo ReqLine { get; private set; }
        public RequestHeadInfo ReqHead { get; private set; }
        public HttpParse(Socket socket)
        {
            this.sock = socket;
            DataList = new List<byte>();
            this.State = ParseState.inited;
        }
        protected byte[] buffer = new byte[1024];
        public List<byte> DataList { get; protected set; }
        private SocketError sockError;
        public SocketError SocketError { get { return this.sockError; } }
        public ParseState State { get; protected set; }
        public void StartRecvAndParse()
        {
            this.sock.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, out sockError, endRecv, null);
        }
        public List<byte> sourceData = new List<byte>();
        public int ReadPackCount = 0;
        private void endRecv(IAsyncResult result)
        {
            int recvCount = 0;
            try
            {
                recvCount = this.sock.EndReceive(result, out this.sockError);
            }
            catch
            {
                OnClose();
                return;
            }
            if (recvCount == 0)
            {
                OnClose();
                return;
            }
            this.DataList.AddRange(this.buffer.Take(recvCount));
            if(State == ParseState.inited)
            {
                int index = this.DataList.IndexOf((byte)('\n'));
                if(index != -1)
                {
                    var reqLineStr = DataList.Take(index - 1).ToArray();
                    var reqLine = new RequestLineInfo(Encoding.ASCII.GetString(reqLineStr));
                    sourceData.AddRange(DataList.Take(index + 1).ToArray());
                    DataList.RemoveRange(0, index + 1);
                    this.ReqLine = reqLine;
                    if (reqLine != null)
                        OnRecvReqLineSuccess(reqLine);
                    State = ParseState.parseRequestLineSuccess;
                }
            }
            if (State == ParseState.parseRequestLineSuccess)
            {
                int headIndex = -1;
                for(int i = 0; i < (this.DataList.Count - 3); i++)
                {
                    if (DataList[i] == '\r' && DataList[i + 1] == '\n' && DataList[i + 2] == '\r' && DataList[i + 3] == '\n')
                        headIndex = i;
                }
                if(headIndex != -1)
                {
                    var reqHeadStr = DataList.Take(headIndex).ToArray();
                    var reqHead = new RequestHeadInfo(Encoding.ASCII.GetString(reqHeadStr));
                    sourceData.AddRange(DataList.Take(headIndex + 4));
                    DataList.RemoveRange(0, headIndex + 4);
                    this.ReqHead = reqHead;
                    if (reqHead != null)
                        OnRecvReqHeadSuccess(reqHead);
                    State = ParseState.parseHeadSuccess;
                }
            }
            if(State == ParseState.parseHeadSuccess)
            {
                //无Content
                if(this.ReqHead.ContextLength == -1 && DataList.Count == 0)
                {
                    this.State = ParseState.parseBodySuccess;
                }
                if(this.ReqHead.ContextLength == DataList.Count)
                {
                    sourceData.AddRange(DataList.Take(DataList.Count));
                    DataList.RemoveRange(0, DataList.Count);
                    this.State = ParseState.parseBodySuccess;
                }
            }
            if (State == ParseState.parseBodySuccess)
            {
                if (ReadPackCount > 0)
                {

                }
                ReadPackCount++;
                OnRecvRequestSuccess();
                this.State = ParseState.inited;
            }
            this.sock.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, out sockError, endRecv, null);
        }
    }
    public enum ReqMethod
    {
        GET,
        POST,
        HEAD
    }
    public class RequestLineInfo
    {
        public ReqMethod RequestMethod;
        public Uri ReqURL;
        public string HttpVersion;
        public RequestLineInfo(string reqLine)
        {
            var temp = reqLine.Split(new string[]{" "}, StringSplitOptions.None);
            if(temp.Length != 3)
                throw new RequestLineParseException(reqLine, "请求行格式错误");
            switch(temp[0].ToUpper())
            {
                case "GET":
                    RequestMethod = ReqMethod.GET;
                    break;
                case "POST":
                    RequestMethod = ReqMethod.POST;
                    break;
                case "HEAD":
                    RequestMethod = ReqMethod.HEAD;
                    break;
                default:
                    throw new RequestLineParseException(reqLine, "reqMethod解析错误");
            }
            ReqURL = new Uri(temp[1]);
            HttpVersion = temp[2];
        }
    }
    public class RequestHeadInfo
    {
        private const string CONTENTLENGTH = "Content-Length";
        public Dictionary<string, string> BaseInfo = new Dictionary<string,string>();
        private int contentLength = -1;
        public int ContextLength 
        { 
            get 
            { 
                if(contentLength == -1) 
                {
                    int outdata;
                    if(BaseInfo.ContainsKey(CONTENTLENGTH) && int.TryParse(BaseInfo[CONTENTLENGTH], out outdata))
                        contentLength = outdata;
                }
                return contentLength;
            } 
        }
        public RequestHeadInfo(string requestHead)
        {
            var lineInfoArray = requestHead.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach(var line in lineInfoArray)
            {
                if(line.IndexOf(":") == -1)
                    throw new RequestHeadParseException(line, "RequestHead解析错误");
                var info = line.Split(new string[] { ": " }, StringSplitOptions.None);
                if (info.Length == 1)
                    BaseInfo.Add(info[0], null);
                else
                    BaseInfo.Add(info[0], info[1]);
            }
        }
    }


    public abstract class ReqParseException
        : Exception
    {
        public ReqParseException(string message)
            : base(message)
        {

        }
    }
    public class RequestLineParseException
        : ReqParseException
    {
        public string ReqLineSource;
        public RequestLineParseException(string reqLine, string message)
            : base(message)
        {
            this.ReqLineSource = reqLine;
        }
    }
    public class RequestHeadParseException
        : ReqParseException
    {
        public string HeadSourStr;
        public RequestHeadParseException(string headSourStr, string message)
            : base(message)
        {
            this.HeadSourStr = headSourStr;
        }
    }
}
