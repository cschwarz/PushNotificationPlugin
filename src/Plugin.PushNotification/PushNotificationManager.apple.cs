﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using UIKit;
using UserNotifications;

namespace Plugin.PushNotification
{
    /// <summary>
    /// Implementation for PushNotification
    /// </summary>
    public class PushNotificationManager : NSObject, IPushNotification, IUNUserNotificationCenterDelegate
    {
        static NotificationResponse delayedNotificationResponse = null;
        const string TokenKey = "Token";

        NSString NotificationIdKey = new NSString("id");
        NSString ApsNotificationIdKey = new NSString("aps.id");

        NSString NotificationTagKey = new NSString("tag");
        NSString ApsNotificationTagKey = new NSString("aps.tag");

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
            return NSUserDefaults.StandardUserDefaults.StringForKey(TokenKey);
        }

        internal static void InternalSaveToken(string token)
        {
            NSUserDefaults.StandardUserDefaults.SetString(token, TokenKey);
        }

        public IPushNotificationHandler NotificationHandler { get; set; }

        public static UNNotificationPresentationOptions CurrentNotificationPresentationOption { get; set; } = UNNotificationPresentationOptions.None;

        static IList<NotificationUserCategory> UsernNotificationCategories { get; } = new List<NotificationUserCategory>();

        static PushNotificationTokenEventHandler _onTokenRefresh;
        public event PushNotificationTokenEventHandler OnTokenRefresh
        {
            add
            {
                _onTokenRefresh += value;
            }
            remove
            {
                _onTokenRefresh -= value;
            }
        }

        static PushNotificationErrorEventHandler _onNotificationError;
        public event PushNotificationErrorEventHandler OnNotificationError
        {
            add
            {
                _onNotificationError += value;
            }
            remove
            {
                _onNotificationError -= value;
            }
        }

        static PushNotificationResponseEventHandler _onNotificationOpened;
        public event PushNotificationResponseEventHandler OnNotificationOpened
        {
            add
            {
                var previousVal = _onNotificationOpened;
                _onNotificationOpened += value;
                if (delayedNotificationResponse != null && previousVal == null)
                {
                    var tmpParams = delayedNotificationResponse;
                    _onNotificationOpened?.Invoke(CrossPushNotification.Current, new PushNotificationResponseEventArgs(tmpParams.Data, tmpParams.Identifier, tmpParams.Type));
                    delayedNotificationResponse = null;
                }
            }
            remove
            {
                _onNotificationOpened -= value;
            }
        }

        public NotificationUserCategory[] GetUserNotificationCategories()
        {
            return UsernNotificationCategories?.ToArray();
        }

        static PushNotificationDataEventHandler _onNotificationReceived;
        public event PushNotificationDataEventHandler OnNotificationReceived
        {
            add
            {
                _onNotificationReceived += value;
            }
            remove
            {
                _onNotificationReceived -= value;
            }
        }

        static PushNotificationDataEventHandler _onNotificationDeleted;
        public event PushNotificationDataEventHandler OnNotificationDeleted
        {
            add
            {
                _onNotificationDeleted += value;
            }
            remove
            {
                _onNotificationDeleted -= value;
            }
        }

        public static void Initialize(NSDictionary options, bool autoRegistration = true, bool enableDelayedResponse = true)
        {
            CrossPushNotification.Current.NotificationHandler = CrossPushNotification.Current.NotificationHandler ?? new DefaultPushNotificationHandler();

            if (options?.ContainsKey(UIApplication.LaunchOptionsRemoteNotificationKey)??false)
            {
                var pushPayload = options[UIApplication.LaunchOptionsRemoteNotificationKey] as NSDictionary;
                if (pushPayload != null)
                {
                    var parameters = GetParameters(pushPayload);

                    var notificationResponse = new NotificationResponse(parameters,string.Empty, NotificationCategoryType.Default);

                    if (_onNotificationOpened == null && enableDelayedResponse)
                        delayedNotificationResponse = notificationResponse;
                    else
                        _onNotificationOpened?.Invoke(CrossPushNotification.Current, new PushNotificationResponseEventArgs(notificationResponse.Data, notificationResponse.Identifier, notificationResponse.Type));

                    CrossPushNotification.Current.NotificationHandler?.OnOpened(notificationResponse);
                }
            }

            if (autoRegistration)
            {
               CrossPushNotification.Current.RegisterForPushNotifications();
            }
        }

