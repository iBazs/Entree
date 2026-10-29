// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Serilog;
using Wpf.Ui.Controls;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Entree.Core.Controller;

internal sealed class TrayIcon : IDisposable
{
   private const int MaxTooltipLength = 127;
   private const int CallbackMessage  = 0x8001;
   private const int LeftButtonUp     = 0x0202;
   private const int RightButtonUp    = 0x0205;
   private const int NimAdd           = 0;
   private const int NimModify        = 1;
   private const int NimDelete        = 2;
   private const int NifMessage       = 0x1;
   private const int NifIcon          = 0x2;
   private const int NifTip           = 0x4;

   private static readonly int TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

   private readonly ContextMenu _menu;
   private readonly Action      _open;
   private readonly HwndSource  _window = new(new HwndSourceParameters("Entrée tray") { WindowStyle = 0 });

   private bool           _added;
   private bool           _disposed;
   private NotifyIconData _data;

   public TrayIcon(Action open, Action exit)
   {
      _open = open;
      _menu = new ContextMenu
              {
                       Placement = PlacementMode.MousePoint,
                       Items =
                       {
                                MenuEntry("Open Entrée", SymbolRegular.Open24, open),
                                MenuEntry("Exit", SymbolRegular.Power24, exit)
                       }
              };
      _ = ExtractIconExW(Environment.ProcessPath!, 0, IntPtr.Zero, out var icon, 1);
      _data = new NotifyIconData
              {
                       Size            = Marshal.SizeOf<NotifyIconData>(),
                       Window          = _window.Handle,
                       Flags           = NifMessage | NifIcon | NifTip,
                       CallbackMessage = CallbackMessage,
                       Icon            = icon,
                       Tip             = "Entrée"
              };
      _window.AddHook(OnMessage);
   }

   public void Show(string tooltip)
   {
      _data.Tip = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
      if (_added)
      {
         _ = Shell_NotifyIconW(NimModify, ref _data);
         return;
      }

      _added = Shell_NotifyIconW(NimAdd, ref _data);
      if (!_added) Log.Warning("Could not add the tray icon (error {Error})", Marshal.GetLastWin32Error());
   }

   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      if (_added) _ = Shell_NotifyIconW(NimDelete, ref _data);
      _window.RemoveHook(OnMessage);
      _window.Dispose();
      if (_data.Icon != IntPtr.Zero) _ = DestroyIcon(_data.Icon);
   }

   private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
   {
      if (message == TaskbarCreated && _added)
      {
         _added = Shell_NotifyIconW(NimAdd, ref _data);
         return IntPtr.Zero;
      }

      if (message != CallbackMessage) return IntPtr.Zero;
      switch ((int)lParam & 0xFFFF)
      {
         case LeftButtonUp:
            _open();
            break;
         case RightButtonUp:
            _ = SetForegroundWindow(hwnd);
            _menu.IsOpen = true;
            break;
      }

      handled = true;
      return IntPtr.Zero;
   }

   private static MenuItem MenuEntry(string header, SymbolRegular symbol, Action action)
   {
      var item = new MenuItem { Header = header, Icon = new SymbolIcon(symbol) };
      item.Click += (_, _) => action();
      return item;
   }

   [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
   private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

   [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
   private static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);

   [DllImport("user32.dll")]
   private static extern bool DestroyIcon(IntPtr icon);

   [DllImport("user32.dll")]
   private static extern bool SetForegroundWindow(IntPtr window);

   [DllImport("user32.dll", CharSet = CharSet.Unicode)]
   private static extern int RegisterWindowMessageW(string name);

   [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
   private struct NotifyIconData
   {
      public int    Size;
      public IntPtr Window;
      public int    Id;
      public int    Flags;
      public int    CallbackMessage;
      public IntPtr Icon;

      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string Tip;

      public int State;
      public int StateMask;

      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
      public string Info;

      public int Version;

      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
      public string InfoTitle;

      public int    InfoFlags;
      public Guid   Item;
      public IntPtr BalloonIcon;
   }
}
