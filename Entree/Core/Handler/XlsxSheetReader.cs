// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Entree.Core.Helpers;

namespace Entree.Core.Handler;

internal sealed record XlsxWorkbook(string FilePath, IReadOnlyList<(string Name, string Target)> Sheets,
                                    string[] SharedStrings);

internal static partial class XlsxSheetReader
{
   private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
   private const string RelNs  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
   private const int    ChunkSize = 1 << 16;

   private static readonly XmlReaderSettings Settings = new()
                                                        {
                                                                 DtdProcessing    = DtdProcessing.Prohibit,
                                                                 IgnoreComments   = true,
                                                                 IgnoreWhitespace = false,
                                                                 XmlResolver      = null
                                                        };

   public static XlsxWorkbook Load(string filePath)
   {
      using var archive = Open(filePath);
      return new XlsxWorkbook(filePath, SheetTargets(archive), ReadSharedStrings(archive));
   }

   public static IEnumerable<string?[]> ReadRows(XlsxWorkbook workbook, string sheetName,
                                                 CancellationToken cancellationToken = default)
   {
      using var archive = Open(workbook.FilePath);
      var target = workbook.Sheets.FirstOrDefault(s => s.Name == sheetName).Target ??
                   throw new InvalidDataException($"Sheet '{sheetName}' not found in {workbook.FilePath}");
      var entry = archive.GetEntry(target) ?? throw new InvalidDataException($"Missing sheet part {target}");

      using var text    = new StreamReader(entry.Open(), Encoding.UTF8, true, ChunkSize);
      var       scanner = new SheetScanner(workbook.SharedStrings);
      var       buffer  = new char[ChunkSize];
      var       length  = 0;
      var       rows    = new List<string?[]>();
      while (true)
      {
         cancellationToken.ThrowIfCancellationRequested();
         if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
         var read = text.ReadBlock(buffer, length, buffer.Length - length);
         length += read;
         var consumed = scanner.Scan(buffer, length, read == 0, rows);
         foreach (var row in rows) yield return row;
         rows.Clear();
         if (read == 0) yield break;
         Array.Copy(buffer, consumed, buffer, 0, length - consumed);
         length -= consumed;
      }
   }

   private static ZipArchive Open(string filePath) =>
            new(SharedFile.OpenRead(filePath), ZipArchiveMode.Read, false);

   private static string ReadStringItem(XmlReader xml, Names names)
   {
      if (xml.IsEmptyElement)
      {
         xml.Read();
         return string.Empty;
      }

      var                        depth   = xml.Depth;
      string?                    single  = null;
      StringBuilder? builder = null;
      xml.Read();
      while (xml.Depth > depth)
      {
         if (xml.NodeType != XmlNodeType.Element)
         {
            if (!xml.Read()) break;
            continue;
         }

         if (ReferenceEquals(xml.LocalName, names.Text))
         {
            var text = xml.ReadElementContentAsString();
            if (single is null) single = text;
            else (builder ??= new StringBuilder(single)).Append(text);
         }
         else if (ReferenceEquals(xml.LocalName, names.Phonetic))
            xml.Skip();
         else if (!xml.Read()) break;
      }

      xml.Read();
      return Unescape(builder?.ToString() ?? single ?? string.Empty);
   }

   private static string[] ReadSharedStrings(ZipArchive archive)
   {
      var entry = archive.GetEntry("xl/sharedStrings.xml");
      if (entry is null) return [];
      using var stream  = entry.Open();
      using var xml     = XmlReader.Create(stream, Settings);
      var       names   = new Names(xml.NameTable);
      var       strings = new List<string>();
      xml.Read();
      while (!xml.EOF)
         if (xml.NodeType == XmlNodeType.Element && ReferenceEquals(xml.LocalName, names.StringItem))
            strings.Add(ReadStringItem(xml, names));
         else
            xml.Read();
      return [.. strings];
   }

