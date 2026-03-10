namespace MyAspNetApp.Models
{
    public enum NotificationType { Info, Success, Error }

    public class Notification
    {
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public NotificationType Type { get; set; } = NotificationType.Info;
    }
}
