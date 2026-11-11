using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CustomStyEditor.Models;

namespace CustomStyEditor.Services
{
    /// <summary>
    /// Reads and writes .sty files. Each line keeps its own line ending and the file keeps its
    /// encoding and byte order mark, so loading and saving without edits gives the same bytes.
    /// New files are UTF-8 without BOM, CRLF.
    /// </summary>
    public static class StyFileService
    {
        public static StyDocument Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var doc = new StyDocument();
            int bomLength;
            doc.Encoding = DetectEncoding(bytes, out bomLength);
            doc.HasBom = bomLength > 0;
            string text = doc.Encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
            Split(text, doc);
            return doc;
        }

        /// <summary>Parses text that is already decoded (used for the base stylesheet and tests).</summary>
        public static StyDocument Parse(string text)
        {
            var doc = new StyDocument();
            Split(text, doc);
            return doc;
        }

        private static void Split(string text, StyDocument doc)
        {
            int crlf = 0, lf = 0;
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    string eol;
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { eol = "\r\n"; crlf++; }
                    else { eol = c.ToString(); if (c == '\n') lf++; }
                    doc.Lines.Add(StyLine.Parse(text.Substring(start, i - start), eol));
                    i += eol.Length;
                    start = i;
                }
                else
                {
                    i++;
                }
            }
            if (start < text.Length)
                doc.Lines.Add(StyLine.Parse(text.Substring(start), ""));
            doc.DefaultEol = lf > crlf ? "\n" : "\r\n";
        }

        public static void Write(StyDocument doc, string path)
        {
            var sb = new StringBuilder();
            foreach (StyLine line in doc.Lines)
                sb.Append(line.Text).Append(line.Eol);
            byte[] body = doc.Encoding.GetBytes(sb.ToString());
            byte[] bom = doc.HasBom ? doc.Encoding.GetPreamble() : new byte[0];

            // Write to a temporary file first so a failed save never truncates the original.
            string temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                fs.Write(bom, 0, bom.Length);
                fs.Write(body, 0, body.Length);
            }
            if (File.Exists(path))
            {
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        /// <summary>The bytes as they would be written (for tests).</summary>
        public static byte[] ToBytes(StyDocument doc)
        {
            var sb = new StringBuilder();
            foreach (StyLine line in doc.Lines)
                sb.Append(line.Text).Append(line.Eol);
            byte[] body = doc.Encoding.GetBytes(sb.ToString());
            byte[] bom = doc.HasBom ? doc.Encoding.GetPreamble() : new byte[0];
            return bom.Concat(body).ToArray();
        }

        /// <summary>BOM first (UTF-16 LE/BE, UTF-8); otherwise UTF-8 when the bytes are valid UTF-8, else the ANSI code page.</summary>
        public static Encoding DetectEncoding(byte[] b, out int bomLength)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                bomLength = 3;
                return new UTF8Encoding(true);
            }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            {
                bomLength = 2;
                return new UnicodeEncoding(false, true);
            }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            {
                bomLength = 2;
                return new UnicodeEncoding(true, true);
            }
            bomLength = 0;
            try
            {
                new UTF8Encoding(false, true).GetString(b);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default;
            }
        }
    }
}
