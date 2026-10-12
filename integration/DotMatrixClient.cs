using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace GpuLcd {
    // Where Dot Matrix Studio's local API listens; it rewrites agent-api.json each time it starts.
    public sealed class DotMatrixEndpoint {
        public int Port, Pid;
        public string Token;
        public string Host = "127.0.0.1";
        public string Signature { get { return Port + "|" + Token + "|" + Pid; } }
    }

    // Talks to Dot Matrix Studio's local API. No UI and no state, so it can be tested against a fake server.
    public static class DotMatrixClient {
        public const string TokenHeader = "x-matrix-token";

        // Parses agent-api.json; null when it is not a usable endpoint.
        public static DotMatrixEndpoint ParseEndpoint(string json) {
            var info = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (info == null) return null;
            object portValue, tokenValue, pidValue;
            int port, pid = 0;
            if (!info.TryGetValue("port", out portValue) || !int.TryParse(Convert.ToString(portValue), out port) || port < 1 || port > 65535) return null;
            var token = info.TryGetValue("token", out tokenValue) ? tokenValue as string : null;
            if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
            if (info.TryGetValue("pid", out pidValue)) int.TryParse(Convert.ToString(pidValue), out pid);
            return new DotMatrixEndpoint { Port = port, Token = token, Pid = pid };
        }

        // The scene the panel shows now (data.scene of GET /api/state), or null when it shows none.
        public static Dictionary<string, object> GetScene(DotMatrixEndpoint endpoint, int timeoutMs) {
            var req = (HttpWebRequest)WebRequest.Create("http://" + endpoint.Host + ":" + endpoint.Port + "/api/state");
            req.Method = "GET"; req.Proxy = null; req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.Headers[TokenHeader] = endpoint.Token;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                var json = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                object data, scene;
                var fields = json != null && json.TryGetValue("data", out data) ? data as Dictionary<string, object> : null;
                if (fields == null) throw new IOException("点阵屏回应格式不对");
                return fields.TryGetValue("scene", out scene) ? scene as Dictionary<string, object> : null;
            }
        }

        // POSTs one scene as a hook update; true when the panel shows it, false when a scene the agent
        // chose holds the panel for now (send again later). Throws on HTTP or format errors.
        public static bool PostScene(DotMatrixEndpoint endpoint, Dictionary<string, object> scene, int timeoutMs) {
            var req = (HttpWebRequest)WebRequest.Create("http://" + endpoint.Host + ":" + endpoint.Port + "/api/scene?source=hook");
            req.Method = "POST"; req.ContentType = "application/json"; req.Proxy = null;
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.Headers[TokenHeader] = endpoint.Token;
            var bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(scene));
            req.ContentLength = bytes.Length;
            using (var stream = req.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                var json = new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
                object data, shown;
                var fields = json != null && json.TryGetValue("data", out data) ? data as Dictionary<string, object> : null;
                if (fields == null) throw new IOException("点阵屏回应格式不对");
                return fields.TryGetValue("shown", out shown) && shown is bool && (bool)shown;
            }
        }
    }
}
