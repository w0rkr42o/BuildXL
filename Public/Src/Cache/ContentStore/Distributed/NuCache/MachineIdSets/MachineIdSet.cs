// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content.
    /// </summary>
    /// <remarks>
    /// The type is immutable.
    /// </remarks>
    public abstract class MachineIdSet : IReadOnlyCollection<MachineId>
    {
        /// <nodoc />
        public const int BitMachineIdSetThreshold = 100;

        /// <summary>
        /// Returns an empty machine set.
        /// </summary>
        public static readonly MachineIdSet Empty = new ArrayMachineIdSet(new ushort[0]);

        /// <summary>
        /// Returns the format of a machine id set.
        /// </summary>
        protected abstract SetFormat Format { get; }

        /// <summary>
        /// Returns true if a machine id set is empty.
        /// </summary>
        public abstract bool IsEmpty { get; }

        /// <summary>
        /// Returns the bit value at position index for the machine id.
        /// </summary>
        public bool this[MachineId id] => this[id.Index];

        /// <summary>
        /// Returns the bit value at position index.
        /// </summary>
        public abstract bool this[int index] { get; }

        /// <summary>
        /// Gets the number of machine locations.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Returns a new instance of <see cref="MachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public abstract MachineIdSet SetExistence(IReadOnlyCollection<MachineId> machines, bool exists);

        /// <nodoc />
        public MachineIdSet Add(params MachineId[] machines) => SetExistence(machines, exists: true);

        /// <nodoc />
        public MachineIdSet Remove(params MachineId[] machines) => SetExistence(machines, exists: false);

        /// <summary>
        /// Enumerates the bits in the machine id set
        /// </summary>
        public abstract IEnumerable<MachineId> EnumerateMachineIds();

        /// <summary>
        /// Returns a position of the <paramref name="currentMachineId"/> in the current machine id list.
        /// </summary>
        /// <returns>-1 if the given id is not part of the machine id list.</returns>
        /// <remarks>
        /// This method can be implemented on top of <see cref="EnumerateMachineIds"/> but it is a separate method
        /// because different subtypes can implement this operation with no extra allocations.
        /// </remarks>
        public abstract int GetMachineIdIndex(MachineId currentMachineId);

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            MachineIdSet serializableInstance = this;
            if (Format == SetFormat.Bits)
            {
                if (Count <= BitMachineIdSetThreshold)
                {
                    serializableInstance = new ArrayMachineIdSet(EnumerateMachineIds().Select(id => (ushort)id.Index).ToArray());
                }
            }
            else
            {
                if (Count > BitMachineIdSetThreshold)
                {
                    serializableInstance = BitMachineIdSet.EmptyInstance.SetExistence(this, exists: true);
                }
            }

            writer.Write((byte)serializableInstance.Format);
            serializableInstance.SerializeCore(writer);
        }

        /// <nodoc />
        protected abstract void SerializeCore(BuildXLWriter writer);

        /// <summary>
        /// Returns true if deserialized instance would have a machine id with a given index.
        /// </summary>
        public static bool HasMachineId(BuildXLReader reader, int index)
        {
            var format = (SetFormat)reader.ReadByte();

            if (format == SetFormat.Bits)
            {
                return BitMachineIdSet.HasMachineIdCore(reader, index);
            }
            else
            {
                return ArrayMachineIdSet.HasMachineIdCore(reader, index);
            }
        }

        /// <nodoc />
        public static MachineIdSet Deserialize(BuildXLReader reader)
        {
            var format = (SetFormat)reader.ReadByte();

            if (format == SetFormat.Bits)
            {
                return BitMachineIdSet.DeserializeCore(reader);
            }
            else
            {
                return ArrayMachineIdSet.DeserializeCore(reader);
            }
        }

        /// <summary>
        /// Format of a machine id set.
        /// </summary>
        protected enum SetFormat
        {
            /// <summary>
            /// Based on a bit vector.
            /// </summary>
            Bits,

            /// <summary>
            /// Based on an array that contains a list of machine ids.
            /// </summary>
            Array
        }

        /// <inheritdoc />
        public IEnumerator<MachineId> GetEnumerator()
        {
            return EnumerateMachineIds().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