   private static List<(string Name, string Target)> SheetTargets(ZipArchive archive)
   {
      var relations = new Dictionary<string, string>(StringComparer.Ordinal);
      using (var stream = (archive.GetEntry("xl/_rels/workbook.xml.rels") ??
                           throw new InvalidDataException("Missing workbook relationships")).Open())
      using (var xml = XmlReader.Create(stream, Settings))
         while (xml.Read())
            if (xml is { NodeType: XmlNodeType.Element, LocalName: "Relationship" } &&
                xml.GetAttribute("Id") is { } id && xml.GetAttribute("Target") is { } target)
               relations[id] = target.StartsWith('/') ? target[1..] : "xl/" + target;

      var sheets = new List<(string, string)>();
      using (var stream = (archive.GetEntry("xl/workbook.xml") ??
                           throw new InvalidDataException("Missing workbook part")).Open())
      using (var xml = XmlReader.Create(stream, Settings))
         while (xml.Read())
            if (xml is { NodeType: XmlNodeType.Element, LocalName: "sheet", NamespaceURI: MainNs } &&
                xml.GetAttribute("name") is { } name && xml.GetAttribute("id", RelNs) is { } relId &&
                relations.TryGetValue(relId, out var target))
               sheets.Add((name, target));
      return sheets;
   }

   internal static string Unescape(string text) =>
            text.Contains("_x", StringComparison.Ordinal)
                     ? EscapedChar().Replace(text, m => ((char)Convert.ToUInt16(m.Groups[1].Value, 16)).ToString())
                     : text;

   [GeneratedRegex("_x([0-9A-Fa-f]{4})_")]
   private static partial Regex EscapedChar();

   internal static int ColumnIndex(ReadOnlySpan<char> reference, int fallback)
   {
      var index = 0;
      foreach (var c in reference)
      {
         if (c is < 'A' or > 'Z') break;
         index = index * 26 + (c - 'A' + 1);
         if (index > ReportHelper.MaxSheetColumns) return fallback;
      }

      return index == 0 ? fallback : index - 1;
   }

   private sealed class Names(XmlNameTable table)
   {
      public readonly string Phonetic   = table.Add("rPh");
      public readonly string StringItem = table.Add("si");
      public readonly string Text       = table.Add("t");
   }

   private sealed class SheetScanner(string[] sharedStrings)
   {
      private readonly List<string?> _row  = [];
      private readonly StringBuilder _text = new();
      private          bool          _capture, _inCell, _inline, _inRow, _phonetic;
      private          int           _column, _depth;
      private          string?       _type, _value;

      public int Scan(char[] buffer, int length, bool atEnd, List<string?[]> rows)
      {
         var position = 0;
         while (position < length)
         {
            var open = Array.IndexOf(buffer, '<', position, length - position);
            if (open < 0) return atEnd ? Finish(length) : position;
            if (_capture) AppendText(buffer, position, open, true);
            var close = TagEnd(buffer, open, length, atEnd);
            if (close < 0)
            {
               if (atEnd) throw new XmlException("Sheet part ends inside a tag");
               return open;
            }

            Tag(buffer, open, close, rows);
            position = close + 1;
         }

         return atEnd ? Finish(position) : position;
      }

      private int Finish(int length) =>
               _depth == 0 ? length : throw new XmlException("Sheet part ends before its elements are closed");

      private static int TagEnd(char[] buffer, int open, int length, bool atEnd)
      {
         var span = buffer.AsSpan(open, length - open);
         if (span.Length > 1 && span[1] == '!')
         {
            if (span.Length < 9 && !atEnd) return -1;
            var terminator = span.StartsWith("<!--") ? "-->" :
                             span.StartsWith("<![CDATA[") ? "]]>" :
                             throw new XmlException("DTD and declarations are not allowed in a sheet part");
            var found = span.IndexOf(terminator);
            return found < 0 ? -1 : open + found + 2;
         }

         for (var i = 1; i < span.Length;)
         {
            var next = span[i..].IndexOfAny('>', '"', '\'');
            if (next < 0) return -1;
            i += next;
            if (span[i] == '>') return open + i;
            var quote = span[(i + 1)..].IndexOf(span[i]);
            if (quote < 0) return -1;
            i += quote + 2;
         }

         return -1;
      }

      private void Tag(char[] buffer, int open, int close, List<string?[]> rows)
      {
         var kind = buffer[open + 1];
         if (kind == '!')
         {
            if (_capture && buffer[open + 2] == '[') AppendText(buffer, open + 9, close - 2, false);
            return;
         }

         if (kind == '?') return;
         var closing   = kind == '/';
         var nameStart = open + (closing ? 2 : 1);
         var nameEnd   = buffer.AsSpan(nameStart, close - nameStart).IndexOfAny(" \t\r\n/>");
         var qualified = buffer.AsSpan(nameStart, nameEnd < 0 ? close - nameStart : nameEnd);
         var name      = qualified[(qualified.IndexOf(':') + 1)..];
         if (closing)
         {
            _depth--;
            End(name, rows);
            return;
         }

         Start(name, buffer.AsSpan(nameStart + qualified.Length, close - nameStart - qualified.Length));
         if (buffer[close - 1] == '/') End(name, rows);
         else _depth++;
      }

