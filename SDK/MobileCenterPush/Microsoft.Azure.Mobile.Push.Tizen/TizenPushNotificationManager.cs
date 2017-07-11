//using Windows.Foundation;
//using Windows.Networking.PushNotifications;

using System.Threading.Tasks;
using Tizen.Messaging.Push;

namespace Microsoft.Azure.Mobile.Push
{
    public class TizenPushNotificationManager
    {
        public static Task<string> RegisterTizenPushService()
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            PushClient.StateChanged += async (object sender, PushConnectionStateEventArgs e) =>
            {
                    switch (e.State)
                    {
                    case PushConnectionStateEventArgs.PushState.Registered:
                        // Already connected
                        tcs.TrySetResult(PushClient.GetRegistrationId());
                        break;

                    case PushConnectionStateEventArgs.PushState.Unregistered:
                        var response = await PushClient.PushServerRegister();
                        if (response.ServerResult != ServerResponse.Result.Success)
                        {
                            tcs.TrySetResult("");
                        }
                        break;
                    case PushConnectionStateEventArgs.PushState.StateError:
                        // TODO Handle state error
                        tcs.TrySetResult("");
                        break;
                    default:
                        // TODO handle other cases
                        tcs.TrySetResult("");
                        break;
                    }
            };
            // TODO TIZEN do a check for push ID when initializing
            PushClient.PushServiceConnect(Push.TizenPushId);
            return tcs.Task;
        }
    }
}
