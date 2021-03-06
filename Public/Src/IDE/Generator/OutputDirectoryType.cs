// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Ide.Generator
{
    /// <summary>
    /// Preferential ordered (most preferred last) output directory types
    /// </summary>
    internal enum OutputDirectoryType
    {
        /// <summary>
        /// Not specified
        /// </summary>
        None,

        /// <summary>
        /// Output directory of primary binary for build (i.e., csc or link output)
        /// </summary>
        Build,

        /// <summary>
        /// Output directory of assembly deployment
        /// </summary>
        AssemblyDeployment,

        /// <summary>
        /// Output directory of assembly deployment
        /// </summary>
        TestDeployment,
    }
}
