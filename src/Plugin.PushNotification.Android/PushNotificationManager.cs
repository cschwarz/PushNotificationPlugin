using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Firebase.Iid;
using Plugin.PushNotification.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Plugin.PushNotification
{
    /// <summary>
    /// Implementation for Feature
    /// </summary>
    public class PushNotificationManager : IPushNotification
    {
        //internal static PushNotificationActionReceiver ActionReceiver = null;
        static NotificationResponse delayedNotificationResponse = null;
        internal const string KeyGroupName = "Plugin.PushNotification";
        internal const string TokenKey = "TokenKey";
        internal const string AppVersionCodeKey = "AppVersionCodeKey";
        internal const string AppVersionNameKey = "AppVersionNameKey";
        internal const string AppVersionPackageNameKey = "AppVersionPackageNameKey";
        static IList<NotificationUserCategory> userNotificationCategories = new List<NotificationUserCategory>();
        public static string NotificationContentTitleKey { get; set; }
        public static string NotificationContentTextKey { get; set; }
        public static string NotificationContentDataKey { get; set; }
        public static int IconResource { get; set; }
        public static Android.Net.Uri SoundUri { get; set; }
        public static Color? Color { get; set; }
        public static Type NotificationActivityType { get; set; }
        public static ActivityFlags? NotificationActivityFlags { get; set; } = ActivityFlags.ClearTop | ActivityFlags.SingleTop;
        public static string DefaultNotificationChannelId { get; set; } = "PushNotificationChannel";
        public static string DefaultNotificationChannelName { get; set; } = "General";
        public static string DefaultNotificationChannelSound { get; set; } = "default";

        internal static Type DefaultNotificationActivityType { get; set; } = null;

        static Context _context;

        [Obsolete("ProcessIntent with these parameters is deprecated, please use the other override instead.")]
        public static void ProcessIntent(Intent intent, bool enableDelayedResponse = true)
        {
            Bundle extras = intent?.Extras;
            if (extras != null && !extras.IsEmpty)
            {
                var parameters = new Dictionary<string, object>();
                foreach (var key in extras.KeySet())
                {
                    if (!parameters.ContainsKey(key) && extras.Get(key) != null)
                        parameters.Add(key, $"{extras.Get(key)}");
                }

                NotificationManager manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
                var notificationId = extras.GetInt(DefaultPushNotificationHandler.ActionNotificationIdKey, -1);
                if (notificationId != -1)
                {
                    var notificationTag = extras.GetString(DefaultPushNotificationHandler.ActionNotificationTagKey, string.Empty);
                    if (notificationTag == null)
                        manager.Cancel(notificationId);
                    else
                        manager.Cancel(notificationTag, notificationId);
                }


                var response = new NotificationResponse(parameters, extras.GetString(DefaultPushNotificationHandler.ActionIdentifierKey, string.Empty));

                if (_onNotificationOpened == null && enableDelayedResponse)
                    delayedNotificationResponse = response;
                else
                    _onNotificationOpened?.Invoke(CrossPushNotification.Current, new PushNotificationResponseEventArgs(response.Data, response.Identifier, response.Type));

                CrossPushNotification.Current.NotificationHandler?.OnOpened(response);
            }
        }

        public static void ProcessIntent(Activity activity, Intent intent, bool enableDelayedResponse = true)
        {
            DefaultNotificationActivityType = activity.GetType();

            Bundle extras = intent?.Extras;
            if (extras != null && !extras.IsEmpty)
            {
                var parameters = new Dictionary<string, object>();
                foreach (var key in extras.KeySet())
                {
                    if (!parameters.ContainsKey(key) && extras.Get(key) != null)
                        parameters.Add(key, $"{extras.Get(key)}");
                }

                NotificationManager manager = _context.GetSystemService(Context.NotificationService) as NotificationManager;
                var notificationId = extras.GetInt(DefaultPushNotificationHandler.ActionNotificationIdKey, -1);
                if (notificationId != -1)
                {
                    var notificationTag = extras.GetString(DefaultPushNotificationHandler.ActionNotificationTagKey, string.Empty);
                    if (notificationTag == null)
                        manager.Cancel(notificationId);
                    else
                        manager.Cancel(notificationTag, notificationId);
                }


                var response = new NotificationResponse(parameters, extras.GetString(DefaultPushNotificationHandler.ActionIdentifierKey, string.Empty));

                if (_onNotificationOpened == null && enableDelayedResponse)
                    delayedNotificationResponse = response;
                else
                    _onNotificationOpened?.Invoke(CrossPushNotification.Current, new PushNotificationResponseEventArgs(response.Data, response.Identifier, response.Type));

                CrossPushNotification.Current.NotificationHandler?.OnOpened(response);
            }
        }

        public static void Initialize(Context context, bool resetToken, bool createDefaultNotificationChannel = true, bool autoRegistration = true)
        {
            _context = context;

            CrossPushNotification.Current.NotificationHandler = CrossPushNotification.Current.NotificationHandler ?? new DefaultPushNotificationHandler();
            if (autoRegistration)
            {
                ThreadPool.QueueUserWorkItem(state =>
                {

                    var packageName = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).PackageName;
                    var versionCode = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).VersionCode;
                    var versionName = Application.Context.PackageManager.GetPackageInfo(Application.Context.PackageName, PackageInfoFlags.MetaData).VersionName;
                    var prefs = Android.App.Application.Context.GetSharedPreferences(PushNotificationManager.KeyGroupName, FileCreationMode.Private);

                    try
                    {

                        var storedVersionName = prefs.GetString(PushNotificationManager.AppVersionNameKey, string.Empty);
                        var storedVersionCode = prefs.GetString(PushNotificationManager.AppVersionCodeKey, string.Empty);
                        var storedPackageName = prefs.GetString(PushNotificationManager.AppVersionPackageNameKey, string.Empty);


                        if (resetToken || (!string.IsNullOrEmpty(storedPackageName) && (!storedPackageName.Equals(packageName, StringComparison.CurrentCultureIgnoreCase) || !storedVersionName.Equals(versionName, StringComparison.CurrentCultureIgnoreCase) || !storedVersionCode.Equals($"{versionCode}", StringComparison.CurrentCultureIgnoreCase))))
                        {
                            CleanUp();

                        }

                    }
                    catch (Exception ex)
                    {
                        _onNotificationError?.Invoke(CrossPushNotification.Current, new PushNotificationErrorEventArgs(PushNotificationErrorType.UnregistrationFailed, ex.ToString()));
                    }
                    finally
                    {
                        var editor = prefs.Edit();
                        editor.PutString(PushNotificationManager.AppVersionNameKey, $"{versionName}");
                        editor.PutString(PushNotificationManager.AppVersionCodeKey, $"{versionCode}");
                        editor.PutString(PushNotificationManager.AppVersionPackageNameKey, $"{packageName}");
                        editor.Commit();
                    }

                    CrossPushNotification.Current.RegisterForPushNotifications();
                });
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O && createDefaultNotificationChannel)
            {
                var notificationChannel = new NotificationChannel(DefaultNotificationChannelId, DefaultNotificationChannelName, NotificationImportance.Default);
                if (DefaultNotificationChannelSound != null)
                {
                    int soundResId = Application.Context.Resources.GetIdentifier(DefaultNotificationChannelSound, "raw", Application.Context.PackageName);
                    Android.Net.Uri soundUri = Android.Net.Uri.Parse($"{ContentResolver.SchemeAndroidResource}://{Application.Context.PackageName}/{soundResId}");
                    AudioAttributes audioAttributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Notification).SetContentType(AudioContentType.Speech).Build();
                    notificationChannel.SetSound(soundUri, audioAttributes);
                }
                NotificationManager notificationManager = (NotificationManager)Application.Context.GetSystemService(Context.NotificationService);
                notificationManager.CreateNotificationChannel(notificationChannel);
            }

            System.Diagnostics.Debug.WriteLine(CrossPushNotification.Current.Token);
        }

        public static void Initialize(Context context, NotificationUserCategory[] notificationCategories, bool resetToken, bool createDefaultNotificationChannel = true, bool autoRegistration = true)
        {

            Initialize(context, resetToken, createDefaultNotificationChannel, autoRegistration);
            RegisterUserNotificationCategories(notificationCategories);
        }

        public async System.Threading.Tasks.Task RegisterForPushNotifications()
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                var token = FirebaseInstanceId.Instance.Token;
                if (!string.IsNullOrEmpty(token))
                {
                    SaveToken(token);
                }
            });

        }
        public void UnregisterForPushNotifications()
        {
            Reset();
        }

        public static void Reset()
        {
            try
            {
                ThreadPool.QueueUserWorkItem(state =>
                {
                    CleanUp();
                });
            }
            catch (Exception ex)
            {
                _onNotificationError?.Invoke(CrossPushNotification.Current, new PushNotificationErrorEventArgs(PushNotificationErrorType.UnregistrationFailed, ex.ToString()));
            }
        }

        static void CleanUp()
        {
            Firebase.Iid.FirebaseInstanceId.Instance.DeleteInstanceId();
            SaveToken(string.Empty);
        }

        public static void Initialize(Context context, IPushNotificationHandler pushNotificationHandler, bool resetToken, bool createDefaultNotificationChannel = true, bool autoRegistration = true)
        {
            CrossPushNotification.Current.NotificationHandler = pushNotificationHandler;
            Initialize(context, resetToken, createDefaultNotificationChannel, autoRegistration);
        }

        public static void ClearUserNotificationCategories()
        {
            userNotificationCategories.Clear();
        }

        public string Token { get { return Android.App.Application.Context.GetSharedPreferences(KeyGroupName, FileCreationMode.Private).GetString(TokenKey, string.Empty); } }

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


        public IPushNotificationHandler NotificationHandler { get; set; }

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

        public NotificationUserCategory[] GetUserNotificationCategories()
        {
            return userNotificationCategories?.ToArray();
        }
        public static void RegisterUserNotificationCategories(NotificationUserCategory[] notificationCategories)
        {
            if (notificationCategories != null && notificationCategories.Length > 0)
            {
                ClearUserNotificationCategories();

                foreach (var userCat in notificationCategories)
                {
                    userNotificationCategories.Add(userCat);
                }

            }
            else
            {
                ClearUserNotificationCategories();
            }
        }

        #region internal methods
        //Raises event for push notification token refresh
        internal static void RegisterToken(string token)
        {
            _onTokenRefresh?.Invoke(CrossPushNotification.Current, new PushNotificationTokenEventArgs(token));
        }
        internal static void RegisterData(IDictionary<string, object> data)
        {
            _onNotificationReceived?.Invoke(CrossPushNotification.Current, new PushNotificationDataEventArgs(data));
        }
        internal static void RegisterDelete(IDictionary<string, object> data)
        {
            _onNotificationDeleted?.Invoke(CrossPushNotification.Current, new PushNotificationDataEventArgs(data));
        }
        internal static void SaveToken(string token)
        {
            var editor = Android.App.Application.Context.GetSharedPreferences(PushNotificationManager.KeyGroupName, FileCreationMode.Private).Edit();
            editor.PutString(PushNotificationManager.TokenKey, token);
            editor.Commit();
        }

        #endregion
    }
}