using Microsoft.Azure.Mobile.Push.Shared.Ingestion.Models;
using Microsoft.Azure.Mobile.Utils;
using Microsoft.Azure.Mobile.Utils.Synchronization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using System.Linq;
using Tizen.Messaging.Push;
using Tizen.Applications;

namespace Microsoft.Azure.Mobile.Push
{
    using TizenPushNotificationReceivedEventArgs = Tizen.Messaging.Push.PushMessageEventArgs;

    public partial class Push : MobileCenterService
    {
        private ApplicationLifecycleHelper _lifecycleHelper = new ApplicationLifecycleHelper();

        protected override int TriggerCount => 1;

        public static void CheckLaunchedFromNotification(AppControlReceivedEventArgs e)
        {
            Instance.InstanceCheckLaunchedFromNotification(e);
        }

        private static string _tizenPushAppId = null;

        public static string TizenPushAppId
        {
            get
            {
                if (_tizenPushAppId == null)
                {
                    return "";
                }
                return _tizenPushAppId;
            }
            set
            {
                if (value != null)
                {
                    _tizenPushAppId = value;
                }
            }
        }

        private void InstanceCheckLaunchedFromNotification(AppControlReceivedEventArgs e)
        {
            IDictionary<string, string> customData = null;
            _mutex.Lock();
            try
            {
                if (!IsInactive)
                {
                    string value;
                    if (e.ReceivedAppControl.ExtraData.TryGet("http://tizen.org/appcontrol/data/push/launch_type", out value))
                    {
                        switch (value)
                        {
                            case "notification":
                                string appData;
                                if (e.ReceivedAppControl.ExtraData.TryGet("http://tizen.org/appcontrol/data/push/appdata", out appData))
                                {
                                    customData = ParseAppData(appData);
                                }
                                else
                                {
                                    Tizen.Log.Debug(LogTag, $"No custom data set data");
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            finally
            {
                _mutex.Unlock();
            }
            if (customData != null)
            {
                PushNotificationReceived?.Invoke(null, new PushNotificationReceivedEventArgs()
                {
                    Title = null,
                    Message = null,
                    CustomData = customData
                });
            }
        }

        private void ApplyEnabledState(bool enabled)
        {
            if (enabled)
            {
                // We expect caller of this method to lock on _mutex, we can't do it here as that lock is not recursive
                var stateSnapshot = _stateKeeper.GetStateSnapshot();
                Task.Run(async () =>
                {
                    var registrationId = await TizenPushNotificationManager.RegisterTizenPushService();
                    try
                    {
                        _mutex.Lock(stateSnapshot);
                        if (!string.IsNullOrEmpty(registrationId))
                        {
                            // Subscribe to push
                            PushClient.NotificationReceived += OnPushNotificationReceivedHandler;

                            // Send push registration ID to backend
                            MobileCenterLog.Debug(LogTag, $"Push token '{registrationId}'");
                            var pushInstallationLog = new PushInstallationLog(0, null, registrationId, Guid.NewGuid());
                            await Channel.Enqueue(pushInstallationLog).ConfigureAwait(false);
                        }
                        else
                        {
                            MobileCenterLog.Error(LogTag, "Push service registering with Mobile Center backend has failed.");
                        }
                    }
                    catch (StatefulMutexException)
                    {
                        MobileCenterLog.Warn(LogTag, "Push Enabled state changed after creating channel.");
                    }
                    finally
                    {
                        _mutex.Unlock();
                    }
                });
            }
            else
            {
                PushClient.NotificationReceived -= OnPushNotificationReceivedHandler;
            }
        }

        private void OnPushNotificationReceivedHandler(object sender, TizenPushNotificationReceivedEventArgs e)
        {
            var message = e.Message;
            var appData = e.AppData;
            MobileCenterLog.Debug(LogTag, $"Received push notification with message == {message}, appData == {appData}");
            if (_lifecycleHelper.IsSuspended)
            {
                MobileCenterLog.Debug(LogTag, "Application in background. Push callback will be called when user clicks the toast notification.");
            }
            else
            {
                var pushNotification = ParseMobileCenterPush(message, appData);
                if (pushNotification != null)
                {
                    PushNotificationReceived?.Invoke(sender, pushNotification);
                    MobileCenterLog.Debug(LogTag, "Application in foreground. Intercept push notification and invoke push callback.");
                }
                else
                {
                    MobileCenterLog.Debug(LogTag, "Push ignored. No customData sent");
                }
            }
        }
        private static PushNotificationReceivedEventArgs ParseMobileCenterPush(string message, string  appData)
        {
            var messageQuery = HttpUtility.ParseQueryString(message);

            var customData = ParseAppData(appData);
            if (customData == null)
            {
                return null;
            }

            return new PushNotificationReceivedEventArgs()
            {
                Title = messageQuery["textTypeTitle"],
                Message = messageQuery["textTypeContent"],
                CustomData = customData
            };
        }

        private static Dictionary<string, string> ParseAppData(string appData)
        {
            try
            {
                if (!string.IsNullOrEmpty(appData))
                {
                    // AppData is of the form {key1=1234&key2=abcd&...}
                    var customData = appData.Substring(1, appData.Length - 2)
                                            .Split("&")
                                            .Select((keyValueStr) => keyValueStr.Split(":"))
                                            .ToDictionary((keyValuePair) => keyValuePair[0], (keyValuePair) => keyValuePair[1]);
                    if (customData == null)
                    {
                        customData = new Dictionary<string, string>();
                    }
                    return customData;
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
