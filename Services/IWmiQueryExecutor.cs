using System;
using System.Collections.Generic;
using System.Threading;

namespace DriverMasterPro.Services
{
    internal interface IWmiQueryExecutor
    {
        IEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQuery(
            string query,
            TimeSpan batchPacketTimeout,
            CancellationToken cancellationToken);
    }
}
