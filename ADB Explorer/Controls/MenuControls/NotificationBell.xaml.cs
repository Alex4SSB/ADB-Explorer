using ADB_Explorer.Helpers;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for NotificationBell.xaml
/// </summary>
public partial class NotificationBell : UserControl
{
    public ObservableList<Notification> Notifications
    {
        get => (ObservableList<Notification>)GetValue(NotificationsProperty);
        set => SetValue(NotificationsProperty, value);
    }

    public static readonly DependencyProperty NotificationsProperty = 
        DependencyProperty.Register(nameof(Notifications), typeof(ObservableList<Notification>),
            typeof(NotificationBell), new PropertyMetadata(null));

    public NotificationBell()
    {
        InitializeComponent();
    }

    public class Notification : BaseAction
    {
        public string Title { get; }

        public static string Tooltip => Strings.Resources.S_TOOLTIP_MORE_INFO;

        public Notification(Action action, string title, ObservableList<Notification> list) : base(() => true, action)
        {
            Title = title;

            ((CommandHandler)Command).OnExecute.PropertyChanged += (sender, e) =>
            {
                list.Remove(this);
            };
        }
    }
}
