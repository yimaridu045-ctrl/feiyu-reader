// 肥鱼阅读器 v2 · 联网搜索
// 设计取舍：不内置任何 API Key。
//   1) 默认用 DuckDuckGo 即时答案 API（无需 Key，返回摘要 + 相关条目）；
//   2) 想拿真正的搜索结果列表，可在 config.ini 里填 serperKey=... 用 Serper；
//   3) 兜底永远是"在系统浏览器里搜索"。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace TxtReader
{
    internal sealed class SearchHit
    {
        public string Title;
        public string Url;
        public string Snippet;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Title) ? Url : Title;
        }
    }

    internal sealed class SearchResult
    {
        public List<SearchHit> Hits = new List<SearchHit>();
        public string Answer;        // 即时答案/摘要（可能为空）
        public string Provider;      // 使用的来源
        public string Error;         // 出错信息
        public bool Empty { get { return Hits.Count == 0 && string.IsNullOrEmpty(Answer); } }
    }

    internal static class WebSearch
    {
        private const int TimeoutMs = 15000;

        static WebSearch()
        {
            // .NET Framework 默认只用 TLS 1.0，多数搜索服务已拒绝，这里显式放开 1.1/1.2
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            }
            catch { }
        }

        public static string BrowserUrl(string query)
        {
            return "https://www.baidu.com/s?wd=" + Uri.EscapeDataString(query ?? "");
        }

        /// <summary>
        /// 联网搜索。proxy 为空则用系统代理；serperKey 为空则走 DuckDuckGo。
        /// </summary>
        public static SearchResult Query(string query, string proxy, string serperKey)
        {
            var result = new SearchResult();
            if (string.IsNullOrWhiteSpace(query))
            {
                result.Error = "搜索内容为空";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(serperKey))
            {
                if (TrySerper(query, proxy, serperKey.Trim(), result)) return result;
                // 失败则继续尝试 DuckDuckGo，把错误留在 result.Error 里但不清空已有提示
            }

            // 首选：百度百科词条（走 Bing 的 site: 限定，百度官方接口对脚本请求全封）
            TryBaiduBaike(query, proxy, result);

            // 百科没有再退到维基百科
            if (result.Empty)
            {
                if (!TryWikipedia(query, "zh", proxy, result))
                    TryWikipedia(query, "en", proxy, result);
            }
            return result;
        }

        /* ---------------- HTTP ---------------- */

        /// <summary>本次进程内已验证可用的代理，避免每次都试错。</summary>
        private static string _goodProxy;

        private static readonly string[] CommonLocalProxies = new string[]
        {
            "http://127.0.0.1:7877",   // Clash Plus
            "http://127.0.0.1:7890",   // Clash / Clash for Windows
            "http://127.0.0.1:7897",   // Clash Verge
            "http://127.0.0.1:10809",  // v2rayN
        };

        public static string DebugRaw(string url, string proxy) { return Get(url, proxy); }

        private static string Get(string url, string proxy)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(proxy)) candidates.Add(proxy.Trim());
            if (!string.IsNullOrWhiteSpace(_goodProxy)) candidates.Add(_goodProxy);
            candidates.Add(null);                     // 系统代理 / 直连

            Exception last = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    string text = GetOnce(url, candidate);
                    if (!string.IsNullOrWhiteSpace(candidate)) _goodProxy = candidate;
                    return text;
                }
                catch (WebException ex)
                {
                    last = ex;
                    // 只有连接层失败（不是 4xx/5xx）才换下一个代理候选
                    if (ex.Status == WebExceptionStatus.ProtocolError) throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            // 最后尝试常见本地代理端口
            foreach (var candidate in CommonLocalProxies)
            {
                try
                {
                    string text = GetOnce(url, candidate);
                    _goodProxy = candidate;
                    return text;
                }
                catch (Exception) { }
            }

            throw last ?? new WebException("网络不可达");
        }

        private static string GetOnce(string url, string proxy)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.UserAgent = "FeiyuReader/2.0 (Windows; .NET " + Environment.Version + ")";
            request.Proxy = BuildProxy(proxy);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static IWebProxy BuildProxy(string proxy)
        {
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                try { return new WebProxy(proxy.Trim(), true); } catch { }
            }
            return WebRequest.DefaultWebProxy;   // 跟随系统（IE/Clash 系统代理）设置
        }

        /* ---------------- DuckDuckGo ---------------- */

        private static void TryDuckDuckGo(string query, string proxy, SearchResult result)
        {
            try
            {
                string url = "https://api.duckduckgo.com/?q=" + Uri.EscapeDataString(query)
                           + "&format=json&no_html=1&skip_disambig=1";
                string json = Get(url, proxy);

                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null)
                {
                    result.Error = "搜索返回内容无法解析";
                    return;
                }

                string heading = Str(root, "Heading");
                string abstractText = Str(root, "AbstractText");
                string abstractUrl = Str(root, "AbstractURL");

                if (!string.IsNullOrEmpty(abstractText))
                {
                    result.Answer = abstractText;
                    result.Provider = "DuckDuckGo 摘要";
                    if (!string.IsNullOrEmpty(abstractUrl))
                        result.Hits.Add(new SearchHit { Title = heading, Url = abstractUrl, Snippet = abstractText });
                }

                AddItems(root, "Results", result);
                AddItems(root, "RelatedTopics", result);

                if (result.Hits.Count == 0 && string.IsNullOrEmpty(result.Answer))
                    result.Error = "DuckDuckGo 没有找到相关条目";
                if (string.IsNullOrEmpty(result.Provider)) result.Provider = "DuckDuckGo";
            }
            catch (WebException ex)
            {
                result.Error = Describe(ex);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
        }

        private static void AddItems(Dictionary<string, object> root, string key, SearchResult result)
        {
            object raw;
            if (!root.TryGetValue(key, out raw)) return;
            var list = AsList(raw);
            if (list == null) return;

            foreach (var item in list)
            {
                if (result.Hits.Count >= 15) return;
                var dict = item as Dictionary<string, object>;
                if (dict == null) continue;

                // RelatedTopics 里可能有嵌套的 Topics 分组
                object nested;
                if (dict.TryGetValue("Topics", out nested))
                {
                    var sub = AsList(nested);
                    if (sub != null)
                    {
                        foreach (var s in sub)
                        {
                            var sd = s as Dictionary<string, object>;
                            if (sd == null) continue;
                            string t = Str(sd, "Text"), u = Str(sd, "FirstURL");
                            if (!string.IsNullOrEmpty(u)) result.Hits.Add(new SearchHit { Title = t, Url = u, Snippet = t });
                        }
                        continue;
                    }
                }

                string text = Str(dict, "Text");
                string link = Str(dict, "FirstURL");
                if (string.IsNullOrEmpty(link)) continue;
                result.Hits.Add(new SearchHit { Title = text, Url = link, Snippet = text });
            }
        }

        /* ---------------- 百度百科（经 Bing site: 限定） ---------------- */

        private static void TryBaiduBaike(string query, string proxy, SearchResult result)
        {
            try
            {
                string url = "https://www.bing.com/search?count=10&q="
                           + Uri.EscapeDataString("site:baike.baidu.com " + query);
                string html = Get(url, proxy);

                var blocks = System.Text.RegularExpressions.Regex.Matches(
                    html, @"<li class=""b_algo"".*?</li>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match block in blocks)
                {
                    if (result.Hits.Count >= 12) break;
                    string body = block.Value;

                    var a = System.Text.RegularExpressions.Regex.Match(
                        body, @"<h2[^>]*>\s*<a[^>]*href=""([^""]+)""[^>]*>(.*?)</a>",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (!a.Success) continue;

                    string link = DecodeBingLink(a.Groups[1].Value);
                    if (link == null || link.IndexOf("baike.baidu.com/item/", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string title = CleanHtml(a.Groups[2].Value);
                    var p = System.Text.RegularExpressions.Regex.Match(
                        body, @"<p[^>]*>(.*?)</p>",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    string snippet = p.Success ? CleanHtml(p.Groups[1].Value) : "";

                    result.Hits.Add(new SearchHit { Title = title, Url = link, Snippet = snippet });
                }

                // 兜底：直接拼词条地址（点开由浏览器判断是否存在）
                if (result.Hits.Count == 0)
                {
                    result.Hits.Add(new SearchHit
                    {
                        Title = "百度百科：" + query,
                        Url = "https://baike.baidu.com/item/" + Uri.EscapeDataString(query),
                        Snippet = "直接打开该词条"
                    });
                }

                result.Provider = "百度百科";
                result.Error = null;
            }
            catch (Exception ex)
            {
                result.Error = "百度百科检索失败：" + Describe(ex);
            }
        }

        /// <summary>Bing 的结果链接是 /ck/a?...&amp;u=a1&lt;base64url&gt; 形式，这里还原真实地址。</summary>
        private static string DecodeBingLink(string href)
        {
            if (string.IsNullOrEmpty(href)) return null;
            href = href.Replace("&amp;", "&");
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                href.IndexOf("bing.com/ck", StringComparison.OrdinalIgnoreCase) < 0)
                return href;

            int idx = href.IndexOf("u=a1", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int end = href.IndexOf('&', idx);
            string token = end < 0 ? href.Substring(idx + 4) : href.Substring(idx + 4, end - idx - 4);

            try
            {
                string b64 = token.Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                }
                return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch { return null; }
        }

        private static string CleanHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
            s = s.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");
            return s.Trim();
        }
        /* ---------------- Wikipedia（无需 Key） ---------------- */

        private static bool TryWikipedia(string query, string lang, string proxy, SearchResult result)
        {
            try
            {
                string url = "https://" + lang + ".wikipedia.org/w/api.php?action=query&list=search&utf8=1&format=json&srlimit=8&srsearch="
                           + Uri.EscapeDataString(query);
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                var root = serializer.Deserialize<Dictionary<string, object>>(Get(url, proxy));
                if (root == null) return false;

                object q;
                if (!root.TryGetValue("query", out q)) return false;
                var qd = q as Dictionary<string, object>;
                if (qd == null) return false;

                object s;
                if (!qd.TryGetValue("search", out s)) return false;
                var rows = AsList(s);
                if (rows == null) return false;

                int before = result.Hits.Count;
                foreach (var row in rows)
                {
                    var d = row as Dictionary<string, object>;
                    if (d == null) continue;
                    string title = Str(d, "title");
                    string snippet = StripTags(Str(d, "snippet"));
                    string pageUrl = "https://" + lang + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                    result.Hits.Add(new SearchHit { Title = title, Url = pageUrl, Snippet = snippet });
                }

                if (result.Hits.Count > before)
                {
                    result.Provider = (result.Provider ?? "") + (result.Provider == null ? "" : " + ") + "维基百科(" + lang + ")";
                    result.Error = null;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                result.Error = "维基搜索失败：" + Describe(ex) + (string.IsNullOrEmpty(result.Error) ? "" : "／" + result.Error);
                return false;
            }
        }

        private static string StripTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
            s = s.Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");
            return s.Trim();
        }
        /// <summary>
        /// JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList（不是 object[]），
        /// 统一转成 IEnumerable 再枚举。
        /// </summary>
        private static System.Collections.IEnumerable AsList(object raw)
        {
            if (raw == null) return null;
            var arr = raw as object[];
            if (arr != null) return arr;
            return raw as System.Collections.IEnumerable;
        }
        private static string Str(Dictionary<string, object> dict, string key)
        {
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        /* ---------------- Serper（可选，自带 Key 才有用） ---------------- */

        private static bool TrySerper(string query, string proxy, string apiKey, SearchResult result)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("https://google.serper.dev/search");
                request.Method = "POST";
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.Proxy = BuildProxy(proxy);
                request.ContentType = "application/json";
                request.Headers.Add("X-API-KEY", apiKey);
                request.UserAgent = "FeiyuReader/2.0";

                string body = "{\"q\":" + new JavaScriptSerializer().Serialize(query) + ",\"num\":10}";
                byte[] data = Encoding.UTF8.GetBytes(body);
                request.ContentLength = data.Length;
                using (var s = request.GetRequestStream()) s.Write(data, 0, data.Length);

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    json = reader.ReadToEnd();

                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 8 * 1024 * 1024;
                var root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null) return false;

                object organic;
                if (root.TryGetValue("organic", out organic))
                {
                    var arr = AsList(organic);
                    if (arr != null)
                    {
                        foreach (var o in arr)
                        {
                            var d = o as Dictionary<string, object>;
                            if (d == null) continue;
                            result.Hits.Add(new SearchHit
                            {
                                Title = Str(d, "title"),
                                Url = Str(d, "link"),
                                Snippet = Str(d, "snippet")
                            });
                        }
                    }
                }
                result.Provider = "Serper (Google)";
                if (result.Hits.Count == 0) result.Error = "Serper 没有返回结果";
                return true;
            }
            catch (Exception ex)
            {
                result.Error = "Serper 失败：" + Describe(ex) + "；已回退到 DuckDuckGo";
                return false;
            }
        }

        private static string Describe(Exception error)
        {
            var ex = error as WebException;
            if (ex != null)
            {
                if (ex.Status == WebExceptionStatus.ProxyNameResolutionFailure ||
                    ex.Status == WebExceptionStatus.NameResolutionFailure)
                    return "域名解析失败（可能需要代理）";
                if (ex.Status == WebExceptionStatus.Timeout) return "请求超时";
                var r = ex.Response as HttpWebResponse;
                if (r != null) return "HTTP " + (int)r.StatusCode + " " + r.StatusDescription;
            }
            return error == null ? "未知错误" : error.Message;
        }
    }
}
