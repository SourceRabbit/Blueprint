// Copyright (c) 2026 Nikolaos Siatras
// SPDX-License-Identifier: MIT

namespace Blueprint.Log
{
    /// <summary>Specifies the severity and verbosity of a log event.</summary>
    public enum EBlueprintLogLevel
    {
        /// <summary>Highly detailed execution information.</summary>
        Trace = 0,

        /// <summary>Information used to diagnose problems.</summary>
        Debug = 1,

        /// <summary>Normal application events.</summary>
        Info = 2,

        /// <summary>A problem that allows execution to continue.</summary>
        Warn = 3,

        /// <summary>A failed operation.</summary>
        Error = 4,

        /// <summary>A critical failure that prevents the application from continuing.</summary>
        Fatal = 5
    }
}
