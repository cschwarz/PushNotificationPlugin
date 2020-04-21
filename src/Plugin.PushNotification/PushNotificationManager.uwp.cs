using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Windows.Data.Xml.Dom;
using Windows.Networking.PushNotifications;
using Windows.Storage;
using Windows.UI.Notifications;

namespace Plugin.PushNotification
{
    /// <summary>
    /// Implementation for PushNotification
    /// </summary>
    public class PushNotificationManager : IPushNotification
    {
        const string TokenKey = "Token";
        const string NotificationIdKey = "id";
        const string NotificationTagKey = "tag";

        public Func<string> RetrieveSavedToken { get; set; } = InternalRetrieveSavedToken;
        public Action<string> SaveToken { get; set; } = InternalSaveToken;

        public string Token
        {
            get
            {
                return RetrieveSavedToken?.Invoke() ?? string.Empty;
            }
            internal set
            {
                SaveToken?.Invoke(value);
            }
        }

        internal static string InternalRetrieveSavedToken()
        {
            return ApplicationData.Current.LocalSettings.Values.ContainsKey(TokenKey) ? ApplicationData.Current.LocalSettings.Values[TokenKey]?.ToString() : null;
        }

        internal static void InternalSaveToken(string token)
        {
            ApplicationData.Current.LocalSettings.Values[TokenKey] = token;
        }

        public IPushNotificationHandler NotificationHandler { get; set; }

        public event PushNotificationTokenEventHandler OnTokenRefresh;
        public event PushNotificationResponseEventHandler OnNotificationOpened;
        public event PushNotificationDataEventHandler OnNotificationReceived;
        public event PushNotificationDataEventHandler OnNotificationDeleted;
        public event PushNotificationErrorEventHandler OnNotificationError;

        static IList<NotificationUserCategory> UserNotificationCategories { get; } = new List<NotificationUserCategory>();

        private PushNotificationChannel channel;

        public static void Initialize()
        {
            CrossPushNotification.Current.NotificationHandler = CrossPushNotification.Current.NotificationHandler ?? new DefaultPushNotificationHandler();
        }

        public static void Initialize(IPushNotificationHandler pushNotificationHandler)
        {
            CrossPushNotification.Current.NotificationHandler = pushNotificationHandler;
            Initialize();
        }

        public void ClearAllNotifications()
        {
            ToastNotificationManager.History.Clear();
        }

        public NotificationUserCategory[] GetUserNotificationCategories()
        {
            return UserNotificationCategories?.ToArray();
        }

        public void RegisterUserNotificationCategories(NotificationUserCategory[] userCategories)
        {
            UserNotificationCategories.Clear();

            foreach (NotificationUserCategory userCategory in userCategories)
                UserNotificationCategories.Add(userCategory);
        }

        public async void RegisterForPushNotifications()
        {
            channel = await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();
            channel.PushNotificationReceived += Channel_PushNotificationReceived;
            InternalSaveToken(channel.Uri);
            OnTokenRefresh?.Invoke(CrossPushNotification.Current, new PushNotificationTokenEventArgs(channel.Uri));
        }

        public void RemoveNotification(int id)
        {
            foreach (ToastNotification notification in ToastNotificationManager.History.GetHistory().Where(n => n.Data.Values.ContainsKey(NotificationIdKey) && n.Data.Values[NotificationIdKey] == id.ToString()).ToList())
                ToastNotificationManager.History.Remove(notification.Tag, notification.Group);
        }

        public void RemoveNotification(string tag, int id)
        {
            if (string.IsNullOrEmpty(tag))
            {
                RemoveNotification(id);
            }
            else
            {
                foreach (ToastNotification notification in ToastNotificationManager.History.GetHistory().Where(n => n.Data.Values.ContainsKey(NotificationTagKey) && n.Data.Values.ContainsKey(NotificationIdKey) && n.Data.Values[NotificationTagKey] == tag && n.Data.Values[NotificationIdKey] == id.ToString()).ToList())
                    ToastNotificationManager.History.Remove(notification.Tag, notification.Group);
            }
        }

        public void UnregisterForPushNotifications()
        {
            if (channel != null)
                channel.PushNotificationReceived -= Channel_PushNotificationReceived;

            ApplicationData.Current.LocalSettings.Values.Remove(TokenKey);
        }

        private void Channel_PushNotificationReceived(PushNotificationChannel sender, PushNotificationReceivedEventArgs args)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (args.NotificationType == PushNotificationType.Raw)
            {                
                foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, string>>(args.RawNotification.Content))
                    data.Add(pair.Key, pair.Value);
            }
            else if (args.NotificationType == PushNotificationType.Toast)
            {
                foreach (XmlAttribute attribute in args.ToastNotification.Content.DocumentElement.Attributes)
                    data.Add(attribute.Name, attribute.Value);
            }

            OnNotificationReceived?.Invoke(CrossPushNotification.Current, new PushNotificationDataEventArgs(data));
        }
    }
}
