using Errandesk.Client.Helpers;
using Errandesk.Client.Models;
using Errandesk.Client.Services;
using Errandesk.Client.ViewModels;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Errandesk.Client.Views;

/// <summary>
/// The editable details of the selected task, used as the right pane on wide windows and as a full-width view on narrow ones.
/// </summary>
public sealed partial class TaskDetailView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(TaskDetailViewModel), typeof(TaskDetailView), new PropertyMetadata(null, OnViewModelChanged));

    public TaskDetailView() => InitializeComponent();

    public TaskDetailViewModel? ViewModel
    {
        get => (TaskDetailViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Shows a back button instead of the close button, for the narrow layout where the details replace the list.
    /// </summary>
    public bool IsBackButtonVisible
    {
        get => DetailBackButton.Visibility == Visibility.Visible;
        set
        {
            DetailBackButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            DetailCloseButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Saves edits of the title and note that are still in their text boxes, before another task is shown.
    /// </summary>
    public void CommitPendingEdits()
    {
        if (ViewModel is not { CurrentTask: not null } viewModel) return;
        _ = viewModel.CommitTitleAsync(TitleTextBox.Text);
        _ = viewModel.CommitBodyAsync(BodyTextBox.Text);
    }

    private static void OnViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) => ((TaskDetailView)dependencyObject).Bindings.Update();

    private async void OnDetailCompletionCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CurrentTask: { } task } viewModel) return;
        await viewModel.ToggleCompletionCommand.ExecuteAsync(null);
        if (ReferenceEquals(viewModel.CurrentTask, task)) ((CheckBox)sender).IsChecked = task.IsCompleted;
    }

    private async void OnTitleTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CommitTitleAsync(TitleTextBox.Text);
        }
    }

    private void OnTitleTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = ViewModel?.CommitTitleAsync(TitleTextBox.Text);
    }

    private async void OnBodyTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CommitBodyAsync(BodyTextBox.Text);
        }
    }

    private async void OnDueDatePickerDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetDueDateAsync(args.NewDate);
        }
    }

    private void OnReminderMenuFlyoutOpening(object sender, object e) => ViewModel?.RefreshReminderSuggestions();

    private async void OnCustomReminderMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CurrentTask: { } task } viewModel) return;

        var customReminderView = new CustomReminderView(viewModel.GetCustomReminderStart());
        if (!await App.GetService<DialogService>().ShowContentAsync(LocalizedStrings.Get("CustomReminderDialogTitle"), customReminderView, LocalizedStrings.Get("DialogSaveButtonText"))) return;
        if (customReminderView.SelectedReminder is { } reminderAt && ReferenceEquals(viewModel.CurrentTask, task)) await viewModel.SetReminderAsync(reminderAt);
    }

    private async void OnCustomRecurrenceMenuItemClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CurrentTask: { } task } viewModel) return;

        var customRecurrenceView = new CustomRecurrenceView(viewModel.GetCustomRecurrenceStart());
        if (!await App.GetService<DialogService>().ShowContentAsync(LocalizedStrings.Get("CustomRecurrenceDialogTitle"), customRecurrenceView, LocalizedStrings.Get("DialogSaveButtonText"))) return;
        if (ReferenceEquals(viewModel.CurrentTask, task)) await viewModel.SetRecurrenceAsync(customRecurrenceView.SelectedRecurrence);
    }

    private void OnNewStepTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel is not { } viewModel) return;
        e.Handled = true;
        viewModel.NewStepTitle = ((TextBox)sender).Text;
        viewModel.AddStepCommand.Execute(null);
    }

    private async void OnStepCompletionCheckBoxClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not CheckBox { DataContext: TaskStepModel step } checkBox) return;
        await viewModel.ToggleStepAsync(step);
        if (ReferenceEquals(checkBox.DataContext, step)) checkBox.IsChecked = step.IsCompleted;
    }

    private async void OnStepTitleTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is TextBox { DataContext: TaskStepModel step } textBox)
        {
            await viewModel.CommitStepTitleAsync(step, textBox.Text);
        }
    }

    private void OnStepTitleTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel is not { } viewModel || sender is not TextBox { DataContext: TaskStepModel step } textBox) return;
        e.Handled = true;
        _ = viewModel.CommitStepTitleAsync(step, textBox.Text);
    }

    private void OnOpenLinkedNoteButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not FrameworkElement { DataContext: LinkedNoteModel note }) return;
        CommitPendingEdits();
        viewModel.OpenNote(note);
    }

    private async void OnUnlinkNoteButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is FrameworkElement { DataContext: LinkedNoteModel note })
        {
            await viewModel.UnlinkNoteAsync(note);
        }
    }

    private async void OnLinkNoteAutoSuggestBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && LinkNoteAutoSuggestBox.Text.Length == 0)
        {
            await viewModel.SearchNoteSuggestionsAsync(string.Empty);
        }
    }

    private async void OnLinkNoteAutoSuggestBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ViewModel is { } viewModel && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            await viewModel.SearchNoteSuggestionsAsync(sender.Text);
        }
    }

    /// <summary>
    /// Links the chosen suggestion, or the only suggestion when Enter is pressed without choosing one.
    /// </summary>
    private async void OnLinkNoteAutoSuggestBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel is not { } viewModel) return;
        var note = args.ChosenSuggestion as NoteItemModel ?? (viewModel.NoteSuggestions.Count == 1 ? viewModel.NoteSuggestions[0] : null);
        if (note is null) return;

        sender.Text = string.Empty;
        await viewModel.LinkNoteAsync(note);
    }

    private async void OnDeleteStepButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is FrameworkElement { DataContext: TaskStepModel step })
        {
            await viewModel.DeleteStepAsync(step);
        }
    }
}
