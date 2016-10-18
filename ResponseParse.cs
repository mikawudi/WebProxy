using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UDPBroadcast
{
    public class RespLineInfo
    {
        public string Version;
        public int RespCode;
        public string RespDescription;
        public RespLineInfo(string str)
        {
            var data = str.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            if (data.Length != 3)
                throw new Exception("Resp Line解析失败");
            if (!int.TryParse(data[1], out this.RespCode))
                throw new Exception("RespLine Code Parse failed");
            this.Version = data[0];
            this.RespDescription = data[2];
        }
    }
    public class RespHeadInfo
    {
        public Dictionary<string, string> Data = new Dictionary<string, string>();
        private int contentLength = -1;
        public int ContentLength 
        { 
            get {
                if(contentLength == -1)
                {
                    string lengStr;
                    int leng = -1;
                    if(!Data.ContainsKey("Content-Length"))
                    {
                        contentLength = 0;
                    }
                    if(Data.TryGetValue("Content-Length", out lengStr))
                    {
                        if (int.TryParse(lengStr, out leng))
                            contentLength = leng;
                    }
                }
                return contentLength;
            } 
        }
        public bool IsChunkedThrans
        {
            get 
            {
                string str = null;
                if (Data.TryGetValue("Transfer-Encoding", out str) && str == "chunked")
                    return true;
                return false;
            }
        }
        public RespHeadInfo(string str)
        {
            var temp = str.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach(var tempstr in temp)
            {
                if (tempstr.IndexOf(':') == -1)
                    throw new Exception("failedLine:" + tempstr);
                var kvp = tempstr.Split(new string[] { ": " }, StringSplitOptions.RemoveEmptyEntries);
                if(kvp.Length == 1)
                {
                    Data.Add(kvp[0], null);
                }
                if(kvp.Length == 2)
                {
                    Data.Add(kvp[0], kvp[1]);
                }
            }
        }
    }
    public class RespInfo
    {
        public RespLineInfo RespLine;
        public RespHeadInfo RespHead;
        public int BufferCount = 0;
        public static RespInfo Parse(byte[] buffer)
        {
            if(buffer == null)
                return null;
            var respLineIndex = -1;
            for(int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == '\n')
                {
                    respLineIndex = i;
                    break;
                }
            }
            if (respLineIndex == -1)
            {
                return null;
            }
            var tempRespLine = new RespLineInfo(Encoding.ASCII.GetString(buffer, 0, respLineIndex - 1));
            if (tempRespLine == null)
                return null;
            buffer = buffer.Skip(respLineIndex + 1).ToArray();
            int dataCount = (respLineIndex + 2);
            int index = -1;
            for(int i = 0; i < buffer.Length - 3; i++)
            {
                if(buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    index = i;
                    break;
                }
            }
            if (index == -1)
                return null;
            dataCount += (index + 4);
            var str = Encoding.ASCII.GetString(buffer, 0, index);
            var responseHead = new RespHeadInfo(str);
            if (responseHead == null)
                return null;
            buffer = buffer.Skip(index + 4).ToArray();
            var clen = responseHead.ContentLength;
            if (clen == -1)
                return null;
            //不包含Conten-length,有可能是非200,或者是Chunked
            if(clen == 0 && responseHead.IsChunkedThrans)
            {
                //解析Checked数据包
            }
            else if(clen > 0)
            {
                dataCount += clen;
                if (clen <= buffer.Length)
                {
                    var result = new RespInfo() { BufferCount = dataCount, RespLine = tempRespLine, RespHead = responseHead };
                    return result;
                }
                return null;
            }
            throw new Exception("length parse exception");
        }
    }
    public class ResponseParse
    {
        public static List<RespInfo> GetInfo(byte[] buffer)
        {
            var result = new List<RespInfo>();
            while(true)
            {
                var respInfo = RespInfo.Parse(buffer);
                if (respInfo == null)
                    break;
                buffer = buffer.Skip(respInfo.BufferCount).ToArray();
                result.Add(respInfo);
                if (buffer.Count() == 0)
                    break;
            }
            return result;
        }
    }
}