        public static void Initialize(NSDictionary options, IPushNotificationHandler pushNotificationHandler, bool autoRegistration = true, bool enableDelayedResponse = true)
        {
            CrossPushNotification.Current.NotificationHandler = pushNotificationHandler;
            Initialize(options, autoRegistration, enableDelayedResponse);
        }

        public void RegisterUserNotificationCategories(NotificationUserCategory[] userCategories)
        {
            if (userCategories != null && userCategories.Length > 0)
            {
                UsernNotificationCategories.Clear();
                IList<UNNotificationCategory> categories = new List<UNNotificationCategory>();
                foreach (var userCat in userCategories)
                {
                    IList<UNNotificationAction> actions = new List<UNNotificationAction>();

                    foreach (var action in userCat.Actions)
                    {
                        // Create action
                        var actionID = action.Id;
                        var title = action.Title;
                        var notificationActionType = UNNotificationActionOptions.None;
                        switch (action.Type)
                        {
                            case NotificationActionType.AuthenticationRequired:
                                notificationActionType = UNNotificationActionOptions.AuthenticationRequired;
                                break;
                            case NotificationActionType.Destructive:
                                notificationActionType = UNNotificationActionOptions.Destructive;
                                break;
                            case NotificationActionType.Foreground:
                                notificationActionType = UNNotificationActionOptions.Foreground;
                                break;
                        }

                        var notificationAction = UNNotificationAction.FromIdentifier(actionID, title, notificationActionType);
                        actions.Add(notificationAction);
                    }

                    // Create category
                    var categoryID = userCat.Category;
                    var notificationActions = actions.ToArray() ?? new UNNotificationAction[] { };
                    var intentIDs = new string[] { };
                    var categoryOptions = new UNNotificationCategoryOptions[] { };

                    var category = UNNotificationCategory.FromIdentifier(categoryID, notificationActions, intentIDs, userCat.Type == NotificationCategoryType.Dismiss ? UNNotificationCategoryOptions.CustomDismissAction : UNNotificationCategoryOptions.None);
                    categories.Add(category);

                    UsernNotificationCategories.Add(userCat);
                }

                // Register categories
                UNUserNotificationCenter.Current.SetNotificationCategories(new NSSet<UNNotificationCategory>(categories.ToArray()));
            }
        }

