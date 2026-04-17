using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Assistent.App.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Assistent.App;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null || e.NewItems.Count == 0)
            return;

        // ScrollIntoView during CollectionChanged runs layout while the item generator is still
        // reconciling adds (e.g. user + assistant back-to-back), which breaks virtualization.
        Dispatcher.BeginInvoke(ScrollChatToEnd, DispatcherPriority.Loaded);
    }

    private void ScrollChatToEnd()
    {
        if (ChatList.Items.Count == 0)
            return;
        try
        {
            ChatList.ScrollIntoView(ChatList.Items[^1]);
        }
        catch (InvalidOperationException)
        {
            // List still updating or unloaded; skip scroll.
        }
    }

    /// <summary>
    /// Enter sends; Shift+Enter inserts a newline. Uses tunneling PreviewKeyDown on the window so we run
    /// before the TextBox handles Enter (AcceptsReturn would otherwise always insert a newline).
    /// </summary>
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            return;
        if (!IsKeyboardFocusInsideComposer(e.OriginalSource))
            return;
        if (DataContext is not MainViewModel vm)
            return;
        if (vm.SendCommand is not IAsyncRelayCommand cmd)
            return;

        e.Handled = true;
        await cmd.ExecuteAsync(null).ConfigureAwait(true);
    }

    private bool IsKeyboardFocusInsideComposer(object? eventSource)
    {
        if (ReferenceEquals(Keyboard.FocusedElement, Composer))
            return true;

        for (var d = eventSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, Composer))
                return true;
        }

        return false;
    }
}
