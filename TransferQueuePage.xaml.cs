using Hyper_Transmit.Models;
using Hyper_Transmit.Models.Enums;
using Hyper_Transmit.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hyper_Transmit
{
    public sealed partial class TransferQueuePage : Page
    {
        private readonly ITransferQueueService _transferService;
        private DispatcherTimer _refreshTimer;
        private readonly Dictionary<string, TaskCardElements> _cardElements = new();

        private readonly HashSet<string> _selectedTasks = new();
        private readonly HashSet<string> _expandedTasks = new();

        private static readonly Brush SelectedBorderBrush = new SolidColorBrush(Colors.DodgerBlue);
        private static readonly Brush SelectedBackgroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 30, 144, 255));
        private static readonly Brush DefaultBackground = (Brush)Application.Current.Resources["SystemControlBackgroundChromeMediumLowBrush"];

        public TransferQueuePage()
        {
            InitializeComponent();
            _transferService = App.Services.GetRequiredService<ITransferQueueService>();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += (s, e) => RefreshUI();
            _refreshTimer.Start();

            Loaded += (s, e) => RefreshUI();
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        #region Selection

        private void ScrollViewer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // Only clear selection if clicking on the ScrollViewer background (not on a card)
            var originalSource = e.OriginalSource as FrameworkElement;
            if (originalSource == null) return;

            // Walk up the tree to see if the tapped element is inside a task card
            var element = originalSource;
            while (element != null)
            {
                if (element is Border border && _cardElements.Values.Any(c => c.Border == border))
                    return; // Clicked on a card, don't clear
                if (element is StackPanel sp && sp == TransferItems)
                    return; // Clicked on the items panel directly (gap between items)
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            // Clicked on empty ScrollViewer area - clear selection
            if (_selectedTasks.Count > 0)
            {
                _selectedTasks.Clear();
                _expandedTasks.Clear();
                UpdateSelectionUI();
                RefreshAllVisuals();
            }
        }

        private void ToggleSelection(string taskId, bool ctrlHeld)
        {
            if (ctrlHeld)
            {
                if (_selectedTasks.Contains(taskId))
                {
                    _selectedTasks.Remove(taskId);
                    _expandedTasks.Remove(taskId);
                }
                else
                {
                    _selectedTasks.Add(taskId);
                    _expandedTasks.Add(taskId);
                }
            }
            else
            {
                if (_selectedTasks.Count == 1 && _selectedTasks.Contains(taskId))
                {
                    if (_expandedTasks.Contains(taskId))
                        _expandedTasks.Remove(taskId);
                    else
                        _expandedTasks.Add(taskId);
                }
                else
                {
                    _selectedTasks.Clear();
                    _expandedTasks.Clear();
                    _selectedTasks.Add(taskId);
                    _expandedTasks.Add(taskId);
                }
            }

            UpdateSelectionUI();
            foreach (var kvp in _cardElements)
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == kvp.Key);
                if (task != null)
                    UpdateCardVisual(kvp.Value, task);
            }
        }

        private void UpdateSelectionUI()
        {
            if (_selectedTasks.Count > 0)
            {
                SelectionActionBar.Visibility = Visibility.Visible;
                HintPanel.Visibility = Visibility.Collapsed;
                SelectionCountText.Text = $"已选: {_selectedTasks.Count}";
            }
            else
            {
                SelectionActionBar.Visibility = Visibility.Collapsed;
                HintPanel.Visibility = Visibility.Visible;
            }
        }

        private void UpdateCardVisual(TaskCardElements elems, TransferTask task)
        {
            bool isSelected = _selectedTasks.Contains(task.Id);
            bool isExpanded = _expandedTasks.Contains(task.Id);

            if (isSelected)
            {
                elems.Border.BorderBrush = SelectedBorderBrush;
                elems.Border.Background = SelectedBackgroundBrush;
                elems.Border.BorderThickness = new Thickness(2);
            }
            else
            {
                elems.Border.BorderBrush = GetStatusBrush(task.Status);
                elems.Border.Background = DefaultBackground;
                elems.Border.BorderThickness = new Thickness(1);
            }

            if (elems.DetailPanel != null)
                elems.DetailPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Top Bar Action Handlers

        private async void SelPauseButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var id in _selectedTasks.ToList())
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == id);
                if (task?.Status == TransferStatus.Transferring)
                    await _transferService.PauseTaskAsync(id);
            }
        }

        private async void SelResumeButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var id in _selectedTasks.ToList())
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == id);
                if (task?.Status == TransferStatus.Paused)
                    await _transferService.ResumeTaskAsync(id);
            }
        }

        private async void SelCancelButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var id in _selectedTasks.ToList())
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == id);
                if (task != null && (task.Status == TransferStatus.Transferring || task.Status == TransferStatus.Pending))
                    await _transferService.CancelTaskAsync(id);
            }
        }

        private void SelClearButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var id in _selectedTasks.ToList())
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == id);
                if (task != null)
                {
                    _transferService.Tasks.Remove(task);
                    _cardElements.Remove(id);
                }
            }
            _selectedTasks.Clear();
            _expandedTasks.Clear();
            UpdateSelectionUI();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedTasks.Clear();
            foreach (var task in _transferService.Tasks)
            {
                _selectedTasks.Add(task.Id);
                _expandedTasks.Add(task.Id);
            }
            UpdateSelectionUI();
            RefreshAllVisuals();
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedTasks.Clear();
            _expandedTasks.Clear();
            UpdateSelectionUI();
            RefreshAllVisuals();
        }

        private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
        {
            var toRemove = _transferService.Tasks
                .Where(t => t.Status == TransferStatus.Completed ||
                            t.Status == TransferStatus.Failed ||
                            t.Status == TransferStatus.Cancelled ||
                            t.Status == TransferStatus.Paused)
                .Select(t => t.Id)
                .ToList();

            foreach (var id in toRemove)
            {
                _selectedTasks.Remove(id);
                _expandedTasks.Remove(id);
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == id);
                if (task != null)
                    _transferService.Tasks.Remove(task);
                _cardElements.Remove(id);
            }
            UpdateSelectionUI();
        }

        private void RefreshAllVisuals()
        {
            foreach (var kvp in _cardElements)
            {
                var task = _transferService.Tasks.FirstOrDefault(t => t.Id == kvp.Key);
                if (task != null)
                    UpdateCardVisual(kvp.Value, task);
            }
        }

        #endregion

        #region UI Refresh

        private void RefreshUI()
        {
            var tasks = _transferService.Tasks;

            var taskIds = new HashSet<string>(tasks.Select(t => t.Id));
            var idsToRemove = _cardElements.Keys.Where(id => !taskIds.Contains(id)).ToList();
            foreach (var id in idsToRemove)
            {
                var card = _cardElements[id];
                TransferItems.Children.Remove(card.Border);
                _cardElements.Remove(id);
                _expandedTasks.Remove(id);
                _selectedTasks.Remove(id);
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];

                if (_cardElements.TryGetValue(task.Id, out var elements))
                {
                    UpdateTaskCard(elements, task);
                    int currentIndex = TransferItems.Children.IndexOf(elements.Border);
                    if (currentIndex != i)
                    {
                        TransferItems.Children.Remove(elements.Border);
                        if (i >= TransferItems.Children.Count)
                            TransferItems.Children.Add(elements.Border);
                        else
                            TransferItems.Children.Insert(i, elements.Border);
                    }
                }
                else
                {
                    var (border, elems) = CreateTaskCard(task);
                    _cardElements[task.Id] = elems;
                    if (i >= TransferItems.Children.Count)
                        TransferItems.Children.Add(border);
                    else
                        TransferItems.Children.Insert(i, border);
                }
            }

            TotalCountText.Text = $"总计: {tasks.Count}";
            CompletedCountText.Text = $"已完成: {tasks.Count(t => t.Status == TransferStatus.Completed)}";
            PendingCountText.Text = $"等待中: {tasks.Count(t => t.Status == TransferStatus.Pending)}";
            FailedCountText.Text = $"失败: {tasks.Count(t => t.Status == TransferStatus.Failed)}";
        }

        #endregion

        #region Card Creation & Update

        private static SolidColorBrush GetStatusBrush(TransferStatus status) => status switch
        {
            TransferStatus.Completed => new SolidColorBrush(Colors.LimeGreen),
            TransferStatus.Failed => new SolidColorBrush(Colors.Red),
            TransferStatus.Transferring => new SolidColorBrush(Colors.DodgerBlue),
            TransferStatus.Paused => new SolidColorBrush(Colors.Orange),
            _ => new SolidColorBrush(Colors.Gray)
        };

        private void UpdateTaskCard(TaskCardElements elems, TransferTask task)
        {
            var brush = GetStatusBrush(task.Status);

            elems.StatusIcon.Glyph = GetStatusIconGlyph(task.Status);
            elems.StatusIcon.Foreground = brush;
            elems.StatusText.Text = task.StatusDisplay;
            elems.StatusText.Foreground = brush;
            elems.SizeText.Text = $"{FormatSize(task.TransferredSize)} / {FormatSize(task.TotalSize)}";

            if (task.Status == TransferStatus.Completed && task.StartTime.HasValue && task.EndTime.HasValue)
            {
                var elapsed = (task.EndTime.Value - task.StartTime.Value).TotalSeconds;
                if (elapsed > 0)
                    elems.SpeedText.Text = $"平均 {FormatSpeed(task.TransferredSize / elapsed)}";
                else
                    elems.SpeedText.Text = task.SpeedDisplay;
            }
            else
            {
                elems.SpeedText.Text = task.SpeedDisplay;
            }

            elems.ProgressBar.Value = task.ProgressPercent;
            elems.PercentText.Text = $"{task.ProgressPercent:F1}%";

            if (elems.DetailPanel != null)
            {
                var statusDetail = task.Status switch
                {
                    TransferStatus.Completed => $"完成于 {task.EndTime?.ToString("HH:mm:ss") ?? "--"}",
                    TransferStatus.Failed => $"错误: {task.ErrorMessage}",
                    TransferStatus.Transferring => $"已传输: {FormatSize(task.TransferredSize)} / {FormatSize(task.TotalSize)}",
                    TransferStatus.Paused => "已暂停",
                    _ => "等待中"
                };
                elems.SourcePathText.Text = $"源: {task.SourcePath ?? ""}";
                elems.DestPathText.Text = $"目标: {task.DestinationPath ?? ""}";
                elems.DetailStatusText.Text = statusDetail;
            }

            UpdateCardVisual(elems, task);
        }

        private (Border border, TaskCardElements elems) CreateTaskCard(TransferTask task)
        {
            var brush = GetStatusBrush(task.Status);

            var rootStack = new StackPanel { Spacing = 4 };

            // Row 1: Icon + Status + FileName + Size + Speed
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var icon = new FontIcon
            {
                Glyph = GetStatusIconGlyph(task.Status),
                FontSize = 14,
                Foreground = brush,
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(icon);

            var statusBlock = new TextBlock
            {
                Text = task.StatusDisplay,
                FontSize = 12,
                Foreground = brush,
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(statusBlock);

            var nameBlock = new TextBlock
            {
                Text = task.FileName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(nameBlock);

            var sizeBlock = new TextBlock
            {
                Text = $"{FormatSize(task.TransferredSize)} / {FormatSize(task.TotalSize)}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            topRow.Children.Add(sizeBlock);

            var speedBlock = new TextBlock
            {
                Text = task.SpeedDisplay,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            topRow.Children.Add(speedBlock);

            rootStack.Children.Add(topRow);

            // Row 2: Progress bar + percentage
            var progressRow = new Grid();
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            var progressBar = new ProgressBar
            {
                Value = task.ProgressPercent,
                Maximum = 100,
                Height = 12,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(progressBar, 0);
            progressRow.Children.Add(progressBar);

            var percentBlock = new TextBlock
            {
                Text = $"{task.ProgressPercent:F1}%",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(percentBlock, 1);
            progressRow.Children.Add(percentBlock);
            rootStack.Children.Add(progressRow);

            // Detail panel (expandable on select)
            var detailPanel = new StackPanel
            {
                Spacing = 2,
                Padding = new Thickness(24, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            detailPanel.Children.Add(new TextBlock
            {
                Text = $"源: {task.SourcePath ?? ""}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            });
            detailPanel.Children.Add(new TextBlock
            {
                Text = $"目标: {task.DestinationPath ?? ""}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            });
            var detailStatusText = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
            };
            detailPanel.Children.Add(detailStatusText);

            rootStack.Children.Add(detailPanel);

            // Border container
            var border = new Border
            {
                Child = rootStack,
                Padding = new Thickness(12, 8, 12, 8),
                Background = DefaultBackground,
                CornerRadius = new CornerRadius(4),
                BorderBrush = brush,
                BorderThickness = new Thickness(1),
                IsTapEnabled = true
            };

            var elems = new TaskCardElements
            {
                Border = border,
                StatusIcon = icon,
                StatusText = statusBlock,
                NameText = nameBlock,
                SizeText = sizeBlock,
                SpeedText = speedBlock,
                ProgressBar = progressBar,
                PercentText = percentBlock,
                DetailPanel = detailPanel,
                SourcePathText = (TextBlock)detailPanel.Children[0],
                DestPathText = (TextBlock)detailPanel.Children[1],
                DetailStatusText = detailStatusText
            };

            // Click to select/deselect
            border.Tapped += (s, e) =>
            {
                var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                bool ctrlHeld = ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                ToggleSelection(task.Id, ctrlHeld);
            };

            return (border, elems);
        }

        #endregion

        #region Helpers

        private class TaskCardElements
        {
            public Border Border { get; set; } = null!;
            public FontIcon StatusIcon { get; set; } = null!;
            public TextBlock StatusText { get; set; } = null!;
            public TextBlock NameText { get; set; } = null!;
            public TextBlock SizeText { get; set; } = null!;
            public TextBlock SpeedText { get; set; } = null!;
            public ProgressBar ProgressBar { get; set; } = null!;
            public TextBlock PercentText { get; set; } = null!;
            public StackPanel? DetailPanel { get; set; }
            public TextBlock SourcePathText { get; set; } = null!;
            public TextBlock DestPathText { get; set; } = null!;
            public TextBlock DetailStatusText { get; set; } = null!;
        }

        private static string GetStatusIconGlyph(TransferStatus status) => status switch
        {
            TransferStatus.Completed => "\uE73E",
            TransferStatus.Failed => "\uE783",
            TransferStatus.Transferring => "\uE768",
            TransferStatus.Paused => "\uE769",
            TransferStatus.Pending => "\uE8F1",
            TransferStatus.Cancelled => "\uE711",
            _ => "\uE8F1"
        };

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {suffixes[order]}";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "--";
            string[] suffixes = ["B/s", "KB/s", "MB/s", "GB/s"];
            int order = 0;
            double size = bytesPerSecond;
            while (size >= 1024 && order < suffixes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {suffixes[order]}";
        }

        #endregion
    }
}