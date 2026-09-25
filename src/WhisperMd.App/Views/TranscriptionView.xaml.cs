using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WhisperMd.App.Models;
using WhisperMd.App.ViewModels;

namespace WhisperMd.App.Views;

public partial class TranscriptionView : UserControl
{
    private Point _dragStartPoint;

    public TranscriptionView()
    {
        InitializeComponent();
    }

    private TranscriptionViewModel? ViewModel => DataContext as TranscriptionViewModel;

    private void SelectFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите записи для транскрибации",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Аудио и видео|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.wma;*.mp4;*.webm;*.mkv;*.mov|Все файлы|*.*"
        };

        if (dialog.ShowDialog() == true)
            ViewModel?.AddFiles(dialog.FileNames);
    }

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (ViewModel?.CanEditJob != true)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            SetDropZoneActive(true);
            e.Handled = true;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        SetDropZoneActive(false);
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        SetDropZoneActive(false);
        if (ViewModel?.CanEditJob != true || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            ViewModel?.AddFiles(files);

        e.Handled = true;
    }

    private void QueueListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void GroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel?.CanEditJob != true)
            return;

        if (sender is ComboBox { IsLoaded: true, DataContext: Recording recording } comboBox &&
            comboBox.SelectedItem is RecordingGroup group)
        {
            recording.GroupId = group.Id;
        }
    }

    private void QueueListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel?.CanEditJob != true || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPosition = e.GetPosition(null);
        var delta = _dragStartPoint - currentPosition;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
            return;

        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not Recording recording)
            return;

        DragDrop.DoDragDrop(item, recording, DragDropEffects.Move);
    }

    private void QueueListBox_DragOver(object sender, DragEventArgs e)
    {
        if (ViewModel?.CanEditJob != true)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(typeof(Recording)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void QueueListBox_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel?.CanEditJob != true)
            return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                ViewModel?.AddFiles(files);

            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(Recording)) is not Recording source)
            return;

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = targetItem?.DataContext as Recording;
        ViewModel?.MoveRecording(source, target);
        e.Handled = true;
    }

    private void SetDropZoneActive(bool active)
    {
        if (DropZoneBorder is null)
            return;

        DropZoneBorder.BorderBrush = active
            ? (Brush)FindResource("PrimaryBrush")
            : (Brush)FindResource("BorderBrush");
        DropZoneBorder.Background = active
            ? (Brush)FindResource("PrimarySoftBrush")
            : (Brush)FindResource("SurfaceBrush");
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button or ComboBox or TextBox)
                return true;

            if (current is ListBoxItem)
                return false;

            current = GetParent(current);
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = GetParent(current);
        }

        return null;
    }
    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            var parent = ContentOperations.GetParent(contentElement);
            if (parent is not null)
                return parent;

            if (contentElement is FrameworkContentElement frameworkContentElement)
                return frameworkContentElement.Parent;
        }

        return VisualTreeHelper.GetParent(child);
    }

}
