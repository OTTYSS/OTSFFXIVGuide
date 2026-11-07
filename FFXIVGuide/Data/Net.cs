using System;
using System.Net;
using System.Net.Http;
using Dalamud.Networking.Http;

namespace FFXIVGuide.Data;

/// <summary>
/// HTTP-клиенты плагина. HappyEyeballs (из Dalamud) пробует IPv4 и IPv6 одновременно:
/// без него запросы зависают, если у провайдера IPv6 объявлен, но не работает.
/// </summary>
public static class Net
{
    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = new HappyEyeballsCallback().ConnectCallback,
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}
