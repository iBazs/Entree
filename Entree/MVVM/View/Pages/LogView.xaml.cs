// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;
using Wpf.Ui.Controls;
using Entree.MVVM.ViewModel.Control;
using Entree.MVVM.View.Converter;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Entree.MVVM.View.Pages;

public partial class LogView
{
   private const  double        MinLogHeight      = 100;
   private const  double        MaxLogHeight      = 400;
   private const  double        AnimationDuration = 0.25;
   private static double        _lastOpenHeight   = 200;
   private        bool          _isAnimating, _isOpen, _isUserScrolling, _hasNewItems;
   private        ScrollViewer? _scrollViewer;

   public LogView()
   {
      InitializeComponent();
      DependencyPropertyDescriptor.FromProperty(InfoBar.IsOpenProperty, typeof(InfoBar))
                                  .AddValueChanged(NotificationBar, NotificationBar_OnIsOpenChanged);
      Loaded             += OnLoaded;
      DataContextChanged += OnDataContextChanged;
   }

   private void OnLoaded(object sender, RoutedEventArgs e)
   {
      FindAndConfigureScrollViewer();
      InitializeLogViewer();
      UpdateBar(false);
   }

   private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
   {
      if (e.OldValue is LogViewModel oldViewModel)
      {
         oldViewModel.LogEvents.CollectionChanged -= LogEvents_CollectionChanged;
         oldViewModel.PropertyChanged             -= ViewModel_PropertyChanged;
      }

      if (e.NewValue is LogViewModel newViewModel)
      {
         newViewModel.LogEvents.CollectionChanged += LogEvents_CollectionChanged;
         newViewModel.PropertyChanged             += ViewModel_PropertyChanged;
      }
   }

   private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
   {
      if (e.PropertyName == nameof(LogViewModel.IsNotificationOpen) && sender is LogViewModel viewModel)
         Dispatcher.InvokeAsync(() => ShowNotification(viewModel.IsNotificationOpen));
   }

   private void ShowNotification(bool open)
   {
      NotificationHost.IsHitTestVisible = open;
      Animate(NotificationHost, OpacityProperty, open ? 1 : 0, true);
      Animate(NotificationShift, TranslateTransform.YProperty, open ? 0 : 12, true);
      UpdateBar(true);
   }

   private void UpdateBar(bool animate)
   {
      var atRight = DataContext is LogViewModel { IsNotificationOpen: true };
      Animate(ButtonShift, TranslateTransform.XProperty, atRight ? 0 : -(Bar.ActualWidth - LogButton.ActualWidth) / 2,
              animate);
      LogButton.IsHitTestVisible = !_isOpen;
      Animate(LogButton, OpacityProperty, _isOpen ? 0 : 1, animate);
   }

   private void NotificationBar_OnIsOpenChanged(object? sender, EventArgs e)
   {
      if (NotificationBar.IsOpen) return;
      NotificationBar.SetCurrentValue(InfoBar.IsOpenProperty, true);
      (DataContext as LogViewModel)?.DismissNotification();
   }

   private static void Animate(IAnimatable target, DependencyProperty property, double to, bool animate) =>
            target.BeginAnimation(property,
                                  new DoubleAnimation(to, TimeSpan.FromSeconds(animate && SystemParameters.ClientAreaAnimation
                                                                                  ? AnimationDuration
                                                                                  : 0))
                                  {
                                           EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                                  });

   private void Bar_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBar(false);

   private void LogEvents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
   {
      if (e.Action != NotifyCollectionChangedAction.Add || _isOpen) return;
      _hasNewItems = true;
      if (InfoBadge.Severity != InfoBadgeSeverity.Caution && InfoBadge.Severity != InfoBadgeSeverity.Critical)
         InfoBadge.Severity = InfoBadgeSeverity.Success;

      if (e.NewItems is null) return;
      var newItem = e.NewItems.OfType<LogEvent>().First();
      InfoBadge.Severity = newItem.Level switch
                           {
                                    LogEventLevel.Warning when InfoBadge.Severity != InfoBadgeSeverity.Critical =>
                                             InfoBadgeSeverity.Caution,
                                    LogEventLevel.Error or LogEventLevel.Fatal => InfoBadgeSeverity.Critical,
                                    _                                          => InfoBadge.Severity
                           };
      UpdateInfoBadgeVisibility();
   }

   private void UpdateInfoBadgeVisibility()
   {
      InfoBadge.Visibility = _hasNewItems ? Visibility.Visible : Visibility.Collapsed;
   }

   private void FindAndConfigureScrollViewer()
   {
      LogItems.ApplyTemplate();
      _scrollViewer = FindScrollViewer(LogItems);

      if (_scrollViewer != null) _scrollViewer.ScrollChanged += OnScrollChanged;
   }

   private void InitializeLogViewer()
   {
      LogViewer.Height     = 0;
      LogViewer.MinHeight  = 0;
      LogViewer.Visibility = Visibility.Collapsed;
      LogSizer.Visibility  = Visibility.Collapsed;
   }

   private static ScrollViewer? FindScrollViewer(DependencyObject parent)
   {
      if (parent is ScrollViewer scrollViewer)
         return scrollViewer;

      var childCount = VisualTreeHelper.GetChildrenCount(parent);
      for (var i = 0; i < childCount; i++)
      {
         var child  = VisualTreeHelper.GetChild(parent, i);
         var result = FindScrollViewer(child);
         if (result != null)
            return result;
      }

      return null;
   }