      private void Start(ReadOnlySpan<char> name, ReadOnlySpan<char> attributes)
      {
         switch (name)
         {
            case "row":
               _row.Clear();
               _inRow = true;
               break;
            case "c" when _inRow:
               _inCell = true;
               _column = ColumnIndex(Attribute(attributes, "r"), _row.Count);
               _type   = Attribute(attributes, "t") switch { "s" => "s", "str" => "str", _ => null };
               _value  = null;
               break;
            case "v" when _inCell:
            case "is" when _inCell:
               _text.Clear();
               _inline  = name is "is";
               _capture = !_inline;
               break;
            case "t" when _inline && !_phonetic:
               _capture = true;
               break;
            case "rPh" when _inline:
               _phonetic = true;
               break;
         }
      }

      private void End(ReadOnlySpan<char> name, List<string?[]> rows)
      {
         switch (name)
         {
            case "row" when _inRow:
               rows.Add(_row.ToArray());
               _inRow = false;
               break;
            case "c" when _inCell:
               _inCell = false;
               Commit();
               break;
            case "v" when _inCell && !_inline:
               _value   = _text.ToString();
               _capture = false;
               break;
            case "is" when _inline:
               _value   = Unescape(_text.ToString());
               _inline  = false;
               _capture = false;
               break;
            case "t" when _inline:
               _capture = false;
               break;
            case "rPh":
               _phonetic = false;
               break;
         }
      }

      private void Commit()
      {
         var value = _type switch
                     {
                              "s" => int.TryParse(_value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                                     index < sharedStrings.Length
                                       ? sharedStrings[index]
                                       : null,
                              "str" when _value is not null => Unescape(_value),
                              _                             => _value
                     };
         while (_row.Count < _column) _row.Add(null);
         if (_row.Count == _column) _row.Add(value);
         else _row[_column] = value;
      }

      private static ReadOnlySpan<char> Attribute(ReadOnlySpan<char> attributes, string name)
      {
         while (true)
         {
            var equals = attributes.IndexOf('=');
            if (equals < 0) return default;
            var key   = attributes[..equals].Trim();
            var rest  = attributes[(equals + 1)..].TrimStart();
            if (rest.IsEmpty || rest[0] is not ('"' or '\'')) return default;
            var end   = rest[1..].IndexOf(rest[0]);
            if (end < 0) return default;
            if (key.SequenceEqual(name)) return rest.Slice(1, end);
            attributes = rest[(end + 2)..];
         }
      }

      private void AppendText(char[] buffer, int start, int end, bool entities)
      {
         var span = buffer.AsSpan(start, end - start);
         while (!span.IsEmpty)
         {
            var special = entities ? span.IndexOfAny('&', '\r') : span.IndexOf('\r');
            if (special < 0)
            {
               _text.Append(span);
               return;
            }

            _text.Append(span[..special]);
            span = span[special..];
            if (span[0] == '\r')
            {
               _text.Append('\n');
               span = span[(span.Length > 1 && span[1] == '\n' ? 2 : 1)..];
               continue;
            }

            var semicolon = span.IndexOf(';');
            if (semicolon < 0) throw new XmlException("Unterminated entity reference in a sheet part");
            _text.Append(Entity(span[1..semicolon]));
            span = span[(semicolon + 1)..];
         }
      }

      private static string Entity(ReadOnlySpan<char> name) =>
               name switch
               {
                        "lt"   => "<",
                        "gt"   => ">",
                        "amp"  => "&",
                        "quot" => "\"",
                        "apos" => "'",
                        ['#', 'x', .. var hex] when int.TryParse(hex, NumberStyles.AllowHexSpecifier,
                                                                 CultureInfo.InvariantCulture, out var code) =>
                                 char.ConvertFromUtf32(code),
                        ['#', .. var dec] when int.TryParse(dec, NumberStyles.None, CultureInfo.InvariantCulture,
                                                            out var code) => char.ConvertFromUtf32(code),
                        _ => throw new XmlException($"Unknown entity &{name};")
               };
   }
}
