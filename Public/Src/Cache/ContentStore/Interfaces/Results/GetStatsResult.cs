// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the GetStats call.
    /// </summary>
    public class GetStatsResult : Result<CounterSet>
    {
        /// <inheritdoc />
        public GetStatsResult(CounterSet result)
            : base(result)
        {
        }

        /// <inheritdoc />
        public GetStatsResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <inheritdoc />
        public GetStatsResult(Exception exception, string? message = null)
            : base(exception, message)
        {
        }

        /// <inheritdoc />
        public GetStatsResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <nodoc />
        public CounterSet? CounterSet => Value;
    }
}