   private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
   {
      if (_scrollViewer == null) return;

      const double scrollTolerance = 10.0;
      var          isAtBottom      = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - scrollTolerance;

      if (e.VerticalChange != 0) _isUserScrolling = !isAtBottom;

      if (e.ExtentHeightChange > 0 && !_isUserScrolling) _scrollViewer.ScrollToBottom();
   }

   private void ToggleVisibility()
   {
      if (_isAnimating) return;

      _isOpen      = !_isOpen;
      _isAnimating = true;

      if (_isOpen)
      {
         _hasNewItems       = false;
         InfoBadge.Severity = InfoBadgeSeverity.Success;
         UpdateInfoBadgeVisibility();
      }

      UpdateBar(true);

      LogViewer.BeginAnimation(HeightProperty, null);

      var targetHeight = _isOpen ? Math.Clamp(_lastOpenHeight, MinLogHeight, MaxLogHeight) : 0;

      var currentHeight = GetEffectiveHeight();

      if (Math.Abs(currentHeight - targetHeight) < 0.1)
      {
         _isAnimating = false;
         SetFinalState(targetHeight);
         return;
      }

      CreateAndRunAnimation(currentHeight, targetHeight);
   }

   private double GetEffectiveHeight()
   {
      LogViewer.UpdateLayout();

      if (_isOpen) return LogViewer.ActualHeight;

      return LogViewer.Visibility == Visibility.Collapsed ? 0 : LogViewer.ActualHeight;
   }

   private void CreateAndRunAnimation(double fromHeight, double toHeight)
   {
      PrepareElementForAnimation(fromHeight);

      var animation = new DoubleAnimation
                      {
                               Duration       = TimeSpan.FromSeconds(SystemParameters.ClientAreaAnimation ? AnimationDuration : 0),
                               EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                               From           = fromHeight,
                               To             = toHeight,
                               FillBehavior   = FillBehavior.Stop
                      };

      animation.Completed += OnAnimationCompleted;
      LogViewer.BeginAnimation(HeightProperty, animation);
   }

   private void PrepareElementForAnimation(double fromHeight)
   {
      if (_isOpen)
      {
         LogViewer.Visibility = Visibility.Visible;
         LogSizer.Visibility  = Visibility.Visible;
         LogViewer.Height     = fromHeight;
         _isUserScrolling     = false;
      }
      else
      {
         LogViewer.Height = fromHeight;
      }

      LogViewer.UpdateLayout();
   }

   private void OnAnimationCompleted(object? sender, EventArgs e)
   {
      if (sender is DoubleAnimation animation) animation.Completed -= OnAnimationCompleted;

      _isAnimating = false;

      var finalHeight = _isOpen ? Math.Clamp(_lastOpenHeight, MinLogHeight, MaxLogHeight) : 0;
      SetFinalState(finalHeight);

      if (_isOpen)
         Dispatcher.BeginInvoke(() => _scrollViewer?.ScrollToBottom(),
                                DispatcherPriority.Background);
   }

   private void SetFinalState(double height)
   {
      LogViewer.Height = height;

      if (height <= 0)
      {
         LogViewer.Visibility = Visibility.Collapsed;
         LogSizer.Visibility  = Visibility.Collapsed;
      }
      else
      {
         LogViewer.Visibility = Visibility.Visible;
         LogSizer.Visibility  = Visibility.Visible;
      }
   }

   protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
   {
      base.OnRenderSizeChanged(sizeInfo);

      if (_isOpen && !_isAnimating && LogViewer.ActualHeight > 0)
         _lastOpenHeight = Math.Clamp(LogViewer.ActualHeight, MinLogHeight, MaxLogHeight);
   }

   private void LogViewButton_Click(object sender, RoutedEventArgs e)
   {
      ToggleVisibility();
   }

   private void CopyLogEntry_Click(object sender, RoutedEventArgs e)
   {
      if (sender is not MenuItem menuButton) return;
      if (menuButton.DataContext is not LogEvent logEntry) return;
      SetClipboard(LogEventRenderConverter.Format(logEntry));
   }

   private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
   {
      if (DataContext is not LogViewModel viewModel) return;
      SetClipboard(string.Join(Environment.NewLine,
                               viewModel.LogEvents.Select(LogEventRenderConverter.Format)));
   }

   private static void SetClipboard(string text)
   {
      try
      {
         Clipboard.SetText(text);
      }
      catch (COMException ex)
      {
         Log.Warning("Clipboard is unavailable: {Message}", ex.Message);
      }
   }

   private void ScrollToBottom_Click(object sender, RoutedEventArgs e)
   {
      _isUserScrolling = false;
      _scrollViewer?.ScrollToBottom();
   }

   private void LogSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
   {
      var currentHeight     = LogViewer.ActualHeight;
      var constrainedHeight = Math.Clamp(currentHeight, MinLogHeight, MaxLogHeight);

      if (Math.Abs(currentHeight - constrainedHeight) > 1) LogViewer.Height = constrainedHeight;

      _lastOpenHeight = constrainedHeight;
   }

   private void LogSplitter_OnDragDelta(object sender, DragDeltaEventArgs e)
   {
      var currentHeight = LogViewer.ActualHeight;
      var newHeight     = Math.Clamp(currentHeight - e.VerticalChange, MinLogHeight, MaxLogHeight);

      if (Math.Abs(newHeight - currentHeight) > 0.1) LogViewer.Height = newHeight;
   }

   private void ClearLog_Click(object sender, RoutedEventArgs e)
   {
      if (DataContext is not LogViewModel viewModel) return;
      viewModel.ClearLogCommand.Execute(true);
      _hasNewItems = false;
      UpdateInfoBadgeVisibility();
   }
}