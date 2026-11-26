// 肥鱼阅读器 · 文本提取层
// 负责把各种格式统一成纯文本：纯文本/代码、RTF、HTML、DOCX、ODT、EPUB、以及编码识别。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TxtReader
{
    internal static class TextExtract
    {
        internal sealed class Result
        {
            public string Text;
            public string Encoding;   // 例如 UTF-8 / GBK
            public string Format;     // 例如 文本 / RTF / DOCX / EPUB / HTML
            public bool Extracted;    // true 表示经过格式转换而不是直接解码
        }

        private static readonly string[] TextExts = new string[]
        {
            ".txt", ".log", ".md", ".markdown", ".text", ".csv", ".tsv",
            ".json", ".xml", ".yml", ".yaml", ".ini", ".conf", ".cfg", ".properties", ".env",
            ".srt", ".ass", ".ssa", ".lrc", ".vtt", ".sub",
            ".html", ".htm", ".xhtml", ".css", ".js", ".mjs", ".ts", ".tsx", ".jsx",
            ".py", ".rb", ".go", ".rs", ".java", ".kt", ".cs", ".c", ".h", ".cpp", ".hpp",
            ".php", ".pl", ".lua", ".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1",
            ".sql", ".tex", ".rst", ".adoc", ".gitignore", ".editorconfig"
        };

        /// <summary>该扩展名是否按纯文本直接解码。</summary>
        public static bool IsPlainText(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return true; // 无扩展名多为文本/脚本
            return Array.IndexOf(TextExts, ext) >= 0;
        }

        /// <summary>给 OpenFileDialog 用的筛选器。</summary>
        public const string DialogFilter =
            "所有支持的格式|*.txt;*.md;*.log;*.rtf;*.docx;*.odt;*.epub;*.html;*.htm;*.csv;*.json;*.srt;*.ass;*.vtt;*.xml;*.yml" +
            "|文本与代码|*.txt;*.md;*.log;*.csv;*.tsv;*.json;*.xml;*.yml;*.yaml;*.ini;*.conf;*.srt;*.ass;*.vtt;*.lrc;*.py;*.js;*.ts;*.cs;*.cpp;*.java;*.go;*.rs;*.sql;*.sh;*.ps1" +
            "|网页|*.html;*.htm;*.xhtml" +
            "|Word / 电子书|*.docx;*.odt;*.epub;*.rtf" +
            "|所有文件 (*.*)|*.*";

        /* ---------------- 对外入口 ---------------- */

        /// <summary>
        /// 读取任意支持的文件并返回纯文本。forcedEncoding 为 "auto"/"utf-8"/"gbk"/"big5"/"utf-16le"。
        /// </summary>
        public static Result FromFile(string path, string forcedEncoding)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            byte[] bytes = File.ReadAllBytes(path);

            if (LooksLikePdf(bytes))
                throw new NotSupportedException("PDF 请先转成文本或 Word 再打开（PDF 的文字层解析需要额外组件）。");

            switch (ext)
            {
                case ".rtf":
                    return new Result { Text = FromRtf(path), Encoding = "RTF", Format = "RTF 富文本", Extracted = true };

                case ".docx":
                case ".odt":
                case ".epub":
                    return FromZipPackage(path, ext);

                case ".html":
                case ".htm":
                case ".xhtml":
                    {
                        string decoded;
                        string enc = DecodeText(bytes, forcedEncoding, out decoded);
                        return new Result { Text = HtmlToText(decoded), Encoding = enc, Format = "HTML（已提取正文）", Extracted = true };
                    }
            }

            if (IsZip(bytes) && (ext == ".doc" || ext == ".xls" || ext == ".ppt" || ext == ".zip"))
                throw new NotSupportedException("这是压缩包或旧版 Office 二进制格式，请先解压或另存为新格式。");

            if (!LooksLikeText(bytes))
                throw new NotSupportedException("这个文件看起来不是文本（含大量二进制字节），暂不支持。");

            string text;
            string encoding = DecodeText(bytes, forcedEncoding, out text);
            string label = FormatLabel(ext);
            return new Result { Text = text, Encoding = encoding, Format = label, Extracted = false };
        }

        private static string FormatLabel(string ext)
        {
            switch (ext)
            {
                case ".md": case ".markdown": return "Markdown";
                case ".csv": case ".tsv": return "表格文本";
                case ".json": return "JSON";
                case ".xml": return "XML";
                case ".yml": case ".yaml": return "YAML";
                case ".srt": case ".ass": case ".ssa": case ".vtt": return "字幕";
                case ".log": return "日志";
                default:
                    if (ext.Length > 1) return "文本 " + ext.Substring(1).ToUpperInvariant();
                    return "文本";
            }
        }

        /* ---------------- 编码 ---------------- */

        public static string DecodeText(byte[] bytes, string forced, out string text)
        {
            text = string.Empty;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
                return "UTF-8 (BOM)";
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                return "UTF-16 LE";
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                return "UTF-16 BE";
            }

            string f = (forced ?? "auto").ToLowerInvariant();
            string forcedName = null;
            Encoding forcedEnc = null;
            if (f == "utf-8") { forcedEnc = new UTF8Encoding(false, true); forcedName = "UTF-8"; }
            else if (f == "gbk") { forcedEnc = Encoding.GetEncoding(936); forcedName = "GBK"; }
            else if (f == "big5") { forcedEnc = Encoding.GetEncoding(950); forcedName = "Big5"; }
            else if (f == "utf-16le") { forcedEnc = Encoding.Unicode; forcedName = "UTF-16"; }

            if (forcedEnc != null && TryDecode(bytes, forcedEnc, out text)) return forcedName;

            if (TryDecode(bytes, new UTF8Encoding(false, true), out text)) return "UTF-8";
            if (TryDecode(bytes, Encoding.GetEncoding(936), out text)) return "GBK";
            if (TryDecode(bytes, Encoding.GetEncoding(950), out text)) return "Big5";
            text = new UTF8Encoding(false).GetString(bytes);
            return "UTF-8 (疑似)";
        }

        private static bool TryDecode(byte[] bytes, Encoding enc, out string text)
        {
            try { text = enc.GetString(bytes); return true; }
            catch { text = null; return false; }
        }

        /* ---------------- 判定 ---------------- */

        private static bool LooksLikePdf(byte[] b)
        {
            return b.Length >= 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F';
        }

        private static bool IsZip(byte[] b)
        {
            return b.Length >= 4 && b[0] == 'P' && b[1] == 'K' && (b[2] == 3 || b[2] == 5 || b[2] == 7);
        }

        /// <summary>前 8KB 里出现 NUL 基本可判定为二进制。</summary>
        public static bool LooksLikeText(byte[] bytes)
        {
            int n = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < n; i++) if (bytes[i] == 0) return false;
            return true;
        }

        /* ---------------- RTF ---------------- */

        private static string FromRtf(string path)
        {
            using (var rtb = new RichTextBox())
            {
                rtb.LoadFile(path, RichTextBoxStreamType.RichText);
                return rtb.Text.Replace("\r\n", "\n");
            }
        }

        /* ---------------- ZIP 包（DOCX / ODT / EPUB） ---------------- */

        private static Result FromZipPackage(string path, string ext)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                if (ext == ".docx")
                {
                    var entry = FindEntry(zip, "word/document.xml");
                    if (entry == null) throw new NotSupportedException("这个 docx 里没找到正文（word/document.xml）。");
                    string xml = ReadEntry(entry);
                    return new Result { Text = WordXmlToText(xml), Encoding = "UTF-8", Format = "Word 文档（已提取文本）", Extracted = true };
                }
                if (ext == ".odt")
                {
                    var entry = FindEntry(zip, "content.xml");
                    if (entry == null) throw new NotSupportedException("这个 odt 里没找到正文（content.xml）。");
                    return new Result { Text = HtmlToText(ReadEntry(entry)), Encoding = "UTF-8", Format = "OpenDocument（已提取文本）", Extracted = true };
                }

                // EPUB：按文件名顺序拼接所有 xhtml/html
                var parts = new List<ZipArchiveEntry>();
                foreach (var e in zip.Entries)
                {
                    string n = e.FullName.ToLowerInvariant();
                    if (n.EndsWith(".xhtml") || n.EndsWith(".html") || n.EndsWith(".htm")) parts.Add(e);
                }
                if (parts.Count == 0) throw new NotSupportedException("这个 epub 里没找到 HTML 正文。");
                parts.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

                var sb = new StringBuilder();
                foreach (var e in parts)
                {
                    string html = ReadEntry(e);
                    string title = Regex.Match(html, @"<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
                    if (!string.IsNullOrEmpty(title))
                        sb.AppendLine().AppendLine(ToFullWidthTitle(HtmlDecode(title))).AppendLine();
                    sb.AppendLine(HtmlToText(html));
                }
                return new Result { Text = Collapse(sb.ToString()), Encoding = "UTF-8", Format = "EPUB 电子书（已提取文本）", Extracted = true };
            }
        }

        private static string ToFullWidthTitle(string s)
        {
            return "【" + s.Trim() + "】";
        }

        private static ZipArchiveEntry FindEntry(ZipArchive zip, string name)
        {
            foreach (var e in zip.Entries)
                if (string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
            {
                return reader.ReadToEnd();
            }
        }

        /* ---------------- DOCX 的 XML → 文本 ---------------- */

        private static string WordXmlToText(string xml)
        {
            // 段落、换行、制表符先转成占位符，再统一清标签
            string s = Regex.Replace(xml, @"<w:p[ >]", "\n<w:p ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<w:br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<w:tab\s*/?>", "\t", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</w:p>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<w:tc[ >]", "\t<w:tc ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", string.Empty);
            return Collapse(HtmlDecode(s));
        }

        /* ---------------- HTML/XML → 文本 ---------------- */

        private static readonly Regex ScriptStyle = new Regex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BlockBreak = new Regex(@"<\s*(br|/p|/div|/li|/tr|/h[1-6]|/section|/article|/blockquote|/pre)\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        public static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string s = ScriptStyle.Replace(html, string.Empty);
            s = BlockBreak.Replace(s, "\n");
            s = AnyTag.Replace(s, string.Empty);
            s = HtmlDecode(s);
            return Collapse(s);
        }

        private static string HtmlDecode(string s)
        {
            s = s.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                 .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'");
            // 数字实体
            s = Regex.Replace(s, @"&#(\d+);", m =>
            {
                int code;
                if (int.TryParse(m.Groups[1].Value, out code) && code > 0 && code < 0x10FFFF)
                    return char.ConvertFromUtf32(code);
                return m.Value;
            });
            return s;
        }

        /// <summary>压缩空白：行内多空格合一、连续空行最多保留一行。</summary>
        public static string Collapse(string text)
        {
            if (text == null) return string.Empty;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = text.Split('\n');
            var sb = new StringBuilder();
            int blank = 0;
            foreach (var raw in lines)
            {
                string line = Regex.Replace(raw, @"[ \t\u00A0]+", " ").TrimEnd();
                if (line.Trim().Length == 0)
                {
                    blank++;
                    if (blank > 1) continue;
                    sb.Append('\n');
                }
                else
                {
                    blank = 0;
                    sb.Append(line).Append('\n');
                }
            }
            return sb.ToString().Trim() + "\n";
        }
    }
}
