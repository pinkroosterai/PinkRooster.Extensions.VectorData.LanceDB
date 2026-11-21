using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.VectorData;

namespace PinkRooster.Extensions.VectorData.LanceDB.Internal;

internal static class VectorStoreExceptionHelper
{
    public static VectorStoreException Wrap(Exception ex, string? collectionName = null, string? operationName = null)
    {
        if (ex is OperationCanceledException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            ExceptionDispatchInfo.Capture(ex).Throw();
        }

        if (ex is VectorStoreException vse)
        {
            return vse;
        }

        if (ex is DllNotFoundException or BadImageFormatException)
        {
            return new VectorStoreException(
                $"Failed to load native LanceDB library for platform '{RuntimeInformation.RuntimeIdentifier}'.",
                ex)
            {
                VectorStoreSystemName = "LanceDB",
                CollectionName = collectionName,
                OperationName = operationName,
            };
        }

        return new VectorStoreException(ex.Message, ex)
        {
            VectorStoreSystemName = "LanceDB",
            CollectionName = collectionName,
            OperationName = operationName,
        };
    }
}
