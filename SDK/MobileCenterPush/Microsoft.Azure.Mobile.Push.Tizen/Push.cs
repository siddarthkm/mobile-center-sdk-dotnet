using Microsoft.Azure.Mobile.Push.Shared.Ingestion.Models;
using Microsoft.Azure.Mobile.Utils;
using Microsoft.Azure.Mobile.Utils.Synchronization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tizen.Messaging.Push;
using Tizen.Applications;
using System.Xml;
//using Windows.ApplicationModel.Activation;
//using Windows.Data.Xml.Dom;
//using Windows.Networking.PushNotifications;

namespace Microsoft.Azure.Mobile.Push
{
    using TizenPushNotificationReceivedEventArgs = Tizen.Messaging.Push.PushMessageEventArgs;

    public partial class Push : MobileCenterService
    {
        private ApplicationLifecycleHelper _lifecycleHelper = new ApplicationLifecycleHelper();

        //private PushNotificationChannel _channel;

        protected override int TriggerCount => 1;

        /// <summary>
        /// Call this method at the end of Application.OnLaunched with the same parameter as OnLaunched.
        /// This method call is needed to handle click on push to trigger the portable PushNotificationReceived event.
        /// </summary>
        /// <param name="e">OnLaunched method event</param>
        public static void CheckLaunchedFromNotification(AppControlReceivedEventArgs e)
        {
            Instance.InstanceCheckLaunchedFromNotification(e);
        }

        private static string _appPushId = null;

        public static string TizenPushId
        {
            get
            {
                if (_appPushId == null)
                {
                    return "";
                }
                return _appPushId;
            }
            set
            {
                if (value != null)
                {
                    _appPushId = value;
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
                    // TODO TIZEN retrieve custom data from AppControl Extra Data
                    // TODO TIZEN check retrieval of push notification from App control
                    //customData = ParseExtraData(e?.ReceivedAppControl.ExtraData);
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

        /// <summary>
        /// If enabled, register push channel and send URI to backend.
        /// Also start intercepting pushes.
        /// If disabled and previously enabled, stop listening for pushes (they will still be received though).
        /// </summary>
        private void ApplyEnabledState(bool enabled)
        {
            if (enabled)
            {
                // We expect caller of this method to lock on _mutex, we can't do it here as that lock is not recursive
                var stateSnapshot = _stateKeeper.GetStateSnapshot();
                Task.Run(async () =>
                {
                    // TODO TIZEN connect and register to push service
                    // TODO TIZEN? define custom channel?
                    //var channel = await new WindowsPushNotificationChannelManager().CreatePushNotificationChannelForApplicationAsync()
                    //    .AsTask().ConfigureAwait(false);

                    var regId = await TizenPushNotificationManager.RegisterTizenPushService();
                    try
                    {
                        _mutex.Lock(stateSnapshot);
                        // TODO TIZEN retrieve pushToken == registration ID
                        //var pushToken = channel.Uri;
                        var pushToken = regId;
                        if (!string.IsNullOrEmpty(pushToken))
                        {
                            // Save channel member
                            //_channel = channel;

                            // Subscribe to push
                            PushClient.NotificationReceived += OnPushNotificationReceivedHandler;

                            // Send channel URI to backend
                            MobileCenterLog.Debug(LogTag, $"Push token '{pushToken}'");
                            var pushInstallationLog = new PushInstallationLog(0, null, pushToken, Guid.NewGuid());
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
                // TODO TIZEN Disconnect only if push service was disconnected during MC launch
                PushClient.PushServiceDisconnect();
            }
        }

        private void OnPushNotificationReceivedHandler(object sender, TizenPushNotificationReceivedEventArgs e)
        {
            // TODO TIZEN check Tizen push types
            // TODO TIZEN check toast notification
            // TODO If not received via mobile center, let default notification handler handle it.

            if (true /*e.Type == 0*/) // Toast type?
            {
                var message = e.Message;
                var appData = e.AppData;
                MobileCenterLog.Debug(LogTag, $"Received push notification payload: {message}, {appData}");
                if (_lifecycleHelper.IsSuspended)
                {
                    MobileCenterLog.Debug(LogTag, "Application in background. Push callback will be called when user clicks the toast notification.");
                }
                else
                {
                    var pushNotification = ParseMobileCenterPush(message, appData);
                    if (pushNotification != null)
                    {
                        //e.Cancel = true;
                        PushNotificationReceived?.Invoke(sender, pushNotification);
                        MobileCenterLog.Debug(LogTag, "Application in foreground. Intercept push notification and invoke push callback.");
                    }
                    else
                    {
                        MobileCenterLog.Debug(LogTag, "Push ignored. It was not sent through Mobile Center.");
                    }
                }
            }
            else
            {
                MobileCenterLog.Debug(LogTag, $"Push ignored. We only handle Toast notifications but PushNotificationType is '{e.Type}'");
            }
        }
        private static PushNotificationReceivedEventArgs ParseMobileCenterPush(string message, string  appData)
        {
            // TODO extract Title and Message from message parameter string
            // TODO extract customData key-values from appData parameter string

            // Check if mobile center push (it always has launch attribute with JSON object having mobile_center key)
            return null;
        }


        private static PushNotificationReceivedEventArgs ParseMobileCenterPush(XmlDocument content)
        {
            // Check if mobile center push (it always has launch attribute with JSON object having mobile_center key)
            var launch = content.SelectSingleNode("/toast/@launch")?.Value;
            var customData = ParseLaunchString(launch);
            if (customData == null)
            {
                return null;
            }

            // Parse title and message using identifiers
            return new PushNotificationReceivedEventArgs()
            {
                Title = content.SelectSingleNode("/toast/visual/binding/text[@id='1']")?.InnerText,
                Message = content.SelectSingleNode("/toast/visual/binding/text[@id='2']")?.InnerText,
                CustomData = customData
            };
        }

        private static Dictionary<string, string> ParseLaunchString(string launchString)
        {
            try
            {
                if (launchString != null)
                {
                    var launchJObject = JObject.Parse(launchString);
                    if (launchJObject?["mobile_center"] is JObject mobileCenterData)
                    {
                        var customData = new Dictionary<string, string>();
                        foreach (var pair in mobileCenterData)
                        {
                            customData.Add(pair.Key, pair.Value.ToString());
                        }
                        return customData;
                    }
                }
                return null;
            }
            catch (JsonReaderException)
            {
                return null;
            }
        }
    }
}
