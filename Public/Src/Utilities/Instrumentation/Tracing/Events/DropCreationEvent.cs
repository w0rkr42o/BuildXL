// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using System.Reflection;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// ETW event emitted after "drop create" invocation.
    /// </summary>
    [EventData]
    public sealed class DropCreationEvent : DropOperationBaseEvent
    {
        private static readonly PropertyInfo[] s_members = typeof(DropCreationEvent).GetProperties();

        /// <inheritdoc />
        internal override PropertyInfo[] Members => s_members;

        /// <summary>
        /// Event version
        /// </summary>
        /// <remarks>
        /// WARNING: INCREMENT IF YOU UPDATE THE PRIMITIVE MEMBERS!
        /// </remarks>
        public override int Version { get; set; } = 1;

        /// <inheritdoc />
        public override EventKind Kind { get; set; } = EventKind.DropCreation;

        /// <summary>
        /// Expiration of the drop in days.
        /// </summary>
        public int DropExpirationInDays { get; set; }
    }
}
