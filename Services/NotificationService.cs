using Microsoft.Extensions.Caching.Memory;
using MyAspNetApp.Models;

namespace MyAspNetApp.Services
{
    // Stores per-user notifications in IMemoryCache (no database table required).
    public class NotificationService(IMemoryCache cache)
    {
        private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

        // Appends a notification for the given user.
        public void AddNotification(string userId, string message,
            NotificationType type = NotificationType.Success)
        {
            var list = GetOrCreateList(userId);
            list.Add(new Notification { Message = message, Type = type, Timestamp = DateTime.Now });
            cache.Set(CacheKey(userId), list, new MemoryCacheEntryOptions { SlidingExpiration = Expiry });
        }

        // Returns all pending notifications for the given user.
        public List<Notification> GetNotifications(string userId) =>
            cache.TryGetValue(CacheKey(userId), out List<Notification>? list) ? list! : [];

        // Removes all notifications for the given user.
        public void ClearNotifications(string userId) => cache.Remove(CacheKey(userId));

        private List<Notification> GetOrCreateList(string userId) =>
            cache.GetOrCreate(CacheKey(userId), e =>
            {
                e.SlidingExpiration = Expiry;
                return new List<Notification>();
            })!;

        private static string CacheKey(string userId) => $"notifications_{userId}";
    }
}
