using CommunityToolkit.Mvvm.ComponentModel;

namespace Assistent.App.ViewModels;

public enum ChatRole
{
    User,
    Assistant
}

public partial class ChatLine : ObservableObject
{
    public ChatRole Role { get; }

    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private string? thinkingText;

    [ObservableProperty]
    private bool isAwaitingResponse;

    public ChatLine(ChatRole role, string text)
    {
        Role = role;
        Text = text;
        IsAwaitingResponse = role == ChatRole.Assistant && string.IsNullOrEmpty(text);
    }
}
