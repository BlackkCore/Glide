using System.Net.Sockets;

namespace ServiceLib.Common;

/// <summary>
/// Tells a failed name lookup (the network or its DNS) from a real application error, so the Updates screen can say
/// which instead of showing .NET's raw "No such host is known".
/// </summary>
public static class NetworkFailure
{
    public static bool IsNameResolution(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData })
            {
                return true;
            }
            if (e is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The message to show: a plain explanation for a failed lookup, the exception's own text otherwise.</summary>
    public static string Describe(Exception ex, string host)
        => IsNameResolution(ex)
            ? $"Could not look up {host} (DNS). The name was not resolved by this computer's network; Glide did not block it. "
              + "Check the connection, or connect to a server first and update through it."
            : ex.Message;
}