        public void RegisterForPushNotifications()
        {
            // Register your app for remote notifications.
            if (UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
            {
                // iOS 10 or later
                var authOptions = UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound;

                // For iOS 10 display notification (sent via APNS)
                UNUserNotificationCenter.Current.Delegate = CrossPushNotification.Current as IUNUserNotificationCenterDelegate;

                UNUserNotificationCenter.Current.RequestAuthorization(authOptions, (granted, error) =>
                {
                    if (error != null)
                    {
                        _onNotificationError?.Invoke(CrossPushNotification.Current, new PushNotificationErrorEventArgs(PushNotificationErrorType.PermissionDenied, error.Description));
                    }
                    else if (!granted)
                    {
                        _onNotificationError?.Invoke(CrossPushNotification.Current, new PushNotificationErrorEventArgs(PushNotificationErrorType.PermissionDenied, "Push notification permission not granted"));
                    }
                    else
                    {
                        this.InvokeOnMainThread(()=> UIApplication.SharedApplication.RegisterForRemoteNotifications());
                    }
                });
            }
            else
            {
                // iOS 9 or before
                var allNotificationTypes = UIUserNotificationType.Alert | UIUserNotificationType.Badge | UIUserNotificationType.Sound;
                var settings = UIUserNotificationSettings.GetSettingsForTypes(allNotificationTypes, null);
                UIApplication.SharedApplication.RegisterUserNotificationSettings(settings);
                UIApplication.SharedApplication.RegisterForRemoteNotifications();
            }
        }

        public void UnregisterForPushNotifications()
        {
            UIApplication.SharedApplication.UnregisterForRemoteNotifications();
            Token = string.Empty;
        }

        // To receive notifications in foreground on iOS 10 devices.
        [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
        public void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
        {
            // Do your magic to handle the notification data
            System.Console.WriteLine(notification.Request.Content.UserInfo);
            System.Diagnostics.Debug.WriteLine("WillPresentNotification");
            var parameters = GetParameters(notification.Request.Content.UserInfo);
            _onNotificationReceived?.Invoke(CrossPushNotification.Current, new PushNotificationDataEventArgs(parameters));
            CrossPushNotification.Current.NotificationHandler?.OnReceived(parameters);

            string[] priorityKeys = new string[] { "priority", "aps.priority" };


            foreach (var pKey in priorityKeys)
            {
                if (parameters.TryGetValue(pKey, out object priority))
                {
                    var priorityValue = $"{priority}".ToLower();
                    switch (priorityValue)
                    {
                        case "max":
                        case "high":
                            if (!CurrentNotificationPresentationOption.HasFlag(UNNotificationPresentationOptions.Alert))
                            {
                                CurrentNotificationPresentationOption |= UNNotificationPresentationOptions.Alert;

                            }

                            if (!CurrentNotificationPresentationOption.HasFlag(UNNotificationPresentationOptions.Sound))
                            {
                                CurrentNotificationPresentationOption |= UNNotificationPresentationOptions.Sound;

                            }
                            break;
                        case "low":
                        case "min":
                        case "default":
                        default:
                            if (CurrentNotificationPresentationOption.HasFlag(UNNotificationPresentationOptions.Alert))
                            {
                                CurrentNotificationPresentationOption &= ~UNNotificationPresentationOptions.Alert;

                            }
                            break;
                    }

                    break;
                }
            }

            completionHandler(CurrentNotificationPresentationOption);
        }

        [Export("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:")]
        public void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            var parameters = GetParameters(response.Notification.Request.Content.UserInfo);

            NotificationCategoryType catType = NotificationCategoryType.Default;
            if (response.IsCustomAction)
                catType = NotificationCategoryType.Custom;
            else if (response.IsDismissAction)
                catType = NotificationCategoryType.Dismiss;

            var notificationResponse = new NotificationResponse(parameters, $"{response.ActionIdentifier}".Equals("com.apple.UNNotificationDefaultActionIdentifier", StringComparison.CurrentCultureIgnoreCase) ? string.Empty : $"{response.ActionIdentifier}", catType);
            _onNotificationOpened?.Invoke(this, new PushNotificationResponseEventArgs(notificationResponse.Data, notificationResponse.Identifier, notificationResponse.Type));

            CrossPushNotification.Current.NotificationHandler?.OnOpened(notificationResponse);

            // Inform caller it has been handled
            completionHandler();
        }

        public static void DidRegisterRemoteNotifications(NSData deviceToken)
        {
            var length = (int)deviceToken.Length;
            if (length == 0)
            {
                return;
            }

            var hex = new StringBuilder(length * 2);
            foreach (var b in deviceToken)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            var cleanedDeviceToken = hex.ToString();
            InternalSaveToken(cleanedDeviceToken);
            _onTokenRefresh?.Invoke(CrossPushNotification.Current, new PushNotificationTokenEventArgs(cleanedDeviceToken));
        }

        public static void DidReceiveMessage(NSDictionary data)
        {
            var parameters = GetParameters(data);

            _onNotificationReceived?.Invoke(CrossPushNotification.Current, new PushNotificationDataEventArgs(parameters));

            CrossPushNotification.Current.NotificationHandler?.OnReceived(parameters);
            System.Diagnostics.Debug.WriteLine("DidReceivedMessage");
        }

        public static void RemoteNotificationRegistrationFailed(NSError error)
        {
            _onNotificationError?.Invoke(CrossPushNotification.Current, new PushNotificationErrorEventArgs(PushNotificationErrorType.RegistrationFailed, error.Description));
        }

        static IDictionary<string, object> GetParameters(NSDictionary data)
        {
            var parameters = new Dictionary<string, object>();

            var keyAps = new NSString("aps");
            var keyAlert = new NSString("alert");

            foreach (var val in data)
            {
                if (val.Key.Equals(keyAps))
                {
                    if (data.ValueForKey(keyAps) is NSDictionary aps)
                    {
                        foreach (var apsVal in aps)
                        {
                            if (apsVal.Value is NSDictionary)
                            {
                                if (apsVal.Key.Equals(keyAlert))
                                {
                                    foreach (var alertVal in apsVal.Value as NSDictionary)
                                    {
                                        parameters.Add($"aps.alert.{alertVal.Key}", $"{alertVal.Value}");
                                    }
                                }
                            }
                            else
                            {
                                parameters.Add($"aps.{apsVal.Key}", $"{apsVal.Value}");
                            }
                        }
                    }
                }
                else
                {
                    parameters.Add($"{val.Key}", $"{val.Value}");
                }
            }

            return parameters;
        }

        public void ClearAllNotifications()
        {
            if (UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
            {
                UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
            }
            else
            {
                UIApplication.SharedApplication.CancelAllLocalNotifications();
            }
        }

        public async void RemoveNotification(int id)
        {
            if (UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
            {
              
                var deliveredNotifications = await UNUserNotificationCenter.Current.GetDeliveredNotificationsAsync();
                var deliveredNotificationsMatches = deliveredNotifications.Where(u => (u.Request.Content.UserInfo.ContainsKey(NotificationIdKey) && $"{u.Request.Content.UserInfo[NotificationIdKey]}".Equals($"{id}")) || (u.Request.Content.UserInfo.ContainsKey(ApsNotificationIdKey) && u.Request.Content.UserInfo[ApsNotificationIdKey].Equals($"{id}"))).Select(s => s.Request.Identifier).ToArray();
                if (deliveredNotificationsMatches.Length > 0)
                {
                    UNUserNotificationCenter.Current.RemoveDeliveredNotifications(deliveredNotificationsMatches);

                }
            }
            else
            {
                var scheduledNotifications = UIApplication.SharedApplication.ScheduledLocalNotifications.Where(u => (u.UserInfo.ContainsKey(NotificationIdKey) && u.UserInfo[NotificationIdKey].Equals($"{id}")) || (u.UserInfo.ContainsKey(ApsNotificationIdKey) && u.UserInfo[ApsNotificationIdKey].Equals($"{id}")));
                foreach (var notification in scheduledNotifications)
                {
                    UIApplication.SharedApplication.CancelLocalNotification(notification);
                }

            }
        }

        public async void RemoveNotification(string tag, int id)
        {
            if (string.IsNullOrEmpty(tag))
            {
                RemoveNotification(id);
            }
            else
            {
                if (UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
                {

                    var deliveredNotifications = await UNUserNotificationCenter.Current.GetDeliveredNotificationsAsync();
                    var deliveredNotificationsMatches = deliveredNotifications.Where(u => (u.Request.Content.UserInfo.ContainsKey(NotificationIdKey) && $"{u.Request.Content.UserInfo[NotificationIdKey]}".Equals($"{id}") && u.Request.Content.UserInfo.ContainsKey(NotificationTagKey) && u.Request.Content.UserInfo[NotificationTagKey].Equals(tag)) || (u.Request.Content.UserInfo.ContainsKey(ApsNotificationIdKey) && u.Request.Content.UserInfo[ApsNotificationIdKey].Equals($"{id}") && u.Request.Content.UserInfo.ContainsKey(ApsNotificationTagKey) && u.Request.Content.UserInfo[ApsNotificationTagKey].Equals(tag))).Select(s => s.Request.Identifier).ToArray();
                    if (deliveredNotificationsMatches.Length > 0)
                    {
                        UNUserNotificationCenter.Current.RemoveDeliveredNotifications(deliveredNotificationsMatches);

                    }
                }
                else
                {
                    var scheduledNotifications = UIApplication.SharedApplication.ScheduledLocalNotifications.Where(u => (u.UserInfo.ContainsKey(NotificationIdKey) && u.UserInfo[NotificationIdKey].Equals($"{id}") && u.UserInfo.ContainsKey(NotificationTagKey) && u.UserInfo[NotificationIdKey].Equals(tag)) || (u.UserInfo.ContainsKey(ApsNotificationIdKey) && u.UserInfo[ApsNotificationIdKey].Equals($"{id}") && u.UserInfo.ContainsKey(ApsNotificationTagKey) && u.UserInfo[ApsNotificationIdKey].Equals($"{tag}")));
                    foreach (var notification in scheduledNotifications)
                    {
                        UIApplication.SharedApplication.CancelLocalNotification(notification);
                    }

                }
            }
                
           
        }
    }
}
