// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Drawing;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LargeXlsx;
using Serilog;
using Entree.Core.BaseClass;
using Entree.MVVM.Model;
using Entree.MVVM.ViewModel;

namespace Entree.Core.Helpers;

public static class ReportHelper
{
   public const int    MaxRows               = 1_000_000;
   public const double MaxMatrixIdentityPart = 10000;
   public const int    MaxSheetColumns       = 16_384;

   private static readonly XlsxStyle ForestFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 15, 203, 106)));

   private static readonly XlsxStyle PinkFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 255, 47, 245)));

   private static readonly XlsxStyle PurpleFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 163, 71, 255)));

   private static readonly XlsxStyle BlueFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 34, 164, 250)));

   private static readonly XlsxStyle YellowFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 239, 239, 65)));

   private static readonly XlsxStyle OrangeFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 255, 132, 0)));

   private static readonly XlsxStyle RedFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 255, 55, 0)));

   private static readonly XlsxStyle DenyFill =
            XlsxStyle.Default.With(new XlsxFill(Color.FromArgb(255, 192, 0, 0)));

   private static readonly Color ForestGreen   = Color.FromArgb(33, 150, 70);
   private static readonly Color PantoneOrange = Color.FromArgb(251, 97, 7);

   public static readonly XlsxStyle HeaderStyle = XlsxStyle.Default.With(XlsxStyle.Default.Font
                                                                                 .With(Color.Azure)
                                                                                 .WithBold())
                                                           .With(new XlsxFill(ForestGreen));

   public static readonly XlsxStyle GroupHeaderStyle = HeaderStyle
                                                      .With(new XlsxBorder(bottom: new XlsxBorder.Line(Color.Azure,
                                                                                  XlsxBorder.Style.Medium)))
                                                      .With(new XlsxAlignment(XlsxAlignment.Horizontal.Center,
                                                                              XlsxAlignment.Vertical.Center));

   public static readonly XlsxStyle
            EnabledStyle = HeaderStyle.With(new XlsxFill(Color.FromArgb(17, 117, 0)));

   public static readonly XlsxStyle DisabledStyle = HeaderStyle.With(new XlsxFill(Color.DimGray));

   public static readonly XlsxStyle UnknownHeaderStyle = HeaderStyle
           .With(XlsxStyle.Default.Font
                          .With(Color.FromArgb(195, 247, 58))
                          .WithBold());

   public static readonly XlsxStyle DuplicateStyle = HeaderStyle.With(new XlsxFill(PantoneOrange));

   public static readonly XlsxStyle LimitedStyle = HeaderStyle.With(new XlsxFill(Color.FromArgb(112, 48, 160)));

   public static XlsxStyle AccountStyle(AccountState state, bool duplicate) =>
            duplicate ? DuplicateStyle
            : state switch
              {
                       AccountState.Enabled => EnabledStyle,
                       AccountState.Limited => LimitedStyle,
                       _                    => DisabledStyle
              };

   public static (string? Character, XlsxStyle Style) GetAccessStyle(AccessType accessType)
   {
      return accessType switch
             {
                      AccessType.None        => (string.Empty, XlsxStyle.Default),
                      AccessType.FullControl => ("F", ForestFill),
                      AccessType.Modify      => ("M", PinkFill),
                      AccessType.Write       => ("W", PurpleFill),
                      AccessType.Read        => ("R", BlueFill),
                      AccessType.List        => ("L", YellowFill),
                      AccessType.Special     => ("S", OrangeFill),
                      AccessType.Unknown     => ("?", RedFill),
                      AccessType.Deny        => ("D", DenyFill),
                      _                      => (null, XlsxStyle.Default)
             };
   }

   public static AccessType GetAccessType(FileSystemRights acl) => acl switch
   {
            _ when acl.HasFlag(FileSystemRights.FullControl) => AccessType.FullControl,
            _ when acl.HasFlag(FileSystemRights.Modify)      => AccessType.Modify,
            _ when acl.HasFlag(FileSystemRights.Write)       => AccessType.Write,
            _ when acl.HasFlag(FileSystemRights.Read)        => AccessType.Read,
            _ when acl.HasFlag(FileSystemRights.ListDirectory) => AccessType.List,
            _                                                => AccessType.Special
   };

   public static SecurityIdentifier? ParseSecurityIdentifier(object? value, int rowIndex, string? sheetName)
   {
      try
      {
         var stringValue = value as string;
         return string.IsNullOrEmpty(stringValue) ? null : new SecurityIdentifier(stringValue);
      }
      catch (Exception ex)
      {
         var sheetInfo = sheetName == null ? "" : " in sheet " + sheetName;
         Log.Error(ex, "Failed to parse '{RawValue}' to SID at row {RowNumber}{SheetName}",
                   value, rowIndex, sheetInfo);
         return null;
      }
   }

   public static T ParseEnum<T>(object? value, int rowIndex, string? sheetName) where T : struct, Enum
   {
      try
      {
         if (value == null || string.IsNullOrWhiteSpace(value.ToString())) return default;

         var stringValue = value.ToString()?.Replace(" ", "");

         if (stringValue != null && !stringValue.Contains(',')) return (T)Enum.Parse(typeof(T), stringValue, true);
         var values = stringValue?.Split(',');

         return (from enumValue in values
                 where !string.IsNullOrWhiteSpace(enumValue)
                 select (T)Enum.Parse(typeof(T), enumValue.Trim(), true))
                 .Aggregate<T, T>(default, BitwiseOr);
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Failed to parse '{RawValue}' at row {RowNumber}{SheetName}",
                   value, rowIndex, sheetName != null ? " in sheet " + sheetName : string.Empty);
         return default;
      }
   }

   private static T BitwiseOr<T>(T a, T b) where T : struct, Enum
   {
      var underlyingType = Enum.GetUnderlyingType(typeof(T));

      if (underlyingType == typeof(int)) return (T)Enum.ToObject(typeof(T), Convert.ToInt32(a) | Convert.ToInt32(b));

      if (underlyingType == typeof(uint)) return (T)Enum.ToObject(typeof(T), Convert.ToUInt32(a) | Convert.ToUInt32(b));

      if (underlyingType == typeof(long)) return (T)Enum.ToObject(typeof(T), Convert.ToInt64(a) | Convert.ToInt64(b));

      if (underlyingType == typeof(ulong))
         return (T)Enum.ToObject(typeof(T), Convert.ToUInt64(a) | Convert.ToUInt64(b));

      if (underlyingType == typeof(short)) return (T)Enum.ToObject(typeof(T), Convert.ToInt16(a) | Convert.ToInt16(b));

      if (underlyingType == typeof(ushort))
         return (T)Enum.ToObject(typeof(T), Convert.ToUInt16(a) | Convert.ToUInt16(b));

      if (underlyingType == typeof(byte)) return (T)Enum.ToObject(typeof(T), Convert.ToByte(a) | Convert.ToByte(b));

      if (underlyingType == typeof(sbyte)) return (T)Enum.ToObject(typeof(T), Convert.ToSByte(a) | Convert.ToSByte(b));

      throw new ArgumentException($"Unsupported enum underlying type: {underlyingType}");
   }

   public static List<SearchConfigViewModel> LoadSearchConfigurations(string filePath)
   {
      var configJson = File.ReadAllText(filePath);
      var result     = JsonSerializer.Deserialize<List<SearchConfigModel>>(configJson);

      if (result == null || result.Count == 0) return [];

      return result.Select(config => new SearchConfigViewModel(config)).ToList();
   }

   public readonly record struct MatrixRow(string Path, AccessType[] Access);
}