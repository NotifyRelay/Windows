using Vanara.PInvoke;

namespace NotifyRelay.Platforms.Windows.Interop.SyncRoot;

public delegate void SyncRootCallback(in CldApi.CF_CALLBACK_INFO callbackInfo, in CldApi.CF_CALLBACK_PARAMETERS callbackParameters);
