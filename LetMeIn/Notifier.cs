using Microsoft.Toolkit.Uwp.Notifications;

namespace LetMeIn;

public static class Notifier
{
    public static void ShowKnock(string fromName, string message)
    {
        new ToastContentBuilder()
            .AddText($"🚪 {fromName} стучится")
            .AddText(message)
            .SetToastDuration(ToastDuration.Long)
            .AddAudio(new Uri("ms-winsoundevent:Notification.Default"))
            .Show();
    }

    public static void ShowInfo(string title, string text)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(text)
            .Show();
    }
}
