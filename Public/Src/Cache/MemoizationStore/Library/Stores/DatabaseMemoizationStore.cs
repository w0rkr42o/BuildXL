﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

extern alias Async;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    ///     An IMemoizationStore implementation using RocksDb.
    /// </summary>
    public class DatabaseMemoizationStore : StartupShutdownBase, IMemoizationStore
    {
        /// <summary>
        /// The database backing the store
        /// </summary>
        public virtual MemoizationDatabase Database { get; }

        /// <summary>
        ///     Store tracer.
        /// </summary>
        private readonly MemoizationStoreTracer _tracer;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        /// The component name
        /// </summary>
        protected string Component => Tracer.Name;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DatabaseMemoizationStore"/> class.
        /// </summary>
        public DatabaseMemoizationStore(MemoizationDatabase database)
        {
            Contract.RequiresNotNull(database);

            _tracer = new MemoizationStoreTracer(database.Name);
            Database = database;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DatabaseMemoizationStore"/> class.
        /// </summary>
        protected DatabaseMemoizationStore(ILogger logger, string name)
        {
            Contract.RequiresNotNull(logger);

            _tracer = new MemoizationStoreTracer(name);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            var session = new ReadOnlyDatabaseMemoizationSession(name, this);
            return new CreateSessionResult<IReadOnlyMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            var session = new DatabaseMemoizationSession(name, this);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            var session = new DatabaseMemoizationSession(name, this, contentSession);
            return new CreateSessionResult<IMemoizationSession>(session);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<MemoizationStoreTracer>.RunAsync(_tracer, new OperationContext(context), () =>
            {
                var counters = new CounterSet();
                counters.Merge(_tracer.GetCounters(), $"{_tracer.Name}.");
                return Task.FromResult(new GetStatsResult(counters));
            });
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return Database.StartupAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return Database.ShutdownAsync(context);
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            var ctx = new OperationContext(context);
            return AsyncEnumerableExtensions.CreateSingleProducerTaskAsyncEnumerable(() => Database.EnumerateStrongFingerprintsAsync(ctx));
        }

        internal Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts)
        {
            var ctx = new OperationContext(context, cts);
            return ctx.PerformOperationAsync(_tracer, async () =>
            {
                var result = await Database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: false);
                return result.Succeeded
                    ? new GetContentHashListResult(result.Value.contentHashListInfo)
                    : new GetContentHashListResult(result);
            },
            extraEndMessage: _ => $"StrongFingerprint=[{strongFingerprint}]",
            traceOperationStarted: false);
        }

        internal Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, IContentSession contentSession, CancellationToken cts)
        {
            var ctx = new OperationContext(context, cts);

            return ctx.PerformOperationAsync(_tracer, async () =>
            {
                // We do multiple attempts here because we have a "CompareExchange" RocksDB in the heart
                // of this implementation, and this may fail if the database is heavily contended.
                // Unfortunately, there is not much we can do at the time of writing to avoid this
                // requirement.
                var maxAttempts = 5;
                while (maxAttempts-- >= 0)
                {
                    var contentHashList = contentHashListWithDeterminism.ContentHashList;
                    var determinism = contentHashListWithDeterminism.Determinism;

                    // Load old value. Notice that this get updates the time, regardless of whether we replace the value or not.
                    var oldContentHashListWithDeterminism = await Database.GetContentHashListAsync(
                        ctx,
                        strongFingerprint,
                        // Prefer shared result because conflicts are resolved at shared level
                        preferShared: true);

                    var (oldContentHashListInfo, replacementToken) = oldContentHashListWithDeterminism.Succeeded
                     ? (oldContentHashListWithDeterminism.Value.contentHashListInfo, oldContentHashListWithDeterminism.Value.replacementToken)
                     : (default(ContentHashListWithDeterminism), string.Empty);

                    var oldContentHashList = oldContentHashListInfo.ContentHashList;
                    var oldDeterminism = oldContentHashListInfo.Determinism;

                    // Make sure we're not mixing SinglePhaseNonDeterminism records
                    if (!(oldContentHashList is null) && oldDeterminism.IsSinglePhaseNonDeterministic != determinism.IsSinglePhaseNonDeterministic)
                    {
                        return AddOrGetContentHashListResult.SinglePhaseMixingError;
                    }

                    if (oldContentHashList is null ||
                        oldDeterminism.ShouldBeReplacedWith(determinism) ||
                        !(await contentSession.EnsureContentIsAvailableAsync(ctx, oldContentHashList, ctx.Token).ConfigureAwait(false)))
                    {
                        // Replace if incoming has better determinism or some content for the existing
                        // entry is missing. The entry could have changed since we fetched the old value
                        // earlier, hence, we need to check it hasn't.
                        var exchanged = await Database.CompareExchange(
                           ctx,
                           strongFingerprint,
                           replacementToken,
                           oldContentHashListInfo,
                           contentHashListWithDeterminism).ThrowIfFailureAsync();
                        if (!exchanged)
                        {
                            // Our update lost, need to retry
                            continue;
                        }

                        return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, determinism));
                    }

                    // If we didn't accept the new value because it is the same as before, just with a not
                    // necessarily better determinism, then let the user know.
                    if (!(oldContentHashList is null) && oldContentHashList.Equals(contentHashList))
                    {
                        return new AddOrGetContentHashListResult(new ContentHashListWithDeterminism(null, oldDeterminism));
                    }

                    // If we didn't accept a deterministic tool's data, then we're in an inconsistent state
                    if (determinism.IsDeterministicTool)
                    {
                        return new AddOrGetContentHashListResult(
                            AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError,
                            oldContentHashListWithDeterminism.Value.contentHashListInfo);
                    }

                    // If we did not accept the given value, return the value in the cache
                    return new AddOrGetContentHashListResult(oldContentHashListWithDeterminism.Value.contentHashListInfo);
                }

                return new AddOrGetContentHashListResult("Hit too many races attempting to add content hash list into the cache");
            },
            extraEndMessage: _ => $"StrongFingerprint=[{strongFingerprint}], Determinism=[{contentHashListWithDeterminism.Determinism}]",
            traceOperationStarted: false);
        }

        internal Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            var ctx = new OperationContext(context);

            return ctx.PerformOperationAsync(_tracer, () => Database.GetLevelSelectorsAsync(ctx, weakFingerprint, level),
                extraEndMessage: _ => $"WeakFingerprint=[{weakFingerprint}], Level=[{level}]",
                traceOperationStarted: false);
        }
    }
}
